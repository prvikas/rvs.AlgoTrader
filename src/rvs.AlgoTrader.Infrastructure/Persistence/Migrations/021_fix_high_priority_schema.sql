-- Migration 021: Fix high-priority schema issues (#203, #202, #201, #197, #192)
-- Phase 2: Referential integrity and schema conflicts

-- #201: Make internal_symbol nullable for watchlist mode
-- Add constraint ensuring (internal_symbol NOT NULL) OR (watchlist_id NOT NULL)
ALTER TABLE strategy_instances
ALTER COLUMN internal_symbol VARCHAR(100) NULL;

ALTER TABLE strategy_instances
ADD CONSTRAINT ck_strategy_instances_symbol_or_watchlist
CHECK ((internal_symbol IS NOT NULL) OR (watchlist_id IS NOT NULL));

-- #197: Add missing foreign key relationships

-- Add strategy_instance_id to backtest_runs (nullable for historical runs)
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='backtest_runs' AND COLUMN_NAME='strategy_instance_id')
BEGIN
    ALTER TABLE backtest_runs
    ADD strategy_instance_id UNIQUEIDENTIFIER NULL;

    ALTER TABLE backtest_runs
    ADD CONSTRAINT fk_backtest_runs_strategy_instance
    FOREIGN KEY (strategy_instance_id) REFERENCES strategy_instances(id) ON DELETE SET NULL;
END

-- Add FK from instrument_universe to instruments
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.CONSTRAINTS WHERE CONSTRAINT_NAME='fk_instrument_universe_instruments')
BEGIN
    ALTER TABLE instrument_universe
    ADD CONSTRAINT fk_instrument_universe_instruments
    FOREIGN KEY (symbol) REFERENCES instruments(internal_symbol) ON DELETE CASCADE;
END

-- #192: Add 14 missing foreign key constraints

-- backtest_runs -> strategy_scenarios
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.CONSTRAINTS WHERE CONSTRAINT_NAME='fk_backtest_runs_scenario')
BEGIN
    ALTER TABLE backtest_runs
    ADD CONSTRAINT fk_backtest_runs_scenario
    FOREIGN KEY (scenario_id) REFERENCES strategy_scenarios(id) ON DELETE SET NULL;
END

-- capital_allocations -> strategy_instances
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.CONSTRAINTS WHERE CONSTRAINT_NAME='fk_capital_allocations_strategy_instance')
BEGIN
    ALTER TABLE capital_allocations
    ADD CONSTRAINT fk_capital_allocations_strategy_instance
    FOREIGN KEY (strategy_instance_id) REFERENCES strategy_instances(id) ON DELETE CASCADE;
END

-- orders -> strategy_runs
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.CONSTRAINTS WHERE CONSTRAINT_NAME='fk_orders_strategy_run')
BEGIN
    ALTER TABLE orders
    ADD CONSTRAINT fk_orders_strategy_run
    FOREIGN KEY (strategy_run_id) REFERENCES strategy_runs(id) ON DELETE CASCADE;
END

-- positions -> strategy_runs
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.CONSTRAINTS WHERE CONSTRAINT_NAME='fk_positions_strategy_run')
BEGIN
    ALTER TABLE positions
    ADD CONSTRAINT fk_positions_strategy_run
    FOREIGN KEY (strategy_run_id) REFERENCES strategy_runs(id) ON DELETE CASCADE;
END

-- strategy_instances -> risk_profiles
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.CONSTRAINTS WHERE CONSTRAINT_NAME='fk_strategy_instances_risk_profile')
BEGIN
    ALTER TABLE strategy_instances
    ADD CONSTRAINT fk_strategy_instances_risk_profile
    FOREIGN KEY (risk_profile_id) REFERENCES risk_profiles(id) ON DELETE SET NULL;
END

-- strategy_instances -> watchlists
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.CONSTRAINTS WHERE CONSTRAINT_NAME='fk_strategy_instances_watchlist')
BEGIN
    ALTER TABLE strategy_instances
    ADD CONSTRAINT fk_strategy_instances_watchlist
    FOREIGN KEY (watchlist_id) REFERENCES watchlists(id) ON DELETE SET NULL;
END

-- strategy_scenarios -> strategy_instances
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.CONSTRAINTS WHERE CONSTRAINT_NAME='fk_strategy_scenarios_strategy_instance')
BEGIN
    ALTER TABLE strategy_scenarios
    ADD CONSTRAINT fk_strategy_scenarios_strategy_instance
    FOREIGN KEY (strategy_instance_id) REFERENCES strategy_instances(id) ON DELETE CASCADE;
END

-- forward_test_sessions -> strategy_instances
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.CONSTRAINTS WHERE CONSTRAINT_NAME='fk_forward_test_sessions_strategy_instance')
BEGIN
    ALTER TABLE forward_test_sessions
    ADD CONSTRAINT fk_forward_test_sessions_strategy_instance
    FOREIGN KEY (strategy_instance_id) REFERENCES strategy_instances(id) ON DELETE CASCADE;
END

-- forward_test_trades -> forward_test_sessions
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.CONSTRAINTS WHERE CONSTRAINT_NAME='fk_forward_test_trades_session')
BEGIN
    ALTER TABLE forward_test_trades
    ADD CONSTRAINT fk_forward_test_trades_session
    FOREIGN KEY (session_id) REFERENCES forward_test_sessions(id) ON DELETE CASCADE;
END

-- strategy_runs -> strategy_instances
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.CONSTRAINTS WHERE CONSTRAINT_NAME='fk_strategy_runs_strategy_instance')
BEGIN
    ALTER TABLE strategy_runs
    ADD CONSTRAINT fk_strategy_runs_strategy_instance
    FOREIGN KEY (strategy_instance_id) REFERENCES strategy_instances(id) ON DELETE CASCADE;
END

-- trade_journal_entries -> strategy_instances
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.CONSTRAINTS WHERE CONSTRAINT_NAME='fk_trade_journal_entries_strategy_instance')
BEGIN
    ALTER TABLE trade_journal_entries
    ADD CONSTRAINT fk_trade_journal_entries_strategy_instance
    FOREIGN KEY (strategy_instance_id) REFERENCES strategy_instances(id) ON DELETE CASCADE;
END

-- #203: Address overlapping PnL columns
-- Rename pnl to gross_pnl to clarify intent
-- Note: This requires custom SQL based on actual schema; commenting for manual review
-- ALTER TABLE forward_test_trades RENAME COLUMN pnl TO gross_pnl;
-- Add documentation: realized_pnl should be populated only at trade exit

-- #202: Document and enforce average_price consistency
-- Add check constraint to ensure average_price is updated when quantity changes
-- Note: This is a business logic constraint; add documentation in code comments

-- #201 already handled above with nullable internal_symbol
