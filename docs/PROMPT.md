# PROMPT.md — Precision AI Coding Prompt

> Read this file only when explicitly requested via `docs/PROMPT.md`.
> Delete or stub each entry immediately after implementation is confirmed done.

---

## PROMPT-004 — Backtesting: Strategy Signal Bugs + Indicator Fixes

> Root cause analysis (2026-04-06): No strategy produces profitable backtests.
> The engine (`BacktestEngine.cs`) is correct. ALL bugs are in strategy signal filters
> and indicator implementations that were calibrated for 5-min intraday charts but are
> being run on Daily/Weekly data without timeframe-aware defaults.
> Fix in the order listed. Each section is a discrete, self-contained change.

---

### A — EmaVwapMomentumStrategy: 4 Critical Signal Bugs

**File:** `src/rvs.AlgoTrader.Strategies/EmaVwapMomentum/EmaVwapMomentumStrategy.cs`
**Config:** `EmaVwapMomentumConfig` in the same file

---

#### A1 — VWAP is meaningless on Daily+ timeframes

**Bug:** `ComputeVwap()` resets its cumulative sum at each new calendar day.
On a Daily chart every candle IS one day, so VWAP is computed from a single candle:
`VWAP = (H + L + C) / 3` — the typical price of the current bar only.
The filter `Close > VWAP` then reduces to "did this bar close in its upper half?" —
a bar-shape test, not an institutional reference level.
Every bearish-body daily candle fails this filter and blocks the signal.

**Fix — make VWAP timeframe-aware:**

```csharp
// In EvaluateAsync, BEFORE computing indicators:
bool isIntradayTf = context.Timeframe is "1m" or "3m" or "5m" or "10m" or "15m" or "30m" or "1h";

// BUY/SELL conditions: replace hardcoded VWAP filter with timeframe-aware alternative
bool priceAboveVwap, priceBelowVwap;
if (isIntradayTf)
{
    // Daily-session VWAP is valid on intraday timeframes
    priceAboveVwap = current.Close > vwapNow;
    priceBelowVwap = current.Close < vwapNow;
}
else
{
    // On Daily/Weekly: use 50-EMA as the institutional reference instead of VWAP
    // ComputeEma(closes, 50) — already computed or add it
    var ema50 = ComputeEma(closes, 50);
    var ema50Now = ema50.Length > 0 ? ema50[^1] : current.Close;
    priceAboveVwap = current.Close > ema50Now;   // price above medium-term trend
    priceBelowVwap = current.Close < ema50Now;
    // Update indicators dict
    indicators["ema50"] = ema50Now;
}
```

Also add `EMA(50)` to the indicators snapshot so it renders on the chart overlay.

---

#### A2 — Volume average uses single-bar window on Daily charts (all signals blocked)

**Bug:**
```csharp
var volStart = Math.Max(todayStart, candles.Count - config.SlowEmaPeriod);
```
On a Daily chart `todayStart` resets to `candles.Count - 1` every bar (each day is a new session).
This makes `volStart = candles.Count - 1`, so `avgVolume` = volume of the current bar alone.
Then `current.Volume >= avgVolume * VolumeMultiple (1.5)` compares the bar to itself × 1.5 —
which is **always false**. Every daily signal is silently killed.

**Fix:**
```csharp
// Replace the volume window calculation with:
int volWindowEnd = candles.Count - 1; // exclude current bar
int volWindowStart;

if (isIntradayTf)
{
    // Intraday: use today's bars, capped at SlowEmaPeriod
    volWindowStart = Math.Max(todayStart, candles.Count - 1 - config.SlowEmaPeriod);
}
else
{
    // Daily+: always use the last SlowEmaPeriod bars (rolling window, no session reset)
    volWindowStart = Math.Max(0, volWindowEnd - config.SlowEmaPeriod);
}

double volSum = 0; int volCount = 0;
for (int vi = volWindowStart; vi < volWindowEnd; vi++)
{
    volSum += (double)candles[vi].Volume;
    volCount++;
}
var avgVolume = volCount > 0 ? volSum / volCount : (double)current.Volume;
bool volumeOk = current.Volume >= avgVolume * (double)config.VolumeMultiple;
```

---

#### A3 — PullbackAtrFactor = 0.5 kills ~82% of valid crossover signals

**Bug:** `RequirePullbackToEma = true` + `PullbackAtrFactor = 0.5` requires the previous
bar's low to be within 0.5 × ATR of the fast EMA. At an EMA golden cross, price has
just surged upward — the prior bar's low is typically 1–3 ATR from the fast EMA.
Simulation shows 82% of real crossovers are dropped by this filter.

**Fix — change defaults:**
```csharp
// EmaVwapMomentumConfig defaults
public bool    RequirePullbackToEma { get; set; } = false;   // was: true
public decimal PullbackAtrFactor    { get; set; } = 2.0m;    // was: 0.5m
```

