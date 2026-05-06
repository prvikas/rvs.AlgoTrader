using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Infrastructure.Persistence;
using rvs.AlgoTrader.IntegrationTests.Infrastructure;
using DomainClock = rvs.AlgoTrader.Domain.Interfaces.IClock;

namespace rvs.AlgoTrader.IntegrationTests.Tests;

/// <summary>
/// Integration tests for the live spread routing path in LiveExecutionEngine.
///
/// Uses SpreadRoutingFactory — a custom WebApplicationFactory that registers a
/// SpreadOrderManagerSpy, allowing assertions on whether routing occurred without
/// needing a real broker connection.
///
/// Covers:
///   Gate 0 (Approval)    — no approval blocks spread
///   Gate 1 (Kill switch) — active kill switch blocks spread
///   Gate 2 (Idempotency) — duplicate signal within TTL blocked
///   Happy path           — all gates pass → SpreadOrderManager called with correct legs
/// </summary>
public sealed class LiveSpreadRoutingIntegrationTests
    : IClassFixture<SpreadRoutingFactory>
{
    private readonly SpreadRoutingFactory _factory;
    private readonly HttpClient _client;

    public LiveSpreadRoutingIntegrationTests(SpreadRoutingFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SpreadSignalResult MakeIronCondorSpread() =>
        new(
            SpreadType: "IronCondor",
            Legs: new List<SpreadLeg>
            {
                new(OptionType.Put,  OrderDirection.Sell, StrikeSelectionMode.OtmByStrike, OtmStrikes: 2),
                new(OptionType.Put,  OrderDirection.Buy,  StrikeSelectionMode.OtmByStrike, OtmStrikes: 4),
                new(OptionType.Call, OrderDirection.Sell, StrikeSelectionMode.OtmByStrike, OtmStrikes: 2),
                new(OptionType.Call, OrderDirection.Buy,  StrikeSelectionMode.OtmByStrike, OtmStrikes: 4),
            },
            Reason:        "Integration test Iron Condor",
            SpotPrice:     22_500m,
            NearExpiryDate: LocalDate.FromDateTime(DateTime.Today.AddDays(7)));

    private async Task<StrategyInstance> SeedApprovedLiveInstanceAsync()
    {
        await using var s = _factory.Services.CreateAsyncScope();
        var db            = s.ServiceProvider.GetRequiredService<AlgoTraderDbContext>();
        var clock         = s.ServiceProvider.GetRequiredService<DomainClock>();
        var now           = clock.NowInstant();

        // Create a test user
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = $"testuser-{Guid.NewGuid():N}",
            Email = $"test-{Guid.NewGuid():N}@example.com",
            DisplayName = "Integration Test User",
            PasswordHash = "dummy",
            IsActive = true,
            CreatedAt = now.ToDateTimeOffset()
        };
        db.Users.Add(user);

        // Create a broker account for the user (brokerAccountId is the UserBrokerAccount ID)
        var brokerAccount = new UserBrokerAccount
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            BrokerId = 1, // MStock
            DisplayName = "Test MStock Account",
            IsActive = true
        };
        db.UserBrokerAccounts.Add(brokerAccount);
        await db.SaveChangesAsync();

        var instance = StrategyInstance.Create(
            name:                   $"SpreadIT-{Guid.NewGuid():N}",
            strategyType:           "IronCondor",
            watchlistId:            null,
            mode:                   StrategyMode.Live,
            brokerAccountId:        brokerAccount.Id,
            createdBy:              "integration-test",
            createdAt:              now,
            internalSymbol:         "NSE:NIFTY50",
            timeframe:              "1d",
            parametersJson:         "{}");

        instance.Status        = StrategyStatus.Running;
        instance.IsActive      = true;
        instance.ApprovalReady = true;
        instance.ApprovedAt    = now;

        // Create an active approval record (Gate 0)
        var approval = new StrategyApproval
        {
            Id                   = Guid.NewGuid(),
            StrategyInstanceId   = instance.Id,
            ApprovedBy           = "integration-test",
            ApprovalNotes        = "Approved for integration test",
            AutomatedChecksPassed = true,
            CagrAtApproval       = 0.30m,
            DrawdownAtApproval   = 0.10m,
            CreatedAt            = now,
            // InvalidatedAt = null → IsActive = true
        };

        db.Set<StrategyInstance>().Add(instance);
        db.Set<StrategyApproval>().Add(approval);
        await db.SaveChangesAsync();

        return instance;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteSignalAsync_SpreadEntry_WhenAllGatesPass_RoutesToSpreadOrderManager()
    {
        // Arrange
        var instance = await SeedApprovedLiveInstanceAsync();
        var signal   = SignalResult.SpreadEntry(MakeIronCondorSpread());
        var corrId   = Guid.NewGuid().ToString();

        var spy = _factory.SpreadManagerSpy;
        spy.Reset();

        await using var scope = _factory.Services.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<ILiveExecutionEngine>();

        // Act
        await engine.ExecuteSignalAsync(instance, signal, corrId, CancellationToken.None);

        // Assert
        spy.CallCount.Should().Be(1,
            "spread signal passing all gates should reach ISpreadOrderManager exactly once");
        spy.LastInstance!.Id.Should().Be(instance.Id);
        spy.LastSignal!.SpreadType.Should().Be("IronCondor");
        spy.LastSignal.Legs.Should().HaveCount(4, "Iron Condor has 4 legs");
        spy.LastCorrelationId.Should().Be(corrId);
    }

    [Fact]
    public async Task ExecuteSignalAsync_SpreadEntry_WhenKillSwitchActive_SuppressesSpread()
    {
        // Arrange
        var instance = await SeedApprovedLiveInstanceAsync();
        var signal   = SignalResult.SpreadEntry(MakeIronCondorSpread());

        await _client.PostAsJsonAsync("/api/killswitch/activate", new
        {
            Actor = "spread-routing-test",
            Reason = "Kill-switch suppression test",
            CorrelationId = Guid.NewGuid().ToString()
        });

        var spy = _factory.SpreadManagerSpy;
        spy.Reset();

        try
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var engine = scope.ServiceProvider.GetRequiredService<ILiveExecutionEngine>();

            // Act
            await engine.ExecuteSignalAsync(instance, signal, Guid.NewGuid().ToString(), CancellationToken.None);

            // Assert — Gate 1 (kill switch) blocks before SpreadOrderManager
            spy.CallCount.Should().Be(0, "kill switch (Gate 1) must suppress spread routing");
        }
        finally
        {
            await _client.PostAsJsonAsync("/api/killswitch/deactivate", new
            {
                Actor = "spread-routing-test",
                CorrelationId = Guid.NewGuid().ToString()
            });
        }
    }

    [Fact]
    public async Task ExecuteSignalAsync_SpreadEntry_WithDuplicateIdempotencyKey_BlocksSecondCall()
    {
        // Arrange — same signal twice in rapid succession triggers Gate 2 (idempotency)
        var instance = await SeedApprovedLiveInstanceAsync();
        var signal   = SignalResult.SpreadEntry(MakeIronCondorSpread());
        var corrId   = Guid.NewGuid().ToString();

        var spy = _factory.SpreadManagerSpy;
        spy.Reset();

        await using var scope1 = _factory.Services.CreateAsyncScope();
        var engine1 = scope1.ServiceProvider.GetRequiredService<ILiveExecutionEngine>();

        await using var scope2 = _factory.Services.CreateAsyncScope();
        var engine2 = scope2.ServiceProvider.GetRequiredService<ILiveExecutionEngine>();

        // Act
        await engine1.ExecuteSignalAsync(instance, signal, corrId, CancellationToken.None);
        await engine2.ExecuteSignalAsync(instance, signal, corrId, CancellationToken.None);

        // Assert — only one placement (idempotency window blocks second)
        spy.CallCount.Should().Be(1,
            "identical signals within idempotency TTL window must only route once to SpreadOrderManager");
    }

    [Fact]
    public async Task ExecuteSignalAsync_SpreadEntry_WithoutApproval_BlockedAtGate0()
    {
        // Arrange — instance with NO approval record
        await using var seedScope = _factory.Services.CreateAsyncScope();
        var db    = seedScope.ServiceProvider.GetRequiredService<AlgoTraderDbContext>();
        var clock = seedScope.ServiceProvider.GetRequiredService<DomainClock>();
        var now   = clock.NowInstant();

        // Create a test user and broker account
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = $"testuser-{Guid.NewGuid():N}",
            Email = $"test-{Guid.NewGuid():N}@example.com",
            DisplayName = "Integration Test User",
            PasswordHash = "dummy",
            IsActive = true,
            CreatedAt = now.ToDateTimeOffset()
        };
        db.Users.Add(user);

        var brokerAccount = new UserBrokerAccount
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            BrokerId = 1, // MStock
            DisplayName = "Test MStock Account",
            IsActive = true
        };
        db.UserBrokerAccounts.Add(brokerAccount);
        await db.SaveChangesAsync();

        var unapprovedInstance = StrategyInstance.Create(
            name:           $"NoApproval-{Guid.NewGuid():N}",
            strategyType:   "IronCondor",
            watchlistId:    null,
            mode:           StrategyMode.Live,
            brokerAccountId: brokerAccount.Id,
            createdBy:      "integration-test",
            createdAt:      now,
            internalSymbol: "NSE:NIFTY50",
            timeframe:      "1d",
            parametersJson: "{}");

        unapprovedInstance.Status   = StrategyStatus.Running;
        unapprovedInstance.IsActive = true;
        // Deliberately no approval record inserted

        db.Set<StrategyInstance>().Add(unapprovedInstance);
        await db.SaveChangesAsync();

        var spy = _factory.SpreadManagerSpy;
        spy.Reset();

        await using var execScope = _factory.Services.CreateAsyncScope();
        var engine = execScope.ServiceProvider.GetRequiredService<ILiveExecutionEngine>();
        var signal = SignalResult.SpreadEntry(MakeIronCondorSpread());

        // Act
        await engine.ExecuteSignalAsync(unapprovedInstance, signal, Guid.NewGuid().ToString(), CancellationToken.None);

        // Assert — Gate 0 (approval) blocks
        spy.CallCount.Should().Be(0, "missing approval (Gate 0) must block spread routing entirely");
    }

    [Fact]
    public async Task ExecuteSignalAsync_SpreadEntry_IronCondorLegDirections_PreservedToManager()
    {
        // Arrange
        var instance = await SeedApprovedLiveInstanceAsync();
        var spy = _factory.SpreadManagerSpy;
        spy.Reset();

        await using var scope  = _factory.Services.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<ILiveExecutionEngine>();
        var signal = SignalResult.SpreadEntry(MakeIronCondorSpread());

        // Act
        await engine.ExecuteSignalAsync(instance, signal, Guid.NewGuid().ToString(), CancellationToken.None);

        // Assert — all 4 legs arrive with correct directions
        spy.LastSignal!.Legs.Should().HaveCount(4);

        var sellLegs = spy.LastSignal.Legs.Count(l => l.Direction == OrderDirection.Sell);
        var buyLegs  = spy.LastSignal.Legs.Count(l => l.Direction == OrderDirection.Buy);

        sellLegs.Should().Be(2, "Iron Condor has 2 short (sell) legs");
        buyLegs.Should().Be(2,  "Iron Condor has 2 long (buy) wing legs");
    }
}

