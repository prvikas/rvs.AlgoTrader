using System.Diagnostics.Metrics;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// #136: SLO/error budget tracking via .NET Meters (OpenTelemetry-compatible).
///
/// Meters are published to Prometheus via the existing OpenTelemetry exporter
/// (configured in Program.cs: AddOpenTelemetry().WithMetrics(...)).
///
/// SLO targets (see docs/SLO.md for rationale):
///   API p95 latency      &lt;= 200 ms   (budget: 5% of requests may exceed)
///   Order placement p95  &lt;= 500 ms
///   Strategy evaluation  &lt;= 50 ms per bar
///   Error rate           &lt; 0.1%    (99.9% success rate)
///
/// Usage: inject ISingleton SloTracker; call Record* methods from middleware/handlers.
/// </summary>
public sealed class SloTracker : IDisposable
{
    private readonly Meter _meter;

    // Histograms — latency distributions (milliseconds)
    public readonly Histogram<double> ApiRequestDuration;
    public readonly Histogram<double> OrderPlacementDuration;
    public readonly Histogram<double> StrategyEvaluationDuration;
    public readonly Histogram<double> BrokerApiDuration;

    // Counters — for error-rate calculation
    public readonly Counter<long> RequestTotal;
    public readonly Counter<long> RequestErrors;
    public readonly Counter<long> OrderTotal;
    public readonly Counter<long> OrderErrors;

    // Gauges — point-in-time state
    public readonly ObservableGauge<int> ActiveStrategyInstances;

    private volatile int _activeInstances;

    public SloTracker()
    {
        _meter = new Meter("rvs.AlgoTrader", "1.0");

        ApiRequestDuration = _meter.CreateHistogram<double>(
            "algotrader_api_request_duration_ms",
            unit: "ms",
            description: "API endpoint response time in milliseconds");

        OrderPlacementDuration = _meter.CreateHistogram<double>(
            "algotrader_order_placement_duration_ms",
            unit: "ms",
            description: "End-to-end order placement latency in milliseconds");

        StrategyEvaluationDuration = _meter.CreateHistogram<double>(
            "algotrader_strategy_evaluation_duration_ms",
            unit: "ms",
            description: "Strategy EvaluateAsync latency per bar in milliseconds");

        BrokerApiDuration = _meter.CreateHistogram<double>(
            "algotrader_broker_api_duration_ms",
            unit: "ms",
            description: "Broker HTTP API call latency in milliseconds");

        RequestTotal = _meter.CreateCounter<long>(
            "algotrader_api_requests_total",
            description: "Total API requests");

        RequestErrors = _meter.CreateCounter<long>(
            "algotrader_api_errors_total",
            description: "Total API 5xx errors (error budget consumption)");

        OrderTotal = _meter.CreateCounter<long>(
            "algotrader_orders_total",
            description: "Total order placement attempts");

        OrderErrors = _meter.CreateCounter<long>(
            "algotrader_order_errors_total",
            description: "Total failed order placements");

        ActiveStrategyInstances = _meter.CreateObservableGauge(
            "algotrader_active_strategy_instances",
            () => _activeInstances,
            description: "Number of currently running strategy instances");
    }

    public void SetActiveInstances(int count) => _activeInstances = count;

    public void Dispose() => _meter.Dispose();
}
