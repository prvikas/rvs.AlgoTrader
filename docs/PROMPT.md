# PROMPT.md — Precision AI Coding Prompt

> Read this file only when explicitly requested via `docs/PROMPT.md`.
> Delete or stub each entry immediately after implementation is confirmed done.

---

## PROMPT-003 — DONE — DB Integrity + Backtest Engine Fixes + Data Services

## PROMPT-005 — DONE — Code-Level Bug Fixes (Direct Source Review)

---

## PROMPT-006 — Active — Full Source Review (BacktestEngine + ForwardTestEngine + 7 Strategies)

> Source files read this session:
> - `BacktestEngine.cs` (39 KB)
> - `ForwardTestEngine.cs` (12 KB)
> - `AlertCandleShortStrategy.cs` (20 KB)
> - `StrategyFactory.cs` (all 10 registrations confirmed)
> - `IApplicationServices.cs`, `IBacktestEngine.cs`, `IRepositories.cs`

---

### Section A — BacktestEngine.cs — Real Bugs Found

**BE-1 · HIGH · `CalculatePositionSize` — 25% equity cap ignores leverage reality**
```
// current code:
var maxByCapital = (int)(equity * 0.25m / entryPrice);
return Math.Min(sizeByRisk, maxByCapital);
```
For a ₹10L account and a ₹500 stock, this allows 500 shares = ₹2.5L exposure (25%). But for
a ₹50 stock (small cap), this allows 5,000 shares = ₹2.5L — this is fine. However, for a
₹5,000 stock (BankNifty spot/futures), maxByCapital = `(1,000,000 × 0.25) / 5000 = 50 lots`.
**There is no lower cap to ensure at least 1 share is affordable** — when equity drops to near
zero, `equity * 0.25 / entryPrice` can be fractional (e.g., `0.8`) which truncates to `0`,
permanently suppressing all signals even though `sizeByRisk > 0`. This causes the engine to
stop trading well before the bankruptcy guard triggers.

**Fix**: Add `if (maxByCapital <= 0) return 0;` BEFORE `Math.Min`, not instead of it.
Currently `Math.Min(sizeByRisk, 0)` returns 0 and the signal is silently skipped —
`skippedSignals` counter increments but no log distinguishes "no capital" from "stop too tight".
Add a specific log: `logger.LogDebug("[Backtest] Signal skipped: capital floor reached (equity={Equity} entry={Entry})", equity, entryPrice);`

---

**BE-2 · MEDIUM · `ComputeGroupedSharpe` — annualisation factor always `√252` regardless of grouping**
```csharp
return (decimal)(avg / stdDev * Math.Sqrt(252));
```
This is called for BOTH `dailySharpe` (groups = trading days → correct to multiply by √252)
AND `monthlySharpe` (groups = months → should multiply by √12, not √252).
The monthly Sharpe is currently **overstated by a factor of ~4.6** (√252/√12 ≈ 4.58).

**Fix**:
```csharp
private static decimal ComputeGroupedSharpe(
    List<BacktestTrade> trades, Func<BacktestTrade, string> keySelector,
    double annualisationFactor = 252)
{
    ...
    return (decimal)(avg / stdDev * Math.Sqrt(annualisationFactor));
}
```
Call sites:
```csharp
var dailySharpe   = ComputeGroupedSharpe(trades, ..., annualisationFactor: 252);
var monthlySharpe = ComputeGroupedSharpe(trades, ..., annualisationFactor: 12);
```

---

**BE-3 · MEDIUM · `TryClosePosition` — short gap-fill comment block is misaligned**
```csharp
if (candle.Open >= trade.StopLoss)
// Gap-fill: bar opened above SL
{ exitPrice = candle.Open; reason = "STOP_LOSS"; }
```
The comment is between the `if` condition and its brace block — syntactically valid C# but
dangerous if any developer inserts a line between the comment and `{`. This is a linting /
style issue but in a financial engine it causes misreads. **Fix**: move comment above the `if`.
Same issue exists in `ForwardTestEngine.TryClosePosition`.

---

**BE-4 · LOW · `ComputeReproducibilityHash` — `CryptoStream` with `Stream.Null` leaks on exception**
```csharp
using var sha = SHA256.Create();
using var cs  = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write);
```
If any `WriteDecimalTo` throws (decimal overflow), `cs.FlushFinalBlock()` is never called and
SHA256 internal state is corrupted silently — `sha.Hash` returns null, `Convert.ToHexString(sha.Hash!)` NRE.
**Fix**: wrap the entire hash body in `try/catch` and return a sentinel hash like `"error"` rather than crashing the whole backtest.

---

### Section B — ForwardTestEngine.cs — Real Bugs Found

**FT-1 · CRITICAL · Singleton holds `_activeStates` in-memory — survives pod restart but NOT app restart**
```csharp
private readonly Dictionary<Guid, ForwardTestState> _activeStates = new();
```
ForwardTestEngine is registered as `Singleton`. On app restart (deploy, crash, OOM kill),
`_activeStates` is empty. Any open positions are **permanently orphaned** — `StopSessionAsync`
will find no state, so the DB session row stays `Status = "Running"` forever. On next startup,
all `Running` sessions are replayed by the candle queue but `_activeStates` is empty → no trades
are tracked, no P&L is recorded.

