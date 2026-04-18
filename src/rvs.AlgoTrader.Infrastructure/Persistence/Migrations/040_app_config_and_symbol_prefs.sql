-- Migration 040: Persistent storage for AppConfig (key-value) and SymbolDataPreferences.
-- Resolves PROMPT-012 AC-1 and SDP-1.
-- No destructive changes to existing tables.

-- ── App config key-value store ────────────────────────────────────────────────
-- Write-through backing for AppConfigService (Redis is the L1 cache; this is L2).
-- All writes go here AND Redis. Redis miss falls back to this table.
CREATE TABLE IF NOT EXISTS app_config (
    key         VARCHAR(200)    PRIMARY KEY,
    value_json  TEXT            NOT NULL,
    actor       VARCHAR(200)    NOT NULL DEFAULT 'system',
    updated_at  TIMESTAMPTZ     NOT NULL DEFAULT now()
);

-- ── Symbol data preferences ───────────────────────────────────────────────────
-- Persists per-symbol data ingestion preferences (timeframes, from-date, priority).
-- Replaces the in-memory Dictionary<string, SymbolDataPreferences> stub.
CREATE TABLE IF NOT EXISTS symbol_data_preferences (
    id              UUID            PRIMARY KEY DEFAULT gen_random_uuid(),
    internal_symbol VARCHAR(50)     NOT NULL,
    timeframes      TEXT            NOT NULL DEFAULT '["1m","5m","15m","1h","1d"]',
    from_date       DATE            NOT NULL,
    priority        SMALLINT        NOT NULL DEFAULT 5,
    is_active       BOOLEAN         NOT NULL DEFAULT TRUE,
    created_at      TIMESTAMPTZ     NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ     NOT NULL DEFAULT now(),
    CONSTRAINT uq_symbol_data_prefs_symbol UNIQUE (internal_symbol)
);

CREATE INDEX IF NOT EXISTS ix_symbol_data_prefs_active ON symbol_data_preferences (is_active) WHERE is_active;
