-- Migration 029: Restore columns dropped or never added to strategy_instances.
--
-- Migration 019 dropped allocated_capital when splitting StrategyInstance into
-- StrategyRuntimeState + BrokerCredential. However the StrategyInstance entity
-- and capital_allocations table serve different purposes:
--   • capital_allocations.allocated_capital  – per-run reservation tracking (book-keeping)
--   • strategy_instances.allocated_capital   – the configured budget for this strategy
-- EF Core still maps StrategyInstance.AllocatedCapital → strategy_instances.allocated_capital,
-- so the column must exist.
--
-- is_active and config_json were also never added via any migration.

ALTER TABLE strategy_instances
    ADD COLUMN IF NOT EXISTS allocated_capital NUMERIC(18,4) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS is_active         BOOL         NOT NULL DEFAULT true,
    ADD COLUMN IF NOT EXISTS config_json       JSONB        NOT NULL DEFAULT '{}',
    ADD COLUMN IF NOT EXISTS created_by        VARCHAR(200) NOT NULL DEFAULT '';

-- Seed is_active from status: anything not Stopped/Error is considered active.
UPDATE strategy_instances
SET is_active = (status NOT IN ('Stopped', 'Error'))
WHERE is_active = true;   -- only touch rows that still carry the default
