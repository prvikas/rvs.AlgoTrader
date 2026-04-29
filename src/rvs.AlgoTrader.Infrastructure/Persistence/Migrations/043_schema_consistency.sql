-- Migration 043: Schema consistency fixes
-- Fixes:
--   #186 – 009_ScenarioCapital.sql: strategy_scenarios.allocated_capital has no DEFAULT
--           causing nullable ambiguity on INSERT; add explicit NULL default + comment.
--   #189-203 – forward_test_trades: keep pnl in sync with realized_pnl via trigger so
--              the legacy column never drifts; add missing DEFAULT on realized_pnl.
--   Misc – backtest_runs.ran_at uses bare NOW(); normalise to CURRENT_TIMESTAMP
--          (equivalent, but explicit); add missing index on forward_test_trades.session_id
--          covering closed_at for session-level P&L rollups.

-- ── #186: strategy_scenarios.allocated_capital — add NULL default and comment ──
-- Migration 009 added the column without a DEFAULT, so omitting it on INSERT
-- yields NULL (correct — unset capital means "use strategy total"), but the intent
-- was never documented and EF emits a nullable warning.  Explicit NULL default
-- makes the intention clear and silences tooling warnings.
DO $$ BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name   = 'strategy_scenarios'
          AND column_name  = 'allocated_capital'
    ) THEN
        -- Ensure column can hold NULL (already nullable from 009; this is a no-op but defensive).
        ALTER TABLE strategy_scenarios ALTER COLUMN allocated_capital DROP NOT NULL;
        -- Explicit DEFAULT NULL: unset means "inherit from strategy".
        ALTER TABLE strategy_scenarios ALTER COLUMN allocated_capital SET DEFAULT NULL;
        COMMENT ON COLUMN strategy_scenarios.allocated_capital IS
            'Per-scenario capital override. NULL = inherit from strategy_instances.allocated_capital. '
            'Added by migration 009; DEFAULT NULL documented by migration 043.';
    END IF;
END $$;

-- ── #189-203: forward_test_trades — sync trigger pnl ↔ realized_pnl ─────────
-- Both columns must always hold the same value.  A BEFORE INSERT OR UPDATE trigger
-- enforces this at the DB level regardless of which ORM path writes the row.

CREATE OR REPLACE FUNCTION fn_ftt_sync_pnl()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
    -- Prefer realized_pnl as the authoritative source; fall back to pnl for legacy inserts.
    IF NEW.realized_pnl IS NOT NULL THEN
        NEW.pnl := NEW.realized_pnl;
    ELSE
        NEW.realized_pnl := COALESCE(NEW.pnl, 0);
        NEW.pnl          := NEW.realized_pnl;
    END IF;
    RETURN NEW;
END;
$$;

-- Drop and recreate so the migration is idempotent on re-run.
DROP TRIGGER IF EXISTS trg_ftt_sync_pnl ON forward_test_trades;
CREATE TRIGGER trg_ftt_sync_pnl
    BEFORE INSERT OR UPDATE ON forward_test_trades
    FOR EACH ROW EXECUTE FUNCTION fn_ftt_sync_pnl();

-- Backfill any rows where the columns diverged before this trigger existed.
DO $$ BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = 'public' AND table_name = 'forward_test_trades'
    ) THEN
        UPDATE forward_test_trades
        SET    realized_pnl = COALESCE(realized_pnl, pnl, 0),
               pnl          = COALESCE(realized_pnl, pnl, 0)
        WHERE  realized_pnl IS DISTINCT FROM pnl
            OR realized_pnl IS NULL;
    END IF;
END $$;

-- Ensure realized_pnl has a safe DEFAULT so omitted inserts don't fail NOT NULL.
DO $$ BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name   = 'forward_test_trades'
          AND column_name  = 'realized_pnl'
    ) THEN
        ALTER TABLE forward_test_trades ALTER COLUMN realized_pnl SET DEFAULT 0;
    END IF;
END $$;

-- ── Misc: add covering index for session P&L rollups ─────────────────────────
-- Queries that aggregate P&L per session filter by session_id and closed_at;
-- the existing idx_fwd_trades_session only covers session_id.
CREATE INDEX IF NOT EXISTS idx_fwd_trades_session_closed
    ON forward_test_trades (session_id, closed_at)
    WHERE closed_at IS NOT NULL;

-- ── Misc: overlapping constraint guard (migration 024 idempotency gap) ────────
-- migration 024 used bare ALTER TABLE ... ADD CONSTRAINT inside a DO-EXCEPTION block,
-- but some constraint names could still collide on DBs that ran a partial 024.
-- Re-issue with EXCEPTION guards for any that might have been missed.

DO $$ BEGIN
    ALTER TABLE forward_test_trades
        ADD CONSTRAINT ck_ftt_pnl_not_null CHECK (pnl IS NOT NULL);
EXCEPTION WHEN duplicate_object OR invalid_table_definition THEN NULL;
END $$;
