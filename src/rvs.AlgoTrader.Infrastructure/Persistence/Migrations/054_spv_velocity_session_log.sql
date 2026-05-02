-- Migration 054: Short Premium Velocity — session log
-- Records a summary row at the end of each intraday trading session.
-- Used by RecoveryManager for rolling Sharpe computation and reporting.

CREATE TABLE IF NOT EXISTS velocity_session_log
(
    id                   UUID           NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    strategy_instance_id UUID           NOT NULL,
    session_date         DATE           NOT NULL,
    regime_label         VARCHAR(60)    NOT NULL,
    gross_pnl            NUMERIC(18, 4) NOT NULL DEFAULT 0,
    fees                 NUMERIC(18, 4) NOT NULL DEFAULT 0,
    slippage             NUMERIC(18, 4) NOT NULL DEFAULT 0,
    hedge_net_cost       NUMERIC(18, 4) NOT NULL DEFAULT 0,
    net_pnl              NUMERIC(18, 4) NOT NULL DEFAULT 0,
    trades_opened        INT            NOT NULL DEFAULT 0,
    trades_closed        INT            NOT NULL DEFAULT 0,
    velocity_score       NUMERIC(6, 2)  NULL,
    opportunity_density  NUMERIC(6, 2)  NULL,
    recovery_step        SMALLINT       NOT NULL DEFAULT 0,
    recovery_multiplier  NUMERIC(6, 4)  NOT NULL DEFAULT 1,
    circuit_breaker_state VARCHAR(20)   NOT NULL DEFAULT 'Normal',
    recorded_at          TIMESTAMPTZ    NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_velocity_session_log_instance_date
    ON velocity_session_log (strategy_instance_id, session_date DESC);

CREATE UNIQUE INDEX IF NOT EXISTS uix_velocity_session_log_instance_date
    ON velocity_session_log (strategy_instance_id, session_date);
