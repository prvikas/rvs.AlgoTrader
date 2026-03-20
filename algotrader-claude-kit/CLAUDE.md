# CLAUDE.md — AlgoTrader Project Memory

> This file is loaded automatically by Claude Code at the start of every session.
> It enforces architectural contracts, lists known anti-patterns, and teaches from past mistakes.
> **Never delete or abbreviate this file.** Append new lessons at the bottom under `## Lessons Learned`.

---

## 🧠 Project Identity

**Project:** AlgoTrader — Production-grade multi-broker algo-trading platform for Indian markets  
**Namespace root:** `rvs.AlgoTrader`  
**Stack:** .NET 9 / C# 13 backend · React 19 + TypeScript frontend · PostgreSQL + TimescaleDB · Redis · RabbitMQ  
**Architecture:** Clean Architecture · Modular Monolith · Three bounded contexts  
**SEBI-compliant** | **100% OSS stack**

> **Namespace rule:** Every C# file in this codebase uses `namespace rvs.AlgoTrader.<Layer>.<Folder>`.  
> Every `<RootNamespace>` and `<AssemblyName>` in every `.csproj` starts with `rvs.AlgoTrader`.  
> See `scaffolding/csproj-templates.md` for all project files with correct namespaces.

---

## ⛔ HARD RULES — Never Violate These

### 1. Clock Rule — ZERO direct system clock access
```csharp
// ❌ FORBIDDEN — in any file, any layer
DateTime.Now
DateTimeOffset.UtcNow
NodaTime.SystemClock.Instance.GetCurrentInstant()  // directly in business logic
ZonedDateTime.Now

// ✅ REQUIRED — always inject IClock
private readonly IClock _clock;
var now = _clock.Now();           // ZonedDateTime (IST)
var instant = _clock.NowInstant(); // Instant (for persistence)
var today = _clock.Today();        // DateOnly (IST)
```
Enforce via: code review + optional Roslyn analyzer.  
Reason: SimulatedClock must control time in backtest/forward-test/unit-tests. Any static clock call breaks determinism.

### 2. No Cross-Context Direct Calls
```
Trading Execution  →  NEVER calls  →  Data Ingestion or Backtesting services directly
Backtesting Engine →  NEVER calls  →  any IBrokerOrderClient or IFullBrokerClient
Data Ingestion     →  NEVER calls  →  IBrokerOrderClient.PlaceOrderAsync()
```
Communication allowed ONLY via: domain events (MassTransit), shared Redis candle cache, or shared DB reads.

### 3. No Partial Candle in Strategy Evaluation
```csharp
// ❌ FORBIDDEN — passing open/current bar to IStrategy.EvaluateAsync
context.CandlesByTimeframe["15m"].Add(currentOpenBar); // NEVER

// ✅ REQUIRED — only fully closed candles
// CandleAggregatorService emits CandleClosedEvent only on bar boundary crossing
// StrategyContext.CandlesByTimeframe contains ONLY fully closed candles
```

### 4. MVC for Business Endpoints; Minimal API for Infrastructure Only
```csharp
// ✅ Business routes → MVC Controllers with [ApiController], versioned, role-protected
[Route("api/v1/orders")]
public class OrdersController : ControllerBase { }

// ✅ Infrastructure → Minimal APIs
app.MapGet("/health/live", ...);
app.MapGet("/health/ready", ...);
app.MapGet("/metrics", ...);
app.MapGet("/hangfire/trigger", ...);
```

### 5. Idempotency-Key Required on Order Placement
```http
POST /api/v1/orders
Idempotency-Key: 550e8400-e29b-41d4-a716-446655440000
```
Missing header → 400 Bad Request.  
Server checks Redis `idempotency:{userId}:{key}` (TTL 24h) before processing.

### 6. Kill Switch Dual-Write
Every kill-switch activation MUST write to BOTH:
- Redis key `killswitch:active` (no expiry, AOF-persisted)
- `app_config` table (`key = 'KillSwitchActive'`)

Every order placement checks kill-switch: Redis first → DB fallback.

### 7. Capital Reservation is Atomic
```lua
-- Redis Lua script — atomic check-and-reserve
-- NEVER use non-atomic read-then-write for capital reservation
local available = tonumber(redis.call('GET', KEYS[1])) - tonumber(redis.call('GET', KEYS[2]))
if available >= tonumber(ARGV[1]) then
    redis.call('INCRBY', KEYS[2], ARGV[1])
    return 1
end
return 0
```

### 8. Response Envelope on ALL Business Endpoints
```json
{ "success": true, "data": {}, "error": null, "correlationId": "uuid" }
```
Never return raw exception text. Never skip the envelope for 4xx/5xx.

### 9. Audit Log is Append-Only (SEBI Compliance)
```sql
-- NEVER run UPDATE or DELETE on audit_log
-- All changes to orders, config, strategies → INSERT to audit_log with before_json + after_json
```

### 10. No DateTime — Always NodaTime
```csharp
// ❌ FORBIDDEN
DateTime, DateTimeOffset  // in domain/application/strategy code

// ✅ REQUIRED
Instant        // persistence (stored as TIMESTAMPTZ)
ZonedDateTime  // IST-aware display and scheduling
LocalDate      // dates without time (NodaTime)
DateOnly       // .NET DateOnly is acceptable for simple date-only use
```

