using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Interfaces;
using IClock = rvs.AlgoTrader.Domain.Interfaces.IClock;

namespace rvs.AlgoTrader.Strategies.ShortPremiumVelocity;

/// <summary>
/// Compounds net session P&amp;L back into the SPV capital pool after fees, slippage, and hedge costs.
///
/// Net = grossPnl − fees − slippage − hedgeNetCost.
/// FrictionTotal = fees + slippage + hedgeNetCost.
/// FrictionLeakage = FrictionTotal / allocatedCapital.
///
/// Reinvested = Net × (1 − FrictionBufferPercent).
/// Friction leakage alert: when FrictionLeakage > FrictionLeakageAlertPercent → WARNING log.
///
/// Capital gate: allocatedCapital after reinvestment must not exceed
///   MaxVelocityLayerPctOfAccount × totalAccountNAV.
/// If it would, the excess is silently not reinvested (held as cash).
///
/// All results logged to in-memory VelocityReinvestmentLog list (persisted in Phase 3).
/// </summary>
public sealed class ReinvestmentEngine(
    ICapitalAllocator              capital,
    IClock                         clock,
    ILogger<ReinvestmentEngine>    log)
    : IReinvestmentEngine
{
    // In-memory log — persisted to DB as velocity_reinvestment_log in Phase 3
    private readonly List<VelocityReinvestmentLog> _log = [];

    // Running total of capital allocated to the SPV layer (proxy — Phase 3 reads from DB)
    private decimal _allocatedCapital = 0m;

    public async Task ProcessSessionCloseAsync(
        decimal                    grossPnl,
        decimal                    fees,
        decimal                    slippage,
        decimal                    hedgeNetCost,
        ShortPremiumVelocityConfig config,
        CancellationToken          ct)
    {
        var now = clock.NowInstant();

        decimal frictionTotal  = fees + slippage + hedgeNetCost;
        decimal netPnl         = grossPnl - frictionTotal;

        // Friction leakage alert
        decimal allocatedSafe  = _allocatedCapital > 0m ? _allocatedCapital : 1m;
        decimal leakagePct     = frictionTotal / allocatedSafe;

        if (leakagePct > config.FrictionLeakageAlertPercent)
        {
            log.LogWarning(
                "ReinvestmentEngine: Friction leakage={L:P2} > alert={A:P2} " +
                "(fees={F:F0} slip={S:F0} hedgeCost={H:F0} allocated={Cap:F0})",
                leakagePct, config.FrictionLeakageAlertPercent,
                fees, slippage, hedgeNetCost, _allocatedCapital);
        }

        // Apply friction buffer — retain a cash cushion
        decimal toReinvest = netPnl * (1m - config.FrictionBufferPercent);

        // Available capital from the allocator (proxy: use strategyInstanceId = Guid.Empty)
        decimal available = await capital.GetAvailableAsync(Guid.Empty, ct);

        // Total account NAV proxy: available + allocated
        decimal totalNav   = available + _allocatedCapital;
        decimal navCeiling = totalNav * config.MaxVelocityLayerPctOfAccount;

        decimal afterReinvest     = _allocatedCapital + toReinvest;
        decimal effectiveReinvest = toReinvest;

        if (afterReinvest > navCeiling)
        {
            effectiveReinvest = Math.Max(0m, navCeiling - _allocatedCapital);
            log.LogInformation(
                "ReinvestmentEngine: capped by MaxVelocityLayerPctOfAccount — " +
                "toReinvest={R:F0} → capped={C:F0} (nav={NAV:F0} ceiling={Ceil:F0})",
                toReinvest, effectiveReinvest, totalNav, navCeiling);
        }

        if (effectiveReinvest > 0m)
        {
            _allocatedCapital += effectiveReinvest;
            log.LogInformation(
                "ReinvestmentEngine: session close — gross={G:F0} net={N:F0} " +
                "reinvest={R:F0} allocatedCapital={Cap:F0}",
                grossPnl, netPnl, effectiveReinvest, _allocatedCapital);
        }
        else if (effectiveReinvest < 0m)
        {
            // Loss session: reduce allocated capital (but floor at 0)
            _allocatedCapital = Math.Max(0m, _allocatedCapital + effectiveReinvest);
            log.LogInformation(
                "ReinvestmentEngine: session loss — net={N:F0} allocatedCapital={Cap:F0}",
                netPnl, _allocatedCapital);
        }

        // Append to in-memory log
        _log.Add(new VelocityReinvestmentLog(
            OccurredAt:        now,
            GrossPnl:          grossPnl,
            Fees:              fees,
            Slippage:          slippage,
            HedgeNetCost:      hedgeNetCost,
            NetPnl:            netPnl,
            FrictionTotal:     frictionTotal,
            FrictionLeakagePct: leakagePct,
            AmountReinvested:  effectiveReinvest,
            AllocatedCapitalAfter: _allocatedCapital));

        await Task.CompletedTask; // Phase 3: persist to velocity_reinvestment_log table
    }

    /// <summary>Snapshot of all reinvestment events this process lifetime.</summary>
    public IReadOnlyList<VelocityReinvestmentLog> Log => _log.AsReadOnly();
}

/// <summary>
/// Immutable record of one session's reinvestment computation.
/// Persisted to the velocity_reinvestment_log table in Phase 3.
/// </summary>
public record VelocityReinvestmentLog(
    NodaTime.Instant OccurredAt,
    decimal GrossPnl,
    decimal Fees,
    decimal Slippage,
    decimal HedgeNetCost,
    decimal NetPnl,
    decimal FrictionTotal,
    decimal FrictionLeakagePct,
    decimal AmountReinvested,
    decimal AllocatedCapitalAfter);