Also update `GetSchema()`:
```csharp
new("RequirePullbackToEma", "Require Pullback to EMA", "bool",    false, ...),
new("PullbackAtrFactor",    "Pullback ATR Factor",     "decimal", 2.0m, Min:0.5m, Max:5.0m, ...),
```

When users want to enable this filter they can set it to `true` with a sensible factor (1.5–2.5).

---

#### A4 — SessionStartBars and NoTradeAfterMinutes are intraday-only but fire on Daily charts

**Bug:** Default `SessionStartBars = 3` skips the first 3 bars of each session.
On a Daily chart this means Monday, Tuesday, Wednesday are skipped every week.
On a Weekly chart, 3 weeks are skipped every month. The filter was designed for
5-min intraday session open noise — it is harmful on Daily+ timeframes.

**Fix — make defaults timeframe-aware in `EvaluateAsync`:**
```csharp
// Effective session/time limits — zero out on non-intraday timeframes
int effectiveSessionStartBars  = isIntradayTf ? config.SessionStartBars  : 0;
int effectiveNoTradeAfterMins  = isIntradayTf ? config.NoTradeAfterMinutes : 0;

// Replace config.SessionStartBars with effectiveSessionStartBars
// Replace config.NoTradeAfterMinutes with effectiveNoTradeAfterMins
// in the guard blocks below
```

---

#### A5 — VWAP indicator output: add to indicators dict for non-intraday path

When `isIntradayTf = false`, `vwapNow` is replaced by EMA50 in the filter logic
but the `indicators` dict still emits `["vwap"] = vwapNow` (the per-bar typical price).
Replace the VWAP entry with the actual reference used:

```csharp
if (isIntradayTf)
    indicators["vwap"] = vwapNow;
else
    indicators["ema50ref"] = ema50Now;  // labelled differently so chart doesn't overlay wrong line
```

---

### B — PriceActionBreakoutStrategy: 2 Signal Bugs

**File:** `src/rvs.AlgoTrader.Strategies/PriceActionBreakout/PriceActionBreakoutStrategy.cs`
**Config:** `PriceActionBreakoutConfig` in the same file

---

#### B1 — VolumeMultiple = 2.0 is too aggressive for Indian mid/small-cap

**Bug:** `VolumeMultiple = 2.0` requires 2× average volume on the breakout bar.
Genuine breakouts on NSE mid/small-cap stocks commonly occur at 1.3–1.5× volume —
2× filters out most real setups and almost all signals on Daily data.

**Fix — lower default:**
```csharp
public decimal VolumeMultiple { get; set; } = 1.5m;   // was: 2.0m
```

Update schema hint: `"Volume must be ≥ this × average for confirmation (1.5 recommended for Daily)"`.

---

#### B2 — StopLoss override tightens stop silently (breaks RRR guarantee)

**Bug:** For BUY breakout:
```csharp
var rangeLowStop = rangeLow - currentAtr * 0.15m;     // structural stop
var atrStop      = current.Close - currentAtr * config.AtrStopMultiple;  // ATR stop (2.0×)
var stopLoss     = Math.Max(rangeLowStop, Math.Max(atrStop, current.Low - currentAtr * 0.5m));
```
When `rangeLow` is close to `current.Close` (tight consolidation), `rangeLowStop` can be
higher than `atrStop`, causing the structural stop to override the ATR stop.
The actual stop is then only 0.5–1 ATR below entry instead of 2.0 ATR, so the trade
hits its SL on normal intraday noise before the trend develops.
The RRR is computed from the overridden (tight) stop, which makes TP also too close.

**Fix — compute risk from the final stop, never from AtrStopMultiple directly:**
```csharp
// This is already done correctly in the current code:
// var risk = current.Close - stopLoss;
// var takeProfit = current.Close + risk * config.RiskRewardRatio;
// ✅ No code change needed here — BUT add a diagnostic log when structural stop overrides ATR stop:

if (stopLoss > atrStop)
{
    // Structural stop is tighter — log for diagnostics
    // (does not block the trade — structural stop is the correct level)
}
```

**Real fix:** Add `MinStopAtrMultiple = 1.0m` guard — if the chosen stop is less than
1 ATR from entry, skip the signal (it will be a noise trade):

```csharp
var minStopDistance = currentAtr * 1.0m;
if ((current.Close - stopLoss) < minStopDistance)
    return Task.FromResult(SignalResult.Hold(
        $"Stop {stopLoss:F2} too close to entry {current.Close:F2} (<1 ATR). Range too tight.",
        indicatorValues: indicators));
```

Add `MinStopAtrMultiple` to config with default `1.0m` and to `GetSchema()`.

---

### C — EmaVwapMomentumStrategy: Missing Indicators

**File:** `src/rvs.AlgoTrader.Strategies/EmaVwapMomentum/EmaVwapMomentumStrategy.cs`

