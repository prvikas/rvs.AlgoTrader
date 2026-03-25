# Skill: Trading Domain — AlgoTrader

## Purpose
Rules and patterns for implementing trading-domain logic in the AlgoTrader codebase.
Load this skill whenever writing strategy code, signal logic, order management, or risk management.

---

## Signal Pipeline Rules

### Never Evaluate a Partial Candle
```csharp
// ❌ FORBIDDEN — evaluating on current open bar
var context = new StrategyContext(symbol, candlesByTf, funds, positions, _clock.Now(), config);
context.CandlesByTimeframe["15m"].Add(_aggregator.CurrentBar); // DO NOT DO THIS

// ✅ REQUIRED — only closed candles
// CandleAggregatorService fires CandleClosedEvent ONLY on bar boundary
// StrategyContext is built from closed candles in ICandleCache
```

### SignalResult Must Always Be Journalled
```csharp
// ALL outcomes — BUY, SELL, HOLD, SKIPPED — must be written to signal_journal
// Never silently drop a signal evaluation
// SKIPPED signals must have a non-null skipped_reason

// Enum: SkippedReason
// THROTTLED        — evaluation queue slot not acquired within timeout
// MARKET_CLOSED    — IMarketCalendarService.IsWithinMarketHours() returned false  
// KILL_SWITCH      — kill switch is active
// RISK_LIMIT       — risk profile limit breached
// INSUFFICIENT_CAPITAL — ICapitalAllocator.TryReserveAsync() returned false
// TIMEOUT          — IStrategy.EvaluateAsync() did not complete within EvaluationTimeoutMs
// OUTSIDE_SCHEDULE — current time is outside strategy's scheduled session window
```

---

## Strategy Implementation Contract

```csharp
// Minimum required for a valid IStrategy implementation:
public class MyStrategy : IStrategy
{
    private readonly IClock _clock;                          // REQUIRED
    private readonly IIndicatorService _indicators;          // batch (backtest use)
    private readonly IIncrementalEMA _ema;                   // O(1) live use
    // ... other incremental indicators

    public string Name => "MyStrategy";
    public StrategyMetadata Metadata => new(
        Description: "...",
        DefaultTimeframe: "15m",
        SupportedTimeframes: ["5m", "15m", "1h"],
        MinCandlesRequired: 50
    );

    public async Task<SignalResult> EvaluateAsync(StrategyContext ctx, CancellationToken ct)
    {
        // 1. Update incremental indicators with latest closed candle FIRST
        var latestCandle = ctx.CandlesByTimeframe["15m"].Last();
        _ema.Update(latestCandle);

        // 2. Check schedule (engine does this, but strategy can double-check)
        // 3. Evaluate signal logic
        // 4. Return SignalResult — never return null
        
        return new SignalResult(
            Signal: SignalType.BUY,
            EntryPrice: latestCandle.Close,
            StopLoss: latestCandle.Low - _config.SlBuffer,
            TakeProfit: entryPrice + (entryPrice - stopLoss) * _config.RRRatio,
            TrailingSLDistance: null,
            TrailingTPStep: null,
            Reason: $"Breakout above {swingHigh:F2} with volume {volumeRatio:F1}x SMA",
            Diagnostics: new Dictionary<string, object> {
                ["ema_value"] = _ema.Current,
                ["volume_ratio"] = volumeRatio,
                ["swing_high"] = swingHigh
            }
        );
    }
}
```

### Strategy Config Pattern (from config_json)
```csharp
// All strategy params must come from config_json — NEVER hardcode
public record PriceActionBreakoutConfig
{
    public int SwingLookback { get; init; } = 10;         // default values only
    public int VolumeSmaperiod { get; init; } = 20;
    public decimal VolumeMultiplier { get; init; } = 1.5m;
    public bool UseEmaFilter { get; init; } = true;
    public int EmaPeriod { get; init; } = 21;
    public decimal MinBodyPercent { get; init; } = 0.4m;  // 40% of candle range
    public decimal RRRatio { get; init; } = 2.0m;
    public decimal SlBufferPercent { get; init; } = 0.002m;
    public decimal TrailingSLActivationPercent { get; init; } = 0.01m;
    public int EvaluationTimeoutMs { get; init; } = 5000;
}

// Deserialize in constructor:
var config = JsonSerializer.Deserialize<PriceActionBreakoutConfig>(instance.ConfigJson)!;
```

