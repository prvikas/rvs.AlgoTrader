-- Migration 038: option_chain_snapshots
--
-- Stores daily EOD option chain snapshots for historical backtest use.
-- Recorded by OptionChainSnapshotJob after market close (~3:45 PM IST).
--
-- Key aggregate columns are denormalised from the JSONB legs array so
-- BacktestEngine can load them efficiently without full JSON parsing.
-- The raw legs array (legs_json) is preserved for full chain reconstruction.
--
-- BacktestEngine usage:
--   Pre-loads the full date range at the start of a backtest (FIB-5).
--   Per-bar: looks up snapshot by date and populates StrategyContext.OptionChain.
--   Falls back to synthetic chain (BuildSyntheticLegs) when no snapshot exists.
--
-- StrategyEvaluationQueue / ForwardTestEngine:
--   Uses IOptionChainService.GetSnapshotAsync() for live data (not this table).

CREATE TABLE IF NOT EXISTS option_chain_snapshots (
    id                UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    underlying_symbol TEXT          NOT NULL,
    snapshot_date     DATE          NOT NULL,
    expiry_date       DATE          NOT NULL,
    spot_price        NUMERIC(12,2) NOT NULL,
    -- Pre-computed aggregate columns for fast per-bar queries
    pcr               NUMERIC(8,4),           -- PutCallRatioOI  (PE OI / CE OI)
    pcr_change        NUMERIC(8,4),           -- PutCallRatioChangeOI (PE ΔOI / CE ΔOI)
    atm_iv            NUMERIC(8,4),           -- Average IV ±200 pts around spot
    max_pain_strike   NUMERIC(12,2),          -- MaxPainStrike
    ce_max_oi_strike  NUMERIC(12,2),          -- Highest CE OI strike (resistance)
    pe_max_oi_strike  NUMERIC(12,2),          -- Highest PE OI strike (support)
    total_ce_oi       BIGINT,                 -- Total CE open interest
    total_pe_oi       BIGINT,                 -- Total PE open interest
    -- Full chain: array of {strikePrice, optionType, ltp, oi, oiChange,
    --             volume, iv, bidPrice, askPrice, delta}
    legs_json         JSONB         NOT NULL DEFAULT '[]',
    recorded_at       TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    -- One snapshot per symbol per day (EOD — overwritten on re-run)
    CONSTRAINT uq_oc_snapshot UNIQUE (underlying_symbol, snapshot_date)
);

CREATE INDEX IF NOT EXISTS idx_oc_snapshots_symbol_date
    ON option_chain_snapshots (underlying_symbol, snapshot_date DESC);
