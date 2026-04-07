# PROMPT.md — Precision AI Coding Prompt

> Read this file only when explicitly requested via `docs/PROMPT.md`.
> Delete or stub each entry immediately after implementation is confirmed done.

---

## PROMPT-003 — DONE — DB Integrity + Backtest Engine Fixes + Data Services

## PROMPT-005 — DONE — Code-Level Bug Fixes (Direct Source Review)

---

## PROMPT-006 — DONE — Full Source Review + Bug Fixes (BacktestEngine + ForwardTestEngine + AlertCandleShort)

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
`emaTrend[i] == 0m || candidate.Close < emaTrend[i]` silently passes as "above trend EMA"
for the first 19 bars, allowing entry on unfiltered signals.

**Fix**: Change guard to `if (candles.Count < Math.Max(config.EmaPeriod + 1, config.TrendFilterPeriod + 1))`.

---

## PROMPT-007 — BacktestJobManager + PositionSizingEngine + BlackScholes + SpreadOrderManager + IronCondor + CommissionModel

> Source files read:
> - `BacktestJobManager.cs`
> - `PositionSizingEngine.cs`
> - `BlackScholesEngine.cs`
> - `OptionLegSelector.cs`
> - `SpreadOrderManager.cs`
> - `IndianMarketCommissionModel.cs`
> - `IronCondorStrategy.cs`

---

### Section A — BacktestJobManager.cs

**BJ-1 · HIGH · `_jobs` ConcurrentDictionary never evicted — unbounded memory leak**
Every `BacktestResultDto` (includes full trade list + chart samples) is stored indefinitely.
**Fix**: Background cleanup task on Singleton — evict jobs where `DateTimeOffset.UtcNow - job.CompletedAt > 24h`.

**BJ-2 · MEDIUM · `StartedAt` is stamped at enqueue time, not execution start**
`BacktestJob` stamps `StartedAt = DateTimeOffset.UtcNow` on construction. Job may queue for minutes. `BacktestResultDto.StartedAt` then uses `UtcNow` at completion — inconsistent.
**Fix**: Add `RunStartedAt` property set at top of `RunJobAsync` try-block. Use in `MapToDto`.

**BJ-3 · LOW · Auto-download hardcodes `"1m"` for all non-daily timeframes**
```csharp
var downloadTf = dto.Timeframe == "1d" ? "1d" : "1m";
```
If user backtests on `"15m"` or `"1h"`, auto-download fetches 1m data. If `CandleAggregatorService` doesn't re-aggregate, the retry fails.
**Fix**: Pass `dto.Timeframe` directly as `downloadTf`. Fall back to `"1m"` only if broker doesn't support requested TF.

---

### Section B — PositionSizingEngine.cs

**PS-1 · HIGH · `KellyCriterion` formula is wrong — produces 2–5× over-sizing**
Current code uses a nonstandard hybrid formula. Correct Half-Kelly:
```csharp
decimal kelly = 0.5m * (w - (1m - w) / r);
```
where `w` = win rate, `r` = avg win / avg loss.

**PS-2 · MEDIUM · `AtrBased` uses raw ATR as risk-per-lot without multiplier**
14-period ATR on Nifty ≈ 100pts. `risk = 1%` on ₹10L = ₹10,000. Gives `10,000/100 = 100 lots` = ₹1.25Cr notional = 12.5× leverage.
**Fix**: Add `AtrMultiplier` to `PositionSizingConfig` (default `2.0`). `riskPerLot = atr.Value * config.AtrMultiplier`.

**PS-3 · LOW · `VolatilityTargeting` uses option premium as denominator — wrong for options**
`lots = targetAmount / price`. For options `price` = ₹50 premium → 20,000 lots on ₹10L.
**Fix**: Guard `if (InstrumentType == Options)` use `underlyingPrice`, not `optionPremium`.

---

### Section C — BlackScholesEngine.cs + OptionLegSelector.cs

