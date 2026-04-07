# PROMPT.md — Precision AI Coding Prompt

> Read this file only when explicitly requested via `docs/PROMPT.md`.
> Delete or stub each entry immediately after implementation is confirmed done.

---

## PROMPT-005 — Confirmed Code-Level Bugs (Direct Source Review 2026-04-07)

> These bugs were found by reading the actual `.cs` source files directly.
> They are separate from PROMPT-004 indicator additions — these are correctness bugs
> that cause wrong results or runtime exceptions.

---

### V4 — VcpSwingStrategy: Negative Skip offset causes wrong SMA200 slope (CRITICAL)

**File:** `src/rvs.AlgoTrader.Strategies/VcpSwing/VcpSwingStrategy.cs`

**Bug:**
```csharp
decimal sma200Prev = closes.Skip(candles.Count - config.Sma200Period - 5)
                           .Take(config.Sma200Period).Average();
```
When `candles.Count` is only slightly above the `required` guard (e.g. 202 bars,
`Sma200Period = 200`): `Skip(202 - 200 - 5) = Skip(-3)`.
`Enumerable.Skip` treats negative arguments as `Skip(0)`, so the SMA is computed
from bar 0 through bar 199 instead of bars 2–201.
The slope `(sma200 - sma200Prev) / sma200Prev` is then computed against the wrong
window, producing a falsely steep positive or negative slope that can gate-block
all entries at the start of a backtest.

**Fix:**
```csharp
// Replace with an explicit guard before the slope computation:
decimal sma200Prev;
int prevOffset = candles.Count - config.Sma200Period - 5;
if (prevOffset >= 0)
    sma200Prev = closes.Skip(prevOffset).Take(config.Sma200Period).Average();
else
{
    // Not enough history for slope — treat slope as neutral (pass filter)
    sma200Prev = sma200;
}
decimal slope = sma200Prev == 0 ? 0
    : (sma200 - sma200Prev) / sma200Prev * 100m;
```

---

### V5 — VcpSwingStrategy: VolumeDryUpEnabled=false default accepts all bases (HIGH)

**File:** `src/rvs.AlgoTrader.Strategies/VcpSwing/VcpSwingStrategy.cs`

**Bug:** `VolumeDryUpEnabled = false` (default) means every consolidation with tightening
contractions is accepted regardless of whether volume has dried up in the base.
A VCP without volume contraction is just a pullback — not a Mark Minervini VCP setup.
This produces many low-quality setups in backtests that show nice entry but fail in
forward tests because the "smart money absorption" signal is missing.

**Fix — change defaults:**
```csharp
public bool    VolumeDryUpEnabled        { get; set; } = true;   // was: false
public decimal VolumeDryUpThresholdPct   { get; set; } = 70m;    // was: 60m (looser — more setups pass)
```

Update `GetSchema()` default value for `VolumeDryUpEnabled` to `true`.

---

### V6 — VcpSwingStrategy: LINQ TakeLast/Select inside EvaluateAsync hot path (PERF)

**File:** `src/rvs.AlgoTrader.Strategies/VcpSwing/VcpSwingStrategy.cs`

**Issue:** Every call to `EvaluateAsync` allocates:
```csharp
var closes = candles.Select(c => c.Close).ToArray();       // full array allocation
var highs  = candles.Select(c => c.High).ToArray();        // full array allocation
var lows   = candles.Select(c => c.Low).ToArray();         // full array allocation
```
In a backtest with 1000 symbols × 500 bars each = 500,000 calls. Each call allocates
3 decimal arrays of up to 500 elements. This causes GC pressure and slows large backtests.

**Fix — allocate once via `ArrayPool` or pass pre-built arrays via `StrategyContext`:**
```csharp
// Option A (simplest): pre-build close/high/low arrays in StrategyContext
// and expose as context.Closes, context.Highs, context.Lows (IReadOnlyList<decimal>)
// The BacktestEngine builds context once per symbol and updates by appending — no per-bar allocation.

// Option B (immediate fix, no context change): use span-based manual loop, no LINQ:
var n = candles.Count;
var closesArr = new decimal[n];
var highsArr  = new decimal[n];
var lowsArr   = new decimal[n];
for (int i = 0; i < n; i++)
{
    closesArr[i] = candles[i].Close;
    highsArr[i]  = candles[i].High;
    lowsArr[i]   = candles[i].Low;
}
```