---

## Order Placement Contract

### Idempotency-Key (Client Side)
```typescript
// React — useOrderSubmit hook
const submitOrder = async (dto: CreateOrderDto) => {
    const idempotencyKey = crypto.randomUUID(); // Generate ONCE per submission
    const response = await api.post('/api/v1/orders', dto, {
        headers: { 'Idempotency-Key': idempotencyKey }
    });
    return response.data;
};
// Never reuse an Idempotency-Key across different order intents
// Safe to retry the same request with the SAME key if the request fails (network error, etc.)
```

### Order Size Calculation
```csharp
// ALWAYS use ITransactionCostCalculator for realistic cost modelling
var tradeCapital = availableCapital * riskProfile.MaxCapitalPerTradePct / 100;
var qty = (int)Math.Floor(tradeCapital / entryPrice);
qty = Math.Min(qty, riskProfile.MaxOpenTradesPerSymbol * instrument.LotSize);

// Include estimated transaction costs in PnL calculation
var totalCost = await _costCalculator.CalculateAsync(new CostInput(
    BrokeragePerOrder: costProfile.BrokeragePerOrder,
    Quantity: qty,
    Price: entryPrice,
    InstrumentType: instrument.Type,
    IsIntraday: isIntraday
));
```

---

## Risk Management Rules

```csharp
// Check all of these before placing ANY live order:

// 1. Kill switch
if (await _killSwitch.IsActiveAsync(ct)) 
    return SkippedResult(SkippedReason.KillSwitch);

// 2. Market hours
if (!await _marketCalendar.IsWithinMarketHoursAsync(ct))
    return SkippedResult(SkippedReason.MarketClosed);

// 3. Capital reservation (atomic)
if (!await _capitalAllocator.TryReserveAsync(instanceId, brokerName, requiredAmount, ct))
    return SkippedResult(SkippedReason.InsufficientCapital);

// 4. Daily drawdown check
var dailyPnl = await _positionRepo.GetDailyPnlAsync(brokerName, ct);
if (dailyPnl < -riskProfile.MaxDailyDrawdownPct / 100 * allocatedCapital)
{
    await _alerts.SendAsync(AlertLevel.Warn, "Daily drawdown limit reached", ct);
    return SkippedResult(SkippedReason.RiskLimit);
}

// 5. Max trades per day
var todayTradeCount = await _orderRepo.GetTodayTradeCountAsync(instanceId, ct);
if (todayTradeCount >= riskProfile.MaxTradesPerDay)
    return SkippedResult(SkippedReason.RiskLimit);
```

---

## Trailing SL/TP Implementation