**BS-1 · MEDIUM · Expired options return `delta = -1` unconditionally for puts**
```csharp
if (timeToExpiryYears <= 0)
    return new GreeksSnapshot(isCall ? 0m : -1m, ...);
```
On expiry day all puts return delta = -1, causing wrong strike selection.
**Fix**: Return `GreeksSnapshot(0, 0, 0, 0, 0, 0)` for expired options.

**BS-2 · LOW · `OptionLegSelector.SelectByDelta` hardcodes IV = `0.15`**
In high-IV regimes (e.g. 35%), wrong delta ranking across strikes.
**Fix**: Accept `decimal? atmIv = null` parameter, fall back to `0.15` only when null.

---

### Section D — SpreadOrderManager.cs

**SO-1 · CRITICAL · Rollback uses `CancelOrderAsync` on market orders that already filled**
Market orders fill in milliseconds. Cancelling a filled order ID is a no-op — the open leg stays live.
**Fix**: Replace rollback with a reverse `PlaceOrderAsync` (opposite direction, same quantity). Add `RollbackSpreadLegAsync` private method.

**SO-2 · HIGH · `CloseSpreadAsync` calls `CancelOrderAsync` on filled entry order IDs**
These are entry order IDs (already filled). Cancelling them is a no-op; actual position stays open.
**Fix**: Call `PlaceOrderAsync` with opposite `Direction` + same `Quantity` to exit each leg.

**SO-3 · MEDIUM · Leg `Quantity` always = `spec.Quantity = 1` — NSE lot size never applied**
Every spread places 1-unit orders instead of 1-lot (Nifty=25, BankNifty=15).
**Fix**: Multiply `spec.Quantity × instrument.LotSize` when building `OrderRequest`.

---

### Section E — IronCondorStrategy.cs

**IC-1 · CRITICAL · `SpreadEntry` signal silently treated as `Hold` in BacktestEngine**
`BacktestEngine.ProcessSignal` has no `SpreadEntry` handler — falls through to default = Hold.
Iron Condor backtests produce 0 trades and a flat equity curve. Completely misleading.
**Fix**:
```csharp
case SignalType.SpreadEntry:
    result.SkippedSignalCount++;
    result.Error ??= "Multi-leg spread strategies require Forward Test mode.";
    break;
```
Set `IsSpreadStrategy = true` in `StrategyFactory` to suppress "Run Backtest" button in UI.

**IC-2 · HIGH · Wing leg `OtmByStrike` anchors from ATM, not from short strike**
Wing at `OtmByStrike=2` from ATM selects a strike BELOW the short call → debit spread, not credit wing.
**Fix**: Add `FromStrike` parameter to `SpreadLeg` / `OptionsLegSpec`. In `SelectOtmByCount` use `FromStrike ?? spotPrice` as anchor.

**IC-3 · MEDIUM · No guard for stale `AtmIv == 0` in OptionChain snapshot**
`iv = 0` passes the `< MinAtmIv` guard (blocks entry safely) but silently suppresses all entries in pre-market.
**Fix**: `if (chain.AtmIv <= 0) return Hold("OptionChain IV is zero — stale or pre-market snapshot")`.

---

### Section F — IndianMarketCommissionModel.cs

**CM-1 · MEDIUM · STT for equity delivery applied on buy side — SEBI charges sell side only**
`stt = tradeValue * 0.001m` runs on both buy and sell. CNC STT is sell-side only (0.1%).
**Fix**: `if (isEquity && isDelivery && !isBuy) stt = tradeValue * 0.001m;`

**CM-2 · LOW · Options exchange fee `0.00002` (0.002%) — NSE FY26 rate is `0.00053` (0.053%)**
Current undercharges by 26×. ₹100 premium: ₹0.002 charged vs ₹0.053 actual.
**Fix**: `exchangeFee = price * quantity * 0.00053m;`

---

## PROMPT-008 — All Strategy Files Deep Review

> Source files read this session:
> - `ShortStraddleStrangleStrategy.cs`
> - `CalendarSpreadStrategy.cs`
> - `VerticalSpreadStrategy.cs`
> - `EmaVwapMomentumStrategy.cs`
> - `FibOptionSpreadStrategy.cs`
> - `PriceActionBreakoutStrategy.cs`

