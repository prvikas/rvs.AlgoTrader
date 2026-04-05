-- Rollback for migration 026_fk_constraints_backtest_scenario.sql
-- Removes FK constraints between backtest_runs ↔ strategy_scenarios.

-- ── Drop FK: strategy_scenarios.last_backtest_run_id → backtest_runs(id) ─
DO $$ BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'fk_strategy_scenarios_last_backtest'
    ) THEN
        ALTER TABLE strategy_scenarios DROP CONSTRAINT fk_strategy_scenarios_last_backtest;
    END IF;
END $$;

-- ── Drop FK: backtest_runs.scenario_id → strategy_scenarios(id) ──────────
DO $$ BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'fk_backtest_runs_scenario'
    ) THEN
        ALTER TABLE backtest_runs DROP CONSTRAINT fk_backtest_runs_scenario;
    END IF;
END $$;

DELETE FROM schema_migrations WHERE file_name = '026_fk_constraints_backtest_scenario.sql';
