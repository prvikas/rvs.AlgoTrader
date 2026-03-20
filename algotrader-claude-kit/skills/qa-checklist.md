# Skill: QA Checklist — AlgoTrader

## Purpose
Structured quality gates for every component type in the AlgoTrader platform.
Load this skill when reviewing, verifying, or testing any generated component.
Run the relevant checklist BEFORE marking any PLAN.md step as complete.

---

## How to Use

For each generated component, find its type below and run through every item.
A component is NOT done until its full checklist passes.

---

## QA-01: Domain Entity or Value Object

```
□ Located in rvs.AlgoTrader.Domain/Entities/ or /ValueObjects/
□ Zero external package dependencies (no NuGet beyond .NET BCL)
□ Immutable where possible (init-only properties, records)
□ Domain invariants enforced in constructor (throw DomainException if invalid)
□ No EF Core annotations in domain entity (use EF Config class in Infrastructure instead)
□ No DateTime — only Instant/ZonedDateTime/DateOnly from NodaTime
□ IEquatable<T> or record equality where needed
□ Domain event raised on state-changing operations (e.g., order filled)
□ Unit test covering invariant violation (wrong input → DomainException)
□ NetArchTest still passes after addition
```

---

## QA-02: MediatR Command or Query

```
□ Located in rvs.AlgoTrader.Application/Commands/ or /Queries/
□ FluentValidation validator exists in /Validators/ with same name + "Validator" suffix
□ All required properties validated (not-null, range, format checks)
□ Handler located in /Handlers/ with matching name + "Handler" suffix
□ Handler has CancellationToken parameter on HandleAsync
□ Handler does NOT reference EF Core DbContext (uses repository interfaces only)
□ Handler does NOT reference Infrastructure implementations
□ DTO result type defined in /DTOs/ if handler returns data
□ Mapperly mapper defined in /Mappers/ for entity ↔ DTO conversion
□ Unit test: valid input → correct result
□ Unit test: invalid input → validator returns expected errors
□ Unit test: repository mock returns expected data → handler maps to correct DTO
```

---

## QA-03: Repository (Infrastructure)

```
□ Interface defined in rvs.AlgoTrader.Application/Interfaces/ (not Infrastructure)
□ Implementation in rvs.AlgoTrader.Infrastructure/Persistence/Repositories/
□ All queries against candles/forward_test_equity_curve include time range filter
□ audit_log repository: INSERT-only (no Update, no Delete methods defined)
□ CancellationToken passed to all EF async methods (ToListAsync(ct), etc.)
□ No raw SQL unless genuinely necessary (document why)
□ Indexes exist in EF configuration for all filtered/sorted columns
□ Integration test: real PostgreSQL via Testcontainers
□ Integration test: Respawn resets DB between tests
□ No N+1 query (use Include() where needed, or explicit second query)
□ TimescaleDB hypertables created after initial migration (select create_hypertable call)
```

---

## QA-04: Redis Infrastructure Component

```
□ Connection via IDatabase from IConnectionMultiplexer (injected, not static)
□ Redis key pattern documented in ARCHITECTURE.md Redis Key Patterns table
□ TTL set correctly per responsibility matrix in CLAUDE.md
□ AOF-required keys: no expiry + verified in Redis config (appendonly yes)
□ Cache miss → falls through to DB (never return null silently)
□ Append to candle cache is idempotent (ZADD NX — no duplicate scores)
□ Capital reservation uses Lua script (single round-trip, atomic)
□ Integration test: real Redis via Testcontainers
□ Integration test: AOF-required key survives Redis restart (kill + restart container)
□ Graceful degradation: Redis.StackExchange.ConnectionException caught → WARN log → fallback
```

---

## QA-05: Broker Client Adapter

```
□ Implements IFullBrokerClient (all sub-interfaces)
□ HTTP client registered via IHttpClientFactory (not new HttpClient())
□ Polly policies attached: retry 3× exponential, circuit breaker, timeout 10s
□ Retry-After header respected on 429 responses
□ All calls wrapped in BrokerLatencyMiddleware (latency recorded to broker_latency_log)
□ SessionAwareBrokerClient decorator applied (transparent token refresh)
□ ReconnectingBrokerStreamClient decorator applied (WebSocket reconnect)
□ Secrets loaded via ISecretsProvider (no hardcoded API keys)
□ WebSocket: uses IAsyncEnumerable + CancellationToken for streaming
□ All broker tokens stored encrypted (IFieldEncryptionService) in Redis + DB
□ Unit test: 429 → Polly retries with backoff
□ Unit test: 401 → token refresh attempted once before failing
□ Unit test: circuit breaker opens after N consecutive failures
□ BrokerName property returns correct string matching appsettings.json key
```

