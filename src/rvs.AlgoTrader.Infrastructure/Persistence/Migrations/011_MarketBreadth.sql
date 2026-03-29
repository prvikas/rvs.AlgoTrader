-- Migration 011: Market breadth snapshots and event calendar.
-- Adds: market_breadth_snapshots, market_events.

-- ── Market breadth ────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS market_breadth_snapshots (
    id                  UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    snapshot_date       DATE        NOT NULL,
    total_symbols       INT         NOT NULL DEFAULT 0,
    above_200_sma       INT         NOT NULL DEFAULT 0,
    above_50_sma        INT         NOT NULL DEFAULT 0,
    highs_count         INT         NOT NULL DEFAULT 0,
    lows_count          INT         NOT NULL DEFAULT 0,
    advancing_count     INT         NOT NULL DEFAULT 0,
    declining_count     INT         NOT NULL DEFAULT 0,
    regime              VARCHAR(20) NOT NULL DEFAULT 'Neutral',
    computed_at         TIMESTAMPTZ NOT NULL DEFAULT now(),

    CONSTRAINT uq_market_breadth_date UNIQUE (snapshot_date)
);

CREATE INDEX IF NOT EXISTS ix_market_breadth_date ON market_breadth_snapshots (snapshot_date DESC);

-- ── Market events (economic calendar) ─────────────────────────────────────────
CREATE TABLE IF NOT EXISTS market_events (
    id              UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    event_date      DATE        NOT NULL,
    event_time      TIME,                          -- NULL = all-day / time unknown
    event_type      VARCHAR(50) NOT NULL,           -- e.g. RBI_POLICY, EARNINGS, BUDGET, EXPIRY
    title           VARCHAR(300) NOT NULL,
    description     TEXT,
    impact          VARCHAR(20) NOT NULL DEFAULT 'Medium',  -- Low, Medium, High
    symbol          VARCHAR(50),                   -- NULL = market-wide event
    source          VARCHAR(100),
    is_recurring    BOOLEAN     NOT NULL DEFAULT FALSE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_market_events_date   ON market_events (event_date);
CREATE INDEX IF NOT EXISTS ix_market_events_type   ON market_events (event_type);
CREATE INDEX IF NOT EXISTS ix_market_events_symbol ON market_events (symbol) WHERE symbol IS NOT NULL;