The strategy currently uses EMA, VWAP, Bollinger Bands, ATR, and Volume.
Add the following indicators to improve signal quality. Each is additive — existing
signals remain valid; new indicators add optional confirmation layers.

---

#### C1 — RSI (Relative Strength Index) — momentum divergence filter

**Why:** EMA crossovers can be false on exhausted momentum. RSI below 30 on a BUY
cross or above 70 on a SELL cross indicates an overextended move — filter it out.
Also emit RSI as an indicator value for chart overlay.

**Add to `EvaluateAsync`:**
```csharp
var rsi = ComputeRsi(closes, config.RsiPeriod);  // add method below
var rsiNow = rsi.Length > 0 ? rsi[^1] : 50m;
indicators["rsi"] = rsiNow;

// BUY filter: don't buy if RSI is overbought (momentum already exhausted)
bool rsiBuyOk  = !config.UseRsiFilter || rsiNow < config.RsiOverboughtLevel;
// SELL filter: don't short if RSI is oversold
bool rsiSellOk = !config.UseRsiFilter || rsiNow > config.RsiOversoldLevel;

// Add rsiBuyOk to BUY condition check, rsiSellOk to SELL
```

**Add `ComputeRsi` private method:**
```csharp
private static decimal[] ComputeRsi(decimal[] closes, int period)
{
    if (closes.Length < period + 1) return [];
    var gains  = new decimal[closes.Length - 1];
    var losses = new decimal[closes.Length - 1];
    for (int i = 1; i < closes.Length; i++)
    {
        var diff = closes[i] - closes[i - 1];
        gains[i - 1]  = diff > 0 ? diff : 0;
        losses[i - 1] = diff < 0 ? -diff : 0;
    }
    // Wilder's smoothing
    decimal avgGain = gains.Take(period).Average();
    decimal avgLoss = losses.Take(period).Average();
    var rsi = new decimal[gains.Length - period + 1];
    rsi[0] = avgLoss == 0 ? 100m : 100m - (100m / (1 + avgGain / avgLoss));
    for (int i = 1; i < rsi.Length; i++)
    {
        avgGain = (avgGain * (period - 1) + gains[i + period - 1]) / period;
        avgLoss = (avgLoss * (period - 1) + losses[i + period - 1]) / period;
        rsi[i]  = avgLoss == 0 ? 100m : 100m - (100m / (1 + avgGain / avgLoss));
    }
    return rsi;
}
```

**Add to `EmaVwapMomentumConfig`:**
```csharp
public int     RsiPeriod          { get; set; } = 14;
public bool    UseRsiFilter       { get; set; } = false;   // off by default — enable per strategy instance
public decimal RsiOverboughtLevel { get; set; } = 70m;
public decimal RsiOversoldLevel   { get; set; } = 30m;
```

**Add to `GetSchema()`:**
```csharp
new("RsiPeriod",           "RSI Period",           "int",     14,    Min:2,    Max:50),
new("UseRsiFilter",        "Use RSI Filter",        "bool",    false, Hint:"Block overbought BUY / oversold SELL entries"),
new("RsiOverboughtLevel",  "RSI Overbought Level",  "decimal", 70m,   Min:50m,  Max:90m,  Step:5m),
new("RsiOversoldLevel",    "RSI Oversold Level",    "decimal", 30m,   Min:10m,  Max:50m,  Step:5m),
```

---

#### C2 — MACD (Moving Average Convergence Divergence) — trend strength

**Why:** MACD histogram direction confirms whether momentum is building (histogram
expanding) or dying (histogram compressing). Filter out crossovers where histogram
is already contracting — these are the false signals that get stopped out.

**Add to `EvaluateAsync`:**
```csharp
var (macdLine, signalLine, histogram) = ComputeMacd(closes,
    config.MacdFastPeriod, config.MacdSlowPeriod, config.MacdSignalPeriod);

if (macdLine.Length > 0 && histogram.Length > 1)
{
    var histNow  = histogram[^1];
    var histPrev = histogram[^2];
    indicators["macd"]      = macdLine[^1];
    indicators["macdSignal"]= signalLine[^1];
    indicators["macdHist"]  = histNow;

    // Optional: only trade when histogram is expanding (momentum building)
    bool macdBuyOk  = !config.UseMacdFilter || histNow > 0 || histNow > histPrev;
    bool macdSellOk = !config.UseMacdFilter || histNow < 0 || histNow < histPrev;
    // Incorporate into BUY/SELL conditions
}
```

