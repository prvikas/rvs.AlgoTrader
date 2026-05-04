using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Strategies.ShortPremiumVelocity;
using IClock = rvs.AlgoTrader.Domain.Interfaces.IClock;

namespace rvs.AlgoTrader.Tests.Unit.Strategies.ShortPremiumVelocity;

/// <summary>
/// Unit tests for ReinvestmentEngine.ProcessSessionCloseAsync.
///
/// Net    = grossPnl − fees − slippage − hedgeNetCost
/// Reinvest = Net × (1 − FrictionBufferPercent)
/// Capped when: allocatedCapital + toReinvest > MaxVelocityLayerPctOfAccount × totalNav
/// FrictionLeakage alert: (fees + slip + hedgeCost) / allocatedCapital > FrictionLeakageAlertPercent
/// </summary>
public class ReinvestmentEngineTests
{
    private static readonly ShortPremiumVelocityConfig DefaultConfig = new();
    private static readonly Guid TestInstanceId = Guid.NewGuid();

    private static ReinvestmentEngine BuildSut(decimal available = 5_000_000m)
    {
        var capital = new Mock<ICapitalAllocator>();
        capital.Setup(c => c.GetAvailableAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(available);

        var clock = new Mock<IClock>();
        clock.Setup(c => c.NowInstant()).Returns(Instant.FromUtc(2024, 6, 3, 16, 0, 0));

        // Empty configuration — ConnectionString will be null, PersistAsync will skip DB and log a warning
        IConfiguration configuration = new ConfigurationBuilder().Build();

        return new ReinvestmentEngine(
            capital.Object, clock.Object, configuration, NullLogger<ReinvestmentEngine>.Instance);
    }

    // ── Log entry created on every session close ──────────────────────────────

    [Fact]
    public async Task SessionClose_AppendsToLog()
    {
        var sut = BuildSut();

        await sut.ProcessSessionCloseAsync(
            strategyInstanceId: TestInstanceId,
            grossPnl:     10_000m,
            fees:         200m,
            slippage:     100m,
            hedgeNetCost: 50m,
            DefaultConfig,
            CancellationToken.None);

        sut.Log.Should().HaveCount(1, because: "every session close must produce exactly one log entry");
    }

    // ── Net P&L is correctly computed ─────────────────────────────────────────

    [Fact]
    public async Task NetPnl_IsGrossMinus_AllFriction()
    {
        var sut = BuildSut();

        await sut.ProcessSessionCloseAsync(TestInstanceId, 10_000m, 200m, 100m, 50m, DefaultConfig, CancellationToken.None);

        var entry = sut.Log[0];
        entry.NetPnl.Should().Be(10_000m - 200m - 100m - 50m,
            because: "NetPnl = grossPnl − fees − slippage − hedgeNetCost");
    }

    [Fact]
    public async Task FrictionTotal_IsSumOfAllCosts()
    {
        var sut = BuildSut();

        await sut.ProcessSessionCloseAsync(TestInstanceId, 10_000m, 200m, 100m, 50m, DefaultConfig, CancellationToken.None);

        sut.Log[0].FrictionTotal.Should().Be(200m + 100m + 50m);
    }

    // ── Loss session reduces allocated capital ─────────────────────────────────

    [Fact]
    public async Task LossSession_ReducesAllocatedCapital()
    {
        var sut = BuildSut();

        // First: record a profit to set allocatedCapital > 0
        await sut.ProcessSessionCloseAsync(TestInstanceId, 50_000m, 0m, 0m, 0m, DefaultConfig, CancellationToken.None);
        decimal capitalAfterGain = sut.Log[0].AllocatedCapitalAfter;

        // Then: record a loss
        await sut.ProcessSessionCloseAsync(TestInstanceId, -20_000m, 0m, 0m, 0m, DefaultConfig, CancellationToken.None);
        decimal capitalAfterLoss = sut.Log[1].AllocatedCapitalAfter;

        capitalAfterLoss.Should().BeLessThan(capitalAfterGain,
            because: "a loss session must reduce the allocated capital");
    }

    // ── Allocated capital floored at zero on loss ─────────────────────────────

    [Fact]
    public async Task LossSession_AllocatedCapital_FlooredAtZero()
    {
        var sut = BuildSut();

        // Huge loss with zero starting capital
        await sut.ProcessSessionCloseAsync(TestInstanceId, -1_000_000m, 0m, 0m, 0m, DefaultConfig, CancellationToken.None);

        sut.Log[0].AllocatedCapitalAfter.Should().BeGreaterThanOrEqualTo(0m,
            because: "allocated capital can never go below zero");
    }

    // ── NAV ceiling cap prevents over-allocation ──────────────────────────────

    [Fact]
    public async Task NavCeiling_Caps_Reinvestment()
    {
        // Available capital = 100k. MaxVelocityLayerPctOfAccount = config value.
        // If we already have large gains, reinvestment should be capped.
        var sut = BuildSut(available: 100_000m);

        // Compound 10 large-gain sessions to push close to the NAV ceiling
        for (int i = 0; i < 10; i++)
        {
            await sut.ProcessSessionCloseAsync(TestInstanceId, 200_000m, 0m, 0m, 0m, DefaultConfig, CancellationToken.None);
        }

        // The last few should have hit the ceiling (effectiveReinvest capped or zero)
        var lastEntry = sut.Log[^1];
        decimal navCeiling = DefaultConfig.MaxVelocityLayerPctOfAccount *
                             (100_000m + lastEntry.AllocatedCapitalAfter);

        lastEntry.AllocatedCapitalAfter.Should().BeLessThanOrEqualTo(navCeiling + 1m,
            because: "allocated capital must not exceed MaxVelocityLayerPctOfAccount × totalNav");
    }

    // ── Friction buffer reduces reinvestment amount ────────────────────────────

    [Fact]
    public async Task FrictionBuffer_ReducesReinvestedAmount()
    {
        var sut = BuildSut();
        decimal grossPnl = 10_000m;

        await sut.ProcessSessionCloseAsync(TestInstanceId, grossPnl, 0m, 0m, 0m, DefaultConfig, CancellationToken.None);

        decimal expected = grossPnl * (1m - DefaultConfig.FrictionBufferPercent);
        sut.Log[0].AmountReinvested.Should().BeLessThanOrEqualTo(expected + 0.01m,
            because: "FrictionBufferPercent is retained as a cash cushion, not reinvested");
    }

    // ── Log is immutable (ReadOnly) ───────────────────────────────────────────

    [Fact]
    public void Log_ExposedAsReadOnly()
    {
        var sut = BuildSut();

        sut.Log.Should().BeAssignableTo<IReadOnlyList<VelocityReinvestmentLog>>();
    }
}
