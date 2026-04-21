-- Migration 042: Re-target backtest_runs.scenario_id FK
--
-- The new Strategy Definitions flow stores scenarios in strategy_definition_scenarios
-- (migration 037), not strategy_scenarios (migration 007).  Backtest runs triggered
-- from the UI now carry IDs from strategy_definition_scenarios, causing FK violation
-- on the old fk_backtest_runs_scenario constraint.
--
-- Fix: drop the old FK (→ strategy_scenarios) and add a new one (→ strategy_definition_scenarios).
-- Existing rows that reference strategy_scenarios are nullified first to avoid orphan errors.

-- 1. Nullify backtest_runs.scenario_id rows that reference the OLD strategy_scenarios table.
UPDATE backtest_runs
SET    scenario_id = NULL
WHERE  scenario_id IS NOT NULL
  AND  scenario_id NOT IN (SELECT id FROM strategy_definition_scenarios);

-- 2. Drop the old FK constraint.
DO $$ BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'fk_backtest_runs_scenario'
          AND table_name      = 'backtest_runs'
    ) THEN
        ALTER TABLE backtest_runs DROP CONSTRAINT fk_backtest_runs_scenario;
    END IF;
END $$;

-- 3. Add new FK pointing to strategy_definition_scenarios.
DO $$ BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'fk_backtest_runs_definition_scenario'
          AND table_name      = 'backtest_runs'
    ) THEN
        ALTER TABLE backtest_runs
            ADD CONSTRAINT fk_backtest_runs_definition_scenario
            FOREIGN KEY (scenario_id)
            REFERENCES strategy_definition_scenarios(id)
            ON DELETE SET NULL
            DEFERRABLE INITIALLY DEFERRED;
    END IF;
END $$;