---

## QA-06: IStrategy Implementation

```
□ Located in rvs.AlgoTrader.Strategies/
□ Implements IStrategy with correct Name and Metadata
□ All params loaded from config_json (zero hardcoded numbers)
□ IClock injected — no DateTime.Now
□ IIncrementalIndicator.Update(closedCandle) called BEFORE EvaluateAsync logic
□ Only closed candles used (ctx.CandlesByTimeframe never contains open bar)
□ SignalResult returned for ALL cases: BUY, SELL, HOLD (never null, never throw)
□ Diagnostics dictionary populated with key indicator values (for signal_journal)
□ Force-exit check: IClock.Now() compared to schedule_json.session_stop
□ Risk profile limits checked (MaxCapitalPerTrade, MaxOpenTrades, MaxDailyDrawdown)
□ skipped_reason = OUTSIDE_SCHEDULE when outside scheduled session window
□ Every code assumption documented as a comment (especially "decoded" logic)
□ Zero dependency on any IBrokerXxxClient, IExecutionEngine (NetArchTest enforces)
□ Unit test: fixture candles that SHOULD trigger BUY → BuySignal returned
□ Unit test: fixture candles below volume threshold → HOLD returned
□ Unit test: force-exit time check via SimulatedClock
□ Parity test: same candles through BacktestEngine + ForwardEngine → identical signals
```

---

## QA-07: API Controller (MVC)

```
□ Located in rvs.AlgoTrader.API/Controllers/
□ Route: /api/v1/{resource} (versioned)
□ [ApiController] and [Route] attributes applied
□ [Authorize(Roles = "...")] on class or per action (correct role per STRATEGY.md)
□ [EnableRateLimiting("OrderPolicy")] on order endpoints
□ IdempotencyMiddleware applied to POST /api/v1/orders
□ All actions use MediatR dispatch (no direct service calls for complex operations)
□ All responses wrapped in ApiResponse<T> with correlationId
□ Swagger XML comments (///<summary>) on every public action
□ 400, 401, 403, 404, 422, 429, 500 all return ApiResponse envelope (no raw exceptions)
□ CancellationToken HttpContext.RequestAborted passed to all MediatR.Send() calls
□ Integration test: authenticated request → correct response shape
□ Integration test: unauthenticated request → 401
□ Integration test: wrong role → 403
□ Integration test: invalid input → 400 with validation errors in envelope
□ Playwright test: form submits correctly and shows expected UI response
```

---

## QA-08: SignalR Hub

```
□ Located in rvs.AlgoTrader.API/Hubs/
□ [Authorize] attribute applied (no anonymous connections)
□ JWT sent via ?access_token= query param from React (SignalR convention)
□ Hub methods use CancellationToken (stoppingToken from IHostedService if applicable)
□ React client connects with onreconnected handler
□ React client disconnects gracefully on component unmount (connection.stop())
□ Integration test: hub broadcasts tick → connected client receives it
□ No database calls inside hub methods (publish via MassTransit or channel, not direct DB write)
```

---

## QA-09: Hangfire Background Job

```
□ Job interface defined in Application layer (e.g., IReconciliationJob)
□ Implementation in Infrastructure layer
□ [DisableConcurrentExecution(timeoutSeconds)] applied for jobs that must not overlap
□ CancellationToken used on all async calls inside the job
□ Market hours check: IMarketCalendarService.IsWithinMarketHoursAsync() called first (where relevant)
□ IClock used (not DateTime.Now) for any time comparisons
□ Hangfire cron expression matches IST schedule requirements (appsettings.json, not hardcoded)
□ Job registered in Hangfire setup extension (recurring or one-off)
□ Max retry configured (not infinite)
□ On max retries exceeded: alert sent via INotificationService + alert_log entry
□ Integration test: job runs → expected side effects (DB rows, events) verified
```

---

## QA-10: Scheduled Strategy Session (IStrategyScheduler)

```
□ All 8+ edge cases from PLAN.md scheduling matrix tested
□ auto_resume = true + within session → AUTO_RESUME
□ auto_resume = false + within session → PAUSE
□ After session_start + SKIP → skip today, MissedSessionWindow event published
□ After session_start + START_LATE → start with WARN log
□ After session_start + ALERT_ONLY → remain PAUSED, alert sent
□ Before session_start → SCHEDULED, no action
□ After session_stop → schedule next eligible day
□ Market holiday → IMarketCalendarService returns false → skip regardless
□ Manually PAUSED before shutdown → never auto-resumed (even if auto_resume = true)
□ Kill switch active → never auto-resume (overrides all other settings)
□ All transitions logged to audit_log with actor "SYSTEM" and before/after state
□ Scheduling Panel in React shows: next session countdown, history of events
```

