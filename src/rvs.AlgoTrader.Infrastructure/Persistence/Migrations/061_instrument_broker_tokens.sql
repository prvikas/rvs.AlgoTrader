-- Migration 061: Normalize instrument tokens from hardcoded columns to child table
-- Creates: instrument_broker_tokens
-- Migrates: existing zerodha_token, upstox_token, mstock_token data
-- Drops: zerodha_token, upstox_token, mstock_token columns from instruments

-- ── 1. Create new child table ────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS instrument_broker_tokens (
    instrument_id UUID NOT NULL REFERENCES instruments(id) ON DELETE CASCADE,
    broker_id     SMALLINT NOT NULL REFERENCES brokers(id),
    token         VARCHAR(100) NOT NULL,
    PRIMARY KEY (instrument_id, broker_id)
);

CREATE INDEX IF NOT EXISTS idx_instrument_broker_tokens_broker
    ON instrument_broker_tokens (broker_id);

-- ── 2. Migrate existing tokens to new table ──────────────────────────────────
-- Insert Zerodha tokens
INSERT INTO instrument_broker_tokens (instrument_id, broker_id, token)
SELECT i.id, (SELECT id FROM brokers WHERE name = 'Zerodha'), i.zerodha_token
FROM instruments i
WHERE i.zerodha_token IS NOT NULL
ON CONFLICT DO NOTHING;

-- Insert Upstox tokens
INSERT INTO instrument_broker_tokens (instrument_id, broker_id, token)
SELECT i.id, (SELECT id FROM brokers WHERE name = 'Upstox'), i.upstox_token
FROM instruments i
WHERE i.upstox_token IS NOT NULL
ON CONFLICT DO NOTHING;

-- Insert MStock tokens
INSERT INTO instrument_broker_tokens (instrument_id, broker_id, token)
SELECT i.id, (SELECT id FROM brokers WHERE name = 'MStock'), i.mstock_token
FROM instruments i
WHERE i.mstock_token IS NOT NULL
ON CONFLICT DO NOTHING;

-- ── 3. Drop old token columns from instruments ───────────────────────────────
ALTER TABLE instruments
    DROP COLUMN IF EXISTS zerodha_token,
    DROP COLUMN IF EXISTS upstox_token,
    DROP COLUMN IF EXISTS mstock_token;
