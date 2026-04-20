-- Migration 040: Persistent storage for AppConfig (key-value) and SymbolDataPreferences.
-- Resolves PROMPT-012 AC-1 and SDP-1.
-- No destructive changes to existing tables.
--
-- IMPORTANT: This migration must be idempotent for BOTH:
--   (a) Fresh databases  — tables don't exist yet  → CREATE TABLE creates them
--   (b) Existing databases — InitialMigration.sql already created both tables with
--       older schemas → CREATE TABLE IF NOT EXISTS is a no-op; ALTER TABLE adds the
--       missing columns idempotently.

-- ── App config key-value store ────────────────────────────────────────────────
-- Write-through backing for AppConfigService (Redis is the L1 cache; this is L2).
-- InitialMigration.sql created app_config as (key, value TEXT, updated_at).
-- We need value_json and actor columns for AppConfigService.GetAsync / SetAsync.

CREATE TABLE IF NOT EXISTS app_config (
    key         VARCHAR(200)    PRIMARY KEY,
    value_json  TEXT,
    actor       VARCHAR(200)    NOT NULL DEFAULT 'system',
    updated_at  TIMESTAMPTZ     NOT NULL DEFAULT now()
);

-- Add missing columns to existing app_config tables (idempotent on fresh DBs too).
ALTER TABLE app_config
    ADD COLUMN IF NOT EXISTS value_json  TEXT,
    ADD COLUMN IF NOT EXISTS actor       VARCHAR(200) NOT NULL DEFAULT 'system';

-- Back-fill: existing rows written with the old 'value' column get a JSON-quoted copy.
UPDATE app_config
   SET value_json = to_json(value)::text
 WHERE value_json IS NULL AND value IS NOT NULL;

-- ── Symbol data preferences ───────────────────────────────────────────────────
-- InitialMigration.sql created symbol_data_preferences as:
--   (internal_symbol PK, preferred_broker, preferred_timeframes TEXT[], is_monitored, notes, updated_at)
-- The new schema (used by SymbolDataPreferencesService) requires:
--   (id UUID PK, internal_symbol UNIQUE, timeframes TEXT, from_date DATE, priority, is_active, created_at, updated_at)

CREATE TABLE IF NOT EXISTS symbol_data_preferences (
    id              UUID            PRIMARY KEY DEFAULT gen_random_uuid(),
    internal_symbol VARCHAR(50)     NOT NULL,
    timeframes      TEXT            NOT NULL DEFAULT '["1m","5m","15m","1h","1d"]',
    from_date       DATE            NOT NULL DEFAULT (CURRENT_DATE - INTERVAL '1 year')::DATE,
    priority        SMALLINT        NOT NULL DEFAULT 5,
    is_active       BOOLEAN         NOT NULL DEFAULT TRUE,
    created_at      TIMESTAMPTZ     NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ     NOT NULL DEFAULT now(),
    CONSTRAINT uq_symbol_data_prefs_symbol UNIQUE (internal_symbol)
);

-- Add columns missing from the old schema (idempotent on fresh DBs too).
ALTER TABLE symbol_data_preferences
    ADD COLUMN IF NOT EXISTS id         UUID        NOT NULL DEFAULT gen_random_uuid(),
    ADD COLUMN IF NOT EXISTS timeframes TEXT        NOT NULL DEFAULT '["1m","5m","15m","1h","1d"]',
    ADD COLUMN IF NOT EXISTS from_date  DATE        NOT NULL DEFAULT (CURRENT_DATE - INTERVAL '1 year')::DATE,
    ADD COLUMN IF NOT EXISTS priority   SMALLINT    NOT NULL DEFAULT 5,
    ADD COLUMN IF NOT EXISTS is_active  BOOLEAN     NOT NULL DEFAULT TRUE,
    ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ NOT NULL DEFAULT now();

-- Ensure the UNIQUE constraint on internal_symbol exists (old schema used it as PK,
-- new schema uses a separate id PK; add the constraint if it isn't already implied).
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname   = 'uq_symbol_data_prefs_symbol'
          AND conrelid  = 'symbol_data_preferences'::regclass
          AND contype   = 'u'
    ) THEN
        -- Only add if internal_symbol is NOT already the primary key
        IF EXISTS (
            SELECT 1 FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
              ON tc.constraint_name = kcu.constraint_name
             AND tc.table_name      = kcu.table_name
            WHERE tc.table_name       = 'symbol_data_preferences'
              AND tc.constraint_type  = 'PRIMARY KEY'
              AND kcu.column_name     = 'internal_symbol'
        ) THEN
            NULL; -- internal_symbol IS the PK → uniqueness is implicit
        ELSE
            ALTER TABLE symbol_data_preferences
                ADD CONSTRAINT uq_symbol_data_prefs_symbol UNIQUE (internal_symbol);
        END IF;
    END IF;
END$$;

-- Create the active-symbols index only once is_active column exists.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name   = 'symbol_data_preferences'
          AND column_name  = 'is_active'
    ) AND NOT EXISTS (
        SELECT 1 FROM pg_indexes
        WHERE schemaname = 'public'
          AND tablename  = 'symbol_data_preferences'
          AND indexname  = 'ix_symbol_data_prefs_active'
    ) THEN
        CREATE INDEX ix_symbol_data_prefs_active ON symbol_data_preferences (is_active) WHERE is_active;
    END IF;
END$$;
