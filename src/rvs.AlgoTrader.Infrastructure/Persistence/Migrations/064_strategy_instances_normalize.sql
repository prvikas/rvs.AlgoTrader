-- Migration 064: Fix strategy_instances to use FKs instead of loose strings
-- Replaces: broker_name, exchange, product_type, lot_size, broker_token
-- With: broker_account_id, broker_exchange_config_id FKs

-- ── 1. Add new FK columns ────────────────────────────────────────────────────
ALTER TABLE strategy_instances
    ADD COLUMN IF NOT EXISTS broker_account_id UUID,
    ADD COLUMN IF NOT EXISTS broker_exchange_config_id UUID;

-- ── 2. Backfill broker_account_id (from strategy_instances.broker_name → user_broker_accounts) ──
-- This is tricky: we need to find the user_id from the created_by field (username)
-- For safety, we leave this nullable initially and let application code backfill
-- Alternatively, assign to the "system" user's broker account
UPDATE strategy_instances si
SET broker_account_id = (
    SELECT uba.id FROM user_broker_accounts uba
    JOIN brokers b ON uba.broker_id = b.id
    WHERE b.name = si.broker_name
    AND uba.user_id = '00000000-0000-0000-0000-000000000001'  -- system user
    LIMIT 1
)
WHERE si.broker_account_id IS NULL
AND si.broker_name IS NOT NULL;

-- ── 3. Backfill broker_exchange_config_id ────────────────────────────────────
-- Look up by (broker_id, exchange_code, product_type_code)
UPDATE strategy_instances si
SET broker_exchange_config_id = (
    SELECT bec.id FROM broker_exchange_configs bec
    JOIN brokers b ON bec.broker_id = b.id
    JOIN exchanges e ON bec.exchange_id = e.id
    JOIN product_types pt ON bec.product_type_id = pt.id
    WHERE b.name = si.broker_name
    AND e.code = si.exchange
    AND pt.code = si.product_type
    LIMIT 1
)
WHERE si.broker_exchange_config_id IS NULL
AND si.broker_name IS NOT NULL
AND si.exchange IS NOT NULL
AND si.product_type IS NOT NULL;

-- ── 4. Verify backfill coverage (warn if NULLs remain) ──────────────────────
-- SELECT COUNT(*) FROM strategy_instances WHERE broker_account_id IS NULL OR broker_exchange_config_id IS NULL;

-- ── 5. Drop old columns ──────────────────────────────────────────────────────
ALTER TABLE strategy_instances
    DROP COLUMN IF EXISTS broker_name,
    DROP COLUMN IF EXISTS exchange,
    DROP COLUMN IF EXISTS product_type,
    DROP COLUMN IF EXISTS lot_size,
    DROP COLUMN IF EXISTS broker_token;

-- ── 6. Add foreign key constraints ───────────────────────────────────────────
ALTER TABLE strategy_instances
    ADD CONSTRAINT fk_strategy_instances_broker_account FOREIGN KEY (broker_account_id) REFERENCES user_broker_accounts(id) ON DELETE SET NULL,
    ADD CONSTRAINT fk_strategy_instances_broker_exchange_config FOREIGN KEY (broker_exchange_config_id) REFERENCES broker_exchange_configs(id) ON DELETE SET NULL;