---

### Section A — ShortStraddleStrangleStrategy.cs

**SS-1 · CRITICAL · `MaxLossMultiple` is configured but NEVER enforced in strategy or engine**
The config has `MaxLossMultiple = 2.0` and the spec comment says
"MANDATORY: force-close when loss ≥ this × premium". But `EvaluateAsync` only emits
`SpreadEntry` — it does NOT pass `MaxLossMultiple` into `SpreadSignalResult` or any
stop-loss field. `SpreadOrderManager` and `ForwardTestEngine` have no code that checks
`realised_loss >= MaxLossMultiple * premium_received`. For a naked short straddle this means
**unlimited loss exposure with no automated stop** — exactly what the spec says must not happen.

**Fix**:
1. Add `MaxLossMultiple` to `SpreadSignalResult` (new field: `decimal? StopLossMultiple`).
2. `ShortStraddleStrangleStrategy` sets `StopLossMultiple = config.MaxLossMultiple` on the result.
3. `SpreadOrderManager.MonitorSpreadAsync` (or new `ISpreadRiskMonitor`) polls MTM P&L;
   calls `CloseSpreadAsync` when `loss >= maxLoss`.
4. `ForwardTestEngine` simulates same: on each candle, if total option premium lost
   `>= initialCredit * MaxLossMultiple`, exit the spread at mid-price.

**SS-2 · HIGH · Strangle `ByDelta` uses same `StrangleDelta` for both call and put — asymmetric IV skew ignored**
```csharp
callLeg = new SpreadLeg(..., TargetDelta: config.StrangleDelta, ...);
putLeg  = new SpreadLeg(..., TargetDelta: config.StrangleDelta, ...);
```
In Indian markets (Nifty/BankNifty), put-side IV skew is consistently 2–5 vol points higher
than call-side. A 20-delta put and a 20-delta call are NOT equidistant from ATM — the put
strike is closer. This biases the strangle to have unequal breakevens and more downside risk.

**Fix**: Add `StrangleCallDelta` and `StranglePutDelta` config fields (default both to `0.20`).
Show in UI schema with hint: "Put delta should be 1–3 points higher than call delta to account for IV skew".

**SS-3 · MEDIUM · No `DTE` (days-to-expiry) filter — strategy can enter on expiry day**
No check for how many days remain until expiry. Entering a short straddle 0 DTE means
maximum gamma risk — the slightest move causes large losses and immediate assignment risk.

**Fix**: Add `MinDte` (minimum days to expiry, default `3`) and `MaxDte` (default `14`) to config.
In `EvaluateAsync`, require `context.OptionChain.NearestExpiryDte >= config.MinDte && <= config.MaxDte`.
Return `Hold` with "DTE out of range" if not satisfied.

**SS-4 · LOW · `SpreadSignalResult.DiagnosticsJson` uses anonymous object — not serialisable to DB column**
```csharp
DiagnosticsJson: new { atmIv = iv, type = typeName, minIv = config.MinAtmIv }
```
`SpreadSignalResult` expects `DiagnosticsJson` as `IReadOnlyDictionary<string, decimal>` (per
interface), but an anonymous object is passed. This compiles only if the parameter type is `object?`.
If it's stored as JSON in DB, anonymous object serialises correctly, but if it's cast to
`Dictionary<string, decimal>` elsewhere, it throws `InvalidCastException` at runtime.

**Fix**: Change to `new Dictionary<string, decimal> { ["atmIv"] = iv, ["minIv"] = config.MinAtmIv }`.
(Drop non-decimal `type` string — put it in `Reason` instead.)

---

### Section B — CalendarSpreadStrategy.cs

**CS-1 · CRITICAL · `NearestWeekly` flag exists on `SpreadLeg` but `OptionLegSelector` has no multi-expiry support**
```csharp
var nearLeg = new SpreadLeg(..., NearestWeekly: true,  ...);
var farLeg  = new SpreadLeg(..., NearestWeekly: false, ...);
```
`OptionLegSelector.ResolveAsync` resolves strikes from `context.OptionChain` — a single
expiry snapshot. It has no logic to select different expiry dates based on `NearestWeekly`.
Both legs will resolve to the SAME expiry (whichever expiry the chain snapshot is for).
The calendar spread becomes a **synthetic straddle** (same expiry, same strike, net-flat position) — completely wrong.