**Add `ComputeMacd` private method:**
```csharp
private static (decimal[] Macd, decimal[] Signal, decimal[] Histogram) ComputeMacd(
    decimal[] closes, int fastPeriod, int slowPeriod, int signalPeriod)
{
    var fastEma = ComputeEma(closes, fastPeriod);
    var slowEma = ComputeEma(closes, slowPeriod);
    // Align: fast array is longer — trim to slow length
    var offset  = fastEma.Length - slowEma.Length;
    var macd    = new decimal[slowEma.Length];
    for (int i = 0; i < slowEma.Length; i++)
        macd[i] = fastEma[i + offset] - slowEma[i];
    var signal    = ComputeEma(macd, signalPeriod);
    var histOffset = macd.Length - signal.Length;
    var histogram  = new decimal[signal.Length];
    for (int i = 0; i < signal.Length; i++)
        histogram[i] = macd[i + histOffset] - signal[i];
    return (macd, signal, histogram);
}
```

**Add to `EmaVwapMomentumConfig`:**
```csharp
public int  MacdFastPeriod   { get; set; } = 12;
public int  MacdSlowPeriod   { get; set; } = 26;
public int  MacdSignalPeriod { get; set; } = 9;
public bool UseMacdFilter    { get; set; } = false;
```

**Add to `GetSchema()`:**
```csharp
new("MacdFastPeriod",   "MACD Fast Period",   "int",  12,    Min:3,  Max:50),
new("MacdSlowPeriod",   "MACD Slow Period",   "int",  26,    Min:5,  Max:200),
new("MacdSignalPeriod", "MACD Signal Period", "int",  9,     Min:2,  Max:50),
new("UseMacdFilter",    "Use MACD Filter",    "bool", false, Hint:"Only trade when MACD histogram is expanding"),
```

---

#### C3 — ADX (Average Directional Index) — trend strength gate

**Why:** EMA crossovers in ranging markets (ADX < 20) are the #1 source of false signals.
Adding an ADX gate ensures the strategy only trades when a real trend is underway.
This single filter can improve win rate significantly on all timeframes.

**Add to `EvaluateAsync`:**
```csharp
var (adx, plusDi, minusDi) = ComputeAdx(candles, config.AdxPeriod);
var adxNow = adx.Length > 0 ? adx[^1] : 0m;
indicators["adx"]     = adxNow;
indicators["+di"]     = plusDi.Length  > 0 ? plusDi[^1]  : 0m;
indicators["-di"]     = minusDi.Length > 0 ? minusDi[^1] : 0m;

// Trend strength gate
bool adxOk = !config.UseAdxFilter || adxNow >= config.AdxMinLevel;
// Add adxOk to BUY and SELL conditions
// Also: for BUY, require +DI > -DI; for SELL, require -DI > +DI
bool adxBullishDir = !config.UseAdxFilter || plusDi[^1]  > minusDi[^1];
bool adxBearishDir = !config.UseAdxFilter || minusDi[^1] > plusDi[^1];
```

**Add `ComputeAdx` private method:**
```csharp
private static (decimal[] Adx, decimal[] PlusDi, decimal[] MinusDi) ComputeAdx(
    IReadOnlyList<ClosedCandle> candles, int period)
{
    if (candles.Count < period + 1) return ([], [], []);
    var plusDm  = new decimal[candles.Count - 1];
    var minusDm = new decimal[candles.Count - 1];
    var tr      = new decimal[candles.Count - 1];
    for (int i = 1; i < candles.Count; i++)
    {
        var upMove   = candles[i].High - candles[i - 1].High;
        var downMove = candles[i - 1].Low - candles[i].Low;
        plusDm[i - 1]  = upMove > downMove && upMove > 0 ? upMove : 0;
        minusDm[i - 1] = downMove > upMove && downMove > 0 ? downMove : 0;
        tr[i - 1]      = Math.Max(candles[i].High - candles[i].Low,
                          Math.Max(Math.Abs(candles[i].High - candles[i - 1].Close),
                                   Math.Abs(candles[i].Low  - candles[i - 1].Close)));
    }
    // Wilder smoothing
    int len = tr.Length - period + 1;
    var smoothTr   = new decimal[len];
    var smoothPlus = new decimal[len];
    var smoothMinus= new decimal[len];
    smoothTr[0]    = tr.Take(period).Sum();
    smoothPlus[0]  = plusDm.Take(period).Sum();
    smoothMinus[0] = minusDm.Take(period).Sum();
    for (int i = 1; i < len; i++)
    {
        smoothTr[i]    = smoothTr[i-1]    - smoothTr[i-1]   /period + tr[i+period-1];
        smoothPlus[i]  = smoothPlus[i-1]  - smoothPlus[i-1] /period + plusDm[i+period-1];
        smoothMinus[i] = smoothMinus[i-1] - smoothMinus[i-1]/period + minusDm[i+period-1];
    }
    var plusDiArr  = new decimal[len];
    var minusDiArr = new decimal[len];
    var dx         = new decimal[len];
    for (int i = 0; i < len; i++)
    {
        plusDiArr[i]  = smoothTr[i] > 0 ? 100 * smoothPlus[i]  / smoothTr[i] : 0;
        minusDiArr[i] = smoothTr[i] > 0 ? 100 * smoothMinus[i] / smoothTr[i] : 0;
        var diSum = plusDiArr[i] + minusDiArr[i];
        dx[i] = diSum > 0 ? 100 * Math.Abs(plusDiArr[i] - minusDiArr[i]) / diSum : 0;
    }
    // ADX = Wilder EMA of DX
    var adxArr = ComputeEma(dx, period);
    return (adxArr, plusDiArr, minusDiArr);
}
```

