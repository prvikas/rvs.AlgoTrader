-- Migration 047: Additional indicator intelligence cards
-- Extends migration 045 with 8 more commonly used indicators.

INSERT INTO indicator_intelligence
    (indicator_key, display_name, what_it_measures, common_mistake, positive_ev_conditions, ignore_conditions, best_paired_with, sizing_implications)
VALUES
(
    'EMA',
    'EMA — Exponential Moving Average',
    'Trend direction and momentum. EMA gives more weight to recent prices than SMA, making it faster to respond to price changes. Shorter EMAs (5, 9, 13) react faster; longer EMAs (20, 50, 200) filter more noise. The slope and relative position of price to EMA defines the trend.',
    'Trading every price crossover through an EMA as an entry signal. In choppy markets an EMA generates relentless whipsaws. Treating a single EMA crossover as a trend change when the broader trend is unchanged is the most common EMA misuse.',
    'Price above a rising EMA = bullish bias for continuation. Golden cross (short EMA crossing above long EMA) in a low-ADX-to-rising-ADX environment = trend beginning. Price pulling back to a rising EMA and holding = trend continuation long entry. EMA acting as dynamic support/resistance in a trending market.',
    'Sideways, range-bound markets (ADX < 20) where price crosses the EMA multiple times per session. VIX shock days. Sessions within 30 minutes of a major scheduled event.',
    'ADX to confirm a trending regime before acting on EMA signals. Volume to confirm conviction on pullback-to-EMA entries. MACD to confirm momentum direction.',
    'Full size on EMA pullback entries in confirmed trends (ADX > 25, price above EMA200). Reduce to 50% on golden/death cross entries alone without ADX confirmation. Never trade EMA crossovers as standalone signals in choppy markets.'
),
(
    'SMA',
    'SMA — Simple Moving Average',
    'Trend direction filter using equal-weighted average of N past closes. Slower than EMA — gives no extra weight to recent bars. SMA200 is the institutional benchmark for the long-term bull/bear divide. SMA50 is the intermediate-term trend filter.',
    'Using SMA for short-term entries on intraday charts — SMA is too slow for sub-daily decisions. Treating a brief dip below SMA200 as a confirmed bear market. The slope of SMA matters far more than a single cross of price through it.',
    'Price above rising SMA200 with SMA50 above SMA200 = confirmed bull regime (SMA200 context for VcpSwing STRAT-001). SMA200 acting as a bounce level after a pullback in a strong trend. Breadth above 60% of stocks over SMA200 = bull market regime (market-wide filter). SMA50 slope turning positive after a base period = emerging intermediate uptrend.',
    'Intraday scalping — SMA is not responsive enough. Rapidly trending markets where price has accelerated far from SMA (mean reversion risk is high). Avoid SMA crossovers during earnings season when gaps reset price away from averages.',
    'Market breadth (% stocks above SMA200) as regime context. ADX to determine if SMA direction is meaningful. Volume to validate SMA support/resistance tests.',
    'Use SMA200 as a binary regime filter: full size only in strategies when price is above SMA200 for the relevant index. At SMA support tests in confirmed uptrends: standard entry size. Do not chase entries far above SMA200 without a pullback.'
),
(
    'Supertrend',
    'Supertrend',
    'ATR-based trailing trend indicator that plots a line above or below price. When price is above the Supertrend line = bullish; below = bearish. The line switches sides on trend reversals. Built from ATR multiplied by a factor (default 3) above/below the midpoint of the recent range.',
    'Treating every Supertrend flip as a high-conviction trade. Supertrend generates false flips in choppy markets — ADX < 20 environments produce whipsaws. The default ATR-3 multiplier may be too tight for high-volatility instruments.',
    'Supertrend flip from bearish to bullish with ADX rising from below 20 = strong early-trend signal. Price holding above Supertrend for multiple bars with increasing volume = trend continuation. Supertrend acting as a trailing stop exit rather than an entry trigger — especially effective for swing trades.',
    'ADX < 20 choppy regimes where Supertrend flips repeatedly. Near earnings/event days. When ATR is in a spike phase that causes the Supertrend band to widen excessively, reducing its usefulness as a stop.',
    'ADX to confirm trend regime before trading Supertrend signals. Volume to validate the flip bar conviction. EMA for direction confirmation when Supertrend gives conflicting signals.',
    'Use as trailing stop or exit trigger rather than primary entry. When using Supertrend as entry, require ADX > 20 confirmation and reduce size to 50–75% versus a structured pullback entry. For exits, respect the Supertrend line without adding discretionary override — the ATR buffer is there for a reason.'
),
(
    'Volume',
    'Volume',
    'Number of shares/contracts traded in a period. Volume is the only leading indicator — it can precede price movement because accumulation and distribution by large participants show up in volume before price reacts. Rising price on rising volume = conviction; rising price on falling volume = weak move.',
    'Treating raw volume numbers as signals without normalisation. A volume of 1M shares means nothing without comparing it to average volume. Ignoring that options and futures volume behave differently from equity volume. Expecting volume to lead every move — some distribution happens quietly.',
    'Volume expansion on a breakout above consolidation = high-conviction breakout (VcpSwing STRAT-001 requires volume confirmation). Volume dry-up (contraction to near-zero) during a price base = accumulation quiet zone before the next move. Above-average volume on a reversal day after a trend = potential exhaustion.',
    'During lunch hours (12–2 PM IST) when intraday volume is seasonally low — false signals. On days with known scheduled events where institutional positioning may suppress volume artificially until the event.',
    'Price breakout patterns (require above-average volume). ATR to contextualise whether the price move is proportionate to the volume. OBV for cumulative distribution/accumulation tracking.',
    'Volume confirmation is a filter, not a signal — it scales your confidence in the primary signal. On breakouts with 1.5× average volume: standard size. On breakouts with >2× average volume: consider 1.25× size. On breakouts with below-average volume: reduce to 50% and watch for follow-through.'
),
(
    'OBV',
    'OBV — On-Balance Volume',
    'Cumulative volume indicator: adds volume on up-days and subtracts on down-days. OBV tracks the flow of money into and out of a security over time. When OBV is rising while price is flat or lagging = accumulation likely preceding an up move. When OBV falls while price holds = distribution.',
    'Treating OBV as a standalone buy/sell signal. OBV is most valuable as a divergence indicator — watching whether OBV and price are confirming each other or diverging. The absolute OBV level is meaningless; only the trend and divergences from price matter.',
    'OBV making new highs while price has not yet (OBV leading) = bullish accumulation underway, price breakout likely. OBV divergence bearish (price makes new high but OBV does not) = distribution, weakening trend. OBV trend aligning with price trend = confirmation of genuine trend momentum.',
    'Short-term intraday charts where OBV noise is high and the divergence signal-to-noise ratio is poor. In thinly traded instruments where a few large trades distort OBV without representing genuine market-wide positioning.',
    'Price structure (compare OBV peak/trough to price peak/trough for divergences). Volume for the individual bar context. RSI or Stochastic for oscillator confirmation of OBV divergence.',
    'OBV divergence is a warning signal that modifies your conviction in a trade — not a standalone entry. When OBV confirms the trend: standard size. When OBV diverges from price (bearish divergence): reduce size by 25–40% or wait for price confirmation before entering the anticipated reversal.'
),
(
    'Fibonacci',
    'Fibonacci Retracements & Extensions',
    'Horizontal price levels derived from Fibonacci ratios (23.6%, 38.2%, 50%, 61.8%, 78.6% retracements; 127.2%, 161.8%, 261.8% extensions). These levels mark areas where the market has historically shown support/resistance during pullbacks or extensions after a significant move.',
    'Drawing Fibonacci from any two arbitrary points and expecting the levels to work. Fibonacci is only meaningful when drawn from a significant swing high to swing low (or vice versa) in the context of the current trend. Treating Fibonacci levels as price magnets rather than zones where reactions are more likely.',
    'Price pulling back to the 61.8% level (golden ratio) of a prior swing in an uptrend with volume tapering = high-probability long setup. 161.8% extension as profit target after a clean base breakout. 78.6% retracement with reversal candle pattern = last-chance entry in a strong trend. Used in STRAT-002 (FibOptionSpread): 61.8% retracement as delta anchor for hedged spread entry.',
    'In random, choppy markets with no clear prior swing to measure from. When multiple Fibonacci grids from different swings conflict with each other creating ambiguous levels. On instruments with very low liquidity where price movement is not technically driven.',
    'Candlestick reversal patterns at Fibonacci levels (hammer, engulfing) for entry timing. Volume for confirmation at the level. RSI divergence at the Fibonacci level for extra confluence.',
    'Fibonacci entries at 61.8% retracement with multiple confirmations: standard size. At 38.2% (shallower pullback, more aggressive): 75% size. Near 78.6% (deep retest): 50% size with tighter stop, as a deeper retrace often signals trend weakness.'
),
(
    'PivotPoints',
    'Pivot Points (CPR / Daily Pivots)',
    'Price levels calculated from the prior session''s high, low, and close. Central Pivot Range (CPR) = (H+L+C)/3 with BC and TC bands. Support and resistance levels (S1, S2, S3, R1, R2, R3) mark potential turning points. Widely used in Indian intraday trading, especially for Nifty and BankNifty.',
    'Treating pivot levels as guaranteed turning points rather than zones where institutional activity tends to cluster. CPR width is a regime indicator (narrow CPR = trending day expected; wide CPR = reversal day expected) — ignoring width is a missed signal.',
    'Narrow CPR (width < 0.1% of price) = trending day bias; take breakout in direction of initial gap. Wide CPR = reversal day bias; look for S1/R1 tests to fade. Price respecting S1/R1 on first test with reversal candle = intraday mean-reversion long/short entry. R2/R3 and S2/S3 used as targets on trending days.',
    'On days with major scheduled events — pivots computed from prior session do not anticipate the event gap. In low-liquidity pre-market or post-market sessions. When the prior session had an unusual range (index options expiry, circuit breaker) that distorts pivot calculations.',
    'VWAP for intraday fair value context alongside pivots. Volume at pivot tests to confirm whether a level is being respected or broken. PCR for intraday directional bias that complements pivot analysis.',
    'S1/R1 tests with reversal candles: standard intraday size. Narrow CPR breakout setups: reduce to 50% at open — wait for first 15-minute range to establish, then enter breakout direction with stop below the range. S2/R2 entries are lower probability; use 50–75% size.'
),
(
    'ParabolicSAR',
    'Parabolic SAR',
    'Trailing stop-and-reverse indicator that plots a dot above or below price. Dot below price = bullish; dot above = bearish. The dot accelerates toward price as the trend matures (acceleration factor 0.02 step, 0.20 max by default). When price crosses the dot, PSAR reverses.',
    'Using PSAR as a standalone entry signal. PSAR is designed as an exit/trailing-stop tool, not an entry trigger. Using default acceleration parameters on all instruments and timeframes — fast markets require tighter parameters; slow markets need wider. PSAR always has a position (long or short) which means it forces trades in range-bound markets.',
    'PSAR acting as a trailing stop in an established trend after an entry from a separate signal. PSAR reversal coinciding with an ADX > 25 trend change confirms the flip has meaning. PSAR tightening (dot very close to price) = trend losing momentum — early warning to scale out or tighten stops.',
    'ADX < 20 range-bound markets where PSAR generates multiple reversals per week at significant loss. Immediately after a PSAR flip — wait one bar for confirmation before acting. High-volatility news environments where the initial wick can trigger PSAR reversal without a genuine trend change.',
    'ADX to confirm that PSAR is operating in a trending environment. ATR to calibrate the acceleration factor: higher ATR instruments need a lower acceleration factor to avoid premature PSAR flips. Volume to confirm trend momentum.',
    'Use PSAR as a trailing stop exit mechanism after a position is established by another signal — not as the primary entry. Scale stop to PSAR level once the trade moves into profit. In confirmed trends (ADX > 25): PSAR trailing stop allows full size. Never size up based on a PSAR reversal alone — always require another entry signal.'
)
ON CONFLICT (indicator_key) DO NOTHING;
