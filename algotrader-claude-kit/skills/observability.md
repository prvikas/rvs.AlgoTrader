# Skill: Observability — AlgoTrader

## Purpose
Patterns and configuration for OpenTelemetry, Prometheus, Grafana, and CI/CD in the AlgoTrader codebase.
Load this skill when implementing metrics, alerts, health checks, the monitoring pipeline, or CI configuration.

---

## OpenTelemetry .NET SDK Setup

### Registration in `Program.cs`
```csharp
// Add OpenTelemetry with Prometheus exporter
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter("rvs.AlgoTrader.Brokers")
            .AddMeter("rvs.AlgoTrader.Strategies")
            .AddMeter("rvs.AlgoTrader.Capital")
            .AddMeter("rvs.AlgoTrader.Streams")
            .AddPrometheusExporter();  // exposes /metrics endpoint
    })
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource("AlgoTrader.*");
    });

// Expose /metrics Minimal API endpoint
app.MapPrometheusScrapingEndpoint("/metrics");
```

### IMeterFactory — Recording Custom Metrics
```csharp
// ALWAYS inject IMeterFactory, not Meter directly
// Correct: constructor injection
public class BrokerLatencyRecorder
{
    private readonly Histogram<double> _latencyHistogram;

    public BrokerLatencyRecorder(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("rvs.AlgoTrader.Brokers");

        _latencyHistogram = meter.CreateHistogram<double>(
            name: "broker.latency.ms",
            unit: "ms",
            description: "Broker API call latency in milliseconds");
    }

    public void Record(string brokerName, double latencyMs)
    {
        _latencyHistogram.Record(latencyMs,
            new TagList { { "broker", brokerName } });
    }
}
```

### Required Custom Meters
```csharp
// Meter: rvs.AlgoTrader.Brokers
broker.latency.ms         // Histogram<double> — tag: broker
broker.order.success      // Counter<long>    — tag: broker
broker.order.rejection    // Counter<long>    — tag: broker, reason

// Meter: rvs.AlgoTrader.Strategies
strategy.evaluations      // Counter<long>    — tag: strategy_name, instance_id
strategy.signals          // Counter<long>    — tag: strategy_name, signal_type (BUY/SELL/HOLD/SKIP)
strategy.eval.duration.ms // Histogram<double> — tag: strategy_name

// Meter: rvs.AlgoTrader.Capital
capital.utilization.pct   // ObservableGauge<double> — tag: broker
                          //   Value = (reserved / allocated) * 100

// Meter: rvs.AlgoTrader.Streams
stream.tick.age.seconds   // ObservableGauge<double> — tag: symbol
                          //   Value = seconds since last tick received
candle.cache.hit          // Counter<long>    — no tags
candle.cache.miss         // Counter<long>    — no tags

// Meter: rvs.AlgoTrader.Scheduling (via IMonitoringAlertEvaluator)
strategy.session.events   // Counter<long>    — tag: event_type (STARTED, STOPPED, MISSED, RESUMED)
```

---

## Prometheus Scrape Configuration

### `prometheus.yml`
```yaml
global:
  scrape_interval: 15s
  evaluation_interval: 15s

scrape_configs:
  - job_name: "algotrader"
    static_configs:
      - targets: ["app:8080"]
    metrics_path: /metrics
    scrape_interval: 5s   # faster scrape for trading metrics

  - job_name: "postgres-exporter"
    static_configs:
      - targets: ["postgres-exporter:9187"]

  - job_name: "redis-exporter"
    static_configs:
      - targets: ["redis-exporter:9121"]

  - job_name: "rabbitmq"
    static_configs:
      - targets: ["rabbitmq:15692"]
    metrics_path: /metrics
```

---

## Grafana Dashboard — Key Panels

The main AlgoTrader Grafana dashboard must contain these panels:

### Row 1: Broker Health
| Panel | Metric Query | Chart Type |
|---|---|---|
| Broker Latency p50/p95/p99 | `histogram_quantile(0.50/0.95/0.99, rate(broker_latency_ms_bucket[1m]))` | Time series |
| Latency Heatmap by Time of Day | `rate(broker_latency_ms_bucket[5m])` | Heatmap |
| Order Success Rate | `rate(broker_order_success_total[5m]) / (rate(broker_order_success_total[5m]) + rate(broker_order_rejection_total[5m]))` | Gauge |
| Rate-Limit Token Status | Redis key `ratelimit:*` via redis-exporter | Stat |