### 11. IStartupOrchestrator — All 11 Steps in Order
The `IStartupOrchestrator.RunAsync(ct)` implementation MUST execute all 11 steps in this exact order:
```
Step  1: Connect PostgreSQL        — FATAL if fails; abort startup with structured log
Step  2: Connect Redis             — WARN if fails; continue in "Redis-degraded" mode
                                     (rate limiting disabled; kill-switch reads from DB only)
Step  3: Connect RabbitMQ          — WARN if fails; use in-process synchronous event
                                     fallback for critical paths (order fills, kill-switch)
Step  4: Check kill-switch         — Redis first, app_config DB fallback
                                     Set a "kill_switch_was_active" flag for Step 6 to read
Step  5: Re-enqueue Hangfire jobs  — Re-queue any IN_PROGRESS historical download jobs
Step  6: Reload strategy instances — Evaluate each instance scheduling state:
           - kill_switch_was_active = true → ALL instances stay STOPPED (never auto-resume)
           - RUNNING + auto_resume_on_restart=true + within session → auto-resume
           - RUNNING + auto_resume_on_restart=false → restore to PAUSED, require manual start
           - RUNNING + outside session → restore to SCHEDULED
           - SCHEDULED/PAUSED → restore as-is
Step  7: Re-authenticate brokers   — Re-auth all brokers with stored encrypted session tokens
Step  8: Reconcile capital state   — Rebuild ICapitalAllocator Redis counters from open
                                     positions + OPEN status orders in DB
Step  9: Re-subscribe WebSockets   — Reconnect broker WebSocket streams for all active
                                     watchlist symbols
Step 10: Warm candle cache         — Populate Redis sorted sets from TimescaleDB for
                                     all active watchlist symbols (last 500 bars)
Step 11: Mark READY                — /health/ready returns healthy; log startup complete
```
**AP-015 reminder:** Kill-switch check in Step 4 MUST set a flag that Step 6 reads. Never auto-resume any instance if kill switch was active at startup.

### 12. Graceful Shutdown — SIGTERM Handler (6 Steps)
The `IHostApplicationLifetime.ApplicationStopping` handler MUST execute all 6 steps:
```
Step 1: Stop accepting new orders and signal evaluations
        (set a "shutting_down" flag; strategy eval queue rejects new work)
Step 2: Complete in-flight strategy evaluations
        (drain the Channels queue with a timeout — default 30s)
Step 3: Persist SimulatedClock state
        (for any running forward test sessions — save current simulated time to DB)
Step 4: Write shutdown event to audit_log
        (actor="SYSTEM"; include list of all running instances at shutdown time;
         IStartupOrchestrator Step 6 reads this on next start)
Step 5: Close all broker WebSocket connections
        (send graceful close frame per broker client)
Step 6: Flush Serilog buffers
        (Log.CloseAndFlush() — MUST be last; ensures all logs land before process exits)
```

### 13. Rate Limiting — Exact Configuration
```csharp
builder.Services.AddRateLimiter(options =>
{
    // Per-user sliding window: 10 requests/second on order endpoints
    options.AddPolicy("OrderPolicy", ctx =>
        RateLimitPartition.GetSlidingWindowLimiter(
            ctx.User?.Identity?.Name ?? ctx.Connection.RemoteIpAddress?.ToString(),
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromSeconds(1),
                SegmentsPerWindow = 2
            }));

    // Global token bucket: 100 tokens/second per IP
    options.AddPolicy("GlobalPolicy", ctx =>
        RateLimitPartition.GetTokenBucketLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 100,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                TokensPerPeriod = 100,
                AutoReplenishment = true
            }));

    options.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.StatusCode = 429;
        await ctx.HttpContext.Response.WriteAsJsonAsync(
            new { success = false, error = "Rate limit exceeded" }, ct);
    };
});
// Apply [EnableRateLimiting("OrderPolicy")] on OrdersController
// Apply GlobalPolicy globally via app.UseRateLimiter() middleware
```

### 14. No Over-Engineering — KISS First
```
❌ FORBIDDEN:
- Introducing abstractions, interfaces, or patterns without concrete justification
- Wrapping a 3-line DB read in a Command + Handler + Validator
- Adding indirection layers "for future flexibility" with no near-term use case

✅ REQUIRED:
- Simplest solution that is correct and maintainable wins
- Plain service class for CRUD — no MediatR, no extra abstraction
- Add abstraction only when complexity genuinely justifies it (multiple implementations,
  cross-cutting concerns, testability requirement)
- SOLID, DRY, KISS applied PRAGMATICALLY — not dogmatically
```
Reason: Every unnecessary abstraction is future maintenance debt. MediatR is justified for
order placement and strategy lifecycle. It is NOT justified for watchlist reads.

### 15. Dual-Timezone Rule — IST for Trading, User Timezone for Display

The system operates across two timezones simultaneously. **Never collapse them.**

```
┌─────────────────────────────────────────────────────────────────────────┐
│  TIMEZONE LAYER RULES                                                   │
│                                                                         │
│  IST (Asia/Kolkata, UTC+5:30)                                           │
│  ✅ All trading operations: market hours, bar boundaries, scheduling     │
│  ✅ IClock.Now() always returns ZonedDateTime in IST                     │
│  ✅ schedule_json.session_start / session_stop always in IST             │
│  ✅ IMarketCalendarService.IsWithinMarketHours() uses IST               │
│  ✅ CandleAggregatorService bar boundary detection uses IST              │
│  ✅ Hangfire job expressions (Zerodha login, EOD report) reference IST  │
│                                                                         │
│  UTC (Instant)                                                          │
│  ✅ ALL persistence — TIMESTAMPTZ columns store UTC Instant             │
│  ✅ ALL API response timestamps — DateTimeOffset serialized as UTC      │
│  ✅ MassTransit message headers — UTC only                              │
│  ✅ Serilog structured log timestamps — UTC                             │
│                                                                         │
│  User's Local Timezone (e.g. CST = America/Chicago)                    │
│  ✅ Stored in user_preferences.timezone (IANA key, e.g. "America/Chicago")
│  ✅ Used by React frontend ONLY — converts UTC API timestamps to local  │
│  ✅ Notification emails/Telegram — include BOTH IST and local time      │
│  ❌ NEVER used in backend trading logic                                 │
│  ❌ NEVER used in IClock, scheduling, or market hours checks            │
└─────────────────────────────────────────────────────────────────────────┘
```