**Fix — Phase 1 (in-session recovery)**:
On startup, query all `forward_test_sessions WHERE status = 'Running'` and re-hydrate
`_activeStates` from the last open `forward_test_trades` row (if any). Add `IForwardTestEngine.RecoverActiveSessionsAsync(CancellationToken)` called from `Program.cs` `IHostedService.StartAsync`.

**Fix — Phase 2 (open position recovery)**:
Add `open_direction`, `open_entry_price`, `open_stop_loss`, `open_take_profit`, `open_quantity`
columns to `forward_test_sessions` table (migration 032). Persist on every new entry, clear on close.

---

**FT-2 · HIGH · `ProcessCandleAsync` race condition — no lock around `_activeStates`**
```csharp
if (!_activeStates.TryGetValue(instance.Id, out var state)) return;
// ... long await chain ...
state.OpenTrade = new ForwardTestOpenTrade(...); // written after awaits
```
Multiple concurrent candle events for different instances can interleave. While `state` for a
given instance is only written by its own consumer (MassTransit serialises per-instance),
the `StartSessionAsync` and `StopSessionAsync` can arrive concurrently with `ProcessCandleAsync`.
If `StopSessionAsync` removes the key between the `TryGetValue` and `state.OpenTrade = ...`,
the orphaned state object is written to but never persisted.

**Fix**: Use `ConcurrentDictionary<Guid, ForwardTestState>` and switch `StartSessionAsync` /
`StopSessionAsync` to `TryAdd` / `TryRemove`. Add `Interlocked` guard on `OpenTrade` assignment
or use a per-instance `SemaphoreSlim(1,1)`.

---

**FT-3 · MEDIUM · Position sizing falls back to entry price as capital when `AllocatedCapital = 0` AND `LotSize = 0`**
```csharp
var allocatedCapital = instance.AllocatedCapital > 0 ? instance.AllocatedCapital
                     : credential.LotSize > 0 ? credential.LotSize * entryPrice : entryPrice;
```
When both are unset the entire sizing calculation uses `entryPrice` (e.g. ₹500) as the capital
base. `FixedFractional` on ₹500 at 1% risk = ₹5 risk budget → sizing engine returns 0 or 1 lot.
This is silently wrong — the engine will trade but size as if capital = ₹500.

**Fix**: Throw `InvalidOperationException` (or log a warning and return) if `allocatedCapital < 100`:
```csharp
if (allocatedCapital < 100)
{
    logger.LogWarning("[ForwardTest] AllocatedCapital not configured for {Instance} — skipping signal", instance.Name);
    return;
}
```

---

### Section C — AlertCandleShortStrategy.cs — Logic Bugs

**ACS-1 · HIGH · Scan loop exits after finding FIRST short + first long — misses better setups**
```csharp
if (shortAlertIdx != null && (longAlertIdx != null || !config.AllowLong)) break;
```
The loop breaks after finding any short alert candle, even if its breakout never fires. It only
advances `shortBreakoutIdx` if `candles[nextI].Low < candidate.Low` — but the break happens
before checking whether `nextI == candles.Count - 1` (i.e., whether it's today's most recent bar).
This means: if an old alert candle fired at 10:00 and never had a live breakout, the loop stops
scanning and misses a *later* alert candle at 14:30 that IS firing right now.