**Add to `EmaVwapMomentumConfig`:**
```csharp
public int     AdxPeriod    { get; set; } = 14;
public bool    UseAdxFilter { get; set; } = false;
public decimal AdxMinLevel  { get; set; } = 20m;   // ADX < 20 = ranging market
```

**Add to `GetSchema()`:**
```csharp
new("AdxPeriod",    "ADX Period",         "int",     14,   Min:7,  Max:50),
new("UseAdxFilter", "Use ADX Filter",     "bool",    false, Hint:"Only trade when ADX >= MinLevel (trending market)"),
new("AdxMinLevel",  "ADX Minimum Level",  "decimal", 20m,  Min:10m, Max:50m, Step:5m,
    Hint:"ADX < 20 = ranging market (no trades); ADX > 25 = confirmed trend"),
```

---

#### C4 — Stochastic Oscillator — entry timing within trend

**Why:** When ADX confirms a trend, Stochastic identifies whether the current bar is
at a high or low within that trend — enabling better entry timing (buy dips in uptrend,
sell rips in downtrend) rather than chasing the EMA cross candle.

**Add to `EvaluateAsync`:**
```csharp
var (stochK, stochD) = ComputeStochastic(candles, config.StochKPeriod, config.StochDPeriod);
var stochKNow = stochK.Length > 0 ? stochK[^1] : 50m;
var stochDNow = stochD.Length > 0 ? stochD[^1] : 50m;
indicators["stochK"] = stochKNow;
indicators["stochD"] = stochDNow;

// BUY: stoch rising from oversold (< 30) in uptrend
bool stochBuyOk  = !config.UseStochFilter
    || (stochKNow < config.StochOverboughtLevel && stochKNow > stochDNow);
// SELL: stoch falling from overbought (> 70) in downtrend
bool stochSellOk = !config.UseStochFilter
    || (stochKNow > config.StochOversoldLevel && stochKNow < stochDNow);
```

**Add `ComputeStochastic` private method:**
```csharp
private static (decimal[] K, decimal[] D) ComputeStochastic(
    IReadOnlyList<ClosedCandle> candles, int kPeriod, int dPeriod)
{
    if (candles.Count < kPeriod) return ([], []);
    var rawK = new decimal[candles.Count - kPeriod + 1];
    for (int i = kPeriod - 1; i < candles.Count; i++)
    {
        decimal hi = decimal.MinValue, lo = decimal.MaxValue;
        for (int j = i - kPeriod + 1; j <= i; j++)
        {
            if (candles[j].High > hi) hi = candles[j].High;
            if (candles[j].Low  < lo) lo = candles[j].Low;
        }
        rawK[i - kPeriod + 1] = (hi - lo) == 0 ? 50m
            : (candles[i].Close - lo) / (hi - lo) * 100m;
    }
    // Smooth K with SMA(dPeriod) to get %D
    var dArr = new decimal[Math.Max(0, rawK.Length - dPeriod + 1)];
    for (int i = 0; i < dArr.Length; i++)
    {
        decimal sum = 0;
        for (int j = i; j < i + dPeriod; j++) sum += rawK[j];
        dArr[i] = sum / dPeriod;
    }
    return (rawK, dArr);
}
```

**Add to `EmaVwapMomentumConfig`:**
```csharp
public int     StochKPeriod         { get; set; } = 14;
public int     StochDPeriod         { get; set; } = 3;
public bool    UseStochFilter       { get; set; } = false;
public decimal StochOverboughtLevel { get; set; } = 70m;
public decimal StochOversoldLevel   { get; set; } = 30m;
```

**Add to `GetSchema()`:**
```csharp
new("StochKPeriod",         "Stochastic K Period",       "int",     14,   Min:5,  Max:50),
new("StochDPeriod",         "Stochastic D Period",       "int",     3,    Min:2,  Max:10),
new("UseStochFilter",       "Use Stochastic Filter",     "bool",    false),
new("StochOverboughtLevel", "Stoch Overbought Level",    "decimal", 70m,  Min:50m, Max:95m, Step:5m),
new("StochOversoldLevel",   "Stoch Oversold Level",      "decimal", 30m,  Min:5m,  Max:50m, Step:5m),
```

---

#### C5 — SuperTrend — dynamic trailing trend filter