Prefer Option A as a follow-up after PROMPT-004 is done.
Option B is acceptable immediately and removes LINQ overhead.

---

### E2 — EmaVwapMomentumStrategy: RequirePullbackToEma logic inversion (CRITICAL)

> Confirmed by reading actual source code. Same as PROMPT-004 A3, reproduced here
> with exact line reference for the implementer.

**File:** `src/rvs.AlgoTrader.Strategies/EmaVwapMomentum/EmaVwapMomentumStrategy.cs`

The `RequirePullbackToEma` guard runs BEFORE the EMA cross is checked.
At the moment of a golden cross, price has just risen above the slow EMA —
the prior candle's low is 1–3 ATR above the fast EMA.
`PullbackAtrFactor = 0.5` means `prevLow must be ≤ fastEma + 0.5×ATR`.
Since `prevLow > fastEma` at a real cross, the condition is **never** satisfied.
Result: 0 BUY signals on any daily dataset.

**Confirm the fix from PROMPT-004 A3 is applied together with A1/A2 — they are
all required simultaneously for any EmaVwapMomentum signal to appear on Daily data.**

---

### P3 — PriceActionBreakoutStrategy: ATR uses Wilder's smoothing but SMA seed is
averaged wrong on first element (MEDIUM)

**File:** `src/rvs.AlgoTrader.Strategies/PriceActionBreakout/PriceActionBreakoutStrategy.cs`

**Bug in `CalculateAtr`:**
```csharp
var firstAtr = trValues.Take(period).Average();   // correct — SMA seed
atrValues.Add(firstAtr);
// ...
for (int i = period; i < trValues.Count; i++)
{
    var atr = (prev * (period - 1) + trValues[i]) / period;   // Wilder's smoothing
    atrValues.Add(atr);
    prev = atr;
}
```
This is correct Wilder's ATR. However `atrValues.TakeLast(config.AtrPeriod).Average()`
is then called on the smoothed ATR series to get `avgAtr`. Averaging smoothed ATR values
is not a standard measure — it double-smooths the data and produces an `avgAtr` that
is meaningfully different from what `MinAtrMultiple` was calibrated against.

**Fix — use the simple average of the last `AtrPeriod` raw TR values for `avgAtr`:**
```csharp
// Replace:
var avgAtr = atrValues.TakeLast(config.AtrPeriod).Average();

// With (average of the last AtrPeriod TR values from trValues, not atrValues):
var trStart = Math.Max(0, trValues.Count - config.AtrPeriod);
decimal trSum = 0; int trCount = 0;
for (int i = trStart; i < trValues.Count; i++) { trSum += trValues[i]; trCount++; }
var avgAtr = trCount > 0 ? trSum / trCount : currentAtr;
```

---

## Definition of Done for PROMPT-005

- [ ] **V4** `sma200Prev` Skip offset guard added; negative offset falls back to neutral slope
- [ ] **V5** `VolumeDryUpEnabled = true`, `VolumeDryUpThresholdPct = 70m` defaults in `VcpSwingConfig`; schema default updated
- [ ] **V6** LINQ `Select/ToArray` in VcpSwing hot path replaced with manual loop (Option B) or `StrategyContext` arrays (Option A)
- [ ] **E2** Confirmed PROMPT-004 A1 + A2 + A3 applied together; run one EmaVwapMomentum daily backtest and verify `>0 signals fired`
- [ ] **P3** `avgAtr` in `PriceActionBreakoutStrategy.CalculateAtr` computed from raw TR values, not smoothed ATR series
- [ ] `dotnet build` zero errors and zero warnings
- [ ] `./run-tests.sh unit` zero failures

**After all items confirmed:** replace this block with:
`## PROMPT-005 — DONE — Code-Level Bug Fixes (Direct Source Review)`
