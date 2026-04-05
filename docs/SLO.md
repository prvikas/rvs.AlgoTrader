# SLO / Error Budget (#136)

## Service Level Objectives

| SLO | Target | Error Budget | Severity | Metric |
|-----|--------|--------------|----------|--------|
| API p95 latency | ≤ 200 ms | 5% of requests | Critical | `algotrader_api_request_duration_ms` |
| Order placement p95 | ≤ 500 ms | 5% of orders | Critical | `algotrader_order_placement_duration_ms` |
| Strategy evaluation p95 | ≤ 50 ms/bar | 5% of evaluations | High | `algotrader_strategy_evaluation_duration_ms` |
| Broker API p95 | ≤ 1000 ms | 10% of calls | High | `algotrader_broker_api_duration_ms` |
| API error rate | < 0.1% | 99.9% success | Critical | `algotrader_api_errors_total / algotrader_api_requests_total` |
| Order success rate | ≥ 99.5% | 0.5% failures | Critical | `algotrader_order_errors_total / algotrader_orders_total` |

## Prometheus Queries (Grafana)

```promql
# API p95 latency (last 1h)
histogram_quantile(0.95, rate(algotrader_api_request_duration_ms_bucket[1h]))

# API error rate (last 5m)
rate(algotrader_api_errors_total[5m]) / rate(algotrader_api_requests_total[5m])

# Order success rate (last 24h)
1 - (rate(algotrader_order_errors_total[24h]) / rate(algotrader_orders_total[24h]))

# Active strategy instances
algotrader_active_strategy_instances
```

## Error Budget Policy

If the error budget for a Critical SLO is exhausted (>100% consumed in a rolling 30-day window):
1. Freeze new feature deployments
2. Dedicate next sprint to reliability work
3. Post-mortem required before resuming feature work

Metrics are scraped by Prometheus at `/metrics` (OpenTelemetry Prometheus exporter).
See `GET /api/health/slo` for the SLO target registry endpoint.