### TrailingStopLossService — Full Contract
```csharp
// Interface (Application layer)
public interface ITrailingStopLossService
{
    /// <summary>
    /// Called on every closed candle for each open position.
    /// Returns true if SL was updated, false if no change needed.
    /// </summary>
    Task<bool> UpdateTrailingStopAsync(
        Position position,
        decimal currentPrice,
        CancellationToken ct);
}

// Implementation (Infrastructure layer)
public class TrailingStopLossService : ITrailingStopLossService
{
    private readonly IOrderRepository _orderRepo;
    private readonly ISignalJournalRepository _journal;

    public async Task<bool> UpdateTrailingStopAsync(
        Position position,
        decimal currentPrice,
        CancellationToken ct)
    {
        if (position.TrailingSLDistance is null || position.TrailingSLActivationPct is null)
            return false; // trailing not configured for this position

        var activationThreshold = position.TrailingSLActivationPct.Value / 100m;

        if (position.Direction == OrderDirection.Buy)
        {
            var profitPct = (currentPrice - position.AvgEntryPrice) / position.AvgEntryPrice;

            // Step 1: Check if activation threshold is met
            if (profitPct < activationThreshold)
                return false; // not yet profitable enough to activate trailing

            // Step 2: Ratchet SL up (never down for a long)
            var newSl = currentPrice - position.TrailingSLDistance.Value;
            if (newSl <= (position.CurrentTrailingSL ?? decimal.MinValue))
                return false; // no improvement

            // Step 3: Persist the new SL level
            position.CurrentTrailingSL = newSl;
            await _orderRepo.UpdateTrailingSlAsync(position.OrderId, newSl, ct);
            return true;
        }
        else if (position.Direction == OrderDirection.Sell)
        {
            var profitPct = (position.AvgEntryPrice - currentPrice) / position.AvgEntryPrice;

            if (profitPct < activationThreshold)
                return false;

            // Ratchet SL down (never up for a short)
            var newSl = currentPrice + position.TrailingSLDistance.Value;
            if (newSl >= (position.CurrentTrailingSL ?? decimal.MaxValue))
                return false;

            position.CurrentTrailingSL = newSl;
            await _orderRepo.UpdateTrailingSlAsync(position.OrderId, newSl, ct);
            return true;
        }

        return false;
    }
}
```

### Trailing SL — Unit Test Coverage Required
```csharp
// rvs.AlgoTrader.UnitTests/Trading/TrailingStopLossServiceTests.cs
// Required test cases:
[Fact] UpdateTrailingStop_WhenBuyPositionBelowActivationThreshold_DoesNotMoveSL()
[Fact] UpdateTrailingStop_WhenBuyPositionAtActivationThreshold_ActivatesTrailingSL()
[Fact] UpdateTrailingStop_WhenBuyPositionPriceRises_RatchetsSLUp()
[Fact] UpdateTrailingStop_WhenBuyPositionPriceFalls_DoesNotLowerSL() // SL only moves up
[Fact] UpdateTrailingStop_WhenSellPositionAboveActivationThreshold_RatchetsSLDown()
[Fact] UpdateTrailingStop_WhenNoTrailingConfigured_ReturnsFalse()
[Fact] UpdateTrailingStop_WhenSLWouldNotImprove_DoesNotCallRepository()
```

### Integration with Execution Engine
```
On every CandleClosedEvent:
  For each open position with trailing_sl_distance IS NOT NULL:
    1. Call ITrailingStopLossService.UpdateTrailingStopAsync(position, candle.Close, ct)
    2. If SL was hit (candle.Low < position.CurrentTrailingSL for BUY):
       → call IExecutionEngine.ClosePositionAsync(position, ExitReason.TrailingSL, ct)
       → write to signal_journal with exit_reason = "TRAILING_SL"
```

---

## Candle Aggregation — Bar Boundary Logic

```csharp
// CandleAggregatorService — bar boundary detection using IClock.Now()
public void OnTick(BrokerTick tick)
{
    var now = _clock.Now(); // ZonedDateTime IST
    var barKey = (tick.Symbol, _timeframe);
    
    if (_currentBars.TryGetValue(barKey, out var currentBar))
    {
        var expectedBarEnd = GetNextBarBoundary(currentBar.Timestamp, _timeframe);
        
        if (now.ToInstant() >= expectedBarEnd)
        {
            // Bar is closed — emit event
            var closedCandle = currentBar.Close(tick.LTP, tick.Volume);
            _currentBars[barKey] = Candle.Open(tick); // Start new bar
            
            // NEVER pass closedCandle to strategy directly here
            // Emit event — consumer picks it up asynchronously
            _publisher.Publish(new CandleClosedEvent(tick.Symbol, _timeframe, closedCandle));
            _cache.AppendAsync(tick.Symbol, _timeframe, closedCandle, CancellationToken.None);
        }
        else
        {
            // Update current bar — DO NOT signal strategy
            currentBar.Update(tick.LTP, tick.Volume);
        }
    }
}
```

