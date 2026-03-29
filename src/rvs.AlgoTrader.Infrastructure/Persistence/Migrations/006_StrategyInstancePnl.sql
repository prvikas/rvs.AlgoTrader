-- 006_StrategyInstancePnl.sql
-- Adds intraday P&L columns to strategy_instances and trailing stop columns to orders.
-- Idempotent — safe to run multiple times.

ALTER TABLE strategy_instances
    ADD COLUMN IF NOT EXISTS today_realized_pnl   NUMERIC(18,4) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS today_unrealized_pnl  NUMERIC(18,4) NOT NULL DEFAULT 0;

ALTER TABLE orders
    ADD COLUMN IF NOT EXISTS trailing_sl NUMERIC(18,4),
    ADD COLUMN IF NOT EXISTS trailing_tp NUMERIC(18,4);