**The schedule_json timezone field is ALWAYS "Asia/Kolkata"** — market sessions are
IST-relative by definition. The frontend Schedule Editor shows session times in IST
and renders a live conversion hint in the user's local timezone:
```
Session: 09:20 – 15:10 IST  (22:50 – 04:40 CST previous day / same day)
```

**API timestamp contract:**
```csharp
// ✅ All DateTimeOffset fields in API responses are UTC
// Frontend converts to user's local timezone using Intl.DateTimeFormat

// Backend: always persist and return UTC
var dto = new OrderDto(
    PlacedAt: order.PlacedAt.ToDateTimeOffset(),  // UTC
    FilledAt: order.FilledAt?.ToDateTimeOffset()   // UTC
);

// Frontend: render in user's local timezone (from JWT claim or user preferences)
const localTime = new Intl.DateTimeFormat(undefined, {
    timeZone: userPreferences.timezone,  // e.g. "America/Chicago"
    dateStyle: 'short',
    timeStyle: 'medium'
}).format(new Date(order.placedAt));
```

**Notification dual-time format (required):**
```
📈 RELIANCE BUY signal triggered
Time: 10:45 IST / 00:15 CST
Session: 09:20–15:10 IST active
```

**Anti-patterns to avoid:**
```csharp
// ❌ WRONG — schedule stored as user's local time
schedule_json.session_start = "03:50";   // CST equivalent of 09:20 IST — NEVER do this

// ❌ WRONG — comparing user time with IST market hours
if (userLocalTime.Hour >= 9 && userLocalTime.Hour < 15) // WRONG timezone

// ✅ CORRECT — always compare in IST via IClock
var istNow = _clock.Now();              // ZonedDateTime in IST
var istHour = istNow.Hour;             // use this for market checks
```

---

### 16. OpenAlgo Is the Reference Implementation for All Broker Integrations

**Repository:** https://github.com/marketcalls/openalgo
**When to use:** Every time you implement or modify `ZerodhaClient`, `UpstoxClient`, `MStockClient`,
`IBrokerSessionManager`, broker authentication flows, historical data fetching, or WebSocket streaming,
you MUST consult the OpenAlgo repository as the canonical reference.

```
Consult OpenAlgo for:
✅ Zerodha auth flow — request_token → access_token exchange, TOTP-based daily login
✅ Upstox OAuth2 flow — authorization_code → access_token, refresh_token handling
✅ mStock authentication — login endpoint, session management, token refresh
✅ Historical data endpoint paths, query params, response JSON shapes per broker
✅ WebSocket subscription message formats (Zerodha binary, Upstox protobuf, mStock JSON)
✅ Instrument/token master download endpoints and CSV/JSON formats per broker
✅ Order placement, modification, and cancellation request/response shapes
✅ Rate limit headers (X-RateLimit-*) and 429 Retry-After behavior per broker
✅ Position and holdings endpoints, response field names for normalization

❌ NEVER invent broker API request shapes from memory
❌ NEVER guess field names — always verify against OpenAlgo source or broker docs
```

**How to use:** When implementing a broker adapter, read the corresponding adapter in OpenAlgo
(`openalgo/broker/zerodha/`, `openalgo/broker/upstox/`, `openalgo/broker/mstock/`) to get
the exact API paths, auth headers, request payloads, and response parsing logic.
Translate from Python (OpenAlgo is Python) to C# using the same data contracts.

---

### 17. No Event Sourcing, No CQRS Read Models, No GraphQL
```
❌ FORBIDDEN in this codebase:
- Event sourcing (no event store, no event replay architecture)
- CQRS read-model projections (no separate read-model DB or materialised views)
- GraphQL (REST + SignalR only for all client communication)

✅ REQUIRED:
- All reads go directly to PostgreSQL via repository interfaces
- All real-time push uses SignalR hubs
- REST endpoints follow the versioned /api/v1/ MVC pattern
```
Reason: These patterns add significant complexity. The domain does not require them.
Direct repository reads are correct and sufficient for all current requirements.

### 18. IStrategy Must Not Access DB, Redis, or External Services
```csharp
// ❌ FORBIDDEN inside any IStrategy.EvaluateAsync implementation:
_dbContext.Orders.ToListAsync();           // NO direct DB access
_redis.GetAsync("some:key");               // NO direct Redis access
_httpClient.GetAsync("https://...");       // NO external HTTP calls
new HttpClient().GetAsync(...);            // NO

// ✅ ALLOWED inside IStrategy.EvaluateAsync:
// - Read from StrategyContext (pre-built from closed candles + indicators)
// - Call IIncrementalIndicator.Update() / .Current
// - Read from ctx.Config (deserialized config_json, already loaded)
// - Call _clock.Now() for time checks
// - Return SignalResult (never null)
```
Reason: Strategies must be deterministic and fast. Any I/O inside EvaluateAsync breaks
backtest parity, adds latency to the hot path, and makes testing impossible without mocks.

### 19. No EF Core in Application Layer
```csharp
// ❌ FORBIDDEN in rvs.AlgoTrader.Application:
using Microsoft.EntityFrameworkCore;       // NO EF Core namespace
DbContext, DbSet<T>                        // NO EF types
.Include(), .ThenInclude()                 // NO EF navigation

// ✅ REQUIRED:
// Application layer defines repository INTERFACES only:
public interface IOrderRepository { Task<Order?> GetByIdAsync(Guid id, CancellationToken ct); }
// EF implementations live in rvs.AlgoTrader.Infrastructure only
// NetArchTest enforces this in CI — violation fails the build
```
Reason: Application layer must stay portable. EF Core is an infrastructure concern.

