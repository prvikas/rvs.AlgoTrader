-- Migration 018: Strategy Approvals (P4 Approval Gate)
-- Creates strategy_approvals table and adds approval columns to strategy_instances.

CREATE TABLE IF NOT EXISTS strategy_approvals (
    id                        UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    strategy_instance_id      UUID        NOT NULL REFERENCES strategy_instances(id) ON DELETE CASCADE,
    approved_by               VARCHAR(200) NOT NULL,
    approval_notes            TEXT,
    backtest_result_id        UUID,
    forward_test_session_id   UUID,
    cagr_at_approval          NUMERIC(18, 6),
    drawdown_at_approval      NUMERIC(18, 6),
    sharpe_at_approval        NUMERIC(18, 6),
    forward_test_days         INT,
    forward_win_rate          NUMERIC(18, 6),
    automated_checks_passed   BOOL        NOT NULL DEFAULT false,
    invalidated_at            TIMESTAMPTZ,
    invalidation_reason       TEXT,
    created_at                TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_strategy_approvals_instance
    ON strategy_approvals(strategy_instance_id);

CREATE INDEX IF NOT EXISTS idx_strategy_approvals_created
    ON strategy_approvals(created_at DESC);

ALTER TABLE strategy_instances
    ADD COLUMN IF NOT EXISTS approval_ready  BOOL        NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS approved_at     TIMESTAMPTZ;
