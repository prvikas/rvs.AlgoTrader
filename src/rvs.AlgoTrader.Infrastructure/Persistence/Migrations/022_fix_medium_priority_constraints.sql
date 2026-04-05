-- Migration 022: Fix medium-priority constraints (#208, #207, #206)
-- Phase 3: Performance and uniqueness constraints
-- PostgreSQL version

-- #208: Broker sessions - add expiry validation and index

-- Add CHECK constraint for expiry validation
DO $$ BEGIN
    IF NOT EXISTS (SELECT FROM information_schema.table_constraints WHERE constraint_name = 'ck_broker_sessions_expiry') THEN
        ALTER TABLE broker_sessions
        ADD CONSTRAINT ck_broker_sessions_expiry CHECK (expires_at > stored_at);
    END IF;
END $$;

-- Create index on expires_at for efficient token expiry queries
DO $$ BEGIN
    IF NOT EXISTS (SELECT FROM pg_indexes WHERE indexname = 'ix_broker_sessions_expires_at') THEN
        CREATE INDEX ix_broker_sessions_expires_at
        ON broker_sessions(expires_at)
        WHERE expires_at IS NOT NULL;
    END IF;
END $$;

-- #207: Strategy scenarios - ensure unique scenario names per instance

DO $$ BEGIN
    IF NOT EXISTS (SELECT FROM pg_indexes WHERE indexname = 'uix_strategy_scenarios_instance_name') THEN
        CREATE UNIQUE INDEX uix_strategy_scenarios_instance_name
        ON strategy_scenarios(strategy_instance_id, name);
    END IF;
END $$;

-- #206: Backtest runs - deduplicate identical scenario runs via data_hash

-- First, ensure data_hash column exists
DO $$ BEGIN
    IF NOT EXISTS (SELECT FROM information_schema.columns WHERE table_name = 'backtest_runs' AND column_name = 'data_hash') THEN
        ALTER TABLE backtest_runs
        ADD COLUMN data_hash VARCHAR(64);
    END IF;
END $$;

-- Create partial unique index to prevent duplicate runs
DO $$ BEGIN
    IF NOT EXISTS (SELECT FROM pg_indexes WHERE indexname = 'uix_backtest_runs_scenario_data_hash') THEN
        CREATE UNIQUE INDEX uix_backtest_runs_scenario_data_hash
        ON backtest_runs(scenario_id, data_hash)
        WHERE scenario_id IS NOT NULL AND data_hash IS NOT NULL;
    END IF;
END $$;