### 20. All Business Config is DB-Driven — Never in appsettings.json
```
❌ FORBIDDEN in appsettings.json (or any config file):
- Strategy parameters (SwingLookback, VolumeMultiplier, RRRatio, etc.)
- Risk profile values (MaxDailyDrawdownPct, MaxTradesPerDay, etc.)
- Capital allocations (AllocatedCapital per broker)
- Schedule definitions (session_start, session_stop, days)
- Failure behavior configuration
- Monitoring alert rules and thresholds
- Notification preferences
- Market calendar overrides

✅ REQUIRED:
- All of the above live in PostgreSQL (app_config table or dedicated tables)
- Accessible via IAppConfigService (DB-backed + Redis cache, 60s TTL)
- All changes → audit_log INSERT
- Editable via UI without application restart

appsettings.json holds ONLY non-secret infrastructure pointers:
  Secrets provider, broker base URLs, DB host:port, Redis host:port,
  RabbitMQ host, JWT issuer/audience/expiry, Serilog sink config.
```

---

## 🏗️ Architecture Contracts

### Layer Dependency Direction
```
Domain ← Application ← Infrastructure ← API
```
- Domain: ZERO external dependencies. No EF, no MediatR, no NodaTime (except via interfaces).
- Application: References Domain only. All interfaces defined here.
- Infrastructure: Implements Application interfaces. References EF, Redis, RabbitMQ, Brokers.
- API: References Application (MediatR dispatch). Never references Infrastructure directly.

### Project Reference Map
```
rvs.AlgoTrader.Domain             → (none)
rvs.AlgoTrader.Application        → Domain
rvs.AlgoTrader.Infrastructure     → Application, Domain, Brokers.Abstractions
rvs.AlgoTrader.Brokers.Abstractions → Domain
rvs.AlgoTrader.Brokers.Zerodha    → Brokers.Abstractions, Application
rvs.AlgoTrader.Brokers.Upstox     → Brokers.Abstractions, Application
rvs.AlgoTrader.Brokers.MStock     → Brokers.Abstractions, Application
rvs.AlgoTrader.Strategies         → Application, Domain
rvs.AlgoTrader.Backtesting        → Application, Domain, Strategies
rvs.AlgoTrader.API                → Application, Infrastructure (DI registration only)
```

### NetArchTest Rules (must pass in CI)
```csharp
// Domain has no deps on Infrastructure
Types.InAssembly(domainAssembly).Should().NotHaveDependencyOn("rvs.AlgoTrader.Infrastructure");

// IStrategy implementations have no broker deps
Types.InAssembly(strategiesAssembly)
    .That().ImplementInterface(typeof(IStrategy))
    .Should().NotHaveDependencyOn("rvs.AlgoTrader.Brokers");

// Application layer has no EF Core
Types.InAssembly(appAssembly).Should().NotHaveDependencyOn("Microsoft.EntityFrameworkCore");

// No direct system clock usage in Domain, Application, or Strategies assemblies
// (enforced via NetArchTest member access check — catches DateTime.Now, DateTimeOffset.UtcNow)
Types.InAssembly(domainAssembly).Union(Types.InAssembly(appAssembly)).Union(Types.InAssembly(strategiesAssembly))
    .Should().NotHaveDependencyOn("System.DateTime")  // blocks DateTime.Now / DateTime.UtcNow
    .GetResult(); // Any violation = CI failure; use IClock instead
// Note: NodaTime.SystemClock.Instance direct usage is also banned in these assemblies —
// only SystemClock : IClock (Infrastructure) may call NodaTime.SystemClock.Instance.
```

---

## 📋 Coding Patterns — Always Follow

### CQRS with MediatR — When to Use
```
✅ Use MediatR for: Order placement, Strategy start/pause/stop, Backtest run, Forward test session
❌ Do NOT use MediatR for: Simple CRUD (watchlists, notification prefs, app_config reads)
   → Use direct IWatchlistService, IAppConfigService instead
```

### FluentValidation — Required For
Every MediatR Command and Query must have a corresponding `*Validator : AbstractValidator<T>`.  
Register with `services.AddValidatorsFromAssemblyContaining<PlaceOrderCommandValidator>()`.

### Repository Pattern
```csharp
// Repositories defined as interfaces in Application layer
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(Order order, CancellationToken ct);
    Task<IReadOnlyList<Order>> GetOpenOrdersAsync(string brokerName, CancellationToken ct);
}
// Implementation in Infrastructure layer, registered in DI
```

### Polly Policies — Required on ALL External Calls
```csharp
// Required for every IBrokerOrderClient, IBrokerMarketDataClient, IBrokerAccountClient
services.AddHttpClient<ZerodhaClient>()
    .AddPolicyHandler(PollyPolicies.RetryAsync(3))
    .AddPolicyHandler(PollyPolicies.CircuitBreakerAsync(5, TimeSpan.FromSeconds(30)))
    .AddPolicyHandler(PollyPolicies.TimeoutAsync(10));
```

### Serilog — Structured Logging + Sinks
```csharp
// Always enrich with CorrelationId
Log.ForContext("CorrelationId", correlationId)
   .Information("Order placed {@Order}", order);

// Never use string interpolation in log messages
Log.Error("Failed for {Symbol} at {Price}", symbol, price); // ✅
Log.Error($"Failed for {symbol}");                          // ❌
```

