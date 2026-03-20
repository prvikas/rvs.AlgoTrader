# Scaffolding: API Contracts & DTOs — rvs.AlgoTrader

All contracts live in `rvs.AlgoTrader.Application` unless noted.
Mapperly zero-alloc mapper handles DTO ↔ Domain conversions — no manual mapping code.

---

## API Response Envelope

Every business endpoint (MVC Controllers on `/api/v1/...`) returns this envelope.
Minimal API internal routes (`/health/*`, `/metrics`) are exempt.

```csharp
// rvs.AlgoTrader.Application.DTOs.ApiResponse
public record ApiResponse<T>(
    bool Success,
    T? Data,
    string? Error,
    string CorrelationId
);

// Factory helpers
public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data, string correlationId)
        => new(true, data, null, correlationId);

    public static ApiResponse<T> Fail<T>(string error, string correlationId)
        => new(false, default, error, correlationId);
}
```

**JSON shape (success):**
```json
{ "success": true, "data": {}, "error": null, "correlationId": "550e8400-e29b-41d4-a716-446655440000" }
```

**JSON shape (error):**
```json
{ "success": false, "data": null, "error": "Idempotency-Key header is required", "correlationId": "..." }
```

All 400, 401, 403, 404, 429, 500 responses use this envelope — never raw exception text in production.

---

## Correlation ID Middleware

```csharp
// Generates UUID per request; propagated through:
//   - All Serilog log entries (enricher)
//   - MassTransit message headers
//   - Broker API call headers (X-Correlation-ID)
public class CorrelationIdMiddleware : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var correlationId = context.Request.Headers["X-Correlation-ID"]
            .FirstOrDefault() ?? Guid.NewGuid().ToString();

        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers["X-Correlation-ID"] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
```

---

## Order DTOs

```csharp
// rvs.AlgoTrader.Application.DTOs.Orders

public record OrderDto(
    Guid Id,
    string BrokerName,
    string? BrokerOrderId,
    string InternalSymbol,
    string OrderType,          // MARKET, LIMIT, SL, SL-M
    string Direction,          // BUY, SELL
    int Quantity,
    decimal? Price,
    decimal? TriggerPrice,
    string Status,             // PENDING, OPEN, COMPLETE, REJECTED, CANCELLED
    Guid? StrategyRunId,
    DateTimeOffset? PlacedAt,
    DateTimeOffset? FilledAt,
    decimal? FillPrice,
    decimal? TrailingSl,
    decimal? TrailingTp,
    DateTimeOffset CreatedAt
);

public record CreateOrderDto(
    string BrokerName,
    string InternalSymbol,
    string OrderType,          // MARKET, LIMIT, SL, SL-M
    string Direction,          // BUY, SELL
    int Quantity,
    decimal? Price,
    decimal? TriggerPrice,
    Guid? StrategyRunId
);

public record ModifyOrderDto(
    int? Quantity,
    decimal? Price,
    decimal? TriggerPrice
);
```

---

## Strategy Instance DTOs

```csharp
// rvs.AlgoTrader.Application.DTOs.Strategy

public record StrategyInstanceDto(
    Guid Id,
    string Name,
    string StrategyType,
    Guid? WatchlistId,
    string Mode,               // BACKTEST, FORWARD, LIVE
    string? BrokerName,
    bool IsActive,
    string Status,             // DRAFT, SCHEDULED, RUNNING, PAUSED, STOPPED
    object ConfigJson,
    object? FailureBehaviorJson,
    Guid? RiskProfileId,
    object? ScheduleJson,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record CreateStrategyInstanceDto(
    string Name,
    string StrategyType,
    Guid WatchlistId,
    string Mode,
    string? BrokerName,
    PriceActionBreakoutConfig ConfigJson,
    FailureBehaviorConfig? FailureBehaviorJson,
    Guid? RiskProfileId,
    ScheduleConfig? ScheduleJson
);

public record UpdateStrategyInstanceDto(
    string? Name,
    string? BrokerName,
    PriceActionBreakoutConfig? ConfigJson,
    FailureBehaviorConfig? FailureBehaviorJson,
    Guid? RiskProfileId,
    ScheduleConfig? ScheduleJson
);
```

---

## Position DTOs

```csharp
public record PositionDto(
    Guid Id,
    string BrokerName,
    string InternalSymbol,
    int Quantity,
    decimal AvgPrice,
    decimal CurrentPrice,
    decimal Pnl,
    Guid? StrategyRunId,
    DateTimeOffset? OpenedAt,
    DateTimeOffset? ClosedAt
);
```

---

## Backtest DTOs

