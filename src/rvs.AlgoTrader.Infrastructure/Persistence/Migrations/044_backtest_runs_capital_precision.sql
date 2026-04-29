-- Migration 044: Normalize backtest_runs.initial_capital precision
-- Issue: backtest_runs.initial_capital was NUMERIC(18,2) (from migration 004) while
--        forward_test_sessions.initial_capital has been NUMERIC(18,4) since day one.
--        Widening to (18,4) is lossless — no existing value is truncated.
--        EF config in BacktestRunConfiguration.cs updated to match.

DO $$ BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name   = 'backtest_runs'
          AND column_name  = 'initial_capital'
    ) THEN
        ALTER TABLE backtest_runs
            ALTER COLUMN initial_capital TYPE NUMERIC(18,4);
    END IF;
END $$;
