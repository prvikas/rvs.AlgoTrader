# Skill: SEBI Compliance — AlgoTrader

## Purpose
Compliance rules and patterns for SEBI-regulated trading systems in India.
Load when writing audit logging, order management, reporting, or any regulatory-facing code.

---

## Audit Log — Non-Negotiable Rules

### Schema
```sql
-- NEVER UPDATE or DELETE rows in audit_log — append-only
CREATE TABLE audit_log (
    id          BIGSERIAL PRIMARY KEY,
    actor       TEXT NOT NULL,     -- user ID, "SYSTEM", or broker name
    action      TEXT NOT NULL,     -- see action constants below
    entity_type TEXT,              -- "Order", "Position", "StrategyInstance", etc.
    entity_id   TEXT,              -- UUID of affected entity
    before_json JSONB,             -- state before change (null for creates)
    after_json  JSONB,             -- state after change (null for deletes)
    ip_address  TEXT,              -- from HttpContext for user actions
    created_at  TIMESTAMPTZ DEFAULT NOW()
);
```

### Required Audit Actions
```csharp
public static class AuditAction
{
    // Orders
    public const string OrderPlaced        = "ORDER_PLACED";
    public const string OrderModified      = "ORDER_MODIFIED";
    public const string OrderCancelled     = "ORDER_CANCELLED";
    public const string OrderFilled        = "ORDER_FILLED";
    public const string OrderRejected      = "ORDER_REJECTED";
    
    // Authentication
    public const string Login              = "LOGIN";
    public const string Logout             = "LOGOUT";
    public const string LoginFailed        = "LOGIN_FAILED";
    public const string TokenRefreshed     = "TOKEN_REFRESHED";
    
    // System
    public const string KillSwitchActivated   = "KILL_SWITCH_ACTIVATED";
    public const string KillSwitchDeactivated = "KILL_SWITCH_DEACTIVATED";
    public const string ConfigChanged         = "CONFIG_CHANGED";
    
    // Strategy
    public const string StrategyStarted      = "STRATEGY_STARTED";
    public const string StrategyPaused       = "STRATEGY_PAUSED";
    public const string StrategyStopped      = "STRATEGY_STOPPED";
    public const string StrategyAutoResumed  = "STRATEGY_AUTO_RESUMED";
    public const string StrategyMissedSession = "STRATEGY_MISSED_SESSION";
    
    // Compliance
    public const string ReconciliationMismatch = "RECONCILIATION_MISMATCH";
    public const string FieldEncrypted         = "FIELD_ENCRYPTED";
    public const string RiskLimitBreached      = "RISK_LIMIT_BREACHED";
}
```

### Correct Audit Pattern
```csharp
// ALWAYS log BEFORE and AFTER state for mutations
// NEVER update the audit_log row after insertion

public async Task StartStrategyAsync(Guid instanceId, string userId, string ipAddress, CancellationToken ct)
{
    var instance = await _repo.GetByIdAsync(instanceId, ct)
        ?? throw new NotFoundException($"Strategy instance {instanceId} not found");
    
    var beforeState = JsonSerializer.Serialize(instance); // capture BEFORE
    
    // Perform the mutation
    instance.Status = StrategyStatus.Running;
    instance.UpdatedAt = _clock.NowInstant();
    await _repo.UpdateAsync(instance, ct);
    
    // Log AFTER mutation
    await _auditLog.InsertAsync(new AuditLogEntry
    {
        Actor = userId,
        Action = AuditAction.StrategyStarted,
        EntityType = "StrategyInstance",
        EntityId = instanceId.ToString(),
        BeforeJson = JsonDocument.Parse(beforeState),
        AfterJson = JsonDocument.Parse(JsonSerializer.Serialize(instance)),
        IpAddress = ipAddress,
        CreatedAt = _clock.NowInstant()
    }, ct);
    
    // NEVER: UPDATE audit_log SET after_json = ... WHERE id = ...
}
```

---

## Market Hours Enforcement

```csharp
// ALL order placement and strategy evaluation MUST check market hours
// Uses IClock.Now() — NEVER DateTime.Now

public class LiveExecutionEngine : IExecutionEngine
{
    public async Task<ExecutionResult> ExecuteAsync(
        SignalResult signal, StrategyContext ctx, CancellationToken ct)
    {
        // Market hours check — MANDATORY
        if (!await _marketCalendar.IsWithinMarketHoursAsync(ct))
        {
            await _signalJournal.RecordSkippedAsync(
                signal, SkippedReason.MarketClosed, ctx.InstanceId, ct);
            return ExecutionResult.Skipped(SkippedReason.MarketClosed);
        }
        
        // Holiday check — MANDATORY
        if (!await _marketCalendar.IsTradingDayAsync(_clock.Today(), "NSE", ct))
        {
            await _signalJournal.RecordSkippedAsync(
                signal, SkippedReason.MarketClosed, ctx.InstanceId, ct);
            return ExecutionResult.Skipped(SkippedReason.MarketClosed);
        }
        
        // Continue with order placement...
    }
}
```

---

## Transaction Cost Itemization

