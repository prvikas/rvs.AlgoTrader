-- Migration 003: Add missing derivative columns to instruments table and normalise
-- column names to snake_case.
--
-- Handles two DB creation paths:
--   A) EnsureCreatedAsync (EF convention): creates strikeprice, optiontype, underlying, expiry
--      (PascalCase property names lowercased by PostgreSQL, no underscore separation).
--      Action: rename to snake_case equivalents.
--   B) InitialMigration.sql only (manual run): these columns do not exist at all.
--      Action: add them with snake_case names.
--
-- Idempotent: safe to run multiple times.

-- Add columns with IF NOT EXISTS (handles path B, no-ops for path A)
ALTER TABLE instruments ADD COLUMN IF NOT EXISTS underlying   VARCHAR(50);
ALTER TABLE instruments ADD COLUMN IF NOT EXISTS strike_price NUMERIC(18,4);
ALTER TABLE instruments ADD COLUMN IF NOT EXISTS option_type  INTEGER;
ALTER TABLE instruments ADD COLUMN IF NOT EXISTS expiry       DATE;

-- Rename from EF convention name → snake_case (handles path A)
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'instruments' AND column_name = 'strikeprice'
    ) THEN
        ALTER TABLE instruments RENAME COLUMN strikeprice TO strike_price;
    END IF;

    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'instruments' AND column_name = 'optiontype'
    ) THEN
        ALTER TABLE instruments RENAME COLUMN optiontype TO option_type;
    END IF;

    -- "underlying" and "expiry" are single-word names: convention and snake_case are identical.
    -- No rename needed.
END $$;

-- Add index on underlying for option chain queries
CREATE INDEX IF NOT EXISTS idx_instruments_underlying ON instruments(underlying) WHERE underlying IS NOT NULL;