```csharp
public record BacktestRequestDto(
    Guid StrategyInstanceId,
    string InternalSymbol,
    DateOnly FromDate,
    DateOnly ToDate,
    string Timeframe,
    Guid? CostProfileId,
    WalkForwardConfig? WalkForward  // null = no walk-forward; non-null = walk-forward mode
);

/// <summary>
/// Walk-forward configuration. When provided, the backtest splits [FromDate..ToDate]
/// into sliding windows of InSampleDays + OutOfSampleDays, trains on in-sample, validates
/// on out-of-sample. Results include per-window metrics and an aggregated summary.
/// </summary>
public record WalkForwardConfig(
    int InSampleDays,           // e.g. 252 (1 trading year)
    int OutOfSampleDays,        // e.g. 63 (1 trading quarter)
    int StepDays                // sliding step between windows; default = OutOfSampleDays
);

public record BacktestResultDto(
    Guid RunId,
    string Status,
    decimal InitialCapital,
    decimal FinalCapital,
    decimal GrossPnl,           // before transaction costs
    decimal NetPnl,             // after brokerage, STT, GST, SEBI, stamp duty, slippage
    decimal TotalReturn,
    decimal MaxDrawdown,
    decimal SharpeRatio,
    decimal CalmarRatio,        // annualised return / max drawdown; 0 if max drawdown = 0
    decimal WinRate,
    int TotalTrades,
    int WinningTrades,
    int LosingTrades,
    string DataIntegrityHash,   // SHA-256 of ordered OHLCV rows — used for reproducibility check
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    object ResultMetrics        // full breakdown including per walk-forward-window metrics if applicable
);

/// <summary>
/// Transaction cost model for backtests. Stored in backtest_cost_profiles table.
/// Select by passing CostProfileId in BacktestRequestDto.
/// </summary>
public record BacktestCostProfileDto(
    Guid Id,
    string Name,                    // e.g. "Zerodha Equity Intraday"
    decimal BrokeragePct,           // e.g. 0.0003 (0.03%)
    decimal SttPct,                 // Securities Transaction Tax
    decimal GstPct,                 // on brokerage
    decimal SebiChargesPct,         // SEBI turnover fee
    decimal StampDutyPct,           // on buy side only
    decimal SlippagePct,            // market impact / spread estimate
    string Description,
    DateTimeOffset CreatedAt
);

public record CreateBacktestCostProfileDto(
    string Name,
    decimal BrokeragePct,
    decimal SttPct,
    decimal GstPct,
    decimal SebiChargesPct,
    decimal StampDutyPct,
    decimal SlippagePct,
    string Description
);
```

---

## strategy `config_json` Schema — `PriceActionBreakoutConfig`

Stored as JSONB in `strategy_instances.config_json`. Deserialized by
`rvs.AlgoTrader.Strategies.PriceActionBreakout.PriceActionBreakoutConfig`.

```csharp
// rvs.AlgoTrader.Strategies.PriceActionBreakout
public record PriceActionBreakoutConfig(
    // Entry parameters
    string Timeframe,                       // e.g. "15m"
    int SwingHighLookback,                  // N-bar swing high lookback
    int VolumeSmaLookback,                  // M-bar SMA for volume
    decimal VolumeMultiplier,               // volume > SMA × multiplier
    bool UseEmaFilter,                      // optional EMA filter
    int EmaLength,                          // EMA period (if UseEmaFilter)
    decimal MinBodyRangePct,                // candle body ≥ 40% of range = 0.40

    // Exit parameters
    decimal SlBufferPct,                    // SL = signal candle low - buffer %
    decimal RiskRewardRatio,                // TP = RR × SL distance
    decimal TrailingSLActivationPct,        // trailing SL activates at X% profit
    decimal TrailingTPStep,                 // trailing TP ratchets per bar (%)

    // Evaluation timeout
    int EvaluationTimeoutMs                 // default 500ms; SKIPPED w/ reason TIMEOUT if exceeded
);
```

**Example JSON (stored in DB):**
```json
{
  "timeframe": "15m",
  "swingHighLookback": 20,
  "volumeSmaLookback": 20,
  "volumeMultiplier": 1.5,
  "useEmaFilter": true,
  "emaLength": 21,
  "minBodyRangePct": 0.40,
  "slBufferPct": 0.002,
  "riskRewardRatio": 2.0,
  "trailingSLActivationPct": 0.01,
  "trailingTPStep": 0.005,
  "evaluationTimeoutMs": 500
}
```

---

## `schedule_json` Schema — `ScheduleConfig`

Stored as JSONB in `strategy_instances.schedule_json`.

