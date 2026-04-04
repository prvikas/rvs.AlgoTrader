-- Migration 025: Fix #170 (audit_log schema conflict), #171 (drop orphaned signal_journal),
--                #147 (candle partial index for is_closed = true)

-- ── #170: audit_log schema conflict ─────────────────────────────────────────
-- InitialMigration created audit_log with (event_type, details JSONB).
-- Migration 010 tried CREATE TABLE IF NOT EXISTS with (action, details_json TEXT)
-- but silently no-oped, leaving those columns permanently missing.
-- Fix: add the missing 010 columns and widen existing ones to the 010 spec.
DO $$ BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = 'public' AND table_name = 'audit_log'
    ) THEN
        -- Add 'action' column if missing (010's primary write column)
        IF NOT EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'audit_log' AND column_name = 'action'
        ) THEN
            ALTER TABLE audit_log ADD COLUMN action VARCHAR(100);
            -- Backfill: event_type and action share the same semantic intent
            UPDATE audit_log SET action = event_type WHERE action IS NULL;
            ALTER TABLE audit_log ALTER COLUMN action SET NOT NULL;
        END IF;

        -- Add 'details_json' column if missing (TEXT form of the old JSONB 'details')
        IF NOT EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'audit_log' AND column_name = 'details_json'
        ) THEN
            ALTER TABLE audit_log ADD COLUMN details_json TEXT;
            UPDATE audit_log
                SET details_json = details::TEXT
                WHERE details_json IS NULL AND details IS NOT NULL;
        END IF;

        -- Widen columns to match migration 010 spec (safe — VARCHAR widening never loses data)
        ALTER TABLE audit_log ALTER COLUMN entity_type     TYPE VARCHAR(100);
        ALTER TABLE audit_log ALTER COLUMN entity_id       TYPE VARCHAR(200);
        ALTER TABLE audit_log ALTER COLUMN actor           TYPE VARCHAR(200);
        ALTER TABLE audit_log ALTER COLUMN correlation_id  TYPE VARCHAR(200);

        -- Actor index from migration 010 (IF NOT EXISTS is safe)
        CREATE INDEX IF NOT EXISTS ix_audit_log_actor ON audit_log (actor);
    END IF;
END $$;

-- ── #171: Drop orphaned signal_journal table ──────────────────────────────
-- All C# code writes to signal_journal_entries (added in migration 010).
-- The signal_journal table from InitialMigration is dead schema.
-- DROP CASCADE handles the ix_signal_journal_instance index automatically.
DROP TABLE IF EXISTS signal_journal CASCADE;

-- ── #147: Candle partial index for closed candles ─────────────────────────
-- All strategy evaluation queries filter WHERE is_closed = true.
-- A partial index covering only closed candles reduces index size and speeds
-- the ORDER BY open_time DESC queries in GetLastNAsync / GetLatestCandlesAsync.
CREATE INDEX IF NOT EXISTS idx_candles_closed
    ON candles (internal_symbol, timeframe, open_time DESC)
    WHERE is_closed = true;