**Fix**: `StrategyContext` must carry two option chains: `NearChain` (nearest weekly) and
`FarChain` (next monthly). `SpreadLeg` with `NearestWeekly = true` resolves from `NearChain`,
`false` from `FarChain`. `StrategyEvaluationQueue` must pre-fetch both chains. Add
`OptionChainNear` and `OptionChainFar` to `StrategyContext`.

**CS-2 · HIGH · IV bias for calendar is inverted — strategy sells near when IV is LOW**
```csharp
if (iv > config.MaxAtmIv)
    return Hold("long vega too expensive");
if (iv < config.MinAtmIv)
    return Hold("near-expiry premium insufficient");
```
The comment says "buy cheap long-dated vega before IV expansion". But in a **low-IV** environment,
the near leg premium is small, making the net debit paid high relative to the potential gain.
Calendar spreads are most profitable when: (a) IV term structure is in contango (near IV < far IV),
OR (b) IV is expected to rise (enter in low IV, exit after IV expansion). The current filter
`iv < MinAtmIv = 8%` blocks entry when IV is very low — but this is exactly when calendars
should be entered (cheapest long-vega). The `MinAtmIv = 8%` floor is overly restrictive for
index options where 8% is already near historical low.

**Fix**: Change filter to check **IV term structure slope** (near IV vs far IV) rather than
absolute ATM IV. Add `context.OptionChainFar?.AtmIv` once CS-1 is fixed. Entry when
`farIv - nearIv >= config.MinTermStructureSlope` (default `1.5` vol points = near cheaper than far).

**CS-3 · LOW · `IsBullishBias` / `IsBearishBias` from `OptionChain` not defined in the codebase**
The strategy reads `chain.IsBullishBias` and `chain.IsBearishBias`. These are properties on
`OptionChainSnapshot`. If they are computed fields (e.g., derived from PCR), they must be
populated by the chain fetcher. If they are hardcoded `false`, all calendars default to call
calendar regardless of market bias. Verify the property source and add test coverage.

---

### Section C — VerticalSpreadStrategy.cs

**VS-1 · HIGH · `OtmByStrike` for wing leg anchors from ATM, same as IC-2 bug in IronCondor**
```csharp
leg2 = new SpreadLeg(OptionType.Call, OrderDirection.Sell, StrikeSelectionMode.OtmByStrike,
                     OtmStrikes: config.SpreadWidthStrikes, Quantity: 1);
```
For `BullCallSpread`: `leg1` = buy call at 40-delta (e.g., strike 24,900). `leg2` = sell call
at `OtmByStrike=2` from **ATM** (e.g., strike 25,100 if ATM=24,900+1 interval). This is
**correct by accident** only when `leg1` strike ≈ ATM. When `LongLegDelta = 0.40` and spot is
at 24,700, the long strike is 24,800 (1 strike ITM). The short call at ATM+2 is 25,100.
Spread width = 300pts, not `2 intervals × 100 = 200pts` as intended.

**Fix**: Same `FromStrike` fix as IC-2 — wing leg `OtmByStrike` must anchor from `leg1`'s
resolved strike, not ATM. This requires two-pass resolution: resolve `leg1` first, pass its
strike as `FromStrike` to `leg2`.

**VS-2 · MEDIUM · `BullPutSpread` and `BearCallSpread` use `ShortLegDelta` but no `LongLegDelta` parameter for the wing**
For credit spreads, the short leg uses `ShortLegDelta` (e.g., 0.25), and the long (wing) leg
uses `OtmByStrike`. But `OtmByStrike` is integer strike-count based while the short leg is
delta-based. When strike spacing is uneven (as in NSE stocks), the actual wing delta could be
0.10 or 0.02 — very different risk profiles. The spread width in rupees is unknown at signal time.

