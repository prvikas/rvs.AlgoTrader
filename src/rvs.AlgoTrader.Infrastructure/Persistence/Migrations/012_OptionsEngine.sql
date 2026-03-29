-- Migration 012: Options Engine
-- option_iv_history: daily ATM IV snapshots per underlying for IV Rank/Percentile computation
-- spread_positions + spread_legs: multi-leg option spread tracking (#84)

-- ── IV History ────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS option_iv_history (
    id               UUID        NOT NULL DEFAULT gen_random_uuid(),
    underlying_symbol TEXT        NOT NULL,
    date             DATE        NOT NULL,
    atm_iv           NUMERIC(8,4) NOT NULL,   -- e.g. 18.5000 = 18.5% annualised
    recorded_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT pk_option_iv_history PRIMARY KEY (id),
    CONSTRAINT uq_option_iv_history UNIQUE (underlying_symbol, date)
);

CREATE INDEX IF NOT EXISTS ix_option_iv_history_symbol_date
    ON option_iv_history (underlying_symbol, date DESC);

-- ── Spread positions (multi-leg) ──────────────────────────────────────────
CREATE TABLE IF NOT EXISTS spread_positions (
    id               UUID        NOT NULL DEFAULT gen_random_uuid(),
    broker_name      TEXT        NOT NULL,
    underlying_symbol TEXT       NOT NULL,
    spread_type      TEXT        NOT NULL,   -- 'IronCondor', 'Vertical', 'Straddle', etc.
    status           TEXT        NOT NULL DEFAULT 'Open',
    net_premium      NUMERIC(12,2),
    realized_pnl     NUMERIC(12,2),
    strategy_run_id  UUID,
    correlation_id   TEXT        NOT NULL DEFAULT '',
    opened_at        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    closed_at        TIMESTAMPTZ,
    CONSTRAINT pk_spread_positions PRIMARY KEY (id)
);

CREATE TABLE IF NOT EXISTS spread_legs (
    id                  UUID        NOT NULL DEFAULT gen_random_uuid(),
    spread_position_id  UUID        NOT NULL REFERENCES spread_positions(id) ON DELETE CASCADE,
    internal_symbol     TEXT        NOT NULL,
    broker_token        TEXT        NOT NULL,
    direction           TEXT        NOT NULL,   -- 'Buy' or 'Sell'
    option_type         TEXT        NOT NULL,   -- 'Call' or 'Put'
    strike_price        NUMERIC(12,2) NOT NULL,
    expiry              DATE        NOT NULL,
    quantity            INT         NOT NULL,
    entry_price         NUMERIC(12,4),
    exit_price          NUMERIC(12,4),
    broker_order_id     TEXT,
    status              TEXT        NOT NULL DEFAULT 'Pending',
    CONSTRAINT pk_spread_legs PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_spread_legs_position
    ON spread_legs (spread_position_id);
