-- Migration 037: strategy_definition_scenarios
--
-- Stores backtest scenarios owned directly by a strategy_definition (GenericRules).
-- This is intentionally separate from strategy_scenarios (which are owned by
-- strategy_instances). A definition scenario captures: what to test + when + with how
-- much capital. It does not require an active deployment.
--
-- Lifecycle: Draft → Running → Backtested → FwdTesting → Live | Archived

CREATE TABLE IF NOT EXISTS strategy_definition_scenarios (
    id                      UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    strategy_definition_id  UUID         NOT NULL
                            REFERENCES strategy_definitions(id) ON DELETE CASCADE,
    name                    TEXT         NOT NULL,
    description             TEXT,
    capital                 NUMERIC(18,4) NOT NULL DEFAULT 100000,
    broker_account          TEXT         NOT NULL DEFAULT 'MStock',
    backtest_from           DATE         NOT NULL DEFAULT '2023-01-01',
    backtest_to             DATE         NOT NULL DEFAULT CURRENT_DATE,
    -- ParameterOverride[] from the frontend — stored as JSONB array
    parameter_overrides     JSONB        NOT NULL DEFAULT '[]',
    status                  TEXT         NOT NULL DEFAULT 'Draft',
    last_run_at             TIMESTAMPTZ,
    -- RunMetrics JSON from the latest completed backtest
    last_metrics            JSONB,
    created_at              TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at              TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_def_scenarios_definition_id
    ON strategy_definition_scenarios (strategy_definition_id);
