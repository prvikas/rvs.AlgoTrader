# PLAN.md — AlgoTrader Generation Plan

> This file defines the exact order in which Claude Code should generate all project components.
> **Follow this order strictly.** Each step produces interfaces and types that later steps depend on.
> Mark each step `[x]` when complete. Do NOT skip steps.

---

## Pre-Generation Checklist

- [ ] Run `hooks/pre-generate.sh` — validates Docker, .NET SDK 9, Node 20, dotnet ef tools are present
- [ ] Verify `.env` file exists (copy from `.env.example`)
- [ ] Confirm `docker compose up -d` succeeded (PostgreSQL, Redis, RabbitMQ running)

---

## Phase 1: Project Scaffolding

### Step 1 — Solution + Project Structure
- [ ] `rvs.AlgoTrader.sln` with all project references
- [ ] All `.csproj` files with correct project references and NuGet packages
  - Domain: zero dependencies
  - Application: Domain only
  - Infrastructure: Application, Domain, Brokers.Abstractions
  - Brokers.Abstractions: Domain
  - Brokers.Zerodha / Upstox / MStock: Brokers.Abstractions, Application
  - Strategies: Application, Domain
  - Backtesting: Application, Domain, Strategies
  - API: Application + Infrastructure (DI only)
  - UnitTests: all src projects
  - IntegrationTests: Infrastructure, API
  - Tests.UI: Playwright