---

## IST Time Handling

```csharp
// All time operations use NodaTime
private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

// Market hours check
public bool IsWithinMarketHours(ZonedDateTime istTime)
{
    if (istTime.DayOfWeek is IsoDayOfWeek.Saturday or IsoDayOfWeek.Sunday)
        return false;

    var timeOfDay = istTime.TimeOfDay;
    return timeOfDay >= new LocalTime(9, 15) && timeOfDay < new LocalTime(15, 30);
}

// Session window check (from schedule_json)
public bool IsWithinScheduledSession(StrategyInstanceConfig config, ZonedDateTime istTime)
{
    var dayOfWeek = istTime.DayOfWeek.ToString()[..3].ToUpper(); // "MON", "TUE", etc.
    if (!config.Schedule.Days.Contains(dayOfWeek))
        return false;

    var sessionStart = LocalTime.Parse(config.Schedule.SessionStart);
    var sessionStop = LocalTime.Parse(config.Schedule.SessionStop);
    var timeOfDay = istTime.TimeOfDay;

    return timeOfDay >= sessionStart && timeOfDay < sessionStop;
}

// Persistence — always use Instant for TIMESTAMPTZ columns
public Instant ToInstant(ZonedDateTime ist) => ist.ToInstant();
public ZonedDateTime FromInstant(Instant instant) => instant.InZone(Ist);
```

---

## Dual-Timezone Handling — IST (Broker) vs User Local Time

**The user may be in a different timezone (e.g. CST = America/Chicago = UTC-6/UTC-5)
while all Indian brokers and market operations run on IST (UTC+5:30).
This is an 11.5h to 12.5h offset depending on daylight savings.**

### Three-Layer Timezone Contract (NEVER mix these layers)

```
Layer 1 — IST (trading operations, backend only)
  • IClock.Now() → ZonedDateTime in Asia/Kolkata
  • All schedule comparisons, market hours, bar boundaries
  • schedule_json.session_start / session_stop are IST time-of-day strings
  • NEVER send raw ZonedDateTime IST to the API response

Layer 2 — UTC (persistence and API wire format)
  • All TIMESTAMPTZ columns: stored as UTC Instant
  • All DateTimeOffset fields in API DTOs: UTC
  • Frontend receives UTC ISO 8601 strings; converts locally

Layer 3 — User Local Timezone (frontend display only)
  • Stored in user_preferences.timezone (IANA key: "America/Chicago", "Asia/Kolkata", etc.)
  • Used ONLY in React for display: Intl.DateTimeFormat, date-fns-tz, Luxon
  • Never sent to backend for trading logic
```

### Timezone-Aware Utility (Backend)

```csharp
// Application/Services/TimezoneService.cs
public static class TimezoneHelper
{
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    /// <summary>
    /// Convert UTC Instant to IST ZonedDateTime for trading operations.
    /// </summary>
    public static ZonedDateTime ToIst(Instant utc) => utc.InZone(Ist);

    /// <summary>
    /// Convert UTC Instant to any IANA timezone for notification display.
    /// Used when sending alerts/emails — show time in BOTH IST and user's local zone.
    /// </summary>
    public static ZonedDateTime ToUserZone(Instant utc, string ianaTimezone)
    {
        var tz = DateTimeZoneProviders.Tzdb[ianaTimezone]; // e.g. "America/Chicago"
        return utc.InZone(tz);
    }

    /// <summary>
    /// Format a timestamp for notifications: shows IST + user local time.
    /// Example output: "10:45 IST (00:15 CST)"
    /// </summary>
    public static string FormatDualTime(Instant utc, string userIanaTimezone)
    {
        var ist = ToIst(utc);
        var local = ToUserZone(utc, userIanaTimezone);
        var istLabel = ist.ToString("HH:mm", null) + " IST";

        // Only add local conversion if user is NOT in IST
        if (userIanaTimezone == "Asia/Kolkata")
            return istLabel;

        var localLabel = local.ToString("HH:mm", null) + " " + local.Zone.Id.Split('/').Last();
        return $"{istLabel} ({localLabel})";
    }
}
```

