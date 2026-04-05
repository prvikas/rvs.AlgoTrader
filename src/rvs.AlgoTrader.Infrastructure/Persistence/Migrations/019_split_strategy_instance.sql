-- Migration 019: Split StrategyInstance into StrategyInstance, StrategyRuntimeState, and BrokerCredential
-- Refactoring to follow Single Responsibility Principle (Issue #7)
-- All operations are idempotent (IF NOT EXISTS / IF EXISTS / ON CONFLICT DO NOTHING).

-- ─────────────────────────────────────────────────────────────────────────────────
-- 1. Create strategy_runtime_states table
-- ─────────────────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS strategy_runtime_states
(
    strategy_instance_id UUID NOT NULL PRIMARY KEY,
    current_run_id UUID,
    today_realized_pnl DECIMAL(18, 4) NOT NULL DEFAULT 0,
    today_unrealized_pnl DECIMAL(18, 4) NOT NULL DEFAULT 0,
    auto_resume_on_restart BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT fk_strategy_runtime_states_instance
        FOREIGN KEY (strategy_instance_id) REFERENCES strategy_instances(id)
        ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_strategy_runtime_states_current_run_id ON strategy_runtime_states(current_run_id);

-- ─────────────────────────────────────────────────────────────────────────────────
-- 2. Create broker_credentials table
-- ─────────────────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS broker_credentials
(
    strategy_instance_id UUID NOT NULL PRIMARY KEY,
    broker_token VARCHAR(100),
    exchange VARCHAR(10) NOT NULL DEFAULT 'NSE',
    product_type VARCHAR(10) NOT NULL DEFAULT 'MIS',
    lot_size INT NOT NULL DEFAULT 1,
    CONSTRAINT fk_broker_credentials_instance
        FOREIGN KEY (strategy_instance_id) REFERENCES strategy_instances(id)
        ON DELETE CASCADE
);

-- ─────────────────────────────────────────────────────────────────────────────────
-- 3. Migrate data — columns on strategy_instances may already be dropped if this
--    migration previously ran partially. Use dynamic SQL to check column existence.
-- ─────────────────────────────────────────────────────────────────────────────────

DO $$
DECLARE
    has_pnl   BOOLEAN;
    has_resume BOOLEAN;
BEGIN
    SELECT COUNT(*) > 0 INTO has_pnl
    FROM information_schema.columns
    WHERE table_name = 'strategy_instances' AND column_name = 'today_realized_pnl';

    SELECT COUNT(*) > 0 INTO has_resume
    FROM information_schema.columns
    WHERE table_name = 'strategy_instances' AND column_name = 'auto_resume_on_restart';

    IF has_pnl AND has_resume THEN
        INSERT INTO strategy_runtime_states (
            strategy_instance_id, current_run_id,
            today_realized_pnl, today_unrealized_pnl,
            auto_resume_on_restart, created_at, updated_at
        )
        SELECT id, NULL,
               today_realized_pnl, today_unrealized_pnl,
               auto_resume_on_restart, created_at, updated_at
        FROM strategy_instances
        WHERE id NOT IN (SELECT strategy_instance_id FROM strategy_runtime_states);
    ELSE
        -- Columns already dropped — seed with defaults for any un-migrated rows
        INSERT INTO strategy_runtime_states (
            strategy_instance_id, current_run_id,
            today_realized_pnl, today_unrealized_pnl,
            auto_resume_on_restart, created_at, updated_at
        )
        SELECT id, NULL, 0, 0, FALSE, created_at, updated_at
        FROM strategy_instances
        WHERE id NOT IN (SELECT strategy_instance_id FROM strategy_runtime_states);
    END IF;
END $$;

DO $$
DECLARE
    has_token BOOLEAN;
    has_exchange BOOLEAN;
BEGIN
    SELECT COUNT(*) > 0 INTO has_token
    FROM information_schema.columns
    WHERE table_name = 'strategy_instances' AND column_name = 'broker_token';

    SELECT COUNT(*) > 0 INTO has_exchange
    FROM information_schema.columns
    WHERE table_name = 'strategy_instances' AND column_name = 'exchange';

    IF has_token AND has_exchange THEN
        INSERT INTO broker_credentials (strategy_instance_id, broker_token, exchange, product_type, lot_size)
        SELECT id, broker_token, exchange, product_type, lot_size
        FROM strategy_instances
        WHERE id NOT IN (SELECT strategy_instance_id FROM broker_credentials);
    ELSE
        INSERT INTO broker_credentials (strategy_instance_id, broker_token, exchange, product_type, lot_size)
        SELECT id, NULL, 'NSE', 'MIS', 1
        FROM strategy_instances
        WHERE id NOT IN (SELECT strategy_instance_id FROM broker_credentials);
    END IF;
END $$;

-- ─────────────────────────────────────────────────────────────────────────────────
-- 4. Remove columns from strategy_instances (IF EXISTS — safe to re-run)
-- ─────────────────────────────────────────────────────────────────────────────────

ALTER TABLE strategy_instances
    DROP COLUMN IF EXISTS "CurrentRunId",
    DROP COLUMN IF EXISTS allocated_capital,
    DROP COLUMN IF EXISTS today_realized_pnl,
    DROP COLUMN IF EXISTS today_unrealized_pnl,
    DROP COLUMN IF EXISTS auto_resume_on_restart,
    DROP COLUMN IF EXISTS broker_token,
    DROP COLUMN IF EXISTS exchange,
    DROP COLUMN IF EXISTS product_type,
    DROP COLUMN IF EXISTS lot_size;
