-- Migration 057: Add structure_type column to positions table
-- Stores the option spread structure (IronCondor, ShortStraddleStrangle, etc.)
-- for ShortPremiumVelocity positions.  Nullable — NULL for plain equity / non-SPV rows.

ALTER TABLE positions
    ADD COLUMN IF NOT EXISTS structure_type VARCHAR(50) NULL;

COMMENT ON COLUMN positions.structure_type
    IS 'SPV spread structure name (e.g. IronCondor, ShortStraddleStrangle). NULL for non-SPV positions.';
