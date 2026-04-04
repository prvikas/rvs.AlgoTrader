-- Migration 022: Fix medium-priority constraints (#208, #207, #206)
-- Phase 3: Performance and uniqueness constraints

-- #208: Broker sessions - add expiry validation and index

-- Add CHECK constraint for expiry validation
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.CONSTRAINTS WHERE CONSTRAINT_NAME='ck_broker_sessions_expiry')
BEGIN
    ALTER TABLE broker_sessions
    ADD CONSTRAINT ck_broker_sessions_expiry CHECK (expires_at > stored_at);
END

-- Create filtered index on expires_at for efficient token expiry queries
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name='ix_broker_sessions_expires_at')
BEGIN
    CREATE NONCLUSTERED INDEX ix_broker_sessions_expires_at
    ON broker_sessions(expires_at)
    WHERE expires_at IS NOT NULL;
END

-- #207: Strategy scenarios - ensure unique scenario names per instance

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name='uix_strategy_scenarios_instance_name')
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX uix_strategy_scenarios_instance_name
    ON strategy_scenarios(strategy_instance_id, name);
END

-- #206: Backtest runs - deduplicate identical scenario runs via data_hash

-- First, ensure data_hash column exists (may need to be added separately if missing)
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='backtest_runs' AND COLUMN_NAME='data_hash')
BEGIN
    ALTER TABLE backtest_runs
    ADD data_hash VARCHAR(64) NULL;

    -- Optional: Populate data_hash with scenario_id + date range hash
    -- UPDATE backtest_runs SET data_hash = CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', CONCAT(scenario_id, from_date, to_date)), 2)
    -- WHERE data_hash IS NULL;
END

-- Create partial unique index to prevent duplicate runs
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name='uix_backtest_runs_scenario_data_hash')
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX uix_backtest_runs_scenario_data_hash
    ON backtest_runs(scenario_id, data_hash)
    WHERE scenario_id IS NOT NULL AND data_hash IS NOT NULL;
END
