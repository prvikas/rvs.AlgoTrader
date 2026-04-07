using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Stateless position sizing engine.  All models apply two hard caps:
///   1. config.MaxLots      — absolute lot ceiling
///   2. config.MaxCapitalPct — position value cannot exceed this % of equity
///
/// Rationale is logged with every result so IRiskManagementService and audit_log
/// have a human-readable explanation alongside every order.
/// </summary>
public sealed class PositionSizingEngine(ILogger<PositionSizingEngine> logger) : IPositionSizingEngine
{
    public (int Lots, string Rationale) Compute(
        SizingModel model,
        decimal equity,
        decimal entryPrice,
        decimal? stopLoss,
        decimal? atr,
        PositionSizingConfig config)
    {
        if (equity <= 0 || entryPrice <= 0)
            return (0, "Zero equity or price — no trade");

        var (rawLots, rationale) = model switch
        {
            SizingModel.FixedLots         => FixedLots(config),
            SizingModel.FixedFractional   => FixedFractional(equity, entryPrice, stopLoss, config),
            SizingModel.AtrBased          => AtrBased(equity, entryPrice, atr, config),
            SizingModel.KellyCriterion    => KellyCriterion(equity, entryPrice, config),
            SizingModel.VolatilityTargeting => VolatilityTargeting(equity, entryPrice, atr, config),
            _ => (config.FixedLots, "Default: fixed lots")
        };

        // Hard caps
        int cappedByLots     = Math.Min(rawLots, config.MaxLots);
        var maxByCapital     = (int)Math.Floor(equity * config.MaxCapitalPct / 100m / entryPrice);
        int final            = Math.Max(1, Math.Min(cappedByLots, maxByCapital));
        string capNote       = final < rawLots ? $" → capped to {final} (MaxLots={config.MaxLots}, MaxCapital={config.MaxCapitalPct}%)" : "";

        logger.LogDebug("[Sizing] {Model}: {Rationale}{Cap}", model, rationale, capNote);
        return (final, rationale + capNote);
    }

    // ── Model implementations ────────────────────────────────────────────────

    private static (int, string) FixedLots(PositionSizingConfig c)
        => (c.FixedLots, $"FixedLots={c.FixedLots}");

    private static (int, string) FixedFractional(
        decimal equity, decimal price, decimal? stopLoss, PositionSizingConfig c)
    {
        if (stopLoss is null or 0 || price == stopLoss)
            return (c.FixedLots, "FixedFractional: no stop → fallback to FixedLots");

        decimal riskAmount = equity * c.RiskPerTradePct / 100m;
        decimal riskPerLot = Math.Abs(price - stopLoss.Value);
        int lots = (int)Math.Floor(riskAmount / riskPerLot);
        return (Math.Max(1, lots),
            $"FixedFractional: risk={c.RiskPerTradePct}% of ₹{equity:N0}=₹{riskAmount:N0}, R/lot=₹{riskPerLot:N2}");
    }

    private static (int, string) AtrBased(
        decimal equity, decimal price, decimal? atr, PositionSizingConfig c)
    {
        if (atr is null or 0)
            return (c.FixedLots, "AtrBased: ATR unavailable → fallback to FixedLots");

        decimal riskAmount = equity * c.RiskPerTradePct / 100m;
        decimal riskPerLot = atr.Value * c.AtrMultiplier;  // ATR × multiplier = stop distance per lot
        int lots = (int)Math.Floor(riskAmount / riskPerLot);
        return (Math.Max(1, lots),
            $"AtrBased: risk=₹{riskAmount:N0}, ATR=₹{atr:N2}, mult={c.AtrMultiplier}");
    }

    private static (int, string) KellyCriterion(
        decimal equity, decimal price, PositionSizingConfig c)
    {
        // Half-Kelly: f* = 0.5 × (W − (1−W)/R)  where W=winRate, R=avgWin/avgLoss
        // Classic formula: W - (1-W)/R; half-Kelly halves it for drawdown safety.
        decimal w = c.WinRate;
        decimal r = c.AvgWinLossRatio;
        decimal kelly = 0.5m * (w - (1m - w) / r);
        if (kelly <= 0)
            return (1, $"KellyCriterion: negative Kelly ({kelly:P1}) → minimum 1 lot");

        decimal investAmount = equity * Math.Min(kelly, 0.25m); // safety cap at 25% per trade
        int lots = (int)Math.Floor(investAmount / price);
        return (Math.Max(1, lots),
            $"KellyCriterion: f*={kelly:P1}, invest=₹{investAmount:N0}");
    }

    private static (int, string) VolatilityTargeting(
        decimal equity, decimal price, decimal? atr, PositionSizingConfig c)
    {
        if (atr is null or 0 || price <= 0)
            return (c.FixedLots, "VolTargeting: ATR unavailable → fallback to FixedLots");

        // Daily vol proxy: ATR / price  (annualise × √252)
        double dailyVol      = (double)(atr.Value / price);
        double annualisedVol = dailyVol * Math.Sqrt(252);
        double targetVol     = (double)(c.TargetVolPct / 100m);
        double scaling       = targetVol / Math.Max(annualisedVol, 0.001);
        decimal targetAmount = equity * (decimal)scaling;
        int lots = (int)Math.Floor(targetAmount / price);
        return (Math.Max(1, lots),
            $"VolTargeting: annVol={annualisedVol:P1}, target={c.TargetVolPct}%, scale={scaling:F2}");
    }
}