- [ ] Global `.editorconfig` (C# + TypeScript consistency)
- [ ] `Directory.Build.props` (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`)
- [ ] `.github/workflows/ci.yml`
- [ ] `docker-compose.yml` (PostgreSQL+TimescaleDB, Redis AOF, RabbitMQ, Prometheus, Grafana, Vault dev mode)
- [ ] `.env.example`
- [ ] `.gitignore`

**Validation:** `dotnet build rvs.AlgoTrader.sln` passes with zero errors

---

## Phase 2: Domain Layer

### Step 2 — Domain Entities, Value Objects, Enums, Domain Events
- [ ] Entities: `Order`, `Position`, `Instrument`, `StrategyInstance`, `RiskProfile`, `CapitalAllocation`, `Candle`, `StrategyRun`, `ForwardTestSession`, `ForwardTestTrade`
- [ ] Value Objects: `Money`, `Price`, `Quantity`, `InstrumentSymbol`, `TimeRange`, `RiskParameters`
- [ ] Enums: `OrderType`, `OrderDirection`, `OrderStatus`, `SignalType`, `StrategyStatus`, `StrategyMode`, `InstrumentType`, `OptionType`, `BrokerName`, `ScheduleAction`, `MissedSessionBehavior`, `AlertSeverity`, `AlertCategory`, `SkippedReason`
- [ ] Domain Events: `OrderPlaced`, `OrderFilled`, `OrderCancelled`, `PositionOpened`, `PositionClosed`, `SignalGenerated`, `AlertTriggered`, `CandleClosedEvent`, `PositionMismatchDetected`, `StreamDisconnected`, `StreamReconnected`, `StrategyAutoResumed`, `StrategyMissedSessionWindow`, `MonitoringAlertTriggered`
- [ ] Domain Interfaces: `IClock`, `IStrategy`, `IExecutionEngine`, `ICapitalAllocator`, `IStrategyScheduler`

**Validation:** `rvs.AlgoTrader.Domain` builds with zero external dependencies (NetArchTest)

---

## Phase 3: Application Layer

### Step 3 — Application Interfaces + Service Contracts
- [ ] Repository interfaces: `IOrderRepository`, `IPositionRepository`, `IInstrumentRepository`, `IStrategyInstanceRepository`, `ICandleRepository`, `IWatchlistRepository`, `ISignalJournalRepository`, `IAuditLogRepository`, `IAlertLogRepository`, `IDownloadJobRepository`
- [ ] Application service interfaces:
  - `IStrategyScheduler`, `ICapitalAllocator`, `IExecutionEngine`
  - `IClock` (defined in Domain, implemented in Infrastructure)
  - `IMonitoringAlertEvaluator`, `IStartupOrchestrator`, `IIdempotencyService`
  - `IFieldEncryptionService`, `ISecretsProvider`, `IStrategyInstanceManager`
  - `IMarketCalendarService`, `IIndicatorService`, `IIncrementalIndicator<T>`
  - `ICandleCache`, `IStrategyExecutionThrottler`, `IHistoricalDataDownloadService`
  - `IBrokerSessionManager`, `IInstrumentTokenResolver`, `IDataQualityService`
  - `IAppConfigService`, `INotificationService`, `IAuditService`, `ITransactionCostCalculator`
  - `IKillSwitchService`, `IPositionReconciliationService`
  - `ITrailingStopLossService` — updates trailing SL/TP on open positions per bar; called by LiveExecutionEngine and SimulatedExecutionEngine after each CandleClosedEvent
  - `IForwardTestFillSimulator` — fill simulation rules for SimulatedExecutionEngine (market → next bar open, limit → bar range cross, SL → bar low/high + slippage)
  - `IRiskManagementService` — evaluates MaxCapitalPerTrade%, MaxDailyDrawdown, MaxOpenTradesPerSymbol, MaxTradesPerDay against current positions/orders before LiveExecutionEngine places an order
  - `ISymbolDataPreferencesService` — manages per-symbol download configuration (timeframes, from_date, priority) stored in `symbol_data_preferences` table; used by HistoricalDownloadService and InstrumentRefreshService to know what to fetch
  - `ISecretsProviderFactory` — selects `VaultSecretsProvider` or `EnvironmentSecretsProvider` based on `appsettings.json "Secrets:Provider"` value; registered as singleton; resolves at startup

### Step 4 — MediatR Commands, Queries, Handlers, DTOs, Validators

**Commands (with Handlers + FluentValidation Validators):**
- [ ] `PlaceOrderCommand` / `PlaceOrderCommandHandler` / `PlaceOrderCommandValidator`
- [ ] `ModifyOrderCommand` / `CancelOrderCommand`
- [ ] `StartStrategyInstanceCommand` / `PauseStrategyInstanceCommand` / `StopStrategyInstanceCommand`
- [ ] `RunBacktestCommand` / `RunBacktestCommandHandler`
- [ ] `StartForwardTestCommand` / `PauseForwardTestCommand` / `StopForwardTestCommand`
- [ ] `ActivateKillSwitchCommand` / `DeactivateKillSwitchCommand`
- [ ] `CreateWatchlistCommand` / `UpdateWatchlistCommand` / `DeleteWatchlistCommand`
- [ ] `AddSymbolToWatchlistCommand` / `RemoveSymbolFromWatchlistCommand`
- [ ] `CreateStrategyInstanceCommand` / `UpdateStrategyInstanceCommand`
- [ ] `EnqueueHistoricalDownloadCommand`
- [ ] `UpdateRiskProfileCommand` / `CreateRiskProfileCommand`
- [ ] `UpdateNotificationPreferencesCommand`
- [ ] `SetAppConfigValueCommand`
- [ ] `UpdateCapitalAllocationCommand` / `CreateCapitalAllocationCommand` — set/update per-strategy capital allocation (AllocatedCapital, BrokerName); triggers Redis counter reconciliation
- [ ] `CreateBacktestCostProfileCommand` / `UpdateBacktestCostProfileCommand` / `DeleteBacktestCostProfileCommand` — manage transaction cost models used in backtests
- [ ] `UpdateSymbolDataPreferencesCommand` / `GetSymbolDataPreferencesQuery` — configure per-symbol download preferences (timeframes, from_date, priority)

**Queries (with Handlers):**
- [ ] `GetOrdersQuery` / `GetOrderByIdQuery`
- [ ] `GetPositionsQuery`
- [ ] `GetStrategyInstancesQuery` / `GetStrategyInstanceByIdQuery`
- [ ] `GetSignalJournalQuery` (filter: strategy, symbol, signal, acted_on, skipped_reason)
- [ ] `GetBacktestResultQuery` / `GetBacktestRunsQuery`
- [ ] `GetForwardTestSessionQuery`
- [ ] `GetAuditLogQuery`
- [ ] `GetAlertLogQuery`
- [ ] `GetBrokerLatencyQuery`
- [ ] `GetDownloadJobsQuery`
- [ ] `GetWatchlistsQuery` / `GetWatchlistByIdQuery`
- [ ] `GetInstrumentSearchQuery`
- [ ] `GetCapitalAllocationQuery`
- [ ] `GetMonitoringAlertRulesQuery`
- [ ] `ReproduceBacktestQuery`
- [ ] `GetBrokerConnectionStatusQuery` — returns connected/disconnected status + last heartbeat for each broker's WebSocket stream; used by BrokerController
- [ ] `GetBacktestCostProfilesQuery` / `GetBacktestCostProfileByIdQuery`

**DTOs (all with mapping via Mapperly):**
- [ ] `OrderDto`, `CreateOrderDto`, `ModifyOrderDto`
- [ ] `PositionDto`
- [ ] `StrategyInstanceDto`, `CreateStrategyInstanceDto`, `UpdateStrategyInstanceDto`
- [ ] `SignalJournalEntryDto`
- [ ] `BacktestResultDto`, `BacktestTradeDto`, `EquityCurvePointDto`
- [ ] `ForwardTestSessionDto`, `ForwardTestTradeDto`
- [ ] `InstrumentDto`
- [ ] `WatchlistDto`, `WatchlistSymbolDto`
- [ ] `AlertLogEntryDto`
- [ ] `AuditLogEntryDto`
- [ ] `BrokerLatencyDto`
- [ ] `MonitoringAlertRuleDto`, `CreateMonitoringAlertRuleDto`
- [ ] `CapitalAllocationDto`, `UpdateCapitalAllocationDto`
- [ ] `ScheduleConfigDto`
- [ ] `BacktestCostProfileDto`, `CreateBacktestCostProfileDto` — brokerage pct, STT, GST, SEBI, stamp duty, slippage model
- [ ] `SymbolDataPreferencesDto`, `UpdateSymbolDataPreferencesDto` — per-symbol timeframes[], from_date, priority
- [ ] `BrokerConnectionStatusDto` — brokerName, isConnected, lastHeartbeatAt, reconnectAttempts

**Validation:** `rvs.AlgoTrader.Application` builds. Every Command/Query has a validator. Zero EF Core dependencies.

---

## Phase 4: Infrastructure

### Step 5 — Database: DDL + EF Core
- [ ] Full PostgreSQL DDL (`migrations/V1__initial_schema.sql`) — all tables from spec
- [ ] TimescaleDB hypertable setup: `candles`, `forward_test_equity_curve`
- [ ] All indexes (GIN for tsvector, composite for candles, expiry for idempotency_keys)
- [ ] `AlgoTraderDbContext : DbContext` with all DbSet<> and EF Core configurations
- [ ] Entity configurations (`IEntityTypeConfiguration<T>` for each entity)
- [ ] EF Core value converters for: NodaTime Instant↔TIMESTAMPTZ, encrypted fields
- [ ] Initial EF Core migration
- [ ] `DatabaseHealthCheck`

### Step 6 — Infrastructure: Repositories
- [ ] All repository implementations (PostgreSQL via EF Core)
- [ ] `CandleRepository` — always uses time range in queries
- [ ] `AuditLogRepository` — INSERT only, no UPDATE/DELETE
- [ ] `IdempotencyKeyRepository` — DB fallback store

### Step 7 — IClock + SimulatedClock
- [ ] `SystemClock : IClock` — NodaTime, IST, production singleton
- [ ] `SimulatedClock : IClock` — Advance(), AdvanceTo(), deterministic
- [ ] Register `SystemClock` in production DI
- [ ] Unit tests: SimulatedClock advance correctness

### Step 8 — IAppConfigService
- [ ] `AppConfigService` — DB-backed, Redis cache with 60s TTL
- [ ] `IAppConfigService` implementation with `GetAsync<T>` / `SetAsync`
- [ ] `audit_log` entry on every config change

### Step 9 — Secrets + Field Encryption
- [ ] `EnvironmentSecretsProvider : ISecretsProvider`
- [ ] `VaultSecretsProvider : ISecretsProvider` (VaultSharp)
- [ ] `IFieldEncryptionService` — AES-256 implementation
- [ ] EF Core value converter: auto encrypt/decrypt `[Encrypted]` columns
- [ ] Unit tests: encrypt/decrypt round-trip, VaultSecretsProvider, EnvironmentSecretsProvider

### Step 10 — Redis Infrastructure
- [ ] `RedisConnection` setup (StackExchange.Redis)
- [ ] `CandleCache : ICandleCache` — sorted set, rolling 500 bars, cache miss → DB fallback
- [ ] `RedisCapitalAllocator : ICapitalAllocator` — Lua script atomic reserve/release
- [ ] `BacktestCapitalAllocator : ICapitalAllocator` — in-memory, no Redis
- [ ] `IdempotencyRedisStore` — 24h TTL
- [ ] Kill-switch Redis client (dual-write helper)
- [ ] Alert dedup Redis store
- [ ] `RedisHealthCheck`

### Step 11 — RabbitMQ / MassTransit
- [ ] MassTransit configuration with RabbitMQ transport
- [ ] All domain event publishers
- [ ] All consumers: `CandleClosedEventConsumer`, `OrderFilledEventConsumer`, `PositionClosedEventConsumer`, `MonitoringAlertTriggeredConsumer`
- [ ] In-process sync fallback for critical paths when RabbitMQ unavailable
- [ ] `RabbitMqHealthCheck`

### Step 12 — ASP.NET Core Identity + JWT
- [ ] `ApplicationUser : IdentityUser`
- [ ] JWT bearer token config (access + refresh)
- [ ] Refresh token rotation service
- [ ] `AuthController` (Minimal API or thin MVC): login, logout, refresh
- [ ] All auth events → `audit_log`

### Step 13 — Hangfire
- [ ] Hangfire setup with PostgreSQL storage
- [ ] Dashboard with Admin auth filter (non-dev environments)
- [ ] Job definitions (interfaces):
  - `IStrategySchedulerJob` — session start/stop checks
  - `IHistoricalDownloadJob` — rate-limited chunk downloader
  - `IInstrumentRefreshJob` — daily master data refresh
  - `IReconciliationJob` — 5-min position reconciliation
  - `IMonitoringAlertJob` — 30s alert threshold evaluation
  - `IIdempotencyCleanupJob` — daily expired key cleanup
  - `IEodReportJob` — 15:35 IST daily PnL report

---

## Phase 5: Market Data & Instruments

### Step 14 — Master Data (Instruments)
- [ ] `InstrumentRefreshService` — download from Zerodha CSV, Upstox API, mStock API
- [ ] Upsert into `instruments` table
- [ ] Refresh Redis `instruments:{exchange}:{symbol}`
- [ ] `InstrumentTokenResolver : IInstrumentTokenResolver`
- [ ] Full-text search via PostgreSQL tsvector
- [ ] Hangfire daily refresh job
- [ ] Unit tests: token resolver mapping

### Step 15 — Market Calendar
- [ ] `MarketCalendarService : IMarketCalendarService`
- [ ] `IsTradingDay(DateOnly date)` — checks `market_calendar` table
- [ ] `IsWithinMarketHours(ZonedDateTime time)` — uses IClock.Now()
- [ ] All calls use IClock — zero system clock
- [ ] Unit tests: holiday detection, market hours, via SimulatedClock

### Step 16 — Data Quality
- [ ] `DataQualityService : IDataQualityService`
- [ ] Gap detection: missing bars between expected timestamps
- [ ] Bad candle detection: OHLC invariants (high ≥ low, etc.), spike detection (> 3σ from mean)
- [ ] Write to `data_quality_log`
- [ ] Warning badges: symbols with issues flagged in watchlist + chart views
- [ ] Unit tests: gap detection, bad candle detection

### Step 17 — Historical Data Download Pipeline
- [ ] `IChunkingStrategy` per broker (Zerodha: 60-day chunks, Upstox: 30-day, mStock: 30-day)
- [ ] `RateLimitedChannel<T>` — token bucket per broker (System.Threading.Channels)
- [ ] `HistoricalDownloadService : IHistoricalDataDownloadService`
- [ ] Idempotent: skip completed chunks from `download_job_chunks`
- [ ] Polly handles 429 with Retry-After or exponential backoff
- [ ] `download_jobs` + `download_job_chunks` progress tracking
- [ ] Resume IN_PROGRESS jobs on cold restart (Hangfire)
- [ ] Up to `MaxParallelDownloads` symbols concurrently
- [ ] Unit tests: chunking date-range splitting per broker

### Step 18 — Broker Session Manager
- [ ] `BrokerSessionManager : IBrokerSessionManager`
- [ ] Upstox: auto-refresh OAuth2 (token expires at 3:30 AM IST daily — full re-auth flow)
- [ ] mStock (Type B): re-run session exchange on 401/403; no OAuth2, no refresh_token
- [ ] Zerodha: alert with login URL on expiry, block orders until re-login
- [ ] `SessionAwareBrokerClient` decorator — transparent refresh
- [ ] Tokens encrypted (AES-256) in Redis (AOF) + DB backup
- [ ] `TokenRefreshed` event → `audit_log`

---

## Phase 6: Broker Adapters

### Step 19 — Broker Clients
- [ ] `ZerodhaClient : IFullBrokerClient` — Kite Connect REST + WebSocket
- [ ] `UpstoxClient : IFullBrokerClient` — Upstox v2 REST + protobuf WebSocket
- [ ] `MStockClient : IFullBrokerClient` — mStock REST + WebSocket
- [ ] `ReconnectingBrokerStreamClient` decorator — exponential backoff, re-subscribe, publishes StreamDisconnected/StreamReconnected
- [ ] `BrokerClientFactory` — resolves by broker name from config
- [ ] `BrokerLatencyMiddleware` — measures every call, writes to `broker_latency_log`
- [ ] All HTTP clients via `IHttpClientFactory` + Polly (retry, circuit breaker, timeout)
- [ ] Integration tests: Polly retry on 429, circuit breaker on consecutive failures

---

## Phase 7: Candle Pipeline + Strategy Evaluation

### Step 20 — Candle Aggregation
- [ ] `CandleAggregatorService`
  - In-memory `CurrentBar` per (symbol, timeframe)
  - IClock.Now() for bar boundary detection
  - On bar close: emit `CandleClosedEvent`, append to `ICandleCache`, persist to TimescaleDB async
  - CurrentBar available for display only — NEVER passed to strategy evaluation
- [ ] Unit tests: bar boundary detection, partial candle guard (assert strategy never receives open bar)

### Step 21 — Strategy Execution Infrastructure
- [ ] `StrategyEvaluationQueue` — `System.Threading.Channels` unbounded channel per strategy instance
- [ ] One dedicated consumer `Task` per strategy instance
- [ ] `IStrategyExecutionThrottler` — `MaxConcurrentEvaluations` (default 1) per instance
- [ ] Dropped evaluations → `signal_journal.skipped_reason = THROTTLED`
- [ ] Evaluation timeout → `signal_journal.skipped_reason = TIMEOUT`

### Step 22 — Capital Allocator (Redis + In-Memory)
- [ ] `RedisCapitalAllocator` — Lua script implementation (see ADR-004)
- [ ] `BacktestCapitalAllocator` — pure in-memory, no Redis
- [ ] Cold restart reconciliation (Step 8 of IStartupOrchestrator)
- [ ] Unit tests: concurrent reservation safety (multiple threads, assert no over-allocation)

### Step 23 — Kill Switch Service
- [ ] `KillSwitchService : IKillSwitchService`
- [ ] Activation: dual-write Redis + DB, cancel ALL orders, stop ALL instances, square-off if configured
- [ ] Override `auto_resume_on_restart` — never auto-resume after kill switch
- [ ] Every order placement checks kill switch: Redis first → DB fallback
- [ ] `CRITICAL` alert via all channels + `audit_log`
- [ ] Integration test: activate, cancel mock orders, dual-write verified

### Step 23b — Position Reconciliation Service + Supporting Services
- [ ] `PositionReconciliationService : IPositionReconciliationService`
  - Fetches `IBrokerAccountClient.GetPositionsAsync()` for each active broker
  - Compares against local `positions` table (open positions only)
  - On mismatch: publishes `PositionMismatchDetected`, writes to `alert_log`, optionally auto-syncs if `AutoSyncEnabled`
  - Called by `IReconciliationJob` (Hangfire every 5 minutes during market hours)
  - Also called on-demand from `ReconciliationController`
- [ ] `TrailingStopLossService : ITrailingStopLossService`
  - `UpdateTrailingStopAsync(position, currentPrice, config, ct)` — ratchets SL up (for long) or down (for short) as price moves in favour
  - Activation threshold: `TrailingSLActivationPct` from `PriceActionBreakoutConfig`
  - Step increment: `TrailingTPStep` from config
  - Updates `position.StopLoss` in DB; never moves SL against the position
  - Unit tests: SL step logic, activation at threshold, no SL regression
- [ ] `RiskManagementService : IRiskManagementService`
  - `CheckAsync(strategyInstanceId, orderRequest, ct)` → `RiskCheckResult` (Allowed / Blocked + reason)
  - Checks: `MaxCapitalPerTradePct` (vs available capital), `MaxOpenTradesPerSymbol`, `MaxTradesPerDay`, `MaxDailyDrawdownPct` (vs realized PnL today)
  - Called by `LiveExecutionEngine` before every order placement
  - Unit tests: each limit check independently; concurrent order requests don't double-count
- [ ] `ForwardTestFillSimulator : IForwardTestFillSimulator`
  - `SimulateFillAsync(signal, candles, clock, config)` → `FillResult`
  - Market order: fill at next bar's open price
  - Limit order: fill if bar's low/high crosses trigger price within the bar
  - SL/TP: fill at bar's low (SL for long) or high (TP for long) + slippage model
  - Used exclusively by `SimulatedExecutionEngine` (forward test mode)
  - Unit tests: each fill rule with fixture candles (market, limit, SL, TP, partial, no-fill)
- [ ] `SymbolDataPreferencesService : ISymbolDataPreferencesService`
  - `GetPreferencesAsync(symbol, ct)` → `SymbolDataPreferences`
  - `UpsertAsync(preferences, ct)` — idempotent; updates `symbol_data_preferences` table
  - `GetAllActiveAsync(ct)` — returns all symbols with preferences; used by `HistoricalDownloadService` at startup to know what to enqueue
  - Default preferences: all timeframes from one year ago if no explicit preferences set

**Validation:** All 5 services built; unit tests pass; integrated into DI

---

## Phase 8: Indicators

### Step 24 — Indicator Services
- [ ] `IndicatorService : IIndicatorService` — batch calculations
  - `EMA(closes, period)`, `SMA(closes, period)`, `VWAP(candles)`, `ATR(candles, period)`
  - `BollingerBands(closes, period, stdDev)`, `SwingPoints(candles, lookback)`, `VolumeProfile(candles, bins)`
- [ ] Incremental implementations (O(1) update):
  - `IncrementalEMA : IIncrementalIndicator<decimal?>`
  - `IncrementalSMA : IIncrementalIndicator<decimal?>`
  - `IncrementalATR : IIncrementalIndicator<decimal?>`
  - `IncrementalVWAP : IIncrementalIndicator<decimal?>` — resets daily via IClock
- [ ] Unit tests: all batch methods for correctness; all incremental Update() for O(1) correctness

---

## Phase 9: Scheduling + Startup

### Step 25 — Strategy Scheduler
- [ ] `StrategyScheduler : IStrategyScheduler`
  - `IsWithinScheduledSession(config)` — uses IClock.Now(), checks market calendar
  - `EvaluateOnStartup(instance, priorStatus)` — returns `ScheduleEvaluation` with `Action` + `Reason`
  - `TimeUntilNextSession(config)`
- [ ] All edge cases (see scheduling matrix in prompt):
  - Within session + auto_resume = true → AUTO_RESUME
  - Within session + auto_resume = false → PAUSE
  - After session_start + SKIP → skip today, publish MissedSessionWindow
  - After session_start + START_LATE → start with WARN
  - After session_start + ALERT_ONLY → pause, alert
  - Before session_start → SCHEDULED
  - After session_stop → next eligible day
  - Holiday → skip (IMarketCalendarService)
  - Manually PAUSED → NEVER auto-resume
- [ ] Hangfire jobs: `strategy-session-start-check` (every min, Mon-Fri 9-15 IST), `strategy-session-stop-check`
- [ ] All scheduling transitions → `audit_log` with actor "SYSTEM"
- [ ] Unit tests: all 8+ edge cases, holiday, manually-paused guard

### Step 26 — IStartupOrchestrator
- [ ] `StartupOrchestrator : IStartupOrchestrator` — `IHostedService`
- [ ] All 11 steps (in order):
  1. Connect PostgreSQL → FATAL if fails
  2. Connect Redis → WARN if fails; degraded mode
  3. Connect RabbitMQ → WARN if fails; in-process fallback
  4. Check kill switch (Redis → DB fallback) → block orders if active
  5. Re-enqueue IN_PROGRESS Hangfire download jobs
  6. Reload strategy instances, evaluate with `IStrategyScheduler.EvaluateOnStartup()`
  7. Re-authenticate all brokers
  8. Reconcile ICapitalAllocator Redis from open positions + orders
  9. Re-subscribe broker WebSocket streams
  10. Warm candle cache (Redis) from TimescaleDB (last 500 bars)
  11. Mark READY → `/health/ready` returns healthy
- [ ] Graceful shutdown SIGTERM handler (see Architecture doc)
- [ ] Integration tests: simulate mid-session restart with auto_resume = true/false

---

## Phase 10: Execution Engines + Strategies

### Step 27 — Execution Engines
- [ ] `LiveExecutionEngine : IExecutionEngine`
  - Checks kill switch, market calendar, risk limits, capital allocation
  - Calls `IBrokerOrderClient.PlaceOrderAsync()` with Idempotency-Key
  - Records all outcomes to `signal_journal` and `orders`
- [ ] `SimulatedExecutionEngine : IExecutionEngine`
  - Forward test: simulates fills from live tick data
  - Uses `ICapitalAllocator` (realistic capital constraints)
  - No real orders placed
- [ ] `BacktestExecutionEngine : IExecutionEngine`
  - Uses `BacktestCapitalAllocator` (in-memory)
  - Simulates fills from historical candles
  - Advances `SimulatedClock`
- [ ] `IStrategyInstanceManager` — manages lifecycle, injects correct engine by mode
- [ ] NetArchTest: IStrategy implementations have zero dependency on any IExecutionEngine
- [ ] Parity test: same candles through Backtest + Simulated engine → identical SignalResult list

### Step 28 — PriceActionBreakoutStrategy
- [ ] `PriceActionBreakoutStrategy : IStrategy`
  - All params from `config_json` (no hardcoded values)
  - Entry: 15m breakout above N-bar swing high, volume filter, EMA filter, candle body % check
  - Exit: SL below signal candle low + buffer, TP = RR × SL, trailing SL, trailing TP
  - Force exit: `schedule_json.session_stop` via `IClock.Now()`
  - Risk from `RiskProfile`: MaxCapitalPerTradePercent, MaxOpenTradesPerSymbol, MaxDailyDrawdown
  - Every evaluation → `signal_journal`
  - `OUTSIDE_SCHEDULE` skipped reason when outside scheduled session
- [ ] Document every decoded assumption as a code comment
- [ ] Unit tests: buy signal with fixture candles, volume filter, EMA filter, trailing SL step logic

---

## Phase 11: Backtesting + Forward Testing

### Step 29 — Backtesting Engine
- [ ] Fully isolated in `rvs.AlgoTrader.Backtesting`
- [ ] `BacktestEngine` — replays historical candles, fires `CandleClosedEvent` one at a time
- [ ] `SimulatedClock` driven by engine (bar-by-bar advance)
- [ ] Transaction costs: from `backtest_cost_profiles` DB row
- [ ] Slippage modelled per fill
- [ ] Walk-forward testing (configurable in-sample/out-of-sample windows)
- [ ] Monte Carlo simulation (N=1000 default, configurable)
- [ ] Output metrics: gross/net PnL (separate), win rate, Sharpe, Calmar, max drawdown
- [ ] Export: CSV + PDF (QuestPDF)
- [ ] `BacktestDataSnapshotService` — SHA-256 hash of ordered OHLCV rows
- [ ] Reproducibility: `GET /api/v1/backtests/{runId}/reproduce` — re-run with same config + data hash
- [ ] Integration test: run same backtest twice → identical result_metrics and same data_hash

### Step 30 — Forward Testing Engine
- [ ] `ForwardTestEngine` — uses `SimulatedExecutionEngine`, live tick data, no real orders
- [ ] Fill simulation: market → next bar open, limit → bar range crossing, SL → bar low/high + slippage
- [ ] `forward_test_sessions`, `forward_test_trades`, `forward_test_equity_curve` persistence
- [ ] Schedule-aware: respects `schedule_json` same as live instances
- [ ] `ForwardTestHub` → real-time equity curve updates via SignalR

---

## Phase 12: Monitoring + Notifications

### Step 31 — Monitoring Alert Evaluator
- [ ] `MonitoringAlertEvaluator : IMonitoringAlertEvaluator`
- [ ] Hangfire 30s job during market hours
- [ ] All built-in metrics: broker latency, stream stale, evaluation gap, disconnect count, rejection rate, capital utilization, drawdown, strategy missed session, strategy auto-resumed
- [ ] Alert dedup via Redis `alert:dedup:{ruleId}`
- [ ] Writes to `alert_log` → publishes `MonitoringAlertTriggered`
- [ ] Integration test: inject metric above threshold → alert_log entry; second fire in window → not re-fired

### Step 32 — Notification Service
- [ ] `NotificationService : INotificationService`
- [ ] Channels: In-app toast (SignalR `AlertHub`), Telegram bot, Email (SMTP)
- [ ] Preferences from `notification_preferences` table per user
- [ ] EOD PnL report: 15:35 IST on trading days → Telegram + email
- [ ] All events in scope: order fills, SL/TP hits, drawdown limit, kill switch, stream reconnect, token expiry, data quality, cold-restart pause, monitoring breach, strategy auto-resumed, strategy missed session

---

## Phase 13: API Layer

### Step 33 — ASP.NET Core API
- [ ] Correlation ID middleware (UUID per request → all Serilog entries, MassTransit headers, broker headers)
- [ ] Global exception handler (structured error envelope, no raw exception text in production)
- [ ] Rate limiting: `OrderPolicy` (sliding window 10/s per user), `GlobalPolicy` (token bucket 100/s per IP)
- [ ] Idempotency middleware on order endpoints
- [ ] Swagger/OpenAPI 3.0 at `/swagger` (development only)

**MVC Controllers (all: `/api/v1/`, versioned, role-protected, Swagger XML comments):**
- [ ] `OrdersController` — place, modify, cancel, list (with idempotency middleware, OrderPolicy rate limit)
- [ ] `PositionsController` — list open/closed
- [ ] `StrategyInstancesController` — CRUD, start/pause/stop/schedule
- [ ] `BacktestsController` — run, list, reproduce
- [ ] `ForwardTestsController` — start/pause/stop, list sessions
- [ ] `SignalJournalController` — list with filters (strategy, symbol, signal, skipped_reason)
- [ ] `InstrumentsController` — full-text search
- [ ] `WatchlistsController` — CRUD, symbol add/remove/reorder
- [ ] `MarketCalendarController` — get, create, update holidays
- [ ] `DownloadJobsController` — enqueue, cancel, list with progress
- [ ] `ReconciliationController` — get status, trigger sync
- [ ] `AuditLogController` — paginated list with filters
- [ ] `AlertRulesController` — CRUD monitoring_alert_rules
- [ ] `AppConfigController` — get/set DB config (Admin only)
- [ ] `AuthController` — login, logout, refresh
- [ ] `BrokerController` — latency stats (`GetBrokerLatencyQuery`), connection status (`GetBrokerConnectionStatusQuery`)
- [ ] `CapitalAllocationController` — get (`GetCapitalAllocationQuery`), update (`UpdateCapitalAllocationCommand`), create (`CreateCapitalAllocationCommand`)
- [ ] `BacktestCostProfilesController` — CRUD for `backtest_cost_profiles` (Admin only); used to select cost model in `BacktestRequestDto.CostProfileId`
- [ ] `SymbolDataPreferencesController` — get/update per-symbol download preferences; triggers `UpdateSymbolDataPreferencesCommand`

**Minimal APIs:**
- [ ] `GET /health/live`
- [ ] `GET /health/ready`
- [ ] `GET /metrics`

**SignalR Hubs:**
- [ ] `QuoteHub` — live tick streaming per symbol
- [ ] `StrategyHub` — signals, strategy status, schedule events, auto-resume notifications
- [ ] `ForwardTestHub` — equity curve updates
- [ ] `AlertHub` — monitoring alert breaches, all notification events

---

## Phase 14: Frontend

### Step 34 — React Application Setup
- [ ] Vite 6 + React 19 + TypeScript 5.4+ project scaffold
- [ ] Tailwind CSS v4 + shadcn/ui setup
- [ ] Zustand 5 store structure (auth, strategy, positions, alerts, ui)
- [ ] TanStack Query v5 setup with API client
- [ ] SignalR client setup (`@microsoft/signalr`)
- [ ] React Router v6 with role-based route guards
- [ ] Dark/light theme toggle (persists in localStorage)
- [ ] Global error boundary + toast notification system
- [ ] **Timezone-aware rendering (REQUIRED):**
  - User's timezone read from JWT `"tz"` claim (set on login from `user_preferences.timezone`)
  - Stored in Zustand `auth` store as `userTimezone` (IANA key, e.g. `"America/Chicago"`)
  - All timestamps from API (UTC ISO strings) rendered via `Intl.DateTimeFormat` with `userTimezone`
  - Never use `new Date().toLocaleString()` without an explicit `timeZone` option
  - Schedule Editor: shows IST times for all session_start/session_stop fields + live conversion hint
    (`<SessionTimeDisplay istTime="09:20" userTimezone={userTimezone} />` → "09:20 IST (22:50 CST)")
  - Settings panel: timezone selector (IANA key picker) — saves via `UpdateUserPreferencesCommand`
  - On timezone change: update JWT claim server-side and refresh JWT; update Zustand store
  - Add `date-fns-tz` or `Luxon` for timezone-safe date math (avoid raw `Date` arithmetic)

### Step 35 — All 18 Dashboard Panels
- [ ] **1. Dashboard Overview** — live PnL, margin, open positions, active instances, alerts feed, kill-switch button (Admin), broker badges, cold-restart notice
- [ ] **2. Chart View** — candlestick (Lightweight Charts v5), timeframe selector, EMA/SMA/VWAP/volume overlays, SignalR streaming, backtest/forward-test markers, partial bar labelled
- [ ] **3. Order Panel** — buy/sell form, order type, trailing SL/TP, margin check, auto Idempotency-Key, 429 feedback
- [ ] **4. Strategy Instance Manager** — CRUD, schedule editor (days, session window in IST with local-time conversion hint, auto_resume toggle, missed_session_behavior selector, force_exit toggle), next session countdown
- [ ] **5. Backtest Panel** — run, equity curve (gross + net), walk-forward, Monte Carlo fan, trade list, data hash, reproduce + diff view, CSV/PDF export
- [ ] **6. Forward Test Panel** — start/pause/stop, live equity curve via SignalR, trade log, schedule-aware
- [ ] **7. Performance Comparison Panel** — backtest vs forward vs live side-by-side, overfit flag
- [ ] **8. Signal Journal Viewer** — all evaluations, filter by skipped_reason (incl. OUTSIDE_SCHEDULE)
- [ ] **9. Broker Latency Dashboard** — p50/p95/p99, heatmap by time of day, rate-limit bucket status
- [ ] **10. Historical Download Panel** — enqueue, live chunk progress bar via SignalR, resume/cancel
- [ ] **11. Watchlist Manager** — CRUD, full-text symbol search, drag-to-reorder
- [ ] **12. Position Reconciliation Panel** — sync time, status per broker, discrepancy list, auto-sync toggle
- [ ] **13. Data Quality Panel** — gap/bad-candle/spike report, warning badges on affected symbols
- [ ] **14. Logs Viewer** — structured log stream, filter by level/strategy/symbol/correlationId
- [ ] **15. Audit Log Viewer** — paginated, filterable (Admin/Viewer), scheduling events visible
- [ ] **16. Alert Rules Manager** — CRUD monitoring_alert_rules, active breach badge count in header
- [ ] **17. Scheduling Panel** — per-instance session window, missed_session_behavior, next session countdown, history of auto-resume/missed events
- [ ] **18. Settings** — broker credentials (masked, Admin), notification prefs, **timezone selector (IANA key, default "Asia/Kolkata")**, theme, user management (Admin), DB config editor, Vault status

---

## Phase 15: Testing

### Step 36 — Unit Tests (rvs.AlgoTrader.UnitTests)
All tests per spec (see STRATEGY.md testing requirements):
- [ ] PriceActionBreakoutStrategy signal generation
- [ ] CandleAggregatorService bar boundary + partial candle guard
- [ ] TrailingStopLossService step logic
- [ ] RiskManagementService drawdown + position-size
- [ ] HistoricalDownloadChunker date-range splitting
- [ ] InstrumentTokenResolver
- [ ] TransactionCostCalculator (all components)
- [ ] MarketCalendarService via SimulatedClock
- [ ] KillSwitchService dual-write + reset
- [ ] ForwardTestFillSimulator fill rules
- [ ] All IIncrementalIndicator implementations
- [ ] IIndicatorService batch methods
- [ ] DataQualityService gap + bad-candle detection
- [ ] ISecretsProvider implementations
- [ ] IFieldEncryptionService AES-256 round-trip
- [ ] ICapitalAllocator concurrent reservation safety
- [ ] IdempotencyMiddleware (same key → cached; missing key → 400)
- [ ] IStrategyScheduler all edge cases + holiday + manually-paused guard
- [ ] SimulatedClock advance/AdvanceTo correctness
- [ ] NetArchTest architecture rules (including no-direct-clock-call assertion in Domain/Application/Strategies)
- [ ] ForwardTestFillSimulator fill rules (market, limit, SL, TP, partial, no-fill)
- [ ] TrailingStopLossService step logic, activation threshold, no SL regression
- [ ] RiskManagementService each limit check (drawdown, per-trade capital, trades-per-day, open-per-symbol)

### Step 37 — Integration Tests (rvs.AlgoTrader.IntegrationTests)
All tests per spec (see STRATEGY.md):
- [ ] Full order placement → fill → position update (real PostgreSQL)
- [ ] Idempotency: same key twice → cached response, no duplicate in DB
- [ ] 429 handling in historical download
- [ ] Master data upsert
- [ ] SignalR tick broadcast
- [ ] Kill switch: cancel orders, stop instances, dual-write verified
- [ ] Reconciliation: mismatch → event → alert_log
- [ ] Forward test session trades + equity curve
- [ ] EOD report → mock Telegram
- [ ] Redis AOF: kill-switch flag survives Redis restart
- [ ] ICapitalAllocator: concurrent requests → only one succeeds
- [ ] IStartupOrchestrator: auto_resume = true/false scenarios
- [ ] missed_session_behavior = SKIP / START_LATE
- [ ] Backtest reproducibility (same result + data hash)
- [ ] Parity test: Backtest + Simulated engines → identical signals
- [ ] Rate limiting 429 on OrderPolicy

### Step 38 — Playwright UI Tests
All tests per spec (see STRATEGY.md):
- [ ] Login all three roles, role-based visibility
- [ ] Chart + timeframe switch + partial bar label
- [ ] Order form validation + fill toast
- [ ] Backtest run + reproduce + diff
- [ ] Forward test SignalR equity curve
- [ ] Performance comparison panel + overfit flag
- [ ] Signal journal filter by OUTSIDE_SCHEDULE
- [ ] Kill switch (Admin only)
- [ ] Strategy lifecycle: create → schedule → start → pause → stop
- [ ] Scheduling Panel: next session countdown + missed event
- [ ] Strategy Manager: schedule selectors save to DB
- [ ] Watchlist CRUD + drag reorder
- [ ] Download panel: progress bar + cancel
- [ ] Audit log pagination + scheduling events
- [ ] Alert Rules Manager: create + toggle + badge
- [ ] Theme toggle persists
- [ ] Cold-restart notice banner

---

## Post-Generation Checklist

- [ ] Run `hooks/post-generate.sh` — all validations pass
- [ ] `dotnet build rvs.AlgoTrader.sln` — zero errors, zero warnings
- [ ] `dotnet test` — all tests green
- [ ] `npm run build` — frontend builds
- [ ] `docker compose up --build` — all services healthy
- [ ] `/health/ready` returns 200
- [ ] Swagger loads at `/swagger`
- [ ] NetArchTest architecture tests pass