**Fix**: Add a `CreditSpreadWingMode` enum to config: `OtmByStrike` (current) or `ByDelta`.
When `ByDelta`, add `WingDelta` config parameter (default `0.10`). Pass as `TargetDelta` to
the wing leg. The delta difference (short-wing) = known max-loss per lot.

**VS-3 · LOW · Single `TrendSmaPeriod` for all 4 spread types — bearish spreads should use downtrend SMA, not same period**
`BullCallSpread` and `BullPutSpread` both use `close > sma` (correct). But `BearPutSpread`
and `BearCallSpread` use `close < sma`. With a single `TrendSmaPeriod = 50`, a fast-moving
bear market might not yet have crossed below SMA50, blocking bearish entries that are clearly
valid on shorter-term MAs. Config should expose `TrendSmaPeriodBull` and `TrendSmaPeriodBear`
separately (can default to the same value to preserve backward compatibility).

---

### Section D — EmaVwapMomentumStrategy.cs

**EV-1 · MEDIUM · VWAP resets on new IST day but daily chart candles have no intraday session boundary**
```csharp
var sessionDate = candles[0].OpenTime.ToInstant().InZone(Ist).Date;
// Reset at each new IST session day
if (candleDate != sessionDate) { cumPV = 0; cumVol = 0; }
```
For daily charts each candle is 1 day — so VWAP resets every single bar, making
`vwap[i] = (H+L+C)/3` (typical price of that single candle). This is just the typical price,
not a VWAP. The indicator label "VWAP" becomes meaningless on daily data, and the filter
`close > vwap` becomes `close > typicalPrice(today)` which is always approximately true for an
up-close candle. This inflates BUY signal frequency on daily backtests.

**Fix**: Add `TimeframeMinutes` to `EmaVwapMomentumConfig` (inferred from candle data or passed
from context). When `TimeframeMinutes >= 1440` (daily), substitute a **20-period VWMA** (volume-
weighted moving average) instead of session VWAP. Document the substitution in the UI hint.

**EV-2 · LOW · `NoTradeAfterMinutes = 900` (15:00 IST) blocks all signals on daily charts**
Daily charts have `OpenTime = 09:15 IST` for the NSE session. `barMinutes = 9*60+15 = 555`,
which is `< 900` so the filter does NOT block daily signals. ✅ No bug here for standard daily
bars. However, for US markets or crypto where `OpenTime` can vary, this IST-hardcoded filter
can produce unexpected blocking. Add a note in config schema:
`Hint: "Minutes from midnight IST. Daily-chart bars at 09:15 = 555 minutes — well below default 900. Disable (set 0) for non-IST instruments."`

**EV-3 · LOW · `PcrBullishThreshold < PcrBearishThreshold` not validated — gap zone has no bias**
With defaults `PcrBullish=0.8, PcrBearish=1.2`, PCR in range [0.8, 1.2] sets BOTH
`ocBullishBias = false` AND `ocBearishBias = false`, blocking ALL signals in the neutral zone.
This is probably intentional (flat market = no trade) but is not documented and can confuse
users who set `PcrBullish=1.0, PcrBearish=0.9` (inverted), which makes both always false.

**Fix**: Add validation in `FromJson`: `if (PcrBullishThreshold >= PcrBearishThreshold) throw ArgumentException("PcrBullishThreshold must be less than PcrBearishThreshold")`.

---

### Section E — FibOptionSpreadStrategy.cs

**FIB-1 · HIGH · Fib 1.618 extension computed incorrectly for UPTREND put-spread entry**
```csharp
fib1618 = swingLow - range * 0.618m;   // "1.618 extension below" for put spread entry
```
The comment says "1.618 extension below (put spread entry zone)" but subtracts `range * 0.618`,
not `range * 1.618`. The actual 1.618 extension BELOW swing low (bearish extension) would be:
`swingLow - range * 1.618`. The current formula computes the **0.618 retracement below swing
low** — a level that is actually the 61.8% retrace of the upswing, well above the swing low.
For an uptrend put-spread, the spec says "price near 1.618 extension ABOVE swing high" (the
overextension zone where reversals happen). The correct formula for uptrend:
```csharp
fib1618 = swingHigh + range * 0.618m;  // 161.8% extension above swing high = overextension zone
```