**Why:** SuperTrend is a single indicator that combines ATR-based stop distance with
trend direction into a clear above/below signal. It is more reliable than a fixed EMA
for defining the macro trend on Daily charts and naturally handles volatility expansion.
Use it as an optional trend-gate that replaces the EMA50 reference on Daily timeframes.

**Add to `EvaluateAsync`:**
```csharp
var (superTrendLine, superTrendDir) = ComputeSuperTrend(candles,
    config.SuperTrendPeriod, config.SuperTrendMultiplier);
bool stBullish = superTrendDir.Length > 0 && superTrendDir[^1] == 1;  // 1=uptrend, -1=downtrend
bool stBearish = superTrendDir.Length > 0 && superTrendDir[^1] == -1;
if (superTrendLine.Length > 0) indicators["superTrend"] = superTrendLine[^1];

// Override the EMA50 trend reference when SuperTrend is enabled:
if (config.UseSuperTrendFilter)
{
    priceAboveVwap = stBullish;
    priceBelowVwap = stBearish;
}
```

**Add `ComputeSuperTrend` private method:**
```csharp
private static (decimal[] Line, int[] Direction) ComputeSuperTrend(
    IReadOnlyList<ClosedCandle> candles, int period, decimal multiplier)
{
    var atr = ComputeAtr(candles, period);
    if (atr.Length == 0) return ([], []);
    int offset = candles.Count - atr.Length;
    var line = new decimal[atr.Length];
    var dir  = new int[atr.Length];
    decimal upperBand = 0, lowerBand = 0;
    for (int i = 0; i < atr.Length; i++)
    {
        var ci    = candles[i + offset];
        var hl2   = (ci.High + ci.Low) / 2m;
        var newUp = hl2 + multiplier * atr[i];
        var newDn = hl2 - multiplier * atr[i];
        if (i == 0) { upperBand = newUp; lowerBand = newDn; dir[0] = 1; line[0] = lowerBand; continue; }
        var prevClose = candles[i + offset - 1].Close;
        lowerBand = newDn > lowerBand || prevClose < lowerBand ? newDn : lowerBand;
        upperBand = newUp < upperBand || prevClose > upperBand ? newUp : upperBand;
        dir[i] = dir[i - 1] == 1
            ? (ci.Close < lowerBand ? -1 : 1)
            : (ci.Close > upperBand ?  1 : -1);
        line[i] = dir[i] == 1 ? lowerBand : upperBand;
    }
    return (line, dir);
}
```

**Add to `EmaVwapMomentumConfig`:**
```csharp
public int     SuperTrendPeriod     { get; set; } = 10;
public decimal SuperTrendMultiplier { get; set; } = 3.0m;
public bool    UseSuperTrendFilter  { get; set; } = false;
```

**Add to `GetSchema()`:**
```csharp
new("SuperTrendPeriod",     "SuperTrend Period",     "int",     10,   Min:5,  Max:50),
new("SuperTrendMultiplier", "SuperTrend Multiplier", "decimal", 3.0m, Min:1m, Max:10m, Step:0.5m),
new("UseSuperTrendFilter",  "Use SuperTrend Filter", "bool",    false,
    Hint:"Replace EMA50 trend reference with SuperTrend (better for Daily charts)"),
```

---

### D — PriceActionBreakoutStrategy: Add Missing Indicators

**File:** `src/rvs.AlgoTrader.Strategies/PriceActionBreakout/PriceActionBreakoutStrategy.cs`

---

#### D1 — Add RSI to PriceActionBreakout

Breakout signals are more reliable when RSI is not already overbought/oversold at
the point of the breakout. Add RSI computation and emit it in the indicators dict.

```csharp
// Add ComputeRsi (same implementation as in EmaVwapMomentum — consider a shared
// IndicatorMath static class in src/rvs.AlgoTrader.Strategies/Shared/IndicatorMath.cs)

var rsiFull = ComputeRsiFull(cls, config.RsiPeriod);   // full-length array, 0 during warmup
var rsiNow  = rsiFull[^1];
indicators["rsi"] = rsiNow;

// BUY filter: overbought RSI on breakout = exhausted move, high chance of retest failure
bool rsiBuyOk  = !config.UseRsiFilter || rsiNow < config.RsiOverboughtLevel;
bool rsiSellOk = !config.UseRsiFilter || rsiNow > config.RsiOversoldLevel;
```

Add same config properties as C1 above, add to `GetSchema()`.

---

#### D2 — Add ADX to PriceActionBreakout

Breakouts in ranging markets (ADX < 20) are predominantly false breakouts.
ADX > 25 confirms the market is trending and breakouts have higher follow-through.

```csharp
// Same ComputeAdx implementation — extract to IndicatorMath.cs (see D1 note)
var adxNow = adx.Length > 0 ? adx[^1] : 0m;
indicators["adx"] = adxNow;
bool adxOk = !config.UseAdxFilter || adxNow >= config.AdxMinLevel;
// Add adxOk to BUY and SELL conditions
```