```csharp
// SEBI requires accurate cost reporting — never use a single % estimate
// Always itemize: brokerage, STT, exchange, GST, SEBI charges, stamp duty, slippage

public record CostBreakdown(
    decimal Brokerage,           // ₹20 flat for discount brokers (intraday)
    decimal STT,                 // Securities Transaction Tax (sell side, intraday: 0.025%)
    decimal ExchangeCharges,     // NSE: 0.00335% of turnover
    decimal GST,                 // 18% of (brokerage + exchange charges)
    decimal SEBICharges,         // ₹10 per crore of turnover
    decimal StampDuty,           // 0.015% of buy side turnover (state tax)
    decimal Slippage,            // estimated market impact (configurable per profile)
    decimal TotalCost            // sum
)
{
    // Turnover = price × quantity × 2 (entry + exit for intraday)
    public static CostBreakdown Calculate(
        decimal price, int quantity, BacktestCostProfile profile, bool isIntraday = true)
    {
        var turnover = price * quantity;
        var brokerage = Math.Min(profile.BrokeragePerOrder, turnover * 0.0003m); // cap at 0.03%
        var stt = isIntraday ? 0m : turnover * profile.SttDelivery; // zero on buy, 0.025% on sell intraday
        var sttSellSide = isIntraday ? turnover * profile.SttIntraday : 0m;
        var exchange = turnover * profile.ExchangeChargePct;
        var gst = (brokerage + exchange) * profile.GstPct;
        var sebi = turnover / 10_000_000m * profile.SebiChargesPerCrore;
        var stamp = turnover * profile.StampDutyPct; // buy side only
        var slippage = turnover * profile.SlippagePct;
        
        var total = brokerage + sttSellSide + exchange + gst + sebi + stamp + slippage;
        return new CostBreakdown(brokerage, stt + sttSellSide, exchange, gst, sebi, stamp, slippage, total);
    }
}
```

---

## EOD PnL Report Requirements

The EOD report sent at 15:35 IST on trading days must include:

```
Subject: AlgoTrader EOD Report — {date}

Summary
-------
Gross PnL: ₹X,XXX.XX
Transaction Costs: ₹XXX.XX
Net PnL: ₹X,XXX.XX (after costs)
Win Rate: XX.X%
Total Trades: N

By Strategy
-----------
{strategy_name}: Net PnL ₹XXX.XX (N trades, X wins)

Open Positions
--------------
{symbol} @ {avg_price} × {qty} = ₹{current_pnl}

Alerts Today
------------
- {count} WARN alerts
- {count} CRITICAL alerts
```

Generated by `IEodReportJob`, triggered by Hangfire at 15:35 IST on trading days.

---

## Position Reconciliation — Mandatory Schedule

```csharp
// Must run every 5 minutes during market hours
// Using IMarketCalendarService.IsWithinMarketHours() — never hardcode hours

[DisableConcurrentExecution(300)]
public class ReconciliationJob : IReconciliationJob
{
    public async Task RunAsync(CancellationToken ct)
    {
        if (!await _marketCalendar.IsWithinMarketHoursAsync(ct))
            return; // Don't reconcile outside market hours
        
        foreach (var broker in _activeBrokers)
        {
            var brokerPositions = await _brokerClient.GetPositionsAsync(ct);
            var localPositions = await _positionRepo.GetOpenAsync(broker.Name, ct);
            
            var mismatches = FindMismatches(brokerPositions, localPositions);
            
            foreach (var mismatch in mismatches)
            {
                Log.Warning("Position mismatch for {Symbol} on {Broker}: broker={BrokerQty}, local={LocalQty}",
                    mismatch.Symbol, broker.Name, mismatch.BrokerQuantity, mismatch.LocalQuantity);
                
                await _auditLog.InsertAsync(new AuditLogEntry
                {
                    Actor = "SYSTEM",
                    Action = AuditAction.ReconciliationMismatch,
                    EntityType = "Position",
                    EntityId = mismatch.Symbol,
                    AfterJson = JsonDocument.Parse(JsonSerializer.Serialize(mismatch))
                }, ct);
                
                await _publisher.Publish(new PositionMismatchDetected(
                    Symbol: mismatch.Symbol, BrokerName: broker.Name,
                    BrokerQuantity: mismatch.BrokerQuantity, LocalQuantity: mismatch.LocalQuantity
                ));
            }
        }
    }
}
```

---

## Data Retention Policy

```sql
-- Audit log: PERMANENT (never delete — SEBI requires 5-year retention)
-- Signal journal: PERMANENT (evidence of all trading decisions)
-- Orders: PERMANENT
-- Positions: PERMANENT (closed positions too)

-- Disposable (safe to purge after TTL):
-- idempotency_keys: 24 hours (Hangfire cleanup job)
-- broker_latency_log: 90 days (performance analysis only)
-- data_quality_log: 30 days (operational)
-- alert_log: 1 year (operational)
```

---

## Role-Based Access Control — SEBI Compliance Points

```csharp
// These specific controls are required for compliance:

// 1. Kill switch — Admin only
[Authorize(Roles = "Admin")]
[HttpPost("kill-switch")]
public async Task<IActionResult> ActivateKillSwitch(...) { }

// 2. Broker credentials — Admin only, always masked in display
[Authorize(Roles = "Admin")]
[HttpGet("settings/broker-credentials")]
public async Task<IActionResult> GetBrokerCredentials(...)
{
    // Return masked values: "kite_***_secret" — never full key
    var masked = credentials.Select(c => c with { ApiSecret = Mask(c.ApiSecret) });
    return Ok(masked);
}

// 3. Audit log — Admin and Viewer (read-only for Viewer)
[Authorize(Roles = "Admin,Viewer")]
[HttpGet("audit-log")]
public async Task<IActionResult> GetAuditLog(...) { }

// 4. Order placement — Trader and Admin only (NOT Viewer)
[Authorize(Roles = "Admin,Trader")]
[HttpPost("orders")]
public async Task<IActionResult> PlaceOrder(...) { }

// Mask helper
private static string Mask(string? value) =>
    value is null or { Length: <= 6 } ? "***" 
    : value[..3] + new string('*', value.Length - 6) + value[^3..];
```