**Fix**: Swap the formulas for both `isUptrend` and `!isUptrend` branches:
```csharp
if (isUptrend)
{
    fib618  = swingHigh - range * 0.618m;   // 61.8% retracement = support
    fib786  = swingHigh - range * 0.786m;   // 78.6% = deep support / invalidation
    fib1618 = swingHigh + range * 0.618m;   // 161.8% extension ABOVE high = put-spread entry zone
}
else
{
    fib618  = swingLow + range * 0.618m;    // 61.8% retracement from low = resistance
    fib786  = swingLow + range * 0.786m;    // 78.6% = deep resistance / invalidation
    fib1618 = swingLow - range * 0.618m;    // 161.8% extension BELOW low = call-spread entry zone
}
```

**FIB-2 · HIGH · `stopLevel = fib786` is BELOW entry in uptrend — stop placed on wrong side**
In the uptrend branch:
- `fib1618` (entry zone) = currently wrong (see FIB-1) but conceptually ABOVE price
- `fib786` = `swingHigh - range * 0.786` = below swing high, well below entry
- The `SignalResult` is `SignalType.Buy` with `StopLoss = fib786`

After FIB-1 fix, entry (`fib1618`) is above swing high. `fib786` is below swing high — so
stop IS below entry (correct for a buy). But the stop distance is `fib1618 - fib786 =
(swingHigh + range*0.618) - (swingHigh - range*0.786) = range * 1.404`. On a ₹500 range
swing, stop distance = ₹702. This is a valid structural stop.

However: for the **spread strategy**, the stop is on the **underlying** (`fib786`), but the
strategy uses `SpreadSignalResult` (option legs). The underlying stop must trigger
`SpreadOrderManager.CloseSpreadAsync`, not `BacktestEngine.TryClosePosition`. There is no
wire connecting `SignalResult.StopLoss` to spread position management.

**Fix**: Add `UnderlyingStopLevel` to `SpreadSignalResult`. `SpreadOrderManager` polls
underlying price; closes spread if `underlying.Close` breaches `UnderlyingStopLevel`.

**FIB-3 · MEDIUM · `context.SymbolIvRank` is null in BacktestEngine — IVP filter always skipped**
`BacktestEngine` does not populate `context.SymbolIvRank`. The code handles `null` gracefully
(skips IVP filter), but this means FibOptionSpread backtests run WITHOUT the IVP gate that is
a core part of the strategy spec. Results will have more trades than live trading would.

**Fix**: Populate `SymbolIvRank` in `BacktestEngine` from `iv_history` table
(migration 031/032 adds this). Add a query: `IVHistoryRepository.GetIvRankAsync(symbol, date)`.

**FIB-4 · LOW · `context.HasUpcomingEvent` is never set in BacktestEngine**
Same issue as FIB-3 — event calendar exclusion always skipped in backtest. The 3-day exclusion
window filters high-IV event risk from live trading but is absent from backtests, producing
more trades (including during earnings/results).

**Fix**: In `BacktestEngine`, for each signal date, query `event_calendar WHERE event_date
BETWEEN candle_date AND candle_date + ExclusionDays`. Populate `context.HasUpcomingEvent`.

---

### Section F — PriceActionBreakoutStrategy.cs

**PAB-1 · MEDIUM · `trendUp` defaults to `true` when `trendEma[^1] == 0` — uninitialized EMA allows all buys**
```csharp
bool trendUp = trendEma == null || trendEma[^1] == 0m || current.Close > trendEma[^1];
```
`ComputeEmaFull` returns `0m` for all bars during the warmup period (index < `TrendEmaPeriod - 1`).
For the first `TrendEmaPeriod - 1` bars, `trendEma[^1] == 0` → `trendUp = true` (allowed).
With `TrendEmaPeriod = 50`, the first 49 bars bypass the trend filter entirely.
If the strategy has `LookbackBars = 20` and `AtrPeriod = 14`, the `required` check only guards
for `max(20+14+1, 50+2) = 52` bars. But bars 1–49 still have `trendEma[^1] = 0`.