### Schedule Editor — What the UI Must Show

```typescript
// React: Schedule Editor component
// ALWAYS store session_start/session_stop as IST time strings in schedule_json
// Display the IST time AND a conversion hint in the user's timezone

function SessionTimeDisplay({ istTimeStr, userTimezone }: { istTimeStr: string, userTimezone: string }) {
    // istTimeStr = "09:20" (always IST from schedule_json)
    const [h, m] = istTimeStr.split(':').map(Number);

    // Build a date on today (IST) at the given time, then convert to user tz
    const istDate = new Date();
    // IST = UTC+5:30 → subtract to get UTC, then re-render in user tz
    const utcMs = Date.UTC(istDate.getFullYear(), istDate.getMonth(), istDate.getDate(), h - 5, m - 30);
    const localTime = new Intl.DateTimeFormat('en-US', {
        timeZone: userTimezone,
        hour: '2-digit',
        minute: '2-digit',
        hour12: false,
        timeZoneName: 'short'
    }).format(new Date(utcMs));

    return (
        <span>
            {istTimeStr} IST
            {userTimezone !== 'Asia/Kolkata' && (
                <span className="text-muted text-xs ml-1">({localTime})</span>
            )}
        </span>
    );
}
// Example render: "09:20 IST (22:50 CST)"
```

### Notification Template (Backend)

```csharp
// INotificationService — always include both times for non-IST users
public string FormatAlertMessage(string body, Instant occurredAt, string userTimezone)
{
    var timeStr = TimezoneHelper.FormatDualTime(occurredAt, userTimezone);
    return $"{body}\nTime: {timeStr}";
}

// Example for a CST user:
// "RELIANCE BUY signal triggered\nTime: 10:45 IST (23:15 CST)"
//
// Example for an IST user:
// "RELIANCE BUY signal triggered\nTime: 10:45 IST"
```

### user_preferences Table (required column)

```sql
-- user_preferences table must have:
timezone VARCHAR(50) NOT NULL DEFAULT 'Asia/Kolkata'  -- IANA timezone key
-- Examples: 'America/Chicago' (CST), 'America/New_York' (EST), 'Europe/London', 'Asia/Kolkata'
-- Validated on write: DateTimeZoneProviders.Tzdb[timezone] must not throw
```

### Common CST ↔ IST Reference (for documentation and manual checks)

```
IST 09:15 (market open)  = CST 22:45 (previous day) / CDT 23:45 (previous day)
IST 09:20 (typical entry)= CST 22:50 / CDT 23:50 (prev day)
IST 15:10 (typical exit) = CST 04:40 / CDT 05:40
IST 15:30 (market close) = CST 05:00 / CDT 06:00

Note: CDT (Central Daylight Time) is UTC-5, active Mar–Nov in the US.
      CST (Central Standard Time) is UTC-6, active Nov–Mar.
      IST does NOT observe daylight savings — offset is always UTC+5:30.
```

---

## Transaction Cost Model (Indian Markets)

```csharp
// All cost components for Indian equities intraday
public record TransactionCostResult(
    decimal Brokerage,          // flat per order (₹20 for discount brokers)
    decimal STT,                // Securities Transaction Tax
    decimal ExchangeCharges,    // NSE/BSE exchange transaction charges
    decimal GST,                // 18% on brokerage + exchange charges
    decimal SEBICharges,        // ₹10 per crore of turnover
    decimal StampDuty,          // 0.015% on buy side only
    decimal Slippage,           // estimated market impact
    decimal Total               // sum of all above
);

// STT rates (intraday equity):
// Buy: 0 (zero for intraday buy)
// Sell: 0.025% of turnover

// Never simplify this to a single percentage — SEBI compliance requires itemized costs
```
