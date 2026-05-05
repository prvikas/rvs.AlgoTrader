-- Migration 063: Restructure user_broker_accounts and drop broker_credentials
-- Transforms user_broker_accounts to use broker_id FK instead of loose strings
-- Moves credential fields from broker_credentials to user_broker_accounts
-- Drops: broker_credentials table

-- ── 1. Add new columns to user_broker_accounts ───────────────────────────────
ALTER TABLE user_broker_accounts
    ADD COLUMN IF NOT EXISTS broker_id SMALLINT,
    ADD COLUMN IF NOT EXISTS broker_token VARCHAR(500),
    ADD COLUMN IF NOT EXISTS api_key VARCHAR(500),
    ADD COLUMN IF NOT EXISTS api_secret VARCHAR(500);

-- ── 2. Backfill broker_id from broker_name ──────────────────────────────────
UPDATE user_broker_accounts
SET broker_id = (SELECT id FROM brokers WHERE name = broker_name)
WHERE broker_id IS NULL AND broker_name IS NOT NULL;

-- ── 3. Migrate broker_token from broker_credentials (if table exists) ────────
DO $$
BEGIN
    IF EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'broker_credentials') THEN
        UPDATE user_broker_accounts uba
        SET broker_token = bc.broker_token
        FROM broker_credentials bc
        WHERE bc.broker_name = (SELECT name FROM brokers WHERE id = uba.broker_id)
        AND uba.broker_token IS NULL;
    END IF;
END $$;

-- ── 4. Drop old columns and enforce NOT NULL ────────────────────────────────
ALTER TABLE user_broker_accounts
    ALTER COLUMN broker_id SET NOT NULL,
    DROP COLUMN broker_name,
    DROP COLUMN market;

-- ── 5. Add unique constraint on (user_id, broker_id) ───────────────────────
ALTER TABLE user_broker_accounts
    ADD CONSTRAINT fk_user_broker_accounts_broker FOREIGN KEY (broker_id) REFERENCES brokers(id),
    ADD CONSTRAINT uq_user_broker_account UNIQUE (user_id, broker_id);

-- Drop old constraint if it exists
ALTER TABLE user_broker_accounts DROP CONSTRAINT IF EXISTS uq_user_broker_market;

-- ── 6. Drop broker_credentials table ─────────────────────────────────────────
DROP TABLE IF EXISTS broker_credentials CASCADE;