> **Timezone rule:** `SessionStart` and `SessionStop` are **always IST time-of-day strings**.
> `Timezone` is always `"Asia/Kolkata"` — never the user's local timezone.
> Market sessions are IST-relative by definition; `IStrategyScheduler` compares them against
> `IClock.Now()` which is always IST. The frontend Schedule Editor renders an additional
> local-time conversion hint (e.g. "09:20 IST (22:50 CST)") for users outside India.

```csharp
// rvs.AlgoTrader.Application.DTOs
public record ScheduleConfig(
    string[] Days,                          // ["MON","TUE","WED","THU","FRI"]
    TimeOnly SessionStart,                  // IST time-of-day ALWAYS — e.g. 09:20
    TimeOnly SessionStop,                   // IST time-of-day ALWAYS — e.g. 15:10
    string Timezone,                        // ALWAYS "Asia/Kolkata" — never user's local tz
    bool AutoResumeOnRestart,               // true = auto-resume if within session on restart
    string MissedSessionBehavior,           // START_LATE | SKIP | ALERT_ONLY
    bool ForceExitOnSessionEnd              // true = close all positions at session_stop
);
```

**Example JSON:**
```json
{
  "days": ["MON","TUE","WED","THU","FRI"],
  "sessionStart": "09:20",
  "sessionStop": "15:10",
  "timezone": "Asia/Kolkata",
  "autoResumeOnRestart": true,
  "missedSessionBehavior": "START_LATE",
  "forceExitOnSessionEnd": true
}
```

**Frontend Schedule Editor renders (for a CST user):**
```
Session start: 09:20 IST  →  22:50 CST (prev day)
Session stop:  15:10 IST  →  04:40 CST
```

---

## `failure_behavior_json` Schema — `FailureBehaviorConfig`

Stored as JSONB in `strategy_instances.failure_behavior_json`.

```csharp
// rvs.AlgoTrader.Application.DTOs
public record FailureBehaviorConfig(
    string OnBrokerCircuitOpen,             // PAUSE_INSTANCE | STOP_INSTANCE | LOG_AND_CONTINUE
    string OnStreamDisconnect,              // PAUSE_NEW_SIGNALS | PAUSE_INSTANCE | STOP_INSTANCE
    string OnDataStale,                     // PAUSE_INSTANCE | LOG_AND_CONTINUE
    int DataStalenessThresholdMinutes,      // default 5
    string OnRiskLimitBreached,             // STOP_INSTANCE | PAUSE_INSTANCE
    string OnEvaluationTimeout,            // LOG_AND_SKIP | PAUSE_INSTANCE
    KillSwitchBehavior KillSwitch
);

public record KillSwitchBehavior(
    bool SquareOff                          // true = close all positions on kill switch
);
```

**Example JSON:**
```json
{
  "onBrokerCircuitOpen": "PAUSE_INSTANCE",
  "onStreamDisconnect": "PAUSE_NEW_SIGNALS",
  "onDataStale": "PAUSE_INSTANCE",
  "dataStalenessThresholdMinutes": 5,
  "onRiskLimitBreached": "STOP_INSTANCE",
  "onEvaluationTimeout": "LOG_AND_SKIP",
  "killSwitch": { "squareOff": true }
}
```

---

## Risk Profile DTO

```csharp
public record RiskProfileDto(
    Guid Id,
    string Name,
    decimal MaxCapitalPerTradePct,
    int MaxOpenTradesPerSymbol,
    decimal MaxDailyDrawdownPct,
    decimal MaxTotalCapitalDeployed,
    int MaxTradesPerDay,
    DateTimeOffset CreatedAt
);
```

---

## Signal Journal DTO

```csharp
public record SignalJournalEntryDto(
    long Id,
    Guid StrategyInstanceId,
    string InternalSymbol,
    DateTimeOffset EvaluatedAt,
    string Timeframe,
    string Signal,             // BUY, SELL, HOLD
    decimal? EntryPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    string? Reason,
    object? DiagnosticsJson,
    bool ActedOn,
    string? SkippedReason      // THROTTLED, MARKET_CLOSED, KILL_SWITCH,
                               // RISK_LIMIT, INSUFFICIENT_CAPITAL,
                               // TIMEOUT, OUTSIDE_SCHEDULE
);
```

---

## MediatR Command/Query Naming Convention

| Pattern | Example |
|---|---|
| Command | `PlaceOrderCommand`, `StartStrategyInstanceCommand`, `ActivateKillSwitchCommand` |
| Command Handler | `PlaceOrderCommandHandler` |
| Query | `GetOrderByIdQuery`, `ListStrategyInstancesQuery` |
| Query Handler | `GetOrderByIdQueryHandler` |
| Validator | `PlaceOrderCommandValidator` (FluentValidation) |
| Result | `PlaceOrderResult` (plain record, never domain entity) |

