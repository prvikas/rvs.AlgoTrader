-- 005_BacktestExtendedStats.sql
-- Adds extended_stats_json column to backtest_runs for new stats (Sortino, Monthly, Yearly breakdowns).
-- Idempotent — safe to run multiple times.

ALTER TABLE backtest_runs
    ADD COLUMN IF NOT EXISTS extended_stats_json TEXT;
