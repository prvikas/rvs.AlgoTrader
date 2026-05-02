using MediatR;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Commands.ShortPremiumVelocity;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Interfaces;
using IClock = rvs.AlgoTrader.Domain.Interfaces.IClock;

namespace rvs.AlgoTrader.Strategies.ShortPremiumVelocity;

/// <summary>
/// Monitors intraday realised-vol spikes via the broker stream (IBrokerStreamClient).
/// Event-driven ONLY — no Timer, no Task.Delay, no polling loops.
/// Subscribed to IBrokerStreamClient on startup via StartAsync (called by DI hosted service).
///
/// Maintains a rolling window of config.JumpRiskRollingWindowMinutes 1-min realised vols.
/// Triggers soft-stop when: currentVol > rollingMean + (JumpRiskSpikeStdDevMultiplier × rollingStdDev).
/// Clears  soft-stop when: currentVol &lt; rollingMean + (JumpRiskClearStdDevMultiplier × rollingStdDev).
///
/// On spike: publishes JumpRiskSoftStopEvent + HedgeActionRequiredEvent (VegaHedge) via MediatR.
/// Subscribes to all config.UnderlyingSymbols.
/// </summary>
public sealed class JumpRiskMonitor(
    IBrokerClientFactory        brokerFactory,
    IPublisher                  publisher,
    IClock                      clock,
    ILogger<JumpRiskMonitor>    log)
    : IJumpRiskMonitor
{
    private volatile JumpRiskState _currentState = new(0m, 0m, 0m, false, null);
    private ShortPremiumVelocityConfig? _config;

    // Rolling window of 1-min realised vols (one queue per symbol — simplified to aggregate)
    private readonly Queue<decimal> _volWindow = new();

    public JumpRiskState CurrentState => _currentState;

    /// <summary>
    /// Wire to IBrokerStreamClient and begin streaming ticks for all configured symbols.
    /// Called once on startup by the DI hosting layer; returns immediately (fire-and-forget via Task).
    /// No polling loop — driven entirely by WebSocket events.
    /// </summary>
    public Task StartAsync(ShortPremiumVelocityConfig config, string brokerName, CancellationToken ct)
    {
        _config = config;
        log.LogInformation(
            "JumpRiskMonitor: subscribing to {N} symbols on broker={Broker}",
            config.UnderlyingSymbols.Length, brokerName);

        // Fire-and-forget stream processing — event-driven, no polling
        _ = StreamTicksAsync(config, brokerName, ct);
        return Task.CompletedTask;
    }

    // ── Private stream processor (runs for lifetime of the process) ───────────

    private async Task StreamTicksAsync(
        ShortPremiumVelocityConfig config,
        string                     brokerName,
        CancellationToken          ct)
    {
        try
        {
            var streamClient = brokerFactory.GetStreamClient(brokerName);

            // Subscribe to underlying symbols
            await streamClient.SubscribeAsync(config.UnderlyingSymbols, ct);
            log.LogInformation("JumpRiskMonitor: WebSocket subscriptions active for {Syms}",
                string.Join(", ", config.UnderlyingSymbols));

            // Maintain previous tick LTP per symbol for per-bar vol computation
            var prevLtp = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            var barAccumulator = new Dictionary<string, List<decimal>>(StringComparer.OrdinalIgnoreCase);

            await foreach (var tick in streamClient.StreamAsync(config.UnderlyingSymbols, ct))
            {
                ProcessTick(tick, config, prevLtp, barAccumulator, ct);
            }
        }
        catch (OperationCanceledException)
        {
            log.LogInformation("JumpRiskMonitor: stream cancelled");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "JumpRiskMonitor: stream error — monitor stopped");
        }
    }

    private void ProcessTick(
        BrokerTick                  tick,
        ShortPremiumVelocityConfig  config,
        Dictionary<string, decimal> prevLtp,
        Dictionary<string, List<decimal>> barAccumulator,
        CancellationToken           ct)
    {
        if (tick.Ltp <= 0) return;

        // Compute 1-min bar log-return contribution
        if (!prevLtp.TryGetValue(tick.Symbol, out decimal prev))
        {
            prevLtp[tick.Symbol] = tick.Ltp;
            return;
        }

        decimal logReturn = (decimal)Math.Log((double)(tick.Ltp / prev));
        prevLtp[tick.Symbol] = tick.Ltp;

        if (!barAccumulator.TryGetValue(tick.Symbol, out var returns))
        {
            returns = [];
            barAccumulator[tick.Symbol] = returns;
        }
        returns.Add(logReturn);

        // Aggregate into a 1-min realised vol when we have enough ticks
        // (simplified: treat each tick as a 1-min bar for the monitor)
        decimal squaredReturn = logReturn * logReturn;
        decimal annualisedVol = (decimal)Math.Sqrt((double)(squaredReturn * 252m * 6.5m * 60m)) * 100m;

        // Update rolling window
        _volWindow.Enqueue(annualisedVol);
        while (_volWindow.Count > config.JumpRiskRollingWindowMinutes)
            _volWindow.Dequeue();

        if (_volWindow.Count < 5) return; // need minimum data

        // ── Compute stats ─────────────────────────────────────────────────────
        var vols     = _volWindow.ToArray();
        decimal mean = vols.Average();
        decimal variance = vols.Average(v => (v - mean) * (v - mean));
        decimal stdDev   = (decimal)Math.Sqrt((double)variance);
        decimal ratio    = mean > 0 ? annualisedVol / mean : 1m;

        // ── Spike detection ───────────────────────────────────────────────────
        decimal spikeThreshold = mean + config.JumpRiskSpikeStdDevMultiplier * stdDev;
        decimal clearThreshold = mean + config.JumpRiskClearStdDevMultiplier  * stdDev;

        if (annualisedVol > spikeThreshold && !_currentState.IsSoftStop)
        {
            var triggeredAt = clock.NowInstant();
            _currentState = new JumpRiskState(ratio, mean, stdDev, true, triggeredAt);

            log.LogWarning(
                "JumpRiskMonitor: SPIKE sym={Sym} vol={Vol:F1}% > threshold={T:F1}% " +
                "ratio={Ratio:F2} mean={Mean:F1} std={Std:F1}",
                tick.Symbol, annualisedVol, spikeThreshold, ratio, mean, stdDev);

            // Publish events (fire-and-forget; monitor cannot await inside sync processor)
            _ = publisher.Publish(new JumpRiskSoftStopEvent(
                tick.Symbol, ratio, triggeredAt), ct);

            _ = publisher.Publish(new HedgeActionRequiredEvent(
                UnderlyingSymbol:  tick.Symbol,
                RequiredHedgeType: Domain.Enums.HedgeType.VegaHedge,
                Reason:            $"JumpRisk spike: ratio={ratio:F2} vol={annualisedVol:F1}%",
                Regime:            MarketRegime.VelocityHighVolExpansion,
                OccurredAt:        triggeredAt), ct);
        }
        else if (_currentState.IsSoftStop && annualisedVol < clearThreshold)
        {
            _currentState = new JumpRiskState(ratio, mean, stdDev, false, null);
            log.LogInformation(
                "JumpRiskMonitor: soft-stop CLEARED sym={Sym} vol={Vol:F1}% < clear={C:F1}%",
                tick.Symbol, annualisedVol, clearThreshold);
        }
        else
        {
            _currentState = new JumpRiskState(ratio, mean, stdDev, _currentState.IsSoftStop,
                _currentState.TriggeredAt);
        }
    }
}