### Row 2: Strategy Performance
| Panel | Metric Query | Chart Type |
|---|---|---|
| Strategy Evaluation Rate | `rate(strategy_evaluations_total[1m])` grouped by `strategy_name` | Time series |
| Signal Distribution (BUY/SELL/HOLD/SKIP) | `rate(strategy_signals_total[5m])` grouped by `signal_type` | Bar chart |
| Evaluation Duration p95 | `histogram_quantile(0.95, rate(strategy_eval_duration_ms_bucket[5m]))` | Time series |

### Row 3: Capital & Risk
| Panel | Metric Query | Chart Type |
|---|---|---|
| Capital Utilization % | `capital_utilization_pct` grouped by `broker` | Gauge |
| Daily PnL | Computed from `positions` table via Grafana datasource | Stat |
| Open Positions Count | Computed from `positions` table | Stat |

### Row 4: Data Quality
| Panel | Metric Query | Chart Type |
|---|---|---|
| Stream Tick Age | `stream_tick_age_seconds` grouped by `symbol` | Time series |
| Candle Cache Hit Rate | `rate(candle_cache_hit_total[5m]) / (rate(candle_cache_hit_total[5m]) + rate(candle_cache_miss_total[5m]))` | Gauge |
| Active Disconnects | `stream_disconnect_count_total` grouped by `broker` | Stat |

### Row 5: Scheduling Events
| Panel | Metric Query | Chart Type |
|---|---|---|
| Session Events (start/stop/missed/resumed) | `increase(strategy_session_events_total[1d])` grouped by `event_type` | Bar chart |
| Auto-Resumed Instances Today | `increase(strategy_session_events_total{event_type="RESUMED"}[1d])` | Stat |
| Missed Sessions Today | `increase(strategy_session_events_total{event_type="MISSED"}[1d])` | Stat |

---

## Monitoring Alert Thresholds

Built-in metrics evaluated by `IMonitoringAlertEvaluator` (Hangfire job, every 30s during market hours):

| Metric | Default Threshold | Severity |
|---|---|---|
| `broker.latency.p95.{broker}` | > 500ms | WARN |
| `broker.latency.p95.{broker}` | > 2000ms | CRITICAL |
| `stream.no_ticks.{symbol}.seconds` | > 60s during market hours | WARN |
| `stream.no_ticks.{symbol}.seconds` | > 300s during market hours | CRITICAL |
| `strategy.no_evaluation.{instanceId}.minutes` | > 20min during market hours | WARN |
| `stream.disconnect.{broker}.count` | > 3 in 10min | WARN |
| `order.rejection_rate.{broker}` | > 20% in 5min | CRITICAL |
| `capital.utilization.{broker}` | > 90% of allocated | WARN |
| `drawdown.daily.{instanceId}` | > 80% of max limit | WARN |
| `data.stale.{symbol}.minutes` | > 5min market hours | WARN |
| `strategy.missed_session.{instanceId}` | any occurrence | WARN |
| `strategy.auto_resumed.{instanceId}` | any occurrence | INFO |

### IMonitoringAlertEvaluator Contract
```csharp
// Hangfire recurring job — every 30 seconds during market hours
public class MonitoringAlertEvaluator : IMonitoringAlertEvaluator
{
    // 1. Load active rules from monitoring_alert_rules table
    // 2. For each rule: read metric value from Prometheus (IMeterFactory) or Redis/DB
    // 3. Compare against threshold using operator (GT, LT, GTE, LTE)
    // 4. If threshold breached:
    //    a. Check alert dedup: Redis key alert:dedup:{ruleId} (TTL = window_seconds)
    //    b. If dedup key exists: skip (already fired in this window)
    //    c. If not: INSERT to alert_log, publish MassTransit event, set dedup key
}
```

**Alert dedup rule:** Once an alert fires, it must not re-fire within the same `window_seconds` period.  
**Redis key:** `alert:dedup:{ruleId}` with TTL = `window_seconds`.

---

## GitHub Actions CI Pipeline

