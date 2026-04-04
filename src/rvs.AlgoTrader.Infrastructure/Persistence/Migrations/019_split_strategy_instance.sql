-- Migration 019: Split StrategyInstance into StrategyInstance, StrategyRuntimeState, and BrokerCredential
-- Refactoring to follow Single Responsibility Principle (Issue #7)

-- ─────────────────────────────────────────────────────────────────────────────────
-- 1. Create strategy_runtime_states table
-- ─────────────────────────────────────────────────────────────────────────────────

CREATE TABLE strategy_runtime_states
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

CREATE INDEX ix_strategy_runtime_states_current_run_id ON strategy_runtime_states(current_run_id);

-- ─────────────────────────────────────────────────────────────────────────────────
-- 2. Create broker_credentials table
-- ─────────────────────────────────────────────────────────────────────────────────

CREATE TABLE broker_credentials
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
-- 3. Migrate data from strategy_instances to new tables
-- ─────────────────────────────────────────────────────────────────────────────────

-- Insert into strategy_runtime_states (copy operational/runtime state)
INSERT INTO strategy_runtime_states (
    strategy_instance_id,
    current_run_id,
    today_realized_pnl,
    today_unrealized_pnl,
    auto_resume_on_restart,
    created_at,
    updated_at
)
SELECT
    id,
    "CurrentRunId",
    today_realized_pnl,
    today_unrealized_pnl,
    auto_resume_on_restart,
    created_at,
    updated_at
FROM strategy_instances;

-- Insert into broker_credentials (copy broker configuration)
INSERT INTO broker_credentials (
    strategy_instance_id,
    broker_token,
    exchange,
    product_type,
    lot_size
)
SELECT
    id,
    broker_token,
    exchange,
    product_type,
    lot_size
FROM strategy_instances;

-- ─────────────────────────────────────────────────────────────────────────────────
-- 4. Remove columns from strategy_instances (these are now in related tables)
-- ─────────────────────────────────────────────────────────────────────────────────

ALTER TABLE strategy_instances
    DROP COLUMN "CurrentRunId",
    DROP COLUMN allocated_capital,
    DROP COLUMN today_realized_pnl,
    DROP COLUMN today_unrealized_pnl,
    DROP COLUMN auto_resume_on_restart,
    DROP COLUMN broker_token,
    DROP COLUMN exchange,
    DROP COLUMN product_type,
    DROP COLUMN lot_size;
