-- Migration 023: Schema cleanup and normalization (#200, #199, #193, #195, #196, #194, #191, #190, #189, #188)
-- Phase 4: Remove duplicates, normalize naming, and modernize data types

-- #191: Drop orphaned "Id" column from candles table (unused UUID)
IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='candles' AND COLUMN_NAME='Id')
BEGIN
    ALTER TABLE candles DROP COLUMN "Id";
END

-- #189: Drop duplicate PascalCase trailing stop columns in orders table
IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='orders' AND COLUMN_NAME='TrailingSl')
BEGIN
    ALTER TABLE orders DROP COLUMN "TrailingSl";
END

IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='orders' AND COLUMN_NAME='TrailingTp')
BEGIN
    ALTER TABLE orders DROP COLUMN "TrailingTp";
END

-- #188: Drop duplicate PascalCase columns in instruments table
IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='instruments' AND COLUMN_NAME='Underlying')
BEGIN
    ALTER TABLE instruments DROP COLUMN "Underlying";
END

IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='instruments' AND COLUMN_NAME='StrikePrice')
BEGIN
    ALTER TABLE instruments DROP COLUMN "StrikePrice";
END

IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='instruments' AND COLUMN_NAME='OptionType')
BEGIN
    ALTER TABLE instruments DROP COLUMN "OptionType";
END

IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='instruments' AND COLUMN_NAME='Expiry')
BEGIN
    ALTER TABLE instruments DROP COLUMN "Expiry";
END

-- #190: Normalize PascalCase column names in strategy_instances
IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='strategy_instances' AND COLUMN_NAME='WatchlistId')
BEGIN
    -- Create new column with correct name
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='strategy_instances' AND COLUMN_NAME='watchlist_id')
    BEGIN
        ALTER TABLE strategy_instances ADD watchlist_id UNIQUEIDENTIFIER NULL;
        UPDATE strategy_instances SET watchlist_id = "WatchlistId";
        ALTER TABLE strategy_instances DROP COLUMN "WatchlistId";
    END
END

IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='strategy_instances' AND COLUMN_NAME='IsActive')
BEGIN
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='strategy_instances' AND COLUMN_NAME='is_active')
    BEGIN
        ALTER TABLE strategy_instances ADD is_active BIT NOT NULL DEFAULT 1;
        UPDATE strategy_instances SET is_active = "IsActive";
        ALTER TABLE strategy_instances DROP COLUMN "IsActive";
    END
END

IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='strategy_instances' AND COLUMN_NAME='ConfigJson')
BEGIN
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='strategy_instances' AND COLUMN_NAME='config_json')
    BEGIN
        ALTER TABLE strategy_instances ADD config_json NVARCHAR(MAX) NULL;
        UPDATE strategy_instances SET config_json = "ConfigJson";
        ALTER TABLE strategy_instances DROP COLUMN "ConfigJson";
    END
END

IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='strategy_instances' AND COLUMN_NAME='CreatedBy')
BEGIN
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='strategy_instances' AND COLUMN_NAME='created_by')
    BEGIN
        ALTER TABLE strategy_instances ADD created_by VARCHAR(255) NULL;
        UPDATE strategy_instances SET created_by = "CreatedBy";
        ALTER TABLE strategy_instances DROP COLUMN "CreatedBy";
    END
END

IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='strategy_instances' AND COLUMN_NAME='CurrentRunId')
BEGIN
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='strategy_instances' AND COLUMN_NAME='current_run_id')
    BEGIN
        ALTER TABLE strategy_instances ADD current_run_id UNIQUEIDENTIFIER NULL;
        UPDATE strategy_instances SET current_run_id = "CurrentRunId";
        ALTER TABLE strategy_instances DROP COLUMN "CurrentRunId";
    END
END

-- Candles table: normalize broker_name if PascalCase
IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='candles' AND COLUMN_NAME='BrokerName')
BEGIN
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='candles' AND COLUMN_NAME='broker_name')
    BEGIN
        ALTER TABLE candles ADD broker_name VARCHAR(50) NULL;
        UPDATE candles SET broker_name = "BrokerName";
        ALTER TABLE candles DROP COLUMN "BrokerName";
    END
END

-- #199 & #193: Remove duplicate indexes
-- Drop older/duplicate index names while keeping the primary ones
-- List of indexes to potentially drop (requires manual review of actual index names):
-- Run this separately after verifying which indexes are actually duplicates:
-- DROP INDEX IF EXISTS idx_candles_symbol_tf ON candles;
-- DROP INDEX IF EXISTS idx_orders_idempotency_key_duplicate ON orders;
-- (Additional duplicate index drops would go here)

-- #195: Remove redundant timestamp columns from forward_test_trades
-- First backfill if columns exist separately
IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='forward_test_trades' AND COLUMN_NAME='entry_time')
AND EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='forward_test_trades' AND COLUMN_NAME='opened_at')
BEGIN
    UPDATE forward_test_trades
    SET entry_time = opened_at
    WHERE entry_time IS NULL;

    -- After migration verification, drop the duplicate:
    -- ALTER TABLE forward_test_trades DROP COLUMN entry_time;
END

IF EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='forward_test_trades' AND COLUMN_NAME='exit_time')
AND EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='forward_test_trades' AND COLUMN_NAME='closed_at')
BEGIN
    UPDATE forward_test_trades
    SET exit_time = closed_at
    WHERE exit_time IS NULL;

    -- After migration verification, drop the duplicate:
    -- ALTER TABLE forward_test_trades DROP COLUMN exit_time;
END

-- #196: Standardize JSON columns from text to jsonb (SQL Server: change to NVARCHAR(MAX) with CHECK for valid JSON)
-- Note: SQL Server doesn't have jsonb; use NVARCHAR(MAX) with JSON_VALUE validation

IF COL_LENGTH('strategy_scenarios', 'parameters_json_override') IS NOT NULL
BEGIN
    -- Add constraint to validate JSON format
    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.CONSTRAINTS WHERE CONSTRAINT_NAME='ck_strategy_scenarios_json_valid')
    BEGIN
        ALTER TABLE strategy_scenarios
        ADD CONSTRAINT ck_strategy_scenarios_json_valid
        CHECK (TRY_CONVERT(NVARCHAR(MAX), JSON_QUERY(parameters_json_override)) IS NOT NULL OR parameters_json_override IS NULL);
    END
END

-- #194: Consolidate signal_journal and signal_journal_entries
-- This requires data migration; placeholder for manual execution:
-- 1. Migrate all data from signal_journal to signal_journal_entries
-- 2. Add missing columns to signal_journal_entries if not present
-- 3. Add FK to strategy_instances
-- 4. DROP TABLE signal_journal;

-- This migration is marked as requiring manual review due to data migration complexity