**Fix**: Change guard to explicitly require `TrendEmaPeriod` bars before enabling trend filter:
```csharp
bool trendFilterReady = trendEma != null && candles.Count >= config.TrendEmaPeriod;
bool trendUp   = !trendFilterReady || trendEma![^1] == 0m || current.Close > trendEma[^1];
bool trendDown = !trendFilterReady || trendEma![^1] == 0m || current.Close < trendEma[^1];
```
And add `trendEma[^1] != 0m` to the warmup condition: `trendFilterReady && trendEma[^1] != 0m`.

**PAB-2 · LOW · `RequireVolumeContraction` division: `secondHalfVol /= (lookbackCount - half)` is wrong when `lookbackCount` is odd**
When `lookbackCount = 21`, `half = 10`. `firstHalf` covers 10 bars, `secondHalf` covers
`lookbackEnd - (lookbackStart + 10) = 21 - 10 = 11` bars. But divisor is `lookbackCount - half = 21 - 10 = 11`. Correct.
Actually this is fine — `lookbackCount - half` equals the actual second-half count. ✅

**PAB-3 · LOW · `MaxEntryExtensionAtr = 0` check uses `> 0` guard but does NOT disable when set to exactly `0`**
```csharp
if (config.MaxEntryExtensionAtr > 0 && (current.Close - rangeHigh) > currentAtr * config.MaxEntryExtensionAtr)
```
Schema says `"0 = disabled"`. `MaxEntryExtensionAtr = 0` means `currentAtr * 0 = 0`, so
`(current.Close - rangeHigh) > 0` would ALWAYS be true for any breakout (close > rangeHigh
by definition). The `> 0` guard in the `if` correctly disables this when set to 0. ✅
But the Schema hint says "0 = disabled" and the code says `> 0` — if a user sets `0.001`, the
filter passes every breakout where close > rangeHigh + 0 = any breakout. Acceptable — no bug.

---

### Section G — Cross-Cutting: SpreadEntry handling in ALL option strategies

**ALL-SPREADS-1 · CRITICAL · All 6 option strategies emit `SpreadEntry` — none work in Backtest mode**
Strategies affected: `IronCondor`, `ShortStraddleStrangle`, `CalendarSpread`, `VerticalSpread`, `FibOptionSpread`.
All return `SignalResult.SpreadEntry`. `BacktestEngine` silently treats this as Hold (0 trades).

**Priority implementation plan**:
1. **Block**: Add `IsSpreadStrategy` flag to `StrategyFactory` registration for these 5 strategies.
   In UI: disable "Run Backtest" button + show tooltip: "Multi-leg strategies require Forward Test mode".
2. **Simulate (P8)**: Build `SpreadBacktestEngine` that:
   - Fetches option chain snapshots from `option_chain_snapshots` table (if available) or uses
     Black-Scholes synthetic pricing with historical ATM IV.
   - Simulates spread entry/exit: net credit/debit, P&L from underlying price movement + theta decay model.
   - Uses `IndianMarketCommissionModel` for 4-leg costs.
   - Generates `BacktestResultDto` with spread-specific metrics: max profit, max loss, breakeven distances, win by target vs stop.

**ALL-SPREADS-2 · HIGH · `SpreadSignalResult.DiagnosticsJson` type inconsistency across all strategies**
- `IronCondor`: `Dictionary<string, decimal>` ✅
- `ShortStraddleStrangle`: anonymous object ❌
- `CalendarSpread`: anonymous object ❌
- `VerticalSpread`: `Dictionary<string, decimal>` ✅
- `FibOptionSpread`: `Dictionary<string, decimal>` ✅
- `PriceActionBreakout`: `Dictionary<string, decimal>` (equity) ✅

**Fix**: Standardise all `DiagnosticsJson` to `Dictionary<string, decimal>`. Move non-decimal
diagnostics (strings, booleans) into `Reason` field.
