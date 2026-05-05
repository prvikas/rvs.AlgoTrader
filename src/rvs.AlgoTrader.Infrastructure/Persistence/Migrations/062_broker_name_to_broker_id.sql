-- Migration 062: Replace broker_name VARCHAR with broker_id SMALLINT FK
-- Applies to: orders, positions, strategy_runs, capital_allocations,
--             broker_latency_log, symbol_data_preferences, broker_sessions

-- ── orders ──────────────────────────────────────────────────────────────────
ALTER TABLE orders
    ADD COLUMN IF NOT EXISTS broker_id SMALLINT;

UPDATE orders SET broker_id = (SELECT id FROM brokers WHERE name = broker_name)
    WHERE broker_id IS NULL AND broker_name IS NOT NULL;

-- Index must be recreated for performance after column change
DROP INDEX IF EXISTS idx_orders_broker_status;

ALTER TABLE orders
    ALTER COLUMN broker_id SET NOT NULL,
    DROP COLUMN broker_name;

CREATE INDEX idx_orders_broker_status ON orders(broker_id, status);
ALTER TABLE orders ADD CONSTRAINT fk_orders_broker FOREIGN KEY (broker_id) REFERENCES brokers(id);

-- ── positions ───────────────────────────────────────────────────────────────
ALTER TABLE positions
    ADD COLUMN IF NOT EXISTS broker_id SMALLINT;

UPDATE positions SET broker_id = (SELECT id FROM brokers WHERE name = broker_name)
    WHERE broker_id IS NULL AND broker_name IS NOT NULL;

DROP INDEX IF EXISTS idx_positions_broker_open;

ALTER TABLE positions
    ALTER COLUMN broker_id SET NOT NULL,
    DROP COLUMN broker_name;

CREATE INDEX idx_positions_broker_open ON positions(broker_id, is_open);
ALTER TABLE positions ADD CONSTRAINT fk_positions_broker FOREIGN KEY (broker_id) REFERENCES brokers(id);

-- ── strategy_runs ───────────────────────────────────────────────────────────
ALTER TABLE strategy_runs
    ADD COLUMN IF NOT EXISTS broker_id SMALLINT;

UPDATE strategy_runs SET broker_id = (SELECT id FROM brokers WHERE name = broker_name)
    WHERE broker_id IS NULL AND broker_name IS NOT NULL;

ALTER TABLE strategy_runs
    DROP COLUMN IF EXISTS broker_name;

ALTER TABLE strategy_runs ADD CONSTRAINT fk_strategy_runs_broker FOREIGN KEY (broker_id) REFERENCES brokers(id);

-- ── capital_allocations ─────────────────────────────────────────────────────
ALTER TABLE capital_allocations
    ADD COLUMN IF NOT EXISTS broker_id SMALLINT;

UPDATE capital_allocations SET broker_id = (SELECT id FROM brokers WHERE name = broker_name)
    WHERE broker_id IS NULL AND broker_name IS NOT NULL;

ALTER TABLE capital_allocations
    ALTER COLUMN broker_id SET NOT NULL,
    DROP COLUMN broker_name;

ALTER TABLE capital_allocations ADD CONSTRAINT fk_capital_allocations_broker FOREIGN KEY (broker_id) REFERENCES brokers(id);

-- ── broker_latency_log (if exists) ──────────────────────────────────────────
DO $$
BEGIN
    IF EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'broker_latency_log') THEN
        ALTER TABLE broker_latency_log ADD COLUMN IF NOT EXISTS broker_id SMALLINT;
        UPDATE broker_latency_log SET broker_id = (SELECT id FROM brokers WHERE name = broker_name)
            WHERE broker_id IS NULL AND broker_name IS NOT NULL;
        ALTER TABLE broker_latency_log ALTER COLUMN broker_id SET NOT NULL;
        ALTER TABLE broker_latency_log DROP COLUMN IF EXISTS broker_name;
        ALTER TABLE broker_latency_log ADD CONSTRAINT fk_broker_latency_log_broker FOREIGN KEY (broker_id) REFERENCES brokers(id);
    END IF;
END $$;

-- ── symbol_data_preferences (if exists) ──────────────────────────────────────
DO $$
BEGIN
    IF EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'symbol_data_preferences') THEN
        ALTER TABLE symbol_data_preferences ADD COLUMN IF NOT EXISTS preferred_broker_id SMALLINT;
        UPDATE symbol_data_preferences SET preferred_broker_id = (SELECT id FROM brokers WHERE name = preferred_broker)
            WHERE preferred_broker_id IS NULL AND preferred_broker IS NOT NULL;
        ALTER TABLE symbol_data_preferences DROP COLUMN IF EXISTS preferred_broker;
        ALTER TABLE symbol_data_preferences ADD CONSTRAINT fk_symbol_prefs_broker FOREIGN KEY (preferred_broker_id) REFERENCES brokers(id);
    END IF;
END $$;

-- ── broker_sessions ─────────────────────────────────────────────────────────
-- This table has broker_name as PK, need to create new structure
DO $$
BEGIN
    IF EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'broker_sessions') THEN
        -- Create new table with broker_id FK
        CREATE TABLE IF NOT EXISTS broker_sessions_new (
            id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            broker_id       SMALLINT NOT NULL REFERENCES brokers(id),
            user_id         UUID REFERENCES users(id) ON DELETE CASCADE,
            session_token   VARCHAR(500),
            expires_at      TIMESTAMPTZ,
            is_active       BOOLEAN NOT NULL DEFAULT true,
            created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            CONSTRAINT uq_broker_sessions_user_broker UNIQUE (user_id, broker_id)
        );

        -- Migrate data if broker_sessions has any rows
        INSERT INTO broker_sessions_new (broker_id, session_token, expires_at, is_active, created_at, updated_at)
        SELECT
            (SELECT id FROM brokers WHERE name = bs.broker_name),
            bs.session_token,
            bs.expires_at,
            bs.is_active,
            bs.created_at,
            bs.updated_at
        FROM broker_sessions bs
        ON CONFLICT DO NOTHING;

        -- Drop old table and rename new one
        DROP TABLE broker_sessions;
        ALTER TABLE broker_sessions_new RENAME TO broker_sessions;

        CREATE INDEX idx_broker_sessions_user ON broker_sessions (user_id) WHERE is_active = true;
    END IF;
END $$;
