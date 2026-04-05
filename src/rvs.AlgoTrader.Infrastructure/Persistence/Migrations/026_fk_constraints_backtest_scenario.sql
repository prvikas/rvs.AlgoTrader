-- Migration 026: Fix #173 and #174 — missing FK constraints between
--                backtest_runs ↔ strategy_scenarios.
--
-- Context:
--   backtest_runs.scenario_id          → strategy_scenarios(id)  [#174]
--   strategy_scenarios.last_backtest_run_id → backtest_runs(id)  [#173]
--
-- These two columns form a circular reference. Both FKs are nullable (no
-- existing row is forced to have a match) and declared DEFERRABLE INITIALLY
-- DEFERRED so PostgreSQL validates them at transaction COMMIT rather than at
-- each statement — necessary because both tables can be written in the same
-- transaction (e.g. when recording a backtest result and updating the scenario).
--
-- Cleanup: nullify any orphaned references before adding the constraints so
-- the migration is safe to run against a DB that already has data.

-- ── Pre-flight: nullify orphaned scenario_id in backtest_runs ─────────────
UPDATE backtest_runs
SET    scenario_id = NULL
WHERE  scenario_id IS NOT NULL
  AND  scenario_id NOT IN (SELECT id FROM strategy_scenarios);

-- ── Pre-flight: nullify orphaned last_backtest_run_id in strategy_scenarios ─
UPDATE strategy_scenarios
SET    last_backtest_run_id = NULL
WHERE  last_backtest_run_id IS NOT NULL
  AND  last_backtest_run_id NOT IN (SELECT id FROM backtest_runs);

-- ── #174: backtest_runs.scenario_id → strategy_scenarios(id) ─────────────
DO $$ BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'fk_backtest_runs_scenario'
          AND table_name = 'backtest_runs'
    ) THEN
        ALTER TABLE backtest_runs
            ADD CONSTRAINT fk_backtest_runs_scenario
            FOREIGN KEY (scenario_id)
            REFERENCES strategy_scenarios(id)
            ON DELETE SET NULL
            DEFERRABLE INITIALLY DEFERRED;
    END IF;
END $$;

-- ── #173: strategy_scenarios.last_backtest_run_id → backtest_runs(id) ────
DO $$ BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'fk_strategy_scenarios_last_backtest'
          AND table_name = 'strategy_scenarios'
    ) THEN
        ALTER TABLE strategy_scenarios
            ADD CONSTRAINT fk_strategy_scenarios_last_backtest
            FOREIGN KEY (last_backtest_run_id)
            REFERENCES backtest_runs(id)
            ON DELETE SET NULL
            DEFERRABLE INITIALLY DEFERRED;
    END IF;
END $$;
