-- Migration 056: Short Premium Velocity — reinvestment log
-- Persists each session-close reinvestment computation from ReinvestmentEngine.
-- Used for compounding analysis, friction leakage reporting, and the SPV capital curve.

CREATE TABLE IF NOT EXISTS velocity_reinvestment_log
(
    id                     UUID           NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    strategy_instance_id   UUID           NOT NULL,
    occurred_at            TIMESTAMPTZ    NOT NULL,
    gross_pnl              NUMERIC(18, 4) NOT NULL DEFAULT 0,
    fees                   NUMERIC(18, 4) NOT NULL DEFAULT 0,
    slippage               NUMERIC(18, 4) NOT NULL DEFAULT 0,
    hedge_net_cost         NUMERIC(18, 4) NOT NULL DEFAULT 0,
    net_pnl                NUMERIC(18, 4) NOT NULL DEFAULT 0,
    friction_total         NUMERIC(18, 4) NOT NULL DEFAULT 0,
    friction_leakage_pct   NUMERIC(8, 6)  NOT NULL DEFAULT 0,
    amount_reinvested      NUMERIC(18, 4) NOT NULL DEFAULT 0,
    allocated_capital_after NUMERIC(18, 4) NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS ix_velocity_reinvestment_log_instance
    ON velocity_reinvestment_log (strategy_instance_id, occurred_at DESC);