---

## QA-11: React Panel / Component

```
□ data-testid attributes on every interactive and key display element
□ Loading state shown while API call is in-flight (skeleton or spinner)
□ Error state shown when API call fails (toast or inline error)
□ Empty state shown when list is empty (not blank white space)
□ Role-based visibility enforced (check user role from auth store before rendering)
□ Dark/light theme respected (uses Tailwind CSS variables, not hardcoded colors)
□ Mobile responsive (test at 375px, 768px, 1440px breakpoints)
□ SignalR connection: reconnects on disconnect, shows stale badge if disconnected > 10s
□ Idempotency-Key: generated per form submission (NOT on component mount)
□ TanStack Query: correct staleTime, refetchInterval, and invalidation on mutations
□ No console.error or console.warn in production build
□ Playwright test covers: render, user interaction, API response, error state
```

---

## QA-12: Audit Log Coverage Verification

Run this after any feature that involves orders, config changes, or auth:

```bash
# Check audit_log writes exist for these mandatory events:
grep -rn "AuditAction.OrderPlaced\|ORDER_PLACED" src/ --include="*.cs"
grep -rn "AuditAction.KillSwitchActivated\|KILL_SWITCH_ACTIVATED" src/ --include="*.cs"
grep -rn "AuditAction.StrategyStarted\|STRATEGY_STARTED" src/ --include="*.cs"
grep -rn "AuditAction.Login\|LOGIN" src/ --include="*.cs"
grep -rn "AuditAction.ConfigChanged\|CONFIG_CHANGED" src/ --include="*.cs"
grep -rn "AuditAction.TokenRefreshed\|TOKEN_REFRESHED" src/ --include="*.cs"
grep -rn "AuditAction.StrategyAutoResumed\|STRATEGY_AUTO_RESUMED" src/ --include="*.cs"
grep -rn "AuditAction.ReconciliationMismatch\|RECONCILIATION_MISMATCH" src/ --include="*.cs"
```

Each must return at least one result in the relevant service/handler.

---

## QA-13: Full Integration Smoke Test

Run after completing a full phase (e.g., Phase 6: Broker Adapters):

```bash
# 1. Start all infrastructure
docker compose up -d

# 2. Apply migrations
dotnet ef database update -p src/rvs.AlgoTrader.Infrastructure -s src/rvs.AlgoTrader.API

# 3. Build
dotnet build rvs.AlgoTrader.sln -p:TreatWarningsAsErrors=true

# 4. Run all tests
dotnet test rvs.AlgoTrader.sln --logger "console;verbosity=normal"

# 5. Start API + check health
cd src/rvs.AlgoTrader.API && dotnet run &
sleep 5
curl -s http://localhost:5000/health/live | jq
curl -s http://localhost:5000/health/ready | jq

# 6. Run post-generate hook
cd ../../ && ./hooks/post-generate.sh

# Expected: all tests green, /health/ready returns { status: "healthy" }
```

---

## QA-14: Performance Spot-Check

After implementing incremental indicators or candle pipeline:

```bash
# Run BenchmarkDotNet if benchmarks exist
cd benchmarks/rvs.AlgoTrader.Benchmarks
dotnet run -c Release -- --filter *Incremental*

# Target: IncrementalEMA.Update() < 100ns
# Target: ICandleCache.GetAsync() (cache hit) < 5ms
```

Check Redis memory usage doesn't grow unboundedly:
```bash
docker exec algotrader-redis redis-cli INFO memory | grep used_memory_human
# Candle cache should stay bounded (rolling 500 bars per key)
```

---

## QA-15: Security Spot-Check

Run before any PR that touches auth, secrets, or broker credentials:

```bash
# No secrets in source
grep -rn --include="*.cs" --include="*.json" \
  -iE '(password|apikey|api_key|secret|token)\s*[=:]\s*"[^{$]' \
  src/ --exclude="appsettings.Development.json"

# All [Authorize] attributes present on controllers
grep -rL "\[Authorize" src/rvs.AlgoTrader.API/Controllers/*.cs

# Hangfire dashboard requires auth in non-dev
grep -rn "HangfireAdminAuthFilter\|DashboardOptions" src/rvs.AlgoTrader.API/ --include="*.cs"

# Swagger disabled in production
grep -rn "UseSwagger\|SwaggerEnabled" src/rvs.AlgoTrader.API/Program.cs
```
