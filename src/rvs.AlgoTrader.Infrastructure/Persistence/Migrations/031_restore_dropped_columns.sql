-- Migration 031: Restore columns incorrectly dropped by migration 023.
--
-- Migration 023 (#188, #189) dropped snake_case columns claiming they were
-- "PascalCase duplicates". They were not duplicates — they are the authoritative
-- columns mapped by EF Core. Restore them all as IF NOT EXISTS (safe to re-run).

-- instruments: option derivative fields (added by migration 003)
ALTER TABLE instruments
    ADD COLUMN IF NOT EXISTS underlying   VARCHAR(50),
    ADD COLUMN IF NOT EXISTS strike_price NUMERIC(18,4),
    ADD COLUMN IF NOT EXISTS option_type  INTEGER,
    ADD COLUMN IF NOT EXISTS expiry       DATE;

-- Restore the index on underlying used by IOptionChainService
CREATE INDEX IF NOT EXISTS idx_instruments_underlying
    ON instruments(underlying)
    WHERE underlying IS NOT NULL;

-- orders: trailing stop columns (present since InitialMigration)
ALTER TABLE orders
    ADD COLUMN IF NOT EXISTS trailing_sl NUMERIC(18,4),
    ADD COLUMN IF NOT EXISTS trailing_tp NUMERIC(18,4);
