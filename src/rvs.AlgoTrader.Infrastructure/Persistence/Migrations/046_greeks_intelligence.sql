-- Migration 046: Greeks Intelligence cards
-- Stores editable intelligence cards for options metrics (Greeks, IV, VIX).
-- Seeded with v1 content for 7 core metrics; users can update any field via the API.

CREATE TABLE IF NOT EXISTS greeks_intelligence (
    id                    UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    metric_key            VARCHAR(100) NOT NULL UNIQUE,
    display_name          VARCHAR(200) NOT NULL,
    what_it_measures      TEXT         NOT NULL DEFAULT '',
    why_it_matters        TEXT         NOT NULL DEFAULT '',
    common_misuse         TEXT         NOT NULL DEFAULT '',
    positive_ev_conditions TEXT        NOT NULL DEFAULT '',
    regime_context        TEXT         NOT NULL DEFAULT '',
    sizing_implications   TEXT         NOT NULL DEFAULT '',
    portfolio_impact      TEXT         NOT NULL DEFAULT '',
    user_notes            TEXT         NOT NULL DEFAULT '',
    updated_at            TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

-- ── Seed v1 intelligence cards ────────────────────────────────────────────────

INSERT INTO greeks_intelligence
    (metric_key, display_name, what_it_measures, why_it_matters, common_misuse, positive_ev_conditions, regime_context, sizing_implications, portfolio_impact)
VALUES
(
    'Delta',
    'Delta',
    'Rate of change of option price for a ₹1 move in the underlying. Call deltas range 0 to +1; put deltas range -1 to 0. ATM options have delta ≈ 0.50. Also loosely approximates probability of expiring in-the-money.',
    'Delta defines your directional exposure. A portfolio delta of +500 means you behave like you are long 500 shares of the underlying. Delta changes as the underlying moves (managed via gamma). Every P&L move in your option book traces back to delta first.',
    'Using delta as a precise probability of ITM — it is only an approximation and breaks down when IV is high or near expiry. Ignoring how delta changes as the underlying moves (gamma effect). Buying high-delta options and treating them like stock without accounting for time decay.',
    'Buying 0.30–0.40 delta calls/puts for directional plays gives meaningful gamma upside without paying full ATM premium. Selling 0.20–0.25 delta OTM options for premium collection with acceptable assignment probability. Delta-neutral spreads (iron condor, straddle) when expecting range-bound action.',
    'In trending regimes: buy options with delta 0.35–0.50 for directional exposure. In range-bound / elevated-IV regimes: sell high-delta options as part of spreads, keeping net portfolio delta near zero.',
    'Target delta per trade matching your directional conviction: 0.30–0.40 for moderate conviction, 0.45–0.55 for high conviction. Cap total portfolio delta so a 1% adverse underlying move stays within your daily loss limit.',
    'Sum all option leg deltas × lot size = net portfolio delta. A balanced book should have net delta close to zero unless you have a deliberate directional view. Rebalance when delta drifts beyond your threshold from underlying moves.'
),
(
    'Theta',
    'Theta (Time Decay)',
    'Rate of option price erosion per calendar day, all else equal. A theta of -5 means the option loses ₹5 of value per day from time passing alone. Theta is always negative for option buyers and positive for sellers.',
    'Theta is the engine of premium-selling strategies. Every day an option seller holds a position, theta works in their favour. Every day a buyer holds, theta works against them. Near expiry (last 7–10 days) theta accelerates sharply — this is the "theta burn" zone.',
    'Ignoring the gamma risk that accompanies high theta near expiry. Selling options with very high theta without accounting for the explosive gamma that makes those options dangerous to be short. Assuming theta income is free — it is compensation for gamma risk.',
    'Selling OTM options with adequate distance from spot and IV rich enough to compensate for gamma exposure. Short straddles / strangles in calm, range-bound regimes with India VIX below 15. Calendar spreads that harvest near-term theta while hedging with a longer-dated long.',
    'Low-VIX, range-bound regimes: theta-selling structures have highest positive EV. Elevated-VIX regimes: higher theta income but gamma risk spikes — reduce size and widen strikes. Near expiry: theta maximum but gamma maximum — only experienced sellers should hold through expiry week.',
    'Theta income should cover your estimated gamma risk: a rough rule is theta/gamma ratio > 2 is acceptable. Scale down all short-theta positions significantly (30–50%) during elevated VIX or ahead of major events.',
    'A book with positive net theta profits from time passing. Risk: a large move in the underlying can erase many days of theta in a single bar. Always size short-theta books so the maximum gamma loss on a 2% underlying move stays within your daily loss limit.'
),
(
    'Vega',
    'Vega (IV Sensitivity)',
    'Rate of change of option price for a 1% change in implied volatility. Long options have positive vega (benefit from IV rising); short options have negative vega (benefit from IV falling). Vega is highest for ATM options and longer-dated contracts.',
    'When you buy an option you are implicitly long volatility. When you sell you are short volatility. A position that looks directionally correct can lose money if IV collapses after your entry — this is the "vol crush" experienced by option buyers after an earnings event.',
    'Buying options right before a volatility collapse event (earnings, RBI policy) and being surprised by the post-event drop in premium despite a correct directional call. Ignoring that long straddles require a move larger than the implied move (priced in via IV) to profit.',
    'Selling rich IV (high IV percentile, >70th) into mean-reversion conditions after a volatility spike. Long vega positions (buying options) when IV is cheap (IVP < 30) and a volatility expansion is expected. Calendar spreads that are vega-positive (long far-dated, short near-dated) in low-IV compression phases.',
    'High-IV / elevated-VIX regime: short vega (sell premium) structures have positive EV from mean reversion. Low-IV / compression regime: long vega (buy options or calendar spreads) benefits from anticipated expansion. Do not be short vega ahead of known binary events.',
    'Cap total portfolio vega so a 5-point VIX spike does not exceed your weekly loss limit. In low-IV regimes, long-vega positions require less size because options are cheap. In high-IV regimes, short-vega positions require tighter size because premium income is high but tail risk is large.',
    'Net portfolio vega tells you how much your book gains or loses per 1% IV change across all strikes. A vega-neutral book profits from neither expansion nor contraction — useful when regime direction is unclear.'
),
(
    'Gamma',
    'Gamma',
    'Rate of change of delta for a ₹1 move in the underlying — the second derivative of option price with respect to the underlying. Gamma is highest for ATM options near expiry. Long options have positive gamma; short options have negative gamma.',
    'Gamma determines how fast your delta — and therefore your P&L — changes as the underlying moves. A long-gamma position accelerates its delta in your favour on big moves (convexity). A short-gamma position accelerates its delta against you on big moves — you lose faster as the underlying moves away from your strike.',
    'Being short gamma near expiry without adequate distance from the strike or robust hedging. Thinking a short straddle is safe because delta is neutral — delta is neutral momentarily, but gamma will create large delta exposure the moment the underlying moves. Ignoring the gamma blowup risk in weeklies near Thursday expiry.',
    'Long gamma (buy straddles / strangles) when you expect a large move and IV is cheap. Short gamma (sell straddles / strangles) in calm regimes with high IV, ample strike distance, and defined exit rules. Gamma scalping (delta-hedging a long-gamma position) in high-volatility regimes with clear intraday ranges.',
    'Low-VIX, range-bound: short gamma structures have positive EV — the underlying stays near your strike and gamma exposure is controlled. High-VIX, directional: avoid naked short gamma. Near expiry (last 3 trading days): gamma becomes extreme for near-ATM strikes — do not be unhedged short gamma on expiry week.',
    'Never hold a naked short-gamma position near expiry without a clearly defined max-loss scenario. For short-gamma books, set a hard delta threshold (e.g., net delta > ±200) that triggers an automatic hedge. Long-gamma positions can afford larger size because losses are capped at premium paid.',
    'Net portfolio gamma tells you how fast your portfolio delta drifts as the underlying moves. Positive gamma books become more profitable the larger the move. Negative gamma books accelerate losses on large moves — the primary tail risk of premium-selling strategies.'
),
(
    'IV',
    'IV — Implied Volatility',
    'The volatility level implied by current market option prices via a pricing model (Black-Scholes). If the market prices a Nifty option at ₹X, IV is the volatility figure that makes the model output ₹X. IV reflects the market''s collective expectation of future price movement.',
    'IV is the price of options. When IV is high, options are expensive — premium sellers have an edge. When IV is low, options are cheap — premium buyers have less edge but potential for expansion. Every option strategy is implicitly a bet on whether realized volatility will be higher or lower than the IV you traded at.',
    'Buying high-IV options expecting a big move — if realized volatility is lower than IV, you lose even if the direction is correct. Selling low-IV options thinking premium income is safe — if realized volatility spikes, short premium positions blow up. Treating IV as a sentiment indicator without checking whether it is historically high or low (always combine with IVP).',
    'Selling options when IV is above 70th percentile of its historical range (IV is rich). Buying options when IV is below 30th percentile (IV is cheap). Straddle sellers enter after a spike and revert when the IV crush follows the event.',
    'High-IV regime: short premium, sell spreads, exploit vol mean reversion. Low-IV regime: buy premium (or calendar spreads / ratio spreads) to benefit from expansion. Structural IV expansion (VIX trending up over weeks): avoid naked short-IV strategies.',
    'Larger size when selling into elevated IV (wide margin of safety). Smaller size when buying cheap IV (loss is capped at premium but must be sized so multiple losing trades stay within risk budget). Scale position size inversely with IV level to keep absolute vega risk constant.',
    'Net portfolio vega is the practical output of IV exposure across all positions. Monitor realized volatility vs IV gap weekly — when realized > IV for two consecutive weeks, re-evaluate short-IV bias in the book.'
),
(
    'IVP',
    'IV Percentile (IVP)',
    'The percentile rank of current IV relative to its own historical distribution over a lookback window (typically 1 year). IVP 80 means current IV is higher than 80% of its historical readings. IVP 20 means IV is cheaper than 80% of past readings.',
    'IVP answers the question "is IV currently rich or cheap relative to itself?" — unlike raw IV which varies by symbol and market phase. A Nifty IV of 15 may be high or low depending on the year. IVP normalises this, making it the single most useful regime input for options strategy selection.',
    'Using raw IV to compare richness across different symbols or time periods. Treating IVP > 50 as the threshold for selling premium — the genuine edge zone is IVP > 70 where options are rich relative to history. Ignoring that IVP can stay in an extreme for weeks during structural vol regimes.',
    'IVP > 70: sell premium (short straddle / strangle / iron condor / credit spreads) expecting mean reversion. IVP < 30: buy premium or use debit spreads / long straddles expecting expansion. IVP 30–70: neutral zone — use ratio spreads, calendars, or stay flat until a clearer regime emerges.',
    'High-IVP (>70) + VIX elevated: premium-selling has maximum theoretical edge but also maximum gamma risk. Combine with India VIX trend — if VIX is falling from a peak, short-premium has highest probability. Low-IVP (<30) + VIX calm: long-premium strategies are cheapest but require a catalyst for expansion.',
    'Highest size for premium-selling when IVP > 80 and VIX is visibly mean-reverting. Scale to minimum size (25–50%) when IVP < 30 for long-premium plays since expansion timing is uncertain. Never max size a short-premium book purely on IVP — always check underlying trend and gamma profile.',
    'IVP is a book-level input to strategy selection — it should determine which option structures are deployed, not just individual trade size. High-IVP books should be predominantly short-premium; low-IVP books should be either neutral or long-premium where structurally justified.'
),
(
    'IndiaVIX',
    'India VIX',
    'India VIX measures the market''s expectation of Nifty 50 volatility over the next 30 calendar days, derived from near- and next-expiry Nifty options using NSE''s proprietary formula (adapted from CBOE VIX methodology). Expressed as annualised volatility in percentage terms.',
    'India VIX is the primary regime classifier for Indian equity options. VIX rising = market expects more movement and uncertainty; option premium is expanding. VIX falling = market expects calm; premium is contracting. VIX level sets the context for every options strategy, stop placement, and position size decision.',
    'Treating VIX rising as a signal to buy puts or short the market — VIX is not directional. A VIX spike can occur in both bull and bear markets. Using a single VIX level snapshot without tracking its trend — the direction of VIX change over 3–5 days is more actionable than the level alone.',
    'VIX > 20 and starting to fall from a spike peak: sell premium (straddles / strangles / iron condors). IVP > 70 with VIX above 3-month average: short-premium structures have maximum theoretical edge. VIX below 14 in sustained compression: expect a breakout; long gamma or long-vega positions are cheap.',
    'VIX 0–14 (low): compression regime — IV cheap, trending strategies work best, option buying is affordable, premium selling has thin edge. VIX 15–20 (moderate): balanced regime — spreads and defined-risk strategies appropriate. VIX 20–30 (elevated): premium selling regime — straddles / strangles / iron condors with wider strikes. VIX > 30 (panic/event): reduce all size, hedge all directional exposure, avoid new short-gamma positions.',
    'Scale all position sizes inversely with VIX. At VIX 15: full size for defined-risk strategies. At VIX 25: 50–70% size. At VIX 35+: minimum size (25%) or flat. The logic is that at high VIX, premium is rich but realized moves are larger — the margin of safety widens in one dimension (IV) but shrinks in another (actual movement).',
    'VIX is a book-level risk input. Above VIX 25, the entire portfolio should shift toward defined-risk structures (spreads rather than naked shorts). Above VIX 35, prioritise capital preservation over premium income. A VIX > 30 environment is where undisciplined books experience their largest losses.'
)
ON CONFLICT (metric_key) DO NOTHING;
