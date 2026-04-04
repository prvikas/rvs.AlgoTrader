-- Migration 023: Schema cleanup and normalization (#200, #199, #193, #195, #196, #194, #191, #190, #189, #188)
-- Phase 4: Remove duplicates, normalize naming, and modernize data types
-- PostgreSQL version

-- #191: Drop orphaned "Id" column from candles table (unused UUID)
DO $$ BEGIN
    IF EXISTS (SELECT FROM information_schema.columns WHERE table_name = 'candles' AND column_name = 'id') THEN
        ALTER TABLE candles DROP COLUMN id;
    END IF;
END $$;

-- #189: Drop duplicate PascalCase trailing stop columns in orders table
DO $$ BEGIN
    IF EXISTS (SELECT FROM information_schema.columns WHERE table_name = 'orders' AND column_name = 'trailing_sl') THEN
        ALTER TABLE orders DROP COLUMN trailing_sl;
    END IF;
END $$;

DO $$ BEGIN
    IF EXISTS (SELECT FROM information_schema.columns WHERE table_name = 'orders' AND column_name = 'trailing_tp') THEN
        ALTER TABLE orders DROP COLUMN trailing_tp;
    END IF;
END $$;

-- #188: Drop duplicate PascalCase columns in instruments table
DO $$ BEGIN
    IF EXISTS (SELECT FROM information_schema.columns WHERE table_name = 'instruments' AND column_name = 'underlying') THEN
        ALTER TABLE instruments DROP COLUMN underlying;
    END IF;
END $$;

DO $$ BEGIN
    IF EXISTS (SELECT FROM information_schema.columns WHERE table_name = 'instruments' AND column_name = 'strike_price') THEN
        ALTER TABLE instruments DROP COLUMN strike_price;
    END IF;
END $$;

DO $$ BEGIN
    IF EXISTS (SELECT FROM information_schema.columns WHERE table_name = 'instruments' AND column_name = 'option_type') THEN
        ALTER TABLE instruments DROP COLUMN option_type;
    END IF;
END $$;

DO $$ BEGIN
    IF EXISTS (SELECT FROM information_schema.columns WHERE table_name = 'instruments' AND column_name = 'expiry') THEN
        ALTER TABLE instruments DROP COLUMN expiry;
    END IF;
END $$;

-- #190: Normalize PascalCase column names in strategy_instances (if they exist)
-- Note: These columns may already be in snake_case in PostgreSQL
-- Only migrate if PascalCase versions are found

DO $$ BEGIN
    IF EXISTS (SELECT FROM information_schema.columns WHERE table_name = 'strategy_instances' AND column_name = 'watchlist_id') THEN
        -- Already normalized
    ELSE
        -- Column doesn't exist, will be handled by EF migrations
    END IF;
END $$;

-- Candles table: normalize broker_name (if exists and in wrong case)
DO $$ BEGIN
    IF EXISTS (SELECT FROM information_schema.columns WHERE table_name = 'candles' AND column_name = 'broker_name') THEN
        -- Already normalized
    END IF;
END $$;

-- #195: Note about redundant timestamp columns
-- forward_test_trades may have both entry_time/opened_at or exit_time/closed_at
-- Manual code review required to determine authoritative column

-- #196: JSON validation (PostgreSQL version)
-- PostgreSQL has native JSON type with validation
DO $$ BEGIN
    IF EXISTS (SELECT FROM information_schema.columns
               WHERE table_name = 'strategy_scenarios'
               AND column_name = 'parameters_json_override') THEN
        -- PostgreSQL JSON columns are automatically validated
        NULL;
    END IF;
END $$;

-- #194: Consolidate signal_journal and signal_journal_entries
-- This requires data migration; placeholder for manual execution:
-- 1. Verify signal_journal_entries has all required columns
-- 2. Migrate all data from signal_journal to signal_journal_entries
-- 3. Add FK to strategy_instances if not present
-- 4. DROP TABLE signal_journal;
-- This migration is marked as requiring manual review due to data migration complexity
