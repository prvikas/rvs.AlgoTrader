using System.Net.Http.Json;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Events;
using rvs.AlgoTrader.Domain.ValueObjects;
using rvs.AlgoTrader.Infrastructure.Persistence;
using rvs.AlgoTrader.IntegrationTests.Infrastructure;
using DomainClock = rvs.AlgoTrader.Domain.Interfaces.IClock;

namespace rvs.AlgoTrader.IntegrationTests.Tests;

/// <summary>
/// End-to-end integration tests for StrategyEvaluationQueue.
///
/// Covers:
///   - CandleClosedEvent → consumer fires → SignalJournalEntry written to DB
///   - Consumer silently skips when cache has fewer than 2 bars (guard in queue)
///   - Consumer skips when no Live/Forward instances match symbol+timeframe
///   - Forward-mode: SignalGenerated NOT published (SEQ-2 rule)
///   - Kill switch active: SignalGenerated NOT published for Live mode
///
/// Infrastructure: real PostgreSQL + Redis + RabbitMQ via Testcontainers.
/// MassTransit test harness replaces the real bus and runs consumers in-process.
/// </summary>
public sealed class StrategyEvaluationQueueIntegrationTests
    : IClassFixture<AlgoTraderWebAppFactory>, IAsyncLifetime
{
    private readonly AlgoTraderWebAppFactory _factory;
    private ITestHarness _harness = null!;
    private IServiceScope _scope = null!;

    public StrategyEvaluationQueueIntegrationTests(AlgoTraderWebAppFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _scope   = _factory.Services.CreateScope();
        _harness = _scope.ServiceProvider.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async Task DisposeAsync()
    {
        await _harness.Stop();
        _scope.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ClosedCandle MakeCandle(string symbol, string tf, int offsetMinutes = 0)
    {
        var tz  = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];
        var now = SystemClock.Instance.GetCurrentInstant()
            .Minus(Duration.FromMinutes(offsetMinutes))
            .InZone(tz);
        return new ClosedCandle(
            symbol, tf,
            OpenTime:  now,
            CloseTime: now.Plus(Duration.FromMinutes(1)),
            Open: 22_400m, High: 22_500m, Low: 22_350m, Close: 22_480m, Volume: 1_000_000L);
    }

    private CandleClosedEvent MakeEvent(string symbol, string tf)
    {
        var tz = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];
        return new CandleClosedEvent(
            symbol, tf,
            MakeCandle(symbol, tf),
            CorrelationId: Guid.NewGuid().ToString(),
            OccurredAt: SystemClock.Instance.GetCurrentInstant().InZone(tz));
    }

    private async Task<StrategyInstance> SeedRunningInstanceAsync(
        string symbol, string timeframe,
        StrategyMode mode = StrategyMode.Live,
        string strategyType = "PriceActionBreakout")
    {
        await using var s  = _factory.Services.CreateAsyncScope();
        var db             = s.ServiceProvider.GetRequiredService<AlgoTraderDbContext>();
        var clock          = s.ServiceProvider.GetRequiredService<DomainClock>();
        var now            = clock.NowInstant();

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

        var instance = StrategyInstance.Create(
            name:           $"IT-{Guid.NewGuid():N}",
            strategyType:   strategyType,
            watchlistId:    null,
            mode:           mode,
            brokerAccountId: brokerAccount.Id,
            createdBy:      "integration-test",
            createdAt:      now,
            internalSymbol: symbol,
            timeframe:      timeframe,
            parametersJson: "{}");

        instance.Status   = StrategyStatus.Running;
        instance.IsActive = true;

        db.Set<StrategyInstance>().Add(instance);
        await db.SaveChangesAsync();
        return instance;
    }

    private async Task WarmCacheAsync(string symbol, string tf, int bars = 5)
    {
        await using var s = _factory.Services.CreateAsyncScope();
        var cache         = s.ServiceProvider.GetRequiredService<ICandleCache>();
        var candles       = Enumerable.Range(1, bars)
            .Select(i => MakeCandle(symbol, tf, offsetMinutes: (bars - i + 1) * 5))
            .ToList();
        await cache.WarmAsync(symbol, tf, candles, CancellationToken.None);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Consume_WithRunningLiveInstance_WritesSignalJournalEntry()
    {
        // Arrange
        const string symbol = "NSE:NIFTY50-SEQ-T1";
        const string tf     = "1d";

        var instance = await SeedRunningInstanceAsync(symbol, tf, StrategyMode.Live);
        await WarmCacheAsync(symbol, tf, bars: 5);

        var evt = MakeEvent(symbol, tf);

        // Act — publish via test harness bus
        await _harness.Bus.Publish(evt);

        // Wait for consumer to finish (MassTransit 8 harness API)
        var consumed = await _harness.Consumed.Any<CandleClosedEvent>(
            x => x.Context.Message.InternalSymbol == symbol);

        // Assert — consumed without faulting
        consumed.Should().BeTrue("StrategyEvaluationQueue should consume the CandleClosedEvent");

        // Allow async journal write to flush
        await Task.Delay(800);

        await using var assert = _factory.Services.CreateAsyncScope();
        var repo    = assert.ServiceProvider.GetRequiredService<ISignalJournalRepository>();
        var entries = await repo.GetFilteredAsync(
            instance.Id, symbol, null, null, 1, 20, CancellationToken.None);

        entries.Should().NotBeEmpty(
            "at least one signal journal entry should be written for the evaluated Live instance");
    }

    [Fact]
    public async Task Consume_WhenCacheEmpty_SkipsWithoutFault()
    {
        // Arrange — no candle cache warmed → queue's < 2 bar guard triggers
        const string symbol = "NSE:RELIANCE-SEQ-T2";
        const string tf     = "1d";

        await SeedRunningInstanceAsync(symbol, tf, StrategyMode.Live);
        // Deliberately no WarmCacheAsync

        var evt = MakeEvent(symbol, tf);

        // Act
        await _harness.Bus.Publish(evt);
        await _harness.Consumed.Any<CandleClosedEvent>(
            x => x.Context.Message.InternalSymbol == symbol);

        // Assert — no faults published
        var faults = _harness.Published
            .Select<MassTransit.Fault<CandleClosedEvent>>()
            .Where(f => f.Context.Message.Message.InternalSymbol == symbol)
            .ToList();

        faults.Should().BeEmpty("insufficient candle history must be silently skipped, not faulted");
    }

    [Fact]
    public async Task Consume_WhenNoMatchingInstances_CompletesCleanly()
    {
        // Arrange — no running instances for this symbol
        const string symbol = "NSE:NOVARTIS-SEQ-T3";
        const string tf     = "1d";

        var evt = MakeEvent(symbol, tf);

        // Act
        await _harness.Bus.Publish(evt);
        var consumed = await _harness.Consumed.Any<CandleClosedEvent>(
            x => x.Context.Message.InternalSymbol == symbol);

        // Assert
        consumed.Should().BeTrue("event should be consumed even with no matching instances");

        var faults = _harness.Published
            .Select<MassTransit.Fault<CandleClosedEvent>>()
            .Where(f => f.Context.Message.Message.InternalSymbol == symbol)
            .ToList();

        faults.Should().BeEmpty();
    }

    [Fact]
    public async Task Consume_ForwardModeInstance_DoesNotPublishSignalGeneratedEvent()
    {
        // SEQ-2: ForwardTestEngine processes the candle; the queue journals but
        // must NOT publish SignalGenerated (that path is Live-only).
        const string symbol = "NSE:NIFTY50-SEQ-T4";
        const string tf     = "1d";

        var instance = await SeedRunningInstanceAsync(symbol, tf, StrategyMode.Forward);
        await WarmCacheAsync(symbol, tf, bars: 5);

        var evt = MakeEvent(symbol, tf);

        // Act
        await _harness.Bus.Publish(evt);
        await _harness.Consumed.Any<CandleClosedEvent>(
            x => x.Context.Message.InternalSymbol == symbol);

        await Task.Delay(800);

        // Assert — journal written
        await using var assert = _factory.Services.CreateAsyncScope();
        var repo    = assert.ServiceProvider.GetRequiredService<ISignalJournalRepository>();
        var entries = await repo.GetFilteredAsync(
            instance.Id, symbol, null, null, 1, 20, CancellationToken.None);

        entries.Should().NotBeEmpty("signal should be journaled for Forward mode instances");

        // No SignalGenerated bus event for Forward mode (SEQ-2 rule)
        var signalEvents = _harness.Published
            .Select<rvs.AlgoTrader.Domain.Events.SignalGenerated>()
            .Where(e => e.Context.Message.StrategyInstanceId == instance.Id)
            .ToList();

        signalEvents.Should().BeEmpty(
            "SignalGenerated must NOT be published for Forward-mode instances (SEQ-2)");
    }

    [Fact]
    public async Task Consume_WhenKillSwitchActive_SuppressesSignalGeneratedForLiveInstance()
    {
        // Arrange
        const string symbol = "NSE:BANKNIFTY-SEQ-T5";
        const string tf     = "1d";

        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/killswitch/activate", new
        {
            Actor = "seq-int-test",
            Reason = "Integration test kill-switch suppression",
            CorrelationId = Guid.NewGuid().ToString()
        });

        var instance = await SeedRunningInstanceAsync(symbol, tf, StrategyMode.Live);
        await WarmCacheAsync(symbol, tf, bars: 5);

        var evt = MakeEvent(symbol, tf);

        try
        {
            // Act
            await _harness.Bus.Publish(evt);
            await _harness.Consumed.Any<CandleClosedEvent>(
                x => x.Context.Message.InternalSymbol == symbol);

            await Task.Delay(400);

            // Assert — kill switch blocks LiveExecutionEngine, so no SignalGenerated
            var signalEvents = _harness.Published
                .Select<rvs.AlgoTrader.Domain.Events.SignalGenerated>()
                .Where(e => e.Context.Message.StrategyInstanceId == instance.Id)
                .ToList();

            signalEvents.Should().BeEmpty(
                "LiveExecutionEngine must not publish SignalGenerated when kill switch is active");
        }
        finally
        {
            await client.PostAsJsonAsync("/api/killswitch/deactivate", new
            {
                Actor = "seq-int-test",
                CorrelationId = Guid.NewGuid().ToString()
            });
        }
    }
}