**Rule:** Every Command and Query has a FluentValidation validator.
Simple CRUD with no complex business logic may use direct service calls instead of MediatR.

---

## Capital Allocation DTOs

```csharp
public record CapitalAllocationDto(
    Guid Id,
    Guid StrategyInstanceId,
    string BrokerName,
    decimal AllocatedCapital,
    decimal UsedCapital,
    decimal AvailableCapital,       // AllocatedCapital - UsedCapital (live from Redis)
    DateTimeOffset UpdatedAt
);

public record UpdateCapitalAllocationDto(
    decimal AllocatedCapital,
    string BrokerName
);
```

---

## Symbol Data Preferences DTOs

```csharp
// Controls which timeframes + historical range are downloaded for each symbol
public record SymbolDataPreferencesDto(
    Guid Id,
    string InternalSymbol,
    string[] Timeframes,            // e.g. ["1m", "5m", "15m", "1h", "1d"]
    DateOnly FromDate,              // earliest date to download
    int Priority,                   // 1 = highest; lower priority symbols downloaded last
    bool IsActive,                  // false = skip in scheduled downloads
    DateTimeOffset UpdatedAt
);

public record UpdateSymbolDataPreferencesDto(
    string[]? Timeframes,
    DateOnly? FromDate,
    int? Priority,
    bool? IsActive
);
```

---

## User Preferences DTO

```csharp
// Stored in user_preferences table (one row per user)
public record UserPreferencesDto(
    Guid UserId,
    string Timezone,                    // IANA timezone key — e.g. "America/Chicago" (CST),
                                        // "America/New_York" (EST), "Asia/Kolkata" (IST)
                                        // Validated: DateTimeZoneProviders.Tzdb[timezone] must resolve
    bool NotifyOnOrderFill,
    bool NotifyOnSLHit,
    bool NotifyOnTpHit,
    bool NotifyOnKillSwitch,
    bool NotifyOnStreamReconnect,
    bool NotifyOnTokenExpiry,
    bool NotifyOnDataQuality,
    bool NotifyOnColdRestartPause,
    bool NotifyOnMonitoringBreach,
    bool NotifyOnStrategyAutoResumed,
    bool NotifyOnStrategyMissedSession,
    bool SendEodReport,
    string[] NotificationChannels      // ["IN_APP", "TELEGRAM", "EMAIL"]
);

public record UpdateUserPreferencesDto(
    string? Timezone,                   // IANA key — validated server-side before save
    bool? NotifyOnOrderFill,
    bool? NotifyOnSLHit,
    bool? NotifyOnTpHit,
    bool? NotifyOnKillSwitch,
    bool? NotifyOnStreamReconnect,
    bool? NotifyOnTokenExpiry,
    bool? NotifyOnDataQuality,
    bool? NotifyOnColdRestartPause,
    bool? NotifyOnMonitoringBreach,
    bool? NotifyOnStrategyAutoResumed,
    bool? NotifyOnStrategyMissedSession,
    bool? SendEodReport,
    string[]? NotificationChannels
);
```

> **Timezone validation rule:** When `UpdateUserPreferencesDto.Timezone` is set, the backend
> must validate it: `DateTimeZoneProviders.Tzdb.GetZoneOrNull(dto.Timezone) != null`.
> If invalid, return 400 with error "Invalid IANA timezone key".
> The timezone is used by `INotificationService` to format dual-time alerts and by the
> JWT claims (included as `"tz"` claim on login) so the React frontend can render local times
> without an extra API call.

---

## Broker Connection Status DTO

```csharp
public record BrokerConnectionStatusDto(
    string BrokerName,
    bool IsConnected,
    bool IsAuthenticated,
    DateTimeOffset? LastHeartbeatAt,
    int ReconnectAttempts,
    string? LastDisconnectReason,   // from StreamDisconnected domain event
    DateTimeOffset? SessionExpiresAt // null for Zerodha (manual renewal)
);
```

---

## Controller Response Pattern

```csharp
// All MVC controllers inherit ApiControllerBase
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected string CorrelationId =>
        HttpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();

    protected IActionResult Ok<T>(T data)
        => base.Ok(ApiResponse.Ok(data, CorrelationId));

    protected IActionResult Fail(int statusCode, string error)
        => StatusCode(statusCode, ApiResponse.Fail<object>(error, CorrelationId));
}
```