// ── Spy ───────────────────────────────────────────────────────────────────────

/// <summary>
/// Thread-safe spy for ISpreadOrderManager — captures calls without broker I/O.
/// </summary>
public sealed class SpreadOrderManagerSpy : ISpreadOrderManager
{
    private volatile int _callCount;

    public int CallCount => _callCount;
    public StrategyInstance?  LastInstance      { get; private set; }
    public SpreadSignalResult? LastSignal        { get; private set; }
    public string?             LastCorrelationId { get; private set; }

    public Task<Guid?> ExecuteSpreadAsync(
        StrategyInstance instance,
        SpreadSignalResult signal,
        decimal spotPrice,
        LocalDate expiryDate,
        string correlationId,
        CancellationToken ct)
    {
        Interlocked.Increment(ref _callCount);
        LastInstance      = instance;
        LastSignal        = signal;
        LastCorrelationId = correlationId;
        return Task.FromResult<Guid?>(Guid.NewGuid());
    }

    public Task CloseSpreadAsync(Guid spreadPositionId, string reason, string correlationId, CancellationToken ct)
        => Task.CompletedTask;

    public Task<SpreadPosition?> GetSpreadAsync(Guid spreadPositionId, CancellationToken ct)
        => Task.FromResult<SpreadPosition?>(null);

    public void Reset()
    {
        _callCount        = 0;
        LastInstance      = null;
        LastSignal        = null;
        LastCorrelationId = null;
    }
}

// ── Custom Factory ────────────────────────────────────────────────────────────

/// <summary>
/// Extends AlgoTraderWebAppFactory — substitutes a SpreadOrderManagerSpy so routing
/// tests can assert calls without invoking a real broker HTTP client.
/// </summary>
public sealed class SpreadRoutingFactory : AlgoTraderWebAppFactory
{
    public SpreadOrderManagerSpy SpreadManagerSpy { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISpreadOrderManager>();
            services.AddSingleton<ISpreadOrderManager>(SpreadManagerSpy);
        });
    }
}