Serilog must be configured with **three sinks** — Console, Rolling File, and OpenTelemetry:
```json
// appsettings.json — canonical structure (non-secret values only)
// Secrets come from Vault/env: broker API keys, JWT secret, DB password,
// Redis password, field encryption key — NEVER in appsettings.json
{
  "Secrets": { "Provider": "Vault" },
  "ActiveBroker": "Zerodha",
  "Brokers": {
    "Zerodha": {
      "BaseUrl": "https://api.kite.trade",
      "WebSocketUrl": "wss://ws.kite.trade",
      "Historical": { "MaxChunkDays": 60, "RateLimitPerSecond": 3, "MaxRetries": 5 }
    },
    "Upstox": {
      "BaseUrl": "https://api.upstox.com",
      "WebSocketUrl": "wss://api.upstox.com/v2/feed/market-data-streamer",
      "Historical": { "MaxChunkDays": 30, "RateLimitPerSecond": 2, "MaxRetries": 5 }
    },
    "MStock": {
      "BaseUrl": "https://api.mstock.trade",
      "Historical": { "MaxChunkDays": 30, "RateLimitPerSecond": 2, "MaxRetries": 5 }
    }
  },
  "Database": { "Host": "localhost", "Port": 5432, "Database": "algotrader" },
  "Redis":    { "Host": "localhost", "Port": 6379 },
  "RabbitMQ": { "Host": "localhost", "VirtualHost": "/" },
  "Jwt": {
    "Issuer": "AlgoTrader",
    "Audience": "AlgoTraderUI",
    "AccessTokenExpiryMinutes": 60,
    "RefreshTokenExpiryDays": 30
  },
  "Serilog": {
    "MinimumLevel": "Information",
    "WriteTo": [
      { "Name": "Console" },
      {
        "Name": "File",
        "Args": { "path": "logs/algotrader-.log", "rollingInterval": "Day" }
      },
      { "Name": "OpenTelemetry" }
    ],
    "Enrich": ["WithCorrelationId", "FromLogContext"]
  }
}
```

All business config (strategies, risk profiles, capital allocations, schedules, failure behavior, monitoring rules) lives in **PostgreSQL `app_config` table** — editable via UI without restart, NEVER in appsettings.json.

### Mapperly — DTO Mapping
```csharp
// Use Mapperly source-gen mapper — never write manual property assignment
[Mapper]
public partial class OrderMapper
{
    public partial OrderDto ToDto(Order order);
    public partial Order ToDomain(CreateOrderDto dto);
}
```

### EF Core — Conventions
```csharp
// Encrypted fields: use EF Core value converters + [Encrypted] attribute
// TimescaleDB hypertables: candles, forward_test_equity_curve — never query without time range filter
// Migrations: always run Add-Migration before any schema change; never edit existing migrations
```

### Monitoring Alert Deduplication — Required Pattern
```csharp
// Before firing any monitoring alert, check Redis dedup key:
// Key: alert:dedup:{ruleId}   TTL: rule.window_seconds
// If key exists → skip (already fired in this window)
// If key absent → INSERT to alert_log, publish MassTransit event, SET dedup key with TTL

// This pattern is MANDATORY in IMonitoringAlertEvaluator.
// Never fire duplicate alerts for the same rule within the same window_seconds period.
// Redis key pattern MUST match the existing key matrix: alert:dedup:{ruleId}
```

### Cold-Restart Notice Banner — Required Frontend Rule
```typescript
// The Dashboard Overview panel MUST display a yellow/amber notice banner when
// any strategy instance was paused due to cold restart
// (i.e., auto_resume_on_restart = false and instance was RUNNING at shutdown).
//
// Banner text: "System restarted. [N] instance(s) paused — manual restart required."
// Banner persists until all paused-by-restart instances are manually started or dismissed.
// Data source: StrategyHub SignalR push on startup OR /api/v1/strategy-instances?filter=cold_restart_paused
// React component: <ColdRestartNoticeBanner /> in Dashboard layout
```

---

## 🔑 Key Interfaces — Never Modify Signatures Without Updating Tests

```csharp
IClock                      // Now(), NowInstant(), Today()
IExecutionEngine            // ExecuteAsync(signal, context, ct) → ExecutionResult
IStrategy                   // EvaluateAsync(StrategyContext, ct) → SignalResult
ICapitalAllocator           // TryReserveAsync, ReleaseAsync, GetAvailableCapitalAsync
IStrategyScheduler          // IsWithinScheduledSession, EvaluateOnStartup, TimeUntilNextSession
IStartupOrchestrator        // RunAsync(ct) — all 11 steps
ICandleCache                // GetAsync, AppendAsync, GetLastAsync
IIndicatorService           // EMA, SMA, VWAP, ATR, BollingerBands, SwingPoints, VolumeProfile
IIncrementalIndicator<T>    // Current, Update(Candle), Reset()
IFullBrokerClient           // BrokerName, AuthenticateAsync, + all sub-interfaces
ISecretsProvider            // GetSecretAsync(key, ct)
IFieldEncryptionService     // Encrypt(plaintext), Decrypt(ciphertext)
IAppConfigService           // GetAsync<T>(key), SetAsync(key, value) — DB-backed + Redis cache
ITrailingStopLossService    // UpdateTrailingStopAsync(position, currentPrice, ct)
IMonitoringAlertEvaluator   // EvaluateAsync(ct) — called by Hangfire every 30s during market hours
```

---

## 📍 JSON Schema Reference — strategy_instances Columns

### `schedule_json` — Exact Shape
```json
{
  "days": ["MON", "TUE", "WED", "THU", "FRI"],
  "session_start": "09:20",
  "session_stop": "15:10",
  "timezone": "Asia/Kolkata",
  "auto_resume_on_restart": true,
  "missed_session_behavior": "SKIP",
  "force_exit_on_session_end": true
}
```
- `session_start` / `session_stop` — IST time window (validated against market calendar)
- `auto_resume_on_restart` — if `true` and system restarts mid-session while RUNNING, auto-resume without manual intervention; if `false`, always restore to PAUSED (safer default)
- `missed_session_behavior` — `"SKIP"` (do nothing today) or `"START_LATE"` (start immediately with WARN log)
- `force_exit_on_session_end` — if `true`, IExecutionEngine force-exits all open positions at `session_stop` via `IClock.Now()`

