-- Migration 021: Fix high-priority schema issues (#203, #202, #201, #197, #192)
-- Phase 2: Referential integrity and schema conflicts
-- PostgreSQL version

-- Drop the bad instrument_universe→instruments FK if it was applied in a previous partial run.
-- See comment below for why this constraint must not exist.
DO $$ BEGIN
    IF EXISTS (SELECT FROM information_schema.table_constraints WHERE constraint_name = 'fk_instrument_universe_instruments') THEN
        ALTER TABLE instrument_universe DROP CONSTRAINT fk_instrument_universe_instruments;
    END IF;
END $$;

-- #201: Make internal_symbol nullable for watchlist mode
ALTER TABLE strategy_instances
ALTER COLUMN internal_symbol DROP NOT NULL;

-- Add watchlist_id column (was missing from InitialMigration.sql)
ALTER TABLE strategy_instances
ADD COLUMN IF NOT EXISTS watchlist_id UUID;

DO $$ BEGIN
    ALTER TABLE strategy_instances
    ADD CONSTRAINT ck_strategy_instances_symbol_or_watchlist
    CHECK ((internal_symbol IS NOT NULL) OR (watchlist_id IS NOT NULL));
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

-- #197: Add missing foreign key relationships

-- Add strategy_instance_id to backtest_runs (nullable for historical runs)
DO $$ BEGIN
    IF NOT EXISTS (SELECT FROM information_schema.columns WHERE table_name = 'backtest_runs' AND column_name = 'strategy_instance_id') THEN
        ALTER TABLE backtest_runs
        ADD COLUMN strategy_instance_id UUID;

        ALTER TABLE backtest_runs
        ADD CONSTRAINT fk_backtest_runs_strategy_instance
        FOREIGN KEY (strategy_instance_id) REFERENCES strategy_instances(id) ON DELETE SET NULL;
    END IF;
END $$;

-- FK from instrument_universe to instruments intentionally omitted.
-- instrument_universe is the full tradeable universe (broad); instruments are actively
-- tracked records. Not every universe symbol has a corresponding instruments row —
-- enforcing this FK causes 23503 violations on normal data inserts (e.g. TATAINVEST).

-- #192: Add 14 missing foreign key constraints

-- backtest_runs -> strategy_scenarios
DO $$ BEGIN
    IF NOT EXISTS (SELECT FROM information_schema.table_constraints WHERE constraint_name = 'fk_backtest_runs_scenario') THEN
        ALTER TABLE backtest_runs
        ADD CONSTRAINT fk_backtest_runs_scenario
        FOREIGN KEY (scenario_id) REFERENCES strategy_scenarios(id) ON DELETE SET NULL;
    END IF;
END $$;

-- capital_allocations -> strategy_instances
DO $$ BEGIN
    IF NOT EXISTS (SELECT FROM information_schema.table_constraints WHERE constraint_name = 'fk_capital_allocations_strategy_instance') THEN
        ALTER TABLE capital_allocations
        ADD CONSTRAINT fk_capital_allocations_strategy_instance
        FOREIGN KEY (strategy_instance_id) REFERENCES strategy_instances(id) ON DELETE CASCADE;
    END IF;
END $$;

-- orders -> strategy_runs
DO $$ BEGIN
    IF NOT EXISTS (SELECT FROM information_schema.table_constraints WHERE constraint_name = 'fk_orders_strategy_run') THEN
        ALTER TABLE orders
        ADD CONSTRAINT fk_orders_strategy_run
        FOREIGN KEY (strategy_run_id) REFERENCES strategy_runs(id) ON DELETE CASCADE;
    END IF;
END $$;

-- positions -> strategy_runs
DO $$ BEGIN
    IF NOT EXISTS (SELECT FROM information_schema.table_constraints WHERE constraint_name = 'fk_positions_strategy_run') THEN
        ALTER TABLE positions
        ADD CONSTRAINT fk_positions_strategy_run
        FOREIGN KEY (strategy_run_id) REFERENCES strategy_runs(id) ON DELETE CASCADE;
    END IF;
END $$;

-- strategy_instances -> risk_profiles
DO $$ BEGIN
    IF NOT EXISTS (SELECT FROM information_schema.table_constraints WHERE constraint_name = 'fk_strategy_instances_risk_profile') THEN
        ALTER TABLE strategy_instances
        ADD CONSTRAINT fk_strategy_instances_risk_profile
        FOREIGN KEY (risk_profile_id) REFERENCES risk_profiles(id) ON DELETE SET NULL;
    END IF;
END $$;

-- strategy_instances -> watchlists
DO $$ BEGIN
    IF NOT EXISTS (SELECT FROM information_schema.table_constraints WHERE constraint_name = 'fk_strategy_instances_watchlist') THEN
        ALTER TABLE strategy_instances
        ADD CONSTRAINT fk_strategy_instances_watchlist
        FOREIGN KEY (watchlist_id) REFERENCES watchlists(id) ON DELETE SET NULL;
    END IF;
END $$;

-- strategy_scenarios -> strategy_instances
DO $$ BEGIN
    IF NOT EXISTS (SELECT FROM information_schema.table_constraints WHERE constraint_name = 'fk_strategy_scenarios_strategy_instance') THEN
        ALTER TABLE strategy_scenarios
        ADD CONSTRAINT fk_strategy_scenarios_strategy_instance
        FOREIGN KEY (strategy_instance_id) REFERENCES strategy_instances(id) ON DELETE CASCADE;
    END IF;
END $$;

-- forward_test_sessions -> strategy_instances
DO $$ BEGIN
    IF NOT EXISTS (SELECT FROM information_schema.table_constraints WHERE constraint_name = 'fk_forward_test_sessions_strategy_instance') THEN
        ALTER TABLE forward_test_sessions
        ADD CONSTRAINT fk_forward_test_sessions_strategy_instance
        FOREIGN KEY (strategy_instance_id) REFERENCES strategy_instances(id) ON DELETE CASCADE;
    END IF;
END $$;

-- forward_test_trades -> forward_test_sessions
DO $$ BEGIN
    IF NOT EXISTS (SELECT FROM information_schema.table_constraints WHERE constraint_name = 'fk_forward_test_trades_session') THEN
        ALTER TABLE forward_test_trades
        ADD CONSTRAINT fk_forward_test_trades_session
        FOREIGN KEY (session_id) REFERENCES forward_test_sessions(id) ON DELETE CASCADE;
    END IF;
END $$;

-- strategy_runs -> strategy_instances
DO $$ BEGIN
    IF NOT EXISTS (SELECT FROM information_schema.table_constraints WHERE constraint_name = 'fk_strategy_runs_strategy_instance') THEN
        ALTER TABLE strategy_runs
        ADD CONSTRAINT fk_strategy_runs_strategy_instance
        FOREIGN KEY (strategy_instance_id) REFERENCES strategy_instances(id) ON DELETE CASCADE;
    END IF;
END $$;

-- trade_journal_entries -> strategy_instances
DO $$ BEGIN
    IF NOT EXISTS (SELECT FROM information_schema.table_constraints WHERE constraint_name = 'fk_trade_journal_entries_strategy_instance') THEN
        ALTER TABLE trade_journal_entries
        ADD CONSTRAINT fk_trade_journal_entries_strategy_instance
        FOREIGN KEY (strategy_instance_id) REFERENCES strategy_instances(id) ON DELETE CASCADE;
    END IF;
END $$;

-- #203: Address overlapping PnL columns
-- Note: Requires manual code review to determine which column is authoritative

-- #202: Document and enforce average_price consistency
-- Note: This is a business logic constraint; add documentation in code comments
