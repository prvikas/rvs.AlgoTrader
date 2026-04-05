-- Rollback for migration 025_fix_schema_conflicts.sql
-- IMPORTANT: Apply rollbacks in REVERSE numeric order.
-- Test in a non-production environment before applying to production.

-- ── Undo #147: Remove candle partial index ───────────────────────────────
DROP INDEX IF EXISTS idx_candles_closed;

-- ── Undo #171: Restore signal_journal table (recreate from InitialMigration schema)
-- NOTE: Data that was in signal_journal before migration 025 is PERMANENTLY LOST.
--       This restores the table structure only.
CREATE TABLE IF NOT EXISTS signal_journal (
    id                   UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    strategy_instance_id UUID         NOT NULL REFERENCES strategy_instances(id),
    strategy_name        VARCHAR(100),
    internal_symbol      VARCHAR(50)  NOT NULL,
    timeframe            VARCHAR(10)  NOT NULL,
    signal               VARCHAR(10)  NOT NULL,
    entry_price          NUMERIC(18,4),
    stop_loss            NUMERIC(18,4),
    take_profit          NUMERIC(18,4),
    reason               TEXT,
    skipped_reason       VARCHAR(50),
    correlation_id       VARCHAR(100),
    occurred_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_signal_journal_instance
    ON signal_journal (strategy_instance_id, occurred_at DESC);

-- ── Undo #170: Remove audit_log columns added by migration 025 ───────────
-- Columns added: action, details_json
-- Widened: entity_type, entity_id, actor, correlation_id
-- NOTE: 'action' was backfilled from 'event_type'; dropping it is safe.
DO $$ BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'audit_log' AND column_name = 'action'
    ) THEN
        ALTER TABLE audit_log DROP COLUMN action;
    END IF;
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'audit_log' AND column_name = 'details_json'
    ) THEN
        ALTER TABLE audit_log DROP COLUMN details_json;
    END IF;
    DROP INDEX IF EXISTS ix_audit_log_actor;
    -- Re-narrow columns back to InitialMigration spec
    ALTER TABLE audit_log ALTER COLUMN entity_type    TYPE VARCHAR(50);
    ALTER TABLE audit_log ALTER COLUMN entity_id      TYPE VARCHAR(100);
    ALTER TABLE audit_log ALTER COLUMN actor          TYPE VARCHAR(100);
    ALTER TABLE audit_log ALTER COLUMN correlation_id TYPE VARCHAR(100);
END $$;

-- Remove from schema_migrations so DatabaseMigrationRunner re-applies on next boot
DELETE FROM schema_migrations WHERE file_name = '025_fix_schema_conflicts.sql';
