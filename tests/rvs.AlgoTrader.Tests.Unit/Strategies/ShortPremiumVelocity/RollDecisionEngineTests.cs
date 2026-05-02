using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.ValueObjects;
using rvs.AlgoTrader.Strategies.ShortPremiumVelocity;
using IClock = rvs.AlgoTrader.Domain.Interfaces.IClock;

namespace rvs.AlgoTrader.Tests.Unit.Strategies.ShortPremiumVelocity;

/// <summary>
/// Unit tests for RollDecisionEngine.EvaluateAsync.
///
/// Key rules:
///   - DTE ≤ 1 AND within GammaPinExitMinutesBeforeClose → Close (hard exit)
///   - Profit target met (gain ≥ ProfitTargetFraction × entry) → Close
///   - Stop-loss: |unrealised loss| ≥ StopLossMultiplier × max-loss → Close
///   - Panic or HighVol → NEVER Roll (Close or Hold only)
///   - Default → Hold
/// </summary>
public class RollDecisionEngineTests
{
    private static readonly ShortPremiumVelocityConfig DefaultConfig = new();

    private static RollDecisionEngine BuildSut(Instant? at = null)
    {
        // Default: market is mid-session (10:00 IST) — far from the 15:30 close
        var nowIst  = at ?? Instant.FromUtc(2024, 6, 3, 4, 30, 0); // 10:00 IST
        var clock   = new Mock<IClock>();
        clock.Setup(c => c.NowInstant()).Returns(nowIst);

        return new RollDecisionEngine(
            new Mock<IPublisher>().Object,
            clock.Object,
            NullLogger<RollDecisionEngine>.Instance);
    }

    // ── Near-market-close helper ──────────────────────────────────────────────

    /// <summary>Returns an Instant that is within GammaPinExitMinutesBeforeClose of 15:30 IST.</summary>
    private static Instant NearCloseInstant()
    {
        // 15:25 IST = UTC 09:55 → within 5 minutes of close; GammaPinExitMinutesBeforeClose default = 5
        return Instant.FromUtc(2024, 6, 3, 9, 55, 0);
    }

    // ── Gamma-pin hard exit ───────────────────────────────────────────────────

    [Fact]
    public async Task GammaPinHardExit_WhenDte0AndNearClose()
    {
        var sut      = BuildSut(at: NearCloseInstant());
        var position = SpvTestHelpers.MakePosition(dte: 0, gammaPerTheta: 3.0m);
        var regime   = SpvTestHelpers.MakeRegime(MarketRegime.VelocityChoppyMeanReversion);

        var result = await sut.EvaluateAsync(position, regime, SpvTestHelpers.MakeCtx(),
            DefaultConfig, CancellationToken.None);

        result.Action.Should().Be(RollAction.Close,
            because: "0-DTE near market close triggers non-negotiable gamma-pin hard exit");
        result.Reason.ToLowerInvariant().Should().Contain("gamma");
    }

    [Fact]
    public async Task GammaPinHardExit_WhenDte1AndNearClose()
    {
        var sut      = BuildSut(at: NearCloseInstant());
        var position = SpvTestHelpers.MakePosition(dte: 1, gammaPerTheta: 2.5m);
        var regime   = SpvTestHelpers.MakeRegime(MarketRegime.VelocityChoppyMeanReversion);

        var result = await sut.EvaluateAsync(position, regime, SpvTestHelpers.MakeCtx(),
            DefaultConfig, CancellationToken.None);

        result.Action.Should().Be(RollAction.Close);
    }

    [Fact]
    public async Task GammaPinHardExit_NotTriggered_WhenFarFromClose()
    {
        // 10:00 IST — far from 15:30 close, DTE = 0 should NOT trigger hard exit
        var sut      = BuildSut(at: Instant.FromUtc(2024, 6, 3, 4, 30, 0)); // 10:00 IST
        var position = SpvTestHelpers.MakePosition(dte: 0, gammaPerTheta: 0.5m);
        var regime   = SpvTestHelpers.MakeRegime(MarketRegime.VelocityChoppyMeanReversion);

        var result = await sut.EvaluateAsync(position, regime, SpvTestHelpers.MakeCtx(),
            DefaultConfig, CancellationToken.None);

        // Should NOT be a gamma-pin exit when far from close
        result.Action.Should().NotBe(RollAction.Close,
            because: "Gamma-pin hard exit only triggers within GammaPinExitMinutesBeforeClose minutes of close");
    }

    // ── Profit target ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ProfitTarget_Met_ReturnsClose()
    {
        // ChoppyMeanReversion profit target = 60%; entry=150, current=55 → gain=63%
        var sut      = BuildSut();
        var position = SpvTestHelpers.MakePosition(
            entryPremium:   150m,
            currentPremium: 55m,   // gain = (150-55)/150 = 63.3% ≥ 60%
            gammaPerTheta:  0.8m,
            dte:            12);
        var regime = SpvTestHelpers.MakeRegime(MarketRegime.VelocityChoppyMeanReversion);

        var result = await sut.EvaluateAsync(position, regime, SpvTestHelpers.MakeCtx(),
            DefaultConfig, CancellationToken.None);

        result.Action.Should().Be(RollAction.Close,
            because: "Realised gain 63% ≥ ProfitTargetFraction 60% (Choppy) → Close");
        result.Reason.ToLowerInvariant().Should().Contain("profit");
    }

    [Fact]
    public async Task ProfitTarget_NotMet_DoesNotClose()
    {
        // gain = (150-120)/150 = 20% < 60%
        var sut      = BuildSut();
        var position = SpvTestHelpers.MakePosition(
            entryPremium: 150m, currentPremium: 120m, gammaPerTheta: 0.8m, dte: 12);
        var regime = SpvTestHelpers.MakeRegime(MarketRegime.VelocityChoppyMeanReversion);

        var result = await sut.EvaluateAsync(position, regime, SpvTestHelpers.MakeCtx(),
            DefaultConfig, CancellationToken.None);

        result.Action.Should().NotBe(RollAction.Close,
            because: "20% gain < 60% profit target; no profit-target close");
    }

    // ── Panic/HighVol: no rolling ─────────────────────────────────────────────

    [Theory]
    [InlineData(MarketRegime.VelocityPanic)]
    [InlineData(MarketRegime.VelocityHighVolExpansion)]
    public async Task PanicOrHighVol_NeverRolls(MarketRegime label)
    {
        var sut      = BuildSut();
        var position = SpvTestHelpers.MakePosition(
            entryPremium: 150m, currentPremium: 120m, gammaPerTheta: 2.5m, dte: 5);
        var regime = SpvTestHelpers.MakeRegime(label);

        var result = await sut.EvaluateAsync(position, regime, SpvTestHelpers.MakeCtx(),
            DefaultConfig, CancellationToken.None);

        result.Action.Should().NotBe(RollAction.Roll,
            because: $"Rolling is prohibited in {label}; only Close or Hold allowed");
    }

    // ── Default: Hold ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Default_ReturnsHold()
    {
        // Normal conditions, profit target not met, no gamma pin
        var sut      = BuildSut();
        var position = SpvTestHelpers.MakePosition(
            entryPremium: 150m, currentPremium: 110m, gammaPerTheta: 0.8m, dte: 15);
        var regime = SpvTestHelpers.MakeRegime(MarketRegime.VelocityChoppyMeanReversion);

        var result = await sut.EvaluateAsync(position, regime, SpvTestHelpers.MakeCtx(),
            DefaultConfig, CancellationToken.None);

        result.Action.Should().Be(RollAction.Hold,
            because: "No trigger conditions met → Hold is the default action");
    }
}