### `failure_behavior_json` — Exact Shape
```json
{
  "OnBrokerCircuitOpen": "PAUSE_INSTANCE",
  "OnStreamDisconnect": "PAUSE_NEW_SIGNALS",
  "OnDataStale": "PAUSE_INSTANCE",
  "DataStalenessThresholdMinutes": 5,
  "OnRiskLimitBreached": "STOP_INSTANCE",
  "OnEvaluationTimeout": "LOG_AND_SKIP"
}
```
- `PAUSE_INSTANCE` — stop evaluating signals; position management (SL/TP) continues
- `PAUSE_NEW_SIGNALS` — stop opening new positions only; existing positions managed normally
- `STOP_INSTANCE` — full stop including position management; requires manual restart
- `LOG_AND_SKIP` — log the event with WARN severity and skip this evaluation cycle
- All failure events: logged with correlation ID → `alert_log` → MassTransit event → notification channels → React alert feed

---

## 🧪 Testing Conventions

### Unit Test Structure
```csharp
// File: rvs.AlgoTrader.UnitTests/Strategies/PriceActionBreakoutStrategyTests.cs
// Naming: MethodName_Scenario_ExpectedResult
[Fact]
public async Task EvaluateAsync_WhenVolumeAboveThreshold_ReturnsBuySignal() { }

// Always use SimulatedClock — never real clock
var clock = new SimulatedClock(Instant.FromUtc(2024, 1, 15, 9, 30, 0));
```

### Integration Test Structure
```csharp
// Use Testcontainers for real PostgreSQL, Redis, RabbitMQ
// Use Respawn for DB reset between tests
// Never mock PostgreSQL or Redis in integration tests — use real containers
public class OrderPlacementIntegrationTests : IClassFixture<IntegrationTestFixture>
```

### Architecture Tests
```csharp
// rvs.AlgoTrader.UnitTests/Architecture/ArchitectureTests.cs
// Run NetArchTest rules — fail CI if violated
```

### Mandatory Parity Tests
Parity tests between engines are **required** — not optional:
```csharp
// REQUIRED: BacktestExecutionEngine vs SimulatedExecutionEngine (forward test)
// File: rvs.AlgoTrader.IntegrationTests/Parity/EngineParity_BacktestVsSimulatedTests.cs
// Arrange: Same strategy + same candle sequence fed to both engines
// Assert: Identical SignalResult list (type, entry price, SL, TP on each bar)
// Why: Forward test engine and backtest engine must agree on signal logic.
//      Any divergence = a bug in one of the engines.

// REQUIRED: Backtest reproducibility test
// File: rvs.AlgoTrader.IntegrationTests/Backtest/BacktestReproducibilityTests.cs
// Arrange: Run same backtest twice with identical config and data
// Assert: result_metrics are identical AND data_hash matches on both runs
// Why: STRATEGY.md AC-03 — reproducibility is a hard acceptance criterion
```

### Backtest Data Integrity — SHA-256 Hashing
```csharp
// Before every backtest run, compute SHA-256 of ordered candle data:
// Hash input: all OHLCV rows ordered by (symbol, timeframe, timestamp ASC)
// Store in: backtest_data_snapshots.data_hash
// On reproduce: compare hashes — if different, warn user: "Data has changed since original run"

// Never run a reproducibility comparison without verifying data_hash first.
// The data_hash is the fingerprint of the exact dataset used — if it changes,
// different results are expected and the diff view must show the hash mismatch.
```

### Forward Test — Scheduling Rules Match Live
```
Forward test sessions MUST follow the same scheduling rules as live strategy instances:
- Uses the same IStrategyScheduler.IsWithinScheduledSession() check
- Respects schedule_json.force_exit_on_session_end (SimulatedExecutionEngine force-exits
  open positions at session_stop via IClock.Now())
- Respects schedule_json.auto_resume_on_restart
- Obeys kill-switch: if kill switch is active, forward test sessions must also pause
- Uses SimulatedExecutionEngine + ICapitalAllocator (no real orders, no real broker calls)

Any divergence between forward test scheduling and live scheduling is a bug.
```

---

## 📦 Package Versions (Pin These)

### Frontend npm (package.json)
```json
{
  "react": "^19.0.0",
  "react-dom": "^19.0.0",
  "typescript": "^5.4.0",
  "vite": "^6.0.0",
  "@vitejs/plugin-react": "^4.0.0",
  "zustand": "^5.0.0",
  "@tanstack/react-query": "^5.0.0",
  "@tanstack/react-table": "^8.0.0",
  "lightweight-charts": "^5.0.0",
  "react-hook-form": "^7.0.0",
  "zod": "^3.0.0",
  "recharts": "^2.0.0",
  "@microsoft/signalr": "latest",
  "tailwindcss": "^4.0.0",
  "@shadcn/ui": "latest"
}
```

