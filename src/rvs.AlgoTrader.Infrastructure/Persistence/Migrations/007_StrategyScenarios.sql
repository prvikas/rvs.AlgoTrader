-- 007_StrategyScenarios.sql
-- Adds strategy_scenarios table and scenario tracking columns to backtest_runs.
-- Idempotent — safe to run multiple times.

-- ── strategy_scenarios ────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS strategy_scenarios (
    id                       UUID         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    strategy_instance_id     UUID         NOT NULL REFERENCES strategy_instances(id) ON DELETE CASCADE,
    name                     TEXT         NOT NULL,
    description              TEXT,
    parameters_json_override TEXT,
    status                   TEXT         NOT NULL DEFAULT 'Draft',
    last_backtest_run_id     UUID,
    created_at               TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at               TIMESTAMPTZ  NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_strategy_scenarios_instance
    ON strategy_scenarios (strategy_instance_id);

-- ── backtest_runs — scenario tracking columns ─────────────────────────────────
ALTER TABLE backtest_runs
    ADD COLUMN IF NOT EXISTS scenario_id               UUID,
    ADD COLUMN IF NOT EXISTS effective_parameters_json TEXT;

CREATE INDEX IF NOT EXISTS ix_backtest_runs_scenario
    ON backtest_runs (scenario_id)
    WHERE scenario_id IS NOT NULL;
