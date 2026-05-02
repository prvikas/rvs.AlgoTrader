using NodaTime;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Tests.Unit.Strategies.ShortPremiumVelocity;

/// <summary>
/// Shared factory helpers for Short Premium Velocity unit tests.
/// Keeps test files concise and ensures consistency.
/// </summary>
internal static class SpvTestHelpers
{
    private static readonly DateTimeZone Ist =
        DateTimeZone.ForOffset(Offset.FromHoursAndMinutes(5, 30));

    // ── Candle factory ────────────────────────────────────────────────────────

    public static ClosedCandle MakeCandle(
        decimal close,
        int     dayOffset = 0,
        string  symbol    = "NIFTY 50",
        string  timeframe = "1d")
    {
        var date  = new LocalDate(2024, 6, 3).PlusDays(dayOffset);
        var open  = new LocalDateTime(date.Year, date.Month, date.Day, 9, 15, 0).InZoneLeniently(Ist);
        var closeTs = open.Plus(Duration.FromHours(6.5));
        return new ClosedCandle(symbol, timeframe, open, closeTs,
            close, close * 1.005m, close * 0.995m, close, 1_000_000);
    }

    // ── Regime factories ──────────────────────────────────────────────────────

    public static VelocityRegimeState MakeRegime(
        MarketRegime label          = MarketRegime.VelocityChoppyMeanReversion,
        decimal      regimeStability = 80m,
        decimal      tailRiskScore  = 20m,
        bool         isResultsSeason = false)
        => new(label, regimeStability, tailRiskScore,
               VolOfVol: 1m, IsResultsSeason: isResultsSeason,
               ClassifiedAt: Instant.FromUtc(2024, 6, 3, 10, 0, 0),
               ConfigVersion: "test-v1");

    // ── VelocityPosition factory ──────────────────────────────────────────────

    public static VelocityPosition MakePosition(
        decimal       entryPremium   = 150m,
        decimal       currentPremium = 100m,
        decimal       gammaPerTheta  = 0.8m,
        int           dte            = 18,
        LegType       legType        = LegType.ShortPremium,
        StructureType structureType  = StructureType.IronCondor,
        HedgeType?    hedgeType      = null,
        Guid?         linkedShortLegId = null)
        => new(
            PositionId:      Guid.NewGuid(),
            UnderlyingSymbol: "NIFTY 50",
            StructureType:   structureType,
            LegType:         legType,
            HedgeType:       hedgeType,
            EntryPremium:    entryPremium,
            CurrentPremium:  currentPremium,
            Delta:           -0.10m,
            Gamma:           0.002m,
            Theta:           -0.0025m,
            Vega:            -0.30m,
            GammaPerTheta:   gammaPerTheta,
            Dte:             dte,
            LinkedShortLegId: linkedShortLegId,
            EnteredAt:       Instant.FromUtc(2024, 5, 20, 9, 30, 0));

    // ── Strategy context factory ──────────────────────────────────────────────

    public static StrategyContext MakeCtx(string symbol = "NIFTY 50", int barCount = 1)
    {
        var candles = Enumerable.Range(0, barCount)
                                .Select(i => MakeCandle(22_000m, i, symbol))
                                .ToList();
        return new StrategyContext(Guid.NewGuid(), symbol, "1d", candles, "{}", "spv-test");
    }

    // ── VelocityScoreResult factory ───────────────────────────────────────────

    public static VelocityScoreResult MakeScore(decimal vs = 70m, decimal od = 70m)
        => new(VelocityScore: vs, OpportunityDensity: od,
               AggressionMultiplier: 1.0m, HedgeCoverageRatio: 0.20m, StructureHint: "test");

    // ── VelocityPortfolioGreeks factory ──────────────────────────────────────

    public static VelocityPortfolioGreeks MakeGreeks(
        decimal netDelta         = 5m,
        decimal netVega          = -200m,
        decimal grossGamma       = 0.05m,
        decimal gammaHedged      = 0.02m,
        decimal netTheta         = -300m,
        decimal hedgeCoverage    = 0.40m,
        decimal totalHedgeCost   = -5_000m)
        => new(netDelta, grossGamma, gammaHedged, netTheta, netVega, hedgeCoverage, totalHedgeCost);
}
