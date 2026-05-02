-- Migration 055: Short Premium Velocity — roll decision log
-- Persists every roll/close/hold decision emitted by RollDecisionEngine.
-- Used for trade attribution, audit trail, and gamma-pin analysis.

CREATE TABLE IF NOT EXISTS velocity_roll_log
(
    id                   UUID           NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    strategy_instance_id UUID           NOT NULL,
    position_id          UUID           NOT NULL,
    action               VARCHAR(10)    NOT NULL,   -- Roll | Close | Hold
    regime_label         VARCHAR(60)    NOT NULL,
    dte_at_decision      INT            NOT NULL DEFAULT 0,
    gamma_per_theta      NUMERIC(10, 4) NULL,
    entry_premium        NUMERIC(18, 4) NULL,
    current_premium      NUMERIC(18, 4) NULL,
    realised_gain_pct    NUMERIC(8, 4)  NULL,       -- (entry - current) / entry as fraction
    blocking_gates       JSONB          NULL,        -- array of gate strings that blocked rolling
    reason               TEXT           NOT NULL,
    target_dte           INT            NULL,        -- populated only when action = Roll
    decided_at           TIMESTAMPTZ    NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_velocity_roll_log_instance
    ON velocity_roll_log (strategy_instance_id, decided_at DESC);

CREATE INDEX IF NOT EXISTS ix_velocity_roll_log_position
    ON velocity_roll_log (position_id);