**Fix**: Separate the "scan for first alert candle" from the "check if breakout fires on the LATEST bar":
```csharp
// Find ALL alert candles today; pair each with the next bar.
// The earliest COMPLETE (alert + breakout bar visible) pattern is used for one-trade-per-day.
// Only fire signal if the breakout bar == candles.Count - 1.
```
Or alternatively: only set `shortAlertIdx` if the paired breakout bar IS the last bar,
continue scanning otherwise (don't `break` on a non-live setup).

---

**ACS-2 · MEDIUM · `sessionAvgVol` uses `candles.Count - 1` as upper bound — includes the current bar**
```csharp
for (int vi = todayStart; vi < candles.Count - 1; vi++)
```
This is actually correct — it correctly excludes the last bar. ✅ No bug here (noting for completeness).

---

**ACS-3 · LOW · `TrendFilterPeriod` EMA seed requires `EmaPeriod + TrendFilterPeriod` total candles but guard only checks `EmaPeriod + 1`**
```csharp
if (candles.Count < config.EmaPeriod + 1)
    return Task.FromResult(SignalResult.Skip(...));
```
When `TrendFilterPeriod = 20`, the trend EMA needs 20 candles to warm up. With only
`EmaPeriod + 1 = 6` candles, `emaTrend[i]` will be 0 for all `i < 19` and the check
`emaTrend[i] == 0m || candidate.Close < emaTrend[i]` silently bypasses the trend filter.
The strategy trades without a trend filter for the first 20 bars of each session.

**Fix**:
```csharp
var minRequired = Math.Max(config.EmaPeriod, config.TrendFilterPeriod) + 1;
if (candles.Count < minRequired)
    return Task.FromResult(SignalResult.Skip(SkippedReason.InsufficientData,
        $"Need {minRequired} candles; have {candles.Count}"));
```

---

### Section D — Options Strategies (CalendarSpread, IronCondor, ShortStraddleStrangle, VerticalSpread, FibOptionSpread, IntradayPcrOptions) — Gap Analysis

These 6 strategy folders exist in the file tree but **were NOT read in this session** (each is
10–25 KB). They must be reviewed in the next session. Known risk areas from the strategy names:

**D-1** — `IronCondor`, `ShortStraddleStrangle`, `VerticalSpread`, `CalendarSpread`:
All are multi-leg option strategies. BacktestEngine tracks a single `openTrade` with one
`EntryPrice` / `ExitPrice`. **Multi-leg strategies cannot be accurately backtested with the
single-position BacktestEngine** — each leg has a different fill price, Greeks, and expiry.
Verify whether these strategies return `SignalResult.Hold` with a note ("multi-leg — forward-test
only") or whether they incorrectly produce single-leg Buy/Sell signals.

**D-2** — `FibOptionSpread` and `IntradayPcrOptions`:
These depend on option chain data (OI, PCR, Greeks). Verify `StrategyContext.Candles` includes
the option chain fields, or whether these strategies are calling an external service via
`IOptionChainRepository` injected through the context. If the data source is missing in
backtest mode, the strategy will silently return `Hold` for every bar.

**D-3 — ACTION REQUIRED**: Read all 6 option strategy `.cs` files in the **next** review pass
and add concrete bugs here before implementation.

---

### Section E — Infrastructure / Missing Implementations

**INF-1 · HIGH · `IBreadthJobDispatcher` interface exists but no implementation registered**
File: `src/rvs.AlgoTrader.Application/Services/IBreadthJobDispatcher.cs`
The interface is defined (confirmed by listing). No concrete class was found in Infrastructure.
If any code path calls `IBreadthJobDispatcher.DispatchAsync(...)`, it will throw a DI resolution
exception at runtime.
**Fix**: Implement `BreadthJobDispatcher` in Infrastructure, register in DI, or add a no-op stub.

**INF-2 · HIGH · `INseEventCalendarImporter` interface exists but no implementation**
Same issue — interface confirmed present, implementation not found in Infrastructure listings.
**Fix**: Implement or stub.

**INF-3 · MEDIUM · `ITaxLotReportService` interface exists but no implementation**
**Fix**: Implement FIFO/LIFO lot matching or add a `NotImplementedException` stub with a TODO.

---

### Section F — `IBacktestEngine.cs` — Contracts vs Implementation Drift

**IBC-1 · MEDIUM · `BacktestRequest.WarmupBars` has no minimum enforcement at the API layer**
`BacktestEngine` uses `Math.Max(request.WarmupBars, strategy.MinWarmupBars)` — correct.
But if the API controller accepts `WarmupBars = 0` from the client and the strategy's
`MinWarmupBars = 200` (VcpSwing needs SMA200), the engine silently uses 200. The client
UI has no feedback that its value was overridden.

**Fix**: Return the effective `WarmupBars` used in `BacktestResult` so the UI can display
`"Using 200 warmup bars (overriding your input of 0)"`.

**IBC-2 · LOW · `BacktestRequest.CircuitBreakerPct` — no validation that it is between 0 and 1**
If the user sends `CircuitBreakerPct = 50` (meaning 50% but entered as a whole number),
`circuitBreakerFloor = initialCapital * 50 = ₹50M` which is higher than any realistic equity —
the circuit breaker fires on bar 1 and the backtest produces 0 trades with a confusing error.

**Fix**: Add a `[Range(0, 1)]` FluentValidation rule for `CircuitBreakerPct` in the validator,
or normalise `> 1` values by dividing by 100.

---

### Implementation Priority Order

| Priority | ID | File | Severity |
|---|---|---|---|
| 1 | FT-1 | ForwardTestEngine — orphaned sessions on restart | CRITICAL |
| 2 | FT-2 | ForwardTestEngine — race on _activeStates | HIGH |
| 3 | BE-2 | BacktestEngine — monthly Sharpe inflated 4.6× | MEDIUM |
| 4 | ACS-1 | AlertCandleShort — scan exits on non-live setup | HIGH |
| 5 | ACS-3 | AlertCandleShort — trend EMA warm-up guard | LOW→MEDIUM |
| 6 | BE-1 | BacktestEngine — capital floor silent skip | HIGH |
| 7 | D-1/D-2 | Read + fix 6 options strategy files | CRITICAL (next review) |
| 8 | INF-1/2/3 | Missing interface implementations | HIGH |
| 9 | IBC-2 | CircuitBreakerPct validation | LOW |
