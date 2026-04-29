-- Migration 048: Quant Lab — quant_conditions table
-- Stores user-defined research conditions with lifecycle tracking and dated notes.
-- is_template = true rows are prebuilt examples users can clone.

CREATE TABLE IF NOT EXISTS quant_conditions (
    id                      UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    name                    VARCHAR(200) NOT NULL,
    hypothesis              TEXT         NOT NULL DEFAULT '',
    -- JSON array of {indicator, operator, value, description}
    conditions_json         JSONB        NOT NULL DEFAULT '[]'::jsonb,
    sizing_rules            TEXT         NOT NULL DEFAULT '',
    invalidation_conditions TEXT         NOT NULL DEFAULT '',
    -- JSON array of {id, date, text}
    notes_json              JSONB        NOT NULL DEFAULT '[]'::jsonb,
    -- Hypothesis | Backtesting | PaperTrading | LiveSmall | LiveFull | Retired
    status                  VARCHAR(50)  NOT NULL DEFAULT 'Hypothesis',
    tags                    TEXT[]       NOT NULL DEFAULT '{}',
    is_template             BOOLEAN      NOT NULL DEFAULT false,
    created_at              TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at              TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_quant_conditions_status ON quant_conditions(status);
CREATE INDEX IF NOT EXISTS ix_quant_conditions_template ON quant_conditions(is_template);

-- ── Prebuilt template conditions ──────────────────────────────────────────────
-- Seeded with is_template=true so they appear in the template library.
-- Users clone these to create their own editable copies.

INSERT INTO quant_conditions
    (name, hypothesis, conditions_json, sizing_rules, invalidation_conditions, status, tags, is_template)
VALUES
(
    'VIX Mean Reversion Short Strangle',
    'When India VIX spikes above 20 and begins reverting from a peak, OTM premium-selling on Nifty has positive expected value. The elevated IV inflates option premiums; as VIX normalises, premium collapses faster than delta risk materialises if strikes are placed with adequate distance.',
    '[
      {"indicator":"IndiaVIX","operator":">","value":"20","description":"VIX elevated above long-term average — option premiums rich"},
      {"indicator":"IVP","operator":">","value":"70","description":"IV Percentile confirms richness of premiums historically"},
      {"indicator":"ADX","operator":"<","value":"28","description":"No strong trend — avoids directional risk for short strangle"},
      {"indicator":"VIX 5d change","operator":"<","value":"0","description":"VIX starting to fall from peak — confirms mean reversion in progress"},
      {"indicator":"Event risk","operator":"=","value":"none","description":"No scheduled RBI policy, earnings, or F&O expiry within 5 days"}
    ]'::jsonb,
    'Risk capped at 2% of account per position. Vega exposure capped at 200 per Nifty lot. No new entry within 3 days of weekly expiry. Stop: underlying moves > 1.5% intraday. Profit target: 50% of max premium collected.',
    'VIX continues rising (> 28) — close all shorts. ADX > 30 (strong trend developing) — exit immediately. IV expanding structurally (IVP rising week-over-week). Any binary event announced within the holding period.',
    'Hypothesis',
    ARRAY['options','premium-selling','nifty','vix','straddle'],
    true
),
(
    'VCP Breakout with Market Breadth Confirmation',
    'VCP (Volatility Contraction Pattern) breakout setups have higher win rates when the broader market is in a bull regime (>60% of NSE stocks above SMA200). The breadth filter eliminates breakout failures caused by broad market weakness pulling leading stocks down.',
    '[
      {"indicator":"MarketBreadth >SMA200","operator":">","value":"60","description":"More than 60% of NSE stocks above 200-day SMA — bull regime confirmed"},
      {"indicator":"SMA200","operator":"<","value":"price","description":"Stock price above its own 200-day SMA — long-term uptrend"},
      {"indicator":"VCP contractions","operator":">=","value":"3","description":"At least 3 volatility contractions from the base — classic VCP structure"},
      {"indicator":"Volume on breakout","operator":">","value":"1.5x avg","description":"Above-average volume confirms institutional participation"},
      {"indicator":"ADX","operator":">","value":"20","description":"Emerging trend strength on the breakout bar"}
    ]'::jsonb,
    'Position size: 1–2% risk per trade. Stop: below the final pivot low of the VCP base. Initial target: prior swing high. Extended target: 3× the base depth. Add to position only if volume expands further on follow-through day.',
    'Market breadth drops below 50% (bear regime) — do not initiate new VCP entries. Stock gaps down on earnings after entry — exit at open. Volume on breakout bar < 1× average (no institutional participation). SMA200 slope turns negative.',
    'Hypothesis',
    ARRAY['equity','vcpswing','breadth','breakout','swing'],
    true
),
(
    'Intraday VWAP PCR Momentum — Nifty Options',
    'When PCR(change in OI) is in an extreme (>1.2 bullish or <0.8 bearish) and the underlying is trading near VWAP (within 0.5%) after the 11:00 IST observation window, buying delta-targeted options (0.30–0.35) has positive EV on trend days.',
    '[
      {"indicator":"PCR change OI","operator":">","value":"1.2","description":"Put-call ratio of OI change above 1.2 = bullish bias from new OI buildup"},
      {"indicator":"Price vs VWAP","operator":"within","value":"0.5%","description":"Price near VWAP — institutional fair value zone, lower adverse excursion"},
      {"indicator":"Time IST","operator":">","value":"11:00","description":"After observation window — sufficient price discovery completed"},
      {"indicator":"ADX (15m)","operator":">","value":"20","description":"Intraday trend forming — not a random oscillation"},
      {"indicator":"India VIX","operator":"<","value":"20","description":"Not a panic day — delta exposure manageable"}
    ]'::jsonb,
    'Buy 1 lot of 0.30–0.35 delta call (PCR bullish) or put (PCR bearish) with nearest weekly expiry. Risk cap: premium paid only (no naked exposure). Target: session high/low or 1.5× risk. Exit: close before 3:00 PM IST regardless. Stop: session low/high of underlying breached.',
    'PCR moves into neutral zone (0.8–1.2) after entry — exit position. Underlying moves > 1% against position within first 30 minutes — stop out. Large gap day (>100 Nifty pts) — defer to 13:00 IST per STRAT-003 spec. VIX > 20 on entry day.',
    'Hypothesis',
    ARRAY['intraday','options','nifty','pcr','vwap','strat-003'],
    true
);