### Backend NuGet (Directory.Packages.props)
```xml
<PackageReference Include="MediatR" Version="12.*" />
<PackageReference Include="MassTransit.RabbitMQ" Version="8.*" />
<PackageReference Include="Polly" Version="8.*" />
<PackageReference Include="FluentValidation.AspNetCore" Version="11.*" />
<PackageReference Include="Serilog.AspNetCore" Version="8.*" />
<PackageReference Include="NodaTime" Version="3.*" />
<PackageReference Include="Hangfire.AspNetCore" Version="1.*" />
<PackageReference Include="Riok.Mapperly" Version="3.*" />
<PackageReference Include="QuestPDF" Version="2024.*" />
<PackageReference Include="VaultSharp" Version="1.*" />
<PackageReference Include="Testcontainers.PostgreSql" Version="3.*" />
<PackageReference Include="Testcontainers.Redis" Version="3.*" />
<PackageReference Include="Testcontainers.RabbitMq" Version="3.*" />
<PackageReference Include="Respawn" Version="6.*" />
<PackageReference Include="NetArchTest.Rules" Version="1.*" />
<PackageReference Include="Microsoft.Playwright" Version="1.*" />
<PackageReference Include="xunit" Version="2.*" />
<PackageReference Include="Moq" Version="4.20.*" />
<PackageReference Include="FluentAssertions" Version="6.*" />
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.*" />
```

---

## 🚫 Anti-Patterns Seen in Past Sessions — Do Not Repeat

> **Each entry below is a mistake that was made and corrected. Claude must not repeat these.**

### AP-001: Using DateTime.Now instead of IClock
**Mistake:** `var now = DateTime.Now;` appeared in CandleAggregatorService  
**Fix:** Inject `IClock _clock`, use `_clock.Now()`  
**Why:** Breaks SimulatedClock determinism in backtesting

### AP-002: Cross-context service injection
**Mistake:** `BacktestingEngine` had `IZerodhaClient` injected  
**Fix:** Backtesting reads only from TimescaleDB via `ICandleRepository`  
**Why:** Backtesting context must be 100% isolated from broker code

### AP-003: MediatR overuse on CRUD
**Mistake:** `GetWatchlistsQuery` + Handler + DTO for a 3-line DB read  
**Fix:** Direct `IWatchlistService.GetAllAsync()` call from controller  
**Why:** KISS — MediatR overhead not justified for simple queries

### AP-004: Missing Idempotency-Key check
**Mistake:** Order endpoint processed without checking Redis idempotency store first  
**Fix:** IdempotencyMiddleware applied to `[Route("api/v1/orders")]` before controller executes  
**Why:** Duplicate orders are catastrophic in live trading

### AP-005: Capital reservation race condition
**Mistake:** Non-atomic read (`GetAvailableCapital`) then write (`ReserveCapital`) in Redis  
**Fix:** Single Lua script with INCRBY and atomic check  
**Why:** Two concurrent strategies can both read "available" and both reserve, causing over-leverage

### AP-006: Hardcoded secrets
**Mistake:** `var apiKey = "kite_api_key_123"` found in ZerodhaClient  
**Fix:** `var apiKey = await _secrets.GetSecretAsync("Brokers:Zerodha:ApiKey", ct)`  
**Why:** Security + ISecretsProvider abstraction allows Vault/env switching

### AP-007: Strategy evaluation on partial candle
**Mistake:** CandleAggregatorService passed `CurrentBar` (open candle) to StrategyEvaluationQueue  
**Fix:** Only emit `CandleClosedEvent` on bar boundary; strategy queue only consumes closed bars  
**Why:** Partial candle data causes false signals

### AP-008: Missing correlation ID in log entries
**Mistake:** `_logger.LogInformation("Order placed")` — no context  
**Fix:** Middleware sets `Activity.Current.Id` as CorrelationId; enriched in Serilog pipeline  
**Why:** Impossible to trace a request across services without it

### AP-009: audit_log row updated after insertion
**Mistake:** `UPDATE audit_log SET after_json = @json WHERE id = @id`  
**Fix:** Always INSERT a complete row with `before_json` + `after_json` in a single write  
**Why:** SEBI compliance — audit log is append-only

### AP-010: Missing Polly policy on broker HTTP client
**Mistake:** `services.AddHttpClient<UpstoxClient>()` — no Polly policies  
**Fix:** Always chain `.AddPolicyHandler(...)` for retry, circuit breaker, timeout  
**Why:** Broker APIs fail; without Polly, a single timeout crashes order placement

### AP-011: IMarketCalendarService not checked before order placement
**Mistake:** Orders placed on market holidays (test environment ran on a holiday)  
**Fix:** `LiveExecutionEngine` checks `IMarketCalendarService.IsTradingDay()` before `IBrokerOrderClient.PlaceOrderAsync()`  
**Why:** SEBI rules prohibit trading on holidays; broker rejects orders with confusing errors

### AP-012: React component calling `/api/v1/orders` without Idempotency-Key header
**Mistake:** Order form did not generate UUID and attach it as header  
**Fix:** `useOrderSubmit` hook generates `crypto.randomUUID()` per submission and attaches as `Idempotency-Key` header  
**Why:** Accidental double-click causes duplicate order

### AP-013: EF migration added without running `dotnet ef migrations add`
**Mistake:** Schema change made directly in DDL SQL without a corresponding EF migration  
**Fix:** Always run `dotnet ef migrations add <Name> -p rvs.AlgoTrader.Infrastructure -s rvs.AlgoTrader.API` before applying schema changes  
**Why:** `dotnet ef database update` will not apply raw SQL changes; migrations are source of truth

### AP-014: TimescaleDB hypertable queried without time range
**Mistake:** `SELECT * FROM candles WHERE symbol = 'RELIANCE'` — full table scan  
**Fix:** Always include `AND timestamp >= @from AND timestamp < @to` in candle queries  
**Why:** TimescaleDB partitions by time; without a time predicate, query hits all chunks = full scan

### AP-015: Kill switch not checked on strategy auto-resume
**Mistake:** `IStartupOrchestrator` auto-resumed a strategy instance even though kill switch was active  
**Fix:** Step 4 (kill-switch check) must set a flag; Step 6 (strategy scheduling) must respect that flag — never auto-resume when kill switch is active  
**Why:** Kill switch must override all automation

