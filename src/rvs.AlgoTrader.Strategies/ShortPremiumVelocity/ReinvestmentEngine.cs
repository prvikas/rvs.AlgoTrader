using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
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
/// All results logged to in-memory VelocityReinvestmentLog list and persisted to DB.
/// </summary>
public sealed class ReinvestmentEngine(
    ICapitalAllocator           capital,
    IClock                      clock,
    IConfiguration              configuration,
    ILogger<ReinvestmentEngine> log)
    : IReinvestmentEngine
{
    private readonly List<VelocityReinvestmentLog> _log = [];
    private decimal _allocatedCapital = 0m;

    private string? ConnectionString =>
        configuration.GetConnectionString("DefaultConnection");

    public async Task ProcessSessionCloseAsync(
        Guid                       strategyInstanceId,
        decimal                    grossPnl,
        decimal                    fees,
        decimal                    slippage,
        decimal                    hedgeNetCost,
        ShortPremiumVelocityConfig config,
        CancellationToken          ct)
    {
        var now = clock.NowInstant();

        decimal frictionTotal = fees + slippage + hedgeNetCost;
        decimal netPnl        = grossPnl - frictionTotal;

        decimal allocatedSafe = _allocatedCapital > 0m ? _allocatedCapital : 1m;
        decimal leakagePct    = frictionTotal / allocatedSafe;

        if (leakagePct > config.FrictionLeakageAlertPercent)
        {
            log.LogWarning(
                "ReinvestmentEngine: Friction leakage={L:P2} > alert={A:P2} " +
                "(fees={F:F0} slip={S:F0} hedgeCost={H:F0} allocated={Cap:F0})",
                leakagePct, config.FrictionLeakageAlertPercent,
                fees, slippage, hedgeNetCost, _allocatedCapital);
        }

        decimal toReinvest = netPnl * (1m - config.FrictionBufferPercent);
        decimal available  = await capital.GetAvailableAsync(strategyInstanceId, ct);
        decimal totalNav   = available + _allocatedCapital;
        decimal navCeiling = totalNav * config.MaxVelocityLayerPctOfAccount;

        decimal afterReinvest     = _allocatedCapital + toReinvest;
        decimal effectiveReinvest = toReinvest;

        if (afterReinvest > navCeiling)
        {
            effectiveReinvest = Math.Max(0m, navCeiling - _allocatedCapital);
            log.LogInformation(
                "ReinvestmentEngine: capped — toReinvest={R:F0} → {C:F0} (ceiling={Ceil:F0})",
                toReinvest, effectiveReinvest, navCeiling);
        }

        if (effectiveReinvest > 0m)
        {
            _allocatedCapital += effectiveReinvest;
            log.LogInformation(
                "ReinvestmentEngine: session close — gross={G:F0} net={N:F0} " +
                "reinvest={R:F0} allocated={Cap:F0}",
                grossPnl, netPnl, effectiveReinvest, _allocatedCapital);
        }
        else if (effectiveReinvest < 0m)
        {
            _allocatedCapital = Math.Max(0m, _allocatedCapital + effectiveReinvest);
            log.LogInformation(
                "ReinvestmentEngine: session loss — net={N:F0} allocated={Cap:F0}",
                netPnl, _allocatedCapital);
        }

        var entry = new VelocityReinvestmentLog(
            StrategyInstanceId:    strategyInstanceId,
            OccurredAt:            now,
            GrossPnl:              grossPnl,
            Fees:                  fees,
            Slippage:              slippage,
            HedgeNetCost:          hedgeNetCost,
            NetPnl:                netPnl,
            FrictionTotal:         frictionTotal,
            FrictionLeakagePct:    leakagePct,
            AmountReinvested:      effectiveReinvest,
            AllocatedCapitalAfter: _allocatedCapital);

        _log.Add(entry);

        await PersistAsync(entry, ct);
    }

    /// <summary>Snapshot of all reinvestment events this process lifetime.</summary>
    public IReadOnlyList<VelocityReinvestmentLog> Log => _log.AsReadOnly();

    // ── DB persistence ────────────────────────────────────────────────────────

    private async Task PersistAsync(VelocityReinvestmentLog e, CancellationToken ct)
    {
        var cs = ConnectionString;
        if (string.IsNullOrEmpty(cs))
        {
            log.LogWarning("ReinvestmentEngine: no DefaultConnection string — skipping DB persist");
            return;
        }

        try
        {
            await using var conn = new NpgsqlConnection(cs);
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO velocity_reinvestment_log
                    (strategy_instance_id, occurred_at, gross_pnl, fees, slippage,
                     hedge_net_cost, net_pnl, friction_total, friction_leakage_pct,
                     amount_reinvested, allocated_capital_after)
                VALUES
                    (@sid, @at, @gross, @fees, @slip,
                     @hedge, @net, @friction, @leakage,
                     @reinvested, @allocated)
                """;

            cmd.Parameters.AddWithValue("sid",       e.StrategyInstanceId);
            cmd.Parameters.AddWithValue("at",         e.OccurredAt.ToDateTimeUtc());
            cmd.Parameters.AddWithValue("gross",      e.GrossPnl);
            cmd.Parameters.AddWithValue("fees",       e.Fees);
            cmd.Parameters.AddWithValue("slip",       e.Slippage);
            cmd.Parameters.AddWithValue("hedge",      e.HedgeNetCost);
            cmd.Parameters.AddWithValue("net",        e.NetPnl);
            cmd.Parameters.AddWithValue("friction",   e.FrictionTotal);
            cmd.Parameters.AddWithValue("leakage",    e.FrictionLeakagePct);
            cmd.Parameters.AddWithValue("reinvested", e.AmountReinvested);
            cmd.Parameters.AddWithValue("allocated",  e.AllocatedCapitalAfter);

            await cmd.ExecuteNonQueryAsync(ct);

            log.LogDebug("ReinvestmentEngine: persisted reinvestment log for instance {Id}", e.StrategyInstanceId);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "ReinvestmentEngine: failed to persist reinvestment log — entry kept in-memory");
            // Do NOT rethrow — DB failure must not crash the strategy
        }
    }
}

/// <summary>
/// Immutable record of one session's reinvestment computation.
/// Persisted to the velocity_reinvestment_log table.
/// </summary>
public record VelocityReinvestmentLog(
    Guid             StrategyInstanceId,
    NodaTime.Instant OccurredAt,
    decimal          GrossPnl,
    decimal          Fees,
    decimal          Slippage,
    decimal          HedgeNetCost,
    decimal          NetPnl,
    decimal          FrictionTotal,
    decimal          FrictionLeakagePct,
    decimal          AmountReinvested,
    decimal          AllocatedCapitalAfter);
