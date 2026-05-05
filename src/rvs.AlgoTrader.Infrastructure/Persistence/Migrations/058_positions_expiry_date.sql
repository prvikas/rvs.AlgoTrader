-- Migration 058: Add expiry_date column to positions table
-- Stores the actual option-series expiry (IST calendar date) for SPV positions.
-- Used by RollDecisionEngine to compute true DTE instead of estimating from position age.
-- NULL for plain equity / non-SPV positions.

ALTER TABLE positions
    ADD COLUMN IF NOT EXISTS expiry_date DATE NULL;

COMMENT ON COLUMN positions.expiry_date
    IS 'Option series expiry date (IST calendar). NULL for non-SPV positions. '
       'Used by RollDecisionEngine for accurate DTE calculation.';

CREATE INDEX IF NOT EXISTS ix_positions_expiry_date
    ON positions (expiry_date)
    WHERE expiry_date IS NOT NULL;