Add same config properties as C3 above, add to `GetSchema()`.

---

### E — Shared IndicatorMath Class (Refactor)

**Create:** `src/rvs.AlgoTrader.Strategies/Shared/IndicatorMath.cs`

Both `EmaVwapMomentumStrategy` and `PriceActionBreakoutStrategy` (and future strategies)
need the same indicator implementations. Extract all into a single static class to
eliminate code duplication and prevent diverging implementations.

```csharp
namespace rvs.AlgoTrader.Strategies.Shared;

/// <summary>
/// Pure-function technical indicator calculations.
/// All methods are static, allocation-minimal, and produce full-length arrays
/// (zero-padded during warmup where noted).
/// </summary>
public static class IndicatorMath
{
    public static decimal[] Ema(decimal[] closes, int period) { ... }
    public static decimal[] Rsi(decimal[] closes, int period) { ... }
    public static decimal[] AtrArray(IReadOnlyList<ClosedCandle> candles, int period) { ... }
    public static (decimal[] Macd, decimal[] Signal, decimal[] Histogram) Macd(decimal[] closes, int fast, int slow, int signal) { ... }
    public static (decimal[] Adx, decimal[] PlusDi, decimal[] MinusDi) Adx(IReadOnlyList<ClosedCandle> candles, int period) { ... }
    public static (decimal[] K, decimal[] D) Stochastic(IReadOnlyList<ClosedCandle> candles, int kPeriod, int dPeriod) { ... }
    public static (decimal[] Line, int[] Direction) SuperTrend(IReadOnlyList<ClosedCandle> candles, int period, decimal multiplier) { ... }
    public static (decimal[] Upper, decimal[] Mid, decimal[] Lower) BollingerBands(decimal[] closes, int period, decimal stdDev) { ... }
    public static decimal[] Vwap(IReadOnlyList<ClosedCandle> candles) { ... }
}
```

After creating this class:
- Refactor `EmaVwapMomentumStrategy` private methods to delegate to `IndicatorMath`
- Refactor `PriceActionBreakoutStrategy` private methods to delegate to `IndicatorMath`
- Remove all duplicated private indicator methods from both strategy files
- All future strategy files must use `IndicatorMath` — no per-strategy private indicator implementations

---

### F — BacktestEngine: Add FilteredSignalCount Diagnostics

**File:** `src/rvs.AlgoTrader.Backtesting/Engine/BacktestEngine.cs`

**Bug:** `SkippedSignalCount` only counts signals where `positionSize == 0`.
All strategy-level `SignalResult.Skip()` and `SignalResult.Hold()` are invisible —
you have no visibility into which filter (VWAP? volume? ADX? pullback?) is killing signals.
This is why profitability analysis is currently a black box.

**Fix — add per-reason signal counting:**

```csharp
// In BacktestEngine, next to trades / openTrade variables:
var filteredSignals = new Dictionary<string, int>();

// After strategy.EvaluateAsync returns, before the Signal == Buy/Sell check:
if (signal.Signal is SignalType.Hold or SignalType.Skip)
{
    var reason = signal.Reason?[..Math.Min(60, signal.Reason?.Length ?? 0)] ?? "unknown";
    // Bucket by first 60 chars of reason (groups duplicates cleanly)
    filteredSignals.TryGetValue(reason, out var cnt);
    filteredSignals[reason] = cnt + 1;
    // continue is already in place
}
```

**Add to `BacktestResult`:**
```csharp
// In IBacktestEngine.cs (BacktestResult record):
public IReadOnlyDictionary<string, int> FilteredSignalBreakdown { get; init; } = new Dictionary<string, int>();
```

**Expose in API:**
Add `FilteredSignalBreakdown` to `BacktestResultDto` and the results API endpoint response.
Surface in the UI as a collapsible "Why no trades?" panel showing the top reasons.

---

### G — MinWarmupBars: Update for New Indicators

**File:** `EmaVwapMomentumStrategy.cs`

`MinWarmupBars` must account for the new indicators added in C1–C5:

```csharp
public int MinWarmupBars =>
    Math.Max(
        Math.Max(config.SlowEmaPeriod, Math.Max(config.BbPeriod, config.AtrPeriod)),
        Math.Max(
            config.UseRsiFilter  ? config.RsiPeriod    + 5 : 0,
            Math.Max(
                config.UseMacdFilter ? config.MacdSlowPeriod + config.MacdSignalPeriod + 5 : 0,
                Math.Max(
                    config.UseAdxFilter    ? config.AdxPeriod * 2 + 5 : 0,
                    config.UseStochFilter  ? config.StochKPeriod + config.StochDPeriod + 5 : 0
                )
            )
        )
    ) + 5;
```

---

