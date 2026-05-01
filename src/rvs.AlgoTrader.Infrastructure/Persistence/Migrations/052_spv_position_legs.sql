-- Migration 052: Short Premium Velocity — leg tagging columns on positions
-- Adds nullable leg classification columns so hedge legs can be tracked in the
-- same positions table without a separate hedge position table (per architecture rule).
-- All columns nullable — existing non-SPV positions keep NULL values.

ALTER TABLE positions
    ADD COLUMN IF NOT EXISTS leg_type          VARCHAR(30)      NULL,
    ADD COLUMN IF NOT EXISTS hedge_type        VARCHAR(30)      NULL,
    ADD COLUMN IF NOT EXISTS hedge_net_cost    NUMERIC(18, 4)   NULL,
    ADD COLUMN IF NOT EXISTS linked_short_leg_id UUID           NULL
        REFERENCES positions (id) ON DELETE RESTRICT;

-- Index to efficiently find all hedge legs for a given short-premium position
CREATE INDEX IF NOT EXISTS ix_positions_linked_short_leg_id
    ON positions (linked_short_leg_id)
    WHERE linked_short_leg_id IS NOT NULL;

-- Index to filter positions by leg type (e.g. find all open ShortPremium legs)
CREATE INDEX IF NOT EXISTS ix_positions_leg_type
    ON positions (leg_type)
    WHERE leg_type IS NOT NULL;
