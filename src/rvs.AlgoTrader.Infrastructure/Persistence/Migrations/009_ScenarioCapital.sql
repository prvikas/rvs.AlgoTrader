-- 009_ScenarioCapital.sql
-- Each scenario may deploy a specific capital amount rather than sharing the strategy total.
ALTER TABLE strategy_scenarios
    ADD COLUMN IF NOT EXISTS allocated_capital NUMERIC(18, 4);
