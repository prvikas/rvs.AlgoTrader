using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Commands.Strategy;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;
using System.Collections.Concurrent;
using IClock = rvs.AlgoTrader.Domain.Interfaces.IClock;

namespace rvs.AlgoTrader.Strategies.ShortPremiumVelocity;

/// <summary>
/// Intraday circuit-breaker state machine: Normal → SoftStop → HardStop.
/// Evaluated on every tick via EvaluateAsync(instanceId, dailyLossPct, config, ct).
///
/// Per-instance isolation: each Guid instanceId has its own CB state bucket.
/// Running two forward-test instances simultaneously will not share CB state.
///
/// SoftStop trigger: dailyLossPct ≥ SoftStopLossPct (default 1.5%).
///   Effects: 50% size cap (RecoveryManager), AggressionMultiplier ≤ 1.0 (VelocityIndicator),
///   no new naked-short entries, continue mandatory hedge rolls, no new VegaHedge.
///
/// HardStop trigger: dailyLossPct ≥ HardStopLossPct (default 2.5%)
///   OR MarginManager forced-liquidation risk.
///   Effects: freeze new entries and discretionary rolls; allow risk-reducing exits,
///   forced Trim-to-Fit, mandatory hedge rolls.
///   PUBLISHES ActivateKillSwitchCommand (existing) via MediatR — no duplicate mechanism.
///
/// Reset: next trading day when MarginState.IsFresh AND JumpRisk.IsSoftStop==false
///   AND regime is NOT Panic/HighVolExpansion. Checked via TryResetAsync.
///
/// Persistence: state is written to velocity_circuit_breaker_state on every transition.
///   On first call per instanceId, state is lazily loaded from DB so process restarts
///   do not silently clear a HardStop.
/// </summary>
public sealed class CircuitBreakerService(
    IServiceScopeFactory            scopeFactory,
    IPublisher                      publisher,
    IClock                          clock,
    IJumpRiskMonitor                jumpRisk,
    ILogger<CircuitBreakerService>  log)
    : ICircuitBreakerService
{
    // NSE IST offset
    private static readonly DateTimeZone Ist =
        DateTimeZone.ForOffset(Offset.FromHoursAndMinutes(5, 30));

    // ── Per-instance state ────────────────────────────────────────────────────

    private sealed class CbInstance
    {
        public CircuitBreakerStateValue State           = CircuitBreakerStateValue.Normal;
        public decimal                  DailyLossPercent;
        public Instant?                 TriggeredAt;
        public Instant?                 ResetEligibleAt;
        public bool                     LoadedFromDb;
    }

    private readonly ConcurrentDictionary<Guid, CbInstance> _instances = new();

    private CbInstance GetOrCreate(Guid instanceId)
        => _instances.GetOrAdd(instanceId, _ => new CbInstance());

    // ── ICircuitBreakerService ────────────────────────────────────────────────

    /// <summary>Global (Guid.Empty) state — kept for backward compat with mocks that set up CurrentState.</summary>
    public CircuitBreakerState CurrentState => GetState(Guid.Empty);

    /// <summary>Per-instance state access.</summary>
    public CircuitBreakerState GetState(Guid instanceId)
    {
        var s = GetOrCreate(instanceId);
        return new CircuitBreakerState(s.State, s.DailyLossPercent, s.TriggeredAt, s.ResetEligibleAt);
    }

    public async Task EvaluateAsync(
        Guid                       instanceId,
        decimal                    dailyLossPct,
        ShortPremiumVelocityConfig config,
        CancellationToken          ct)
    {
        var s   = await EnsureLoadedAsync(instanceId, ct);
        var now = clock.NowInstant();
        s.DailyLossPercent = dailyLossPct;

        // ── HardStop trigger ──────────────────────────────────────────────────
        if (dailyLossPct >= config.HardStopLossPct && s.State != CircuitBreakerStateValue.HardStop)
        {
            s.State           = CircuitBreakerStateValue.HardStop;
            s.TriggeredAt     = now;
            s.ResetEligibleAt = NextTradingDayOpen(now);

            log.LogCritical(
                "CircuitBreaker[{I}]: HARD STOP triggered — dailyLoss={Loss:P2} ≥ HardCap={Cap:P2} at {At}",
                instanceId, dailyLossPct, config.HardStopLossPct, now);

            await PersistAsync(instanceId, s, ct);

            await publisher.Publish(
                new ActivateKillSwitchCommand(
                    Actor:         "CircuitBreakerService",
                    Reason:        $"SPV HardStop[{instanceId}]: dailyLoss={dailyLossPct:P2} ≥ {config.HardStopLossPct:P2}",
                    CorrelationId: Guid.NewGuid().ToString()),
                ct);
            return;
        }

        // ── SoftStop trigger ──────────────────────────────────────────────────
        if (dailyLossPct >= config.SoftStopLossPct && s.State == CircuitBreakerStateValue.Normal)
        {
            s.State       = CircuitBreakerStateValue.SoftStop;
            s.TriggeredAt = now;
            s.ResetEligibleAt = NextTradingDayOpen(now);

            log.LogWarning(
                "CircuitBreaker[{I}]: SOFT STOP triggered — dailyLoss={Loss:P2} ≥ SoftCap={Cap:P2} at {At}",
                instanceId, dailyLossPct, config.SoftStopLossPct, now);

            await PersistAsync(instanceId, s, ct);
        }
    }

    public async Task TryResetAsync(
        Guid                instanceId,
        MarginState         marginState,
        VelocityRegimeState regime,
        CancellationToken   ct)
    {
        var s = await EnsureLoadedAsync(instanceId, ct);
        if (s.State == CircuitBreakerStateValue.Normal) return;
        if (!s.ResetEligibleAt.HasValue)               return;

        var now = clock.NowInstant();
        if (now < s.ResetEligibleAt.Value)             return;

        bool jumpRiskClear = !jumpRisk.CurrentState.IsSoftStop;
        bool marginFresh   = marginState.IsFresh;
        bool regimeSafe    = regime.Label is not (MarketRegime.VelocityPanic
                                                or MarketRegime.VelocityHighVolExpansion);

        if (jumpRiskClear && marginFresh && regimeSafe)
        {
            log.LogInformation(
                "CircuitBreaker[{I}]: resetting to Normal from {Prior} — " +
                "marginFresh={MF} jumpRiskClear={JR} regime={R} at {At}",
                instanceId, s.State, marginFresh, jumpRiskClear, regime.Label, now);

            s.State           = CircuitBreakerStateValue.Normal;
            s.TriggeredAt     = null;
            s.ResetEligibleAt = null;

            await PersistAsync(instanceId, s, ct);
        }
        else
        {
            log.LogDebug(
                "CircuitBreaker[{I}]: reset deferred — jumpRiskClear={J} marginFresh={M} regimeSafe={R}",
                instanceId, jumpRiskClear, marginFresh, regimeSafe);
        }
    }

    // ── DB load/persist ───────────────────────────────────────────────────────

    private async Task<CbInstance> EnsureLoadedAsync(Guid instanceId, CancellationToken ct)
    {
        var s = GetOrCreate(instanceId);
        if (s.LoadedFromDb) return s;

        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ISpvStateStore>();

        var row = await store.LoadCbStateAsync(instanceId, ct);
        if (row != null)
        {
            s.State = Enum.TryParse<CircuitBreakerStateValue>(row.State, out var parsed)
                ? parsed : CircuitBreakerStateValue.Normal;
            s.DailyLossPercent = row.DailyLossPercent;
            s.TriggeredAt      = row.TriggeredAt.HasValue
                ? Instant.FromDateTimeUtc(DateTime.SpecifyKind(row.TriggeredAt.Value, DateTimeKind.Utc))
                : null;
            s.ResetEligibleAt  = row.ResetEligibleAt.HasValue
                ? Instant.FromDateTimeUtc(DateTime.SpecifyKind(row.ResetEligibleAt.Value, DateTimeKind.Utc))
                : null;

            log.LogInformation(
                "CircuitBreaker[{I}]: loaded persisted state={State} from DB (dailyLoss={L:P2})",
                instanceId, s.State, s.DailyLossPercent);
        }
        else
        {
            log.LogDebug(
                "CircuitBreaker[{I}]: no persisted state found — starting Normal", instanceId);
        }

        s.LoadedFromDb = true;
        return s;
    }

    private async Task PersistAsync(Guid instanceId, CbInstance s, CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<ISpvStateStore>();
            await store.SaveCbStateAsync(instanceId, new PersistedCbState
            {
                State            = s.State.ToString(),
                DailyLossPercent = s.DailyLossPercent,
                TriggeredAt      = s.TriggeredAt?.ToDateTimeUtc(),
                ResetEligibleAt  = s.ResetEligibleAt?.ToDateTimeUtc(),
                UpdatedAt        = clock.NowInstant().ToDateTimeUtc(),
            }, ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "CircuitBreaker[{I}]: failed to persist state — in-memory state still valid", instanceId);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Instant NextTradingDayOpen(Instant now)
    {
        // Next trading day at 09:15 IST
        var nowInIst = now.InZone(Ist);
        var nextDay  = nowInIst.Date.PlusDays(1);
        // Skip weekends (simple; production uses MarketCalendarService)
        if (nextDay.DayOfWeek == IsoDayOfWeek.Saturday)  nextDay = nextDay.PlusDays(2);
        if (nextDay.DayOfWeek == IsoDayOfWeek.Sunday)    nextDay = nextDay.PlusDays(1);
        var openTime = nextDay.At(new LocalTime(9, 15));
        return openTime.InZoneLeniently(Ist).ToInstant();
    }
}
