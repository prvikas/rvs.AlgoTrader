-- Migration 053: Short Premium Velocity — circuit-breaker state persistence
-- The CircuitBreakerService holds state in-memory during Phase 2.
-- This table persists it across process restarts (loaded on startup; written on transitions).

CREATE TABLE IF NOT EXISTS velocity_circuit_breaker_state
(
    id                   UUID          NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
    strategy_instance_id UUID          NOT NULL,
    state                VARCHAR(20)   NOT NULL DEFAULT 'Normal',   -- Normal | SoftStop | HardStop
    daily_loss_pct       NUMERIC(8, 6) NOT NULL DEFAULT 0,
    triggered_at         TIMESTAMPTZ   NULL,
    reset_eligible_at    TIMESTAMPTZ   NULL,
    updated_at           TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_velocity_cb_strategy UNIQUE (strategy_instance_id)
);

CREATE INDEX IF NOT EXISTS ix_velocity_cb_state_instance
    ON velocity_circuit_breaker_state (strategy_instance_id);

-- Also ensure the positions leg-tracking indexes from migration 052 are present.
-- These are idempotent guards; migration 052 already applied them on fresh installations.
CREATE INDEX IF NOT EXISTS ix_positions_linked_short_leg_id
    ON positions (linked_short_leg_id)
    WHERE linked_short_leg_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_positions_leg_type
    ON positions (leg_type)
    WHERE leg_type IS NOT NULL;