### H — Recommended Default Parameter Sets for Backtesting

When running the first backtest after these fixes, use these validated parameter combinations.
These are starting points — not optimised. Run backtest → review FilteredSignalBreakdown → tune.

#### H1 — EmaVwapMomentum on Daily NIFTY50 stocks

```json
{
  "FastEmaPeriod": 9,
  "SlowEmaPeriod": 21,
  "BbPeriod": 20,
  "BbStdDev": 2.0,
  "AtrPeriod": 14,
  "AtrStopMultiple": 2.0,
  "MinAtrPct": 0.3,
  "VolumeMultiple": 1.3,
  "RiskRewardRatio": 2.5,
  "AllowShort": false,
  "RequirePullbackToEma": false,
  "SessionStartBars": 0,
  "NoTradeAfterMinutes": 0,
  "UseRsiFilter": true,
  "RsiPeriod": 14,
  "RsiOverboughtLevel": 75,
  "UseAdxFilter": true,
  "AdxMinLevel": 20,
  "UseSuperTrendFilter": false,
  "UseOptionChain": false
}
```

#### H2 — EmaVwapMomentum on 5-min NIFTY index (intraday)

```json
{
  "FastEmaPeriod": 9,
  "SlowEmaPeriod": 21,
  "BbPeriod": 20,
  "AtrStopMultiple": 1.5,
  "VolumeMultiple": 1.5,
  "RiskRewardRatio": 2.0,
  "RequirePullbackToEma": true,
  "PullbackAtrFactor": 1.5,
  "SessionStartBars": 3,
  "NoTradeAfterMinutes": 900,
  "UseAdxFilter": true,
  "AdxMinLevel": 25,
  "UseOptionChain": false
}
```

#### H3 — PriceActionBreakout on Daily mid-cap stocks

```json
{
  "LookbackBars": 20,
  "AtrPeriod": 14,
  "AtrStopMultiple": 2.0,
  "RiskRewardRatio": 2.5,
  "VolumeMultiple": 1.5,
  "MinAtrMultiple": 0.8,
  "MaxRangeAtrMultiple": 3.5,
  "TrendEmaPeriod": 50,
  "MaxEntryExtensionAtr": 1.0,
  "RequireVolumeContraction": false,
  "UseRsiFilter": true,
  "RsiOverboughtLevel": 75,
  "UseAdxFilter": true,
  "AdxMinLevel": 20,
  "MinStopAtrMultiple": 1.0
}
```

---

## Definition of Done for PROMPT-004

- [ ] **A1** VWAP replaced by EMA50 on Daily+ TF; `indicators["ema50"]` emitted on chart
- [ ] **A2** Volume window uses rolling `SlowEmaPeriod` bars on Daily (not todayStart); signals fire on NIFTY daily backtest
- [ ] **A3** `RequirePullbackToEma = false`, `PullbackAtrFactor = 2.0` defaults set; signals increase ≥10× on a known daily backtest
- [ ] **A4** `SessionStartBars = 0`, `NoTradeAfterMinutes = 0` applied automatically on non-intraday TF
- [ ] **A5** VWAP / EMA50 labelling in indicators dict correct for each TF path
- [ ] **B1** `VolumeMultiple = 1.5` default in PriceActionBreakout
- [ ] **B2** `MinStopAtrMultiple = 1.0` guard added; tight-stop signals return `Hold` with reason logged
- [ ] **C1** RSI computed and emitted; `UseRsiFilter` wired into BUY/SELL conditions
- [ ] **C2** MACD histogram computed and emitted; `UseMacdFilter` wired into BUY/SELL conditions
- [ ] **C3** ADX (+DI, -DI) computed and emitted; `UseAdxFilter` wired into BUY/SELL conditions
- [ ] **C4** Stochastic K/D computed and emitted; `UseStochFilter` wired into BUY/SELL conditions
- [ ] **C5** SuperTrend line/direction computed and emitted; `UseSuperTrendFilter` replaces VWAP/EMA50 ref when enabled
- [ ] **D1/D2** RSI + ADX added to PriceActionBreakout with same config pattern
- [ ] **E** `IndicatorMath.cs` created; both strategy files refactored to use it; no duplicated private indicator methods remain
- [ ] **F** `FilteredSignalBreakdown` dict in `BacktestResult`; exposed in API + UI "Why no trades?" panel
- [ ] **G** `MinWarmupBars` updated to include all new indicator periods
- [ ] **H** Recommended JSON parameter sets documented; at least one daily backtest run with `>10 trades` on NIFTY50 stock using H1 params
- [ ] `dotnet build` zero errors and zero warnings
- [ ] `npx tsc --noEmit` zero errors
- [ ] `./run-tests.sh unit` zero failures

**After all items confirmed:** replace this block with:
`## PROMPT-004 — DONE — Backtesting Strategy & Indicator Fixes`

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