### `.github/workflows/ci.yml` — Complete Pipeline
```yaml
name: CI

on:
  push:
    branches: [main, develop]
  pull_request:
    branches: [main, develop]

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    steps:
      # Step 1: Checkout
      - uses: actions/checkout@v4

      # Step 2: Setup .NET 9
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      # Step 3: Restore dependencies
      - name: Restore
        run: dotnet restore rvs.AlgoTrader.sln

      # Step 4: Build (treat warnings as errors)
      - name: Build
        run: dotnet build rvs.AlgoTrader.sln --no-restore -c Release

      # Step 5: Unit tests (fast, no containers)
      - name: Unit Tests
        run: dotnet test rvs.AlgoTrader.UnitTests/rvs.AlgoTrader.UnitTests.csproj \
               --no-build -c Release \
               --logger "trx;LogFileName=unit-results.trx" \
               --collect:"XPlat Code Coverage"

      # Step 6: Integration tests (Testcontainers — PostgreSQL, Redis, RabbitMQ)
      - name: Integration Tests
        run: dotnet test rvs.AlgoTrader.IntegrationTests/rvs.AlgoTrader.IntegrationTests.csproj \
               --no-build -c Release \
               --logger "trx;LogFileName=integration-results.trx" \
               --collect:"XPlat Code Coverage"
        env:
          TESTCONTAINERS_RYUK_DISABLED: "true"

      # Step 7: Architecture tests (NetArchTest — blocks merge if violated)
      - name: Architecture Tests
        run: dotnet test rvs.AlgoTrader.UnitTests/rvs.AlgoTrader.UnitTests.csproj \
               --no-build -c Release \
               --filter "Category=Architecture"

      # Step 8: Playwright UI tests (full browser automation)
      - name: Install Playwright Browsers
        run: pwsh rvs.AlgoTrader.Tests.UI/bin/Release/net9.0/playwright.ps1 install --with-deps chromium

      - name: Playwright UI Tests
        run: dotnet test rvs.AlgoTrader.Tests.UI/rvs.AlgoTrader.Tests.UI.csproj \
               --no-build -c Release \
               --logger "trx;LogFileName=playwright-results.trx"

      # Step 9: Publish test results
      - name: Publish Test Results
        uses: dorny/test-reporter@v1
        if: always()
        with:
          name: Test Results
          path: '**/*.trx'
          reporter: dotnet-trx

  docker:
    needs: build-and-test
    runs-on: ubuntu-latest
    if: github.ref == 'refs/heads/main'
    steps:
      # Step 10: Docker build
      - uses: actions/checkout@v4

      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v3

      # Step 11: Login to GHCR
      - name: Login to GitHub Container Registry
        uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      # Step 12: Docker build + push
      - name: Build and Push
        uses: docker/build-push-action@v5
        with:
          context: .
          file: rvs.AlgoTrader.API/Dockerfile
          push: true
          tags: |
            ghcr.io/${{ github.repository }}/algotrader:latest
            ghcr.io/${{ github.repository }}/algotrader:${{ github.sha }}
          cache-from: type=gha
          cache-to: type=gha,mode=max
```

### CI Rules
- All 4 test suites (unit, integration, architecture, Playwright) must pass before Docker push
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` ensures build step catches all compiler warnings
- Architecture tests use `--filter "Category=Architecture"` to run only `[Trait("Category", "Architecture")]` tests
- Playwright tests run against the built binary — no live service needed (uses mocked backend in test mode)
- Docker image pushed only on `main` branch merge

---

## Health Check Endpoints

```csharp
// /health/live — process alive (Minimal API, no auth required)
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));

// /health/ready — all dependencies healthy + startup complete
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgresql")
    .AddRedis(redisConnectionString, name: "redis")
    .AddRabbitMQ(rabbitMqUri, name: "rabbitmq")
    .AddCheck<BrokerWebSocketHealthCheck>("broker-websocket-zerodha")
    .AddCheck<BrokerWebSocketHealthCheck>("broker-websocket-upstox")
    .AddCheck<HangfireHealthCheck>("hangfire-heartbeat")
    .AddCheck<StartupOrchestratorHealthCheck>("startup-orchestrator");
    // StartupOrchestratorHealthCheck: returns Unhealthy until Step 11 completes
    // This is what /health/ready reports to the orchestrator (Kubernetes, Docker Compose)
```
