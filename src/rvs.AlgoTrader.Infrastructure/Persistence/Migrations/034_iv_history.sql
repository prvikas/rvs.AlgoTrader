-- Migration 034: iv_history table for IVP (IV Percentile Rank) computation
-- Source: daily IV snapshots from mStock option chain API.
-- IVP = PERCENT_RANK() of today's iv_close vs the prior 252 trading days.
-- Queried by IOptionIvRankService to populate IvRankSnapshot used in STRAT-002.

CREATE TABLE IF NOT EXISTS iv_history (
    id               UUID        NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    internal_symbol  VARCHAR(64) NOT NULL,
    date             DATE        NOT NULL,
    iv_close         NUMERIC(10,4) NOT NULL CHECK (iv_close >= 0),
    iv_rank_20d      NUMERIC(6,4),   -- IV rank (0-1) vs prior 20 trading days
    iv_rank_52w      NUMERIC(6,4),   -- IV rank (0-1) vs prior 52 weeks (252 trading days)
    iv_percentile_52w NUMERIC(6,4),  -- IV percentile (0-100) vs prior 252 days
    created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_iv_history_symbol_date UNIQUE (internal_symbol, date)
);

CREATE INDEX IF NOT EXISTS idx_iv_history_symbol_date
    ON iv_history (internal_symbol, date DESC);
