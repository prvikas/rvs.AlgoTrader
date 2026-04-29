-- Migration 045: Indicator Intelligence cards
-- Stores editable intelligence cards for technical indicators.
-- Seeded with v1 content for 7 core indicators; users can update any field via the API.

CREATE TABLE IF NOT EXISTS indicator_intelligence (
    id                    UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    indicator_key         VARCHAR(100) NOT NULL UNIQUE,
    display_name          VARCHAR(200) NOT NULL,
    what_it_measures      TEXT         NOT NULL DEFAULT '',
    common_mistake        TEXT         NOT NULL DEFAULT '',
    positive_ev_conditions TEXT        NOT NULL DEFAULT '',
    ignore_conditions     TEXT         NOT NULL DEFAULT '',
    best_paired_with      TEXT         NOT NULL DEFAULT '',
    sizing_implications   TEXT         NOT NULL DEFAULT '',
    user_notes            TEXT         NOT NULL DEFAULT '',
    updated_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

-- ── Seed v1 intelligence cards ────────────────────────────────────────────────
-- ON CONFLICT DO NOTHING ensures re-running migration is safe.

INSERT INTO indicator_intelligence
    (indicator_key, display_name, what_it_measures, common_mistake, positive_ev_conditions, ignore_conditions, best_paired_with, sizing_implications)
VALUES
(
    'ADX',
    'ADX — Average Directional Index',
    'Trend strength, not direction. ADX rising = momentum building regardless of whether the move is up or down. +DI > -DI = bulls in control; -DI > +DI = bears in control.',
    'Assuming ADX > 25 means bullish. ADX only measures strength — direction comes from +DI/-DI crossover. Buying because ADX is high while -DI > +DI is a common expensive mistake.',
    'ADX > 25 with +DI > -DI for long trend-following. ADX > 25 with -DI > +DI for bearish trend-following. ADX rising from below 20 → trend is just starting to emerge.',
    'ADX < 20 for any trend-following setup. Choppy or mean-reverting markets. VIX shock days where momentum signals whipsaw. Sessions with major event risk (earnings, RBI policy).',
    'EMA crossover for direction confirmation. ATR for stop placement. Volume for entry conviction. RSI for exhaustion read near resistance/support.',
    'Full size only when ADX > 25 AND direction aligns (+DI/-DI). At ADX 20–25 use 50–75% size. Below ADX 20 avoid trend-following positions entirely or use mean-reversion setups instead.'
),
(
    'RSI',
    'RSI — Relative Strength Index',
    'Momentum oscillator measuring the ratio of average gains to average losses over N periods (default 14). Oscillates 0–100. Above 70 = overbought territory; below 30 = oversold territory.',
    'Treating overbought (>70) as a sell signal and oversold (<30) as a buy signal. In strong trends RSI can stay overbought or oversold for extended periods. Fading a trending RSI 80 is the most common RSI misuse.',
    'RSI divergence (price makes new high, RSI makes lower high) in overbought zone = bearish reversal warning. RSI recovering from 30 with price holding support = bullish. RSI 50 reclaim after a pullback in an uptrend = continuation entry.',
    'Strong trending markets where RSI stays pinned in overbought/oversold for weeks. Event-driven gaps that reset momentum without a tradeable setup. ADX < 20 choppy conditions where every oscillation is noise.',
    'Price structure (support/resistance levels) to confirm reversals. MACD histogram to confirm momentum direction. Volume to validate conviction on divergence signals.',
    'Use divergence signals at 50–75% size until price confirms the reversal. In trend-following mode ignore RSI extremes — only use RSI 50 cross for continuation confirmation.'
),
(
    'MACD',
    'MACD — Moving Average Convergence/Divergence',
    'Trend and momentum indicator showing the relationship between two EMAs (typically 12/26). The MACD line is EMA(12) - EMA(26). Signal line is EMA(9) of MACD. Histogram = MACD - Signal.',
    'Trading every MACD/signal line crossover as an entry. MACD crossovers are lagging — by the time the signal fires, the move may be 60–80% complete. Crossovers in choppy markets produce relentless whipsaws.',
    'Histogram divergence (price makes new extreme but histogram shrinks) is the highest-signal MACD setup. MACD zero-line crossover in a trending regime confirms trend shift. Histogram expanding from zero = momentum building.',
    'Sideways, range-bound markets where MACD crosses many times per week. Low-ADX environments. Near major event risk when news can invalidate any technical signal instantly.',
    'ADX to confirm a trending regime before acting on MACD signals. EMA slope to verify overall trend direction. Volume to confirm conviction on histogram expansions.',
    'Full size on histogram divergence confirmed by price action. Reduce size by 30–40% on simple crossover entries without divergence. Zero-line cross in a confirmed trend = treat as continuation with standard size.'
),
(
    'ATR',
    'ATR — Average True Range',
    'Volatility measure — the average of the true range (max of: current high-low, |high-prev close|, |low-prev close|) over N periods. Not directional. Measures how much price moves per bar on average.',
    'Using ATR as a directional signal. ATR expanding does not mean the move continues up or down — it only means volatility is increasing. Placing stops at exactly 1× ATR mechanically without considering structure.',
    'ATR-based stops that adapt to current volatility prevent both premature stop-outs (tight stops in high-vol regimes) and excessive risk (wide stops in low-vol regimes). ATR contraction after an expansion often precedes a directional move.',
    'As a standalone entry signal — ATR has no directional information. Avoid relying on ATR alone in extremely low-vol compression phases where a spike can be in either direction.',
    'All entry strategies for stop placement. ADX to understand whether volatility expansion is trending or random. Bollinger Bands (which use ATR-like standard deviation) for squeeze/breakout detection.',
    'Scale stops to 1.5–2.0× ATR in normal regimes. In elevated-VIX / high-ATR regimes, widen stops and reduce position size so absolute INR risk stays constant. Never use tight fixed-point stops in high-ATR conditions.'
),
(
    'BollingerBands',
    'Bollinger Bands',
    'Volatility envelope: a middle band (SMA-20) with upper/lower bands at ±2 standard deviations of price. Bands widen when volatility rises and contract during low-volatility compression.',
    'Treating every touch of the upper band as a sell and every touch of the lower band as a buy. In trending markets price can walk the band for many bars. Fading a strong trend at the upper band in a bull move is costly.',
    'Bollinger Squeeze (bands contract to a multi-month low width) followed by an expansion breakout is a high-probability setup. %B near 0 (price at lower band) with bullish divergence and price above SMA-200 = mean-reversion long. Band walk (price hugging upper band with closes near band) = trend continuation.',
    'In strong trending markets the walk-the-band condition invalidates reversal logic. Near event risk where a squeeze can break in any direction. When IV/ATR is structurally elevated and bands never truly contract.',
    'Volume to confirm breakout from squeeze. ADX to distinguish band-walk continuation from mean-reversion fade setups. RSI to identify exhaustion near band extremes.',
    'Squeeze breakout: full size with clear stop below the band at entry. Mean-reversion fade at band extreme: 50% entry with scale-in if price extends further, stop at band ± ATR buffer.'
),
(
    'VWAP',
    'VWAP — Volume-Weighted Average Price',
    'Intraday fair-value anchor. VWAP is the cumulative average price weighted by volume from market open. Institutions use it as a benchmark — algos and large funds buy near or below VWAP, sell above.',
    'Using daily VWAP across multiple sessions or treating it as a support/resistance level on a daily chart. VWAP resets every session and is only meaningful intraday. It is a reference, not an absolute entry trigger.',
    'Price reclaiming VWAP from below with increasing volume = intraday bullish bias. Price rejected at VWAP from above = short continuation. Price consolidating near VWAP in a low-ATR session = wait for breakout. Entries within VWAP tolerance (±0.5%) on momentum setups have lower adverse excursion.',
    'Near session open (first 15–30 min) when VWAP has insufficient volume to be statistically meaningful. On event days where a large gap puts price far from VWAP at open. On weekly timeframes or longer — VWAP is exclusively an intraday tool.',
    'PCR/OI for intraday directional bias (STRAT-003 pattern). ATR for stop placement away from VWAP. Volume delta / buying-selling ratio to confirm conviction.',
    'Entries near VWAP on momentum setups: standard size. Entries far from VWAP (price extended >1% from VWAP): reduce size by 30–50% and expect reversion risk. Never chase momentum entries that are significantly extended from VWAP without a retest.'
),
(
    'Stochastic',
    'Stochastic Oscillator',
    'Momentum oscillator comparing current close to the high-low range over N periods (default %K=14, %D=3 smoothed). Above 80 = overbought; below 20 = oversold. Measures where price is within its recent range.',
    'Using overbought/oversold as standalone buy/sell triggers in trending markets. Like RSI, stochastic can stay above 80 for extended periods in strong trends. %K/%D crossovers in the middle of the range are particularly unreliable.',
    'Stochastic divergence in overbought/oversold zones during range-bound markets. %K crossing above %D from below 20 = bullish setup. %K crossing below %D from above 80 = bearish setup. Works best in confirmed low-ADX, range-bound regimes.',
    'Strong trending markets (ADX > 30) — stochastic stays pinned and generates false signals. High VIX environments where oscillator extremes are meaningless. Sessions with binary event risk.',
    'ADX to confirm range-bound regime before relying on stochastic extremes. Bollinger Band width to confirm low-volatility range. Price structure to confirm support/resistance at oscillator extremes.',
    'Range-bound stochastic reversal: 50–75% size with tight stop just beyond the prior extreme. Avoid stochastic setups when ADX > 25. Scale to full size only when price structure, volume, and stochastic all align.'
)
ON CONFLICT (indicator_key) DO NOTHING;