### AP-016: CandleAggregatorService using system clock for bar boundary detection
**Mistake:** `CandleAggregatorService` used `DateTime.UtcNow` directly for bar boundary detection, breaking SimulatedClock in forward tests  
**Fix:** `CandleAggregatorService` MUST inject and use `IClock.Now()` for ALL time operations, including `GetNextBarBoundary()` comparisons  
**Why:** Forward tests advance a SimulatedClock — any static clock call in `CandleAggregatorService` means forward test candles will never close at the right simulated time. This is the single most critical IClock usage in the system.

### AP-017: Cold-restart notice banner missing from Dashboard
**Mistake:** After system restart, instances paused by `auto_resume_on_restart = false` were silent — no UI indication, trader unaware  
**Fix:** `IStartupOrchestrator` Step 6 must push a `ColdRestartPauseEvent` via StrategyHub; Dashboard must render `<ColdRestartNoticeBanner />` listing paused instances with a "Start" shortcut  
**Why:** Trader must know immediately if instances need manual restart — missed session windows cost real money

---

## 📝 Lessons Learned Log

> Append new lessons here as they are discovered during development.
> Format: `### LL-NNN: Short Title` then description + fix.

### LL-001: Hangfire dashboard exposed in production
**Context:** `/hangfire` was accessible without auth  
**Fix:** Use `app.UseHangfireDashboard("/hangfire", new DashboardOptions { Authorization = [new HangfireAdminAuthFilter()] })`  
**Rule:** Hangfire dashboard requires Admin role in non-development environments

### LL-002: SignalR hub accessible without JWT
**Context:** QuoteHub accepted anonymous connections  
**Fix:** Add `[Authorize]` to all SignalR hubs; send JWT via `?access_token=` query param from React  
**Rule:** All SignalR hubs must require authentication

### LL-003: Redis sorted set score collision on candle cache
**Context:** Two candles for the same timestamp (duplicate data) caused score collision  
**Fix:** Before `ZADD`, check for existing member with same score; log to `data_quality_log` if duplicate detected  
**Rule:** `ICandleCache.AppendAsync` must be idempotent

### LL-004: RabbitMQ consumer not using `CancellationToken` on shutdown
**Context:** MassTransit consumers did not handle graceful shutdown, causing message loss  
**Fix:** All consumers accept `ConsumeContext<T>` which provides `CancellationToken`; use it on all awaited calls  
**Rule:** All async operations in consumers must accept and propagate the consumer's CancellationToken

---

## 🔄 Generation Order — Hard Contract

**Always generate in this layer order. Never skip a layer. Each layer depends on types defined by the layer above it.**

```
Layer 1: Domain
  └─ Entities, Value Objects, Enums, Domain Events, Domain Interfaces
  └─ Zero external dependencies. Builds standalone.
  └─ REQUIRED FIRST: IClock, IStrategy, IExecutionEngine, ICapitalAllocator,
                   IStrategyScheduler, all domain events, all domain entities

Layer 2: Application Interfaces
  └─ All repository interfaces (IOrderRepository, ICandleRepository, etc.)
  └─ All application service interfaces (ISecretsProvider, IAppConfigService,
     IMonitoringAlertEvaluator, ITrailingStopLossService, etc.)
  └─ References: Domain only. Zero EF Core.

Layer 3: Application Services
  └─ MediatR Commands, Queries, Handlers, DTOs
  └─ FluentValidation validators (one per Command/Query)
  └─ References: Domain + Application interfaces only.

Layer 4: Infrastructure Implementations
  └─ EF Core DbContext + Migrations + Repository implementations
  └─ Redis, RabbitMQ/MassTransit, Broker Clients (Zerodha/Upstox/MStock)
  └─ IClock (SystemClock + SimulatedClock), ISecretsProvider (Vault)
  └─ All implementations of Application interfaces
  └─ DI registration via InfrastructureServiceExtensions

Layer 5: API Controllers
  └─ MVC Controllers (/api/v1/*) with [ApiController], [Authorize], [EnableRateLimiting]
  └─ Minimal APIs: /health/live, /health/ready, /metrics only
  └─ SignalR Hubs: QuoteHub, StrategyHub, ForwardTestHub, AlertHub
  └─ References: Application only (dispatches MediatR). Never Infrastructure directly.

Layer 6: Frontend Components
  └─ React 19 panels (all 18) — build after API contracts are finalized
  └─ Zustand stores, TanStack Query hooks, SignalR client setup
  └─ Form validation with React Hook Form + Zod schemas matching backend DTOs

Layer 7: Tests
  └─ Unit tests (strategies, indicators, services) — written alongside Layer 3
  └─ Integration tests (Testcontainers) — written alongside Layer 4
  └─ Architecture tests (NetArchTest) — written at Layer 1/2
  └─ Parity tests (Backtest vs Simulated) — written at Layer 4
  └─ Playwright UI tests — written after Layer 6
```

**If asked to generate a specific component out of order:**
> "Have layers 1–N been completed for this component? If not, I will generate the missing interfaces and domain types first before writing the implementation."

This order maps exactly to PLAN.md phases. The 38 steps in PLAN.md are the detailed breakdown of these 7 layers. **Never skip a step. Never write an Infrastructure class before its Application interface exists.**

---

## ✅ Definition of Done — Per Component

A component is DONE only when:
- [ ] Implementation written
- [ ] Unit tests written and passing
- [ ] Integration test written (for infrastructure components)
- [ ] Added to DI registration in `Program.cs` / extension methods
- [ ] All interfaces respected (no signature changes without updating tests)
- [ ] Swagger XML comment added to controller actions
- [ ] No compiler warnings (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`)
- [ ] No hardcoded secrets, no `DateTime.Now`, no cross-context violations
- [ ] `dotnet build` passes with zero errors
- [ ] NetArchTest rules still pass
