-- Migration 010: Persistent tables for repositories previously backed by in-memory stubs.
-- Adds: audit_log, alert_log, download_jobs, signal_journal_entries, user_preferences.
-- No destructive changes to existing tables.

-- ── Audit log ─────────────────────────────────────────────────────────────────
-- INSERT-only (AP-009). Reads support pagination by entity_type / actor.
CREATE TABLE IF NOT EXISTS audit_log (
    id              BIGSERIAL       PRIMARY KEY,
    action          VARCHAR(100)    NOT NULL,
    actor           VARCHAR(200)    NOT NULL,
    entity_type     VARCHAR(100)    NOT NULL,
    entity_id       VARCHAR(200)    NOT NULL,
    details_json    TEXT,
    correlation_id  VARCHAR(200)    NOT NULL,
    occurred_at     TIMESTAMPTZ     NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_audit_log_entity   ON audit_log (entity_type, entity_id);
CREATE INDEX IF NOT EXISTS ix_audit_log_actor    ON audit_log (actor);
CREATE INDEX IF NOT EXISTS ix_audit_log_occurred ON audit_log (occurred_at DESC);

-- ── Alert log ─────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS alert_log (
    id          UUID            PRIMARY KEY DEFAULT gen_random_uuid(),
    alert_type  VARCHAR(100)    NOT NULL,
    severity    VARCHAR(20)     NOT NULL,
    message     TEXT            NOT NULL,
    occurred_at TIMESTAMPTZ     NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_alert_log_occurred ON alert_log (occurred_at DESC);

-- ── Historical data download jobs ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS download_jobs (
    id                  UUID            PRIMARY KEY DEFAULT gen_random_uuid(),
    symbol              VARCHAR(50)     NOT NULL,
    timeframe           VARCHAR(20)     NOT NULL,
    from_date           DATE            NOT NULL,
    to_date             DATE            NOT NULL,
    broker_name         VARCHAR(50)     NOT NULL,
    status              VARCHAR(30)     NOT NULL DEFAULT 'Queued',
    completed_chunks    INT             NOT NULL DEFAULT 0,
    total_chunks        INT             NOT NULL DEFAULT 0,
    created_at          TIMESTAMPTZ     NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_download_jobs_status  ON download_jobs (status) WHERE status NOT IN ('Completed', 'Failed');
CREATE INDEX IF NOT EXISTS ix_download_jobs_created ON download_jobs (created_at DESC);

-- ── Signal journal ────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS signal_journal_entries (
    id                      BIGSERIAL       PRIMARY KEY,
    strategy_instance_id    UUID            NOT NULL,
    internal_symbol         VARCHAR(50)     NOT NULL,
    evaluated_at            TIMESTAMPTZ     NOT NULL,
    timeframe               VARCHAR(20)     NOT NULL,
    signal                  VARCHAR(20)     NOT NULL,
    entry_price             NUMERIC(18,4),
    stop_loss               NUMERIC(18,4),
    take_profit             NUMERIC(18,4),
    reason                  TEXT,
    diagnostics_json        TEXT,
    acted_on                BOOLEAN         NOT NULL DEFAULT FALSE,
    skipped_reason          TEXT
);

CREATE INDEX IF NOT EXISTS ix_signal_journal_instance ON signal_journal_entries (strategy_instance_id, evaluated_at DESC);
CREATE INDEX IF NOT EXISTS ix_signal_journal_symbol   ON signal_journal_entries (internal_symbol, evaluated_at DESC);

-- ── User preferences ──────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS user_preferences (
    user_id         VARCHAR(200)    PRIMARY KEY,
    preferences_json TEXT           NOT NULL DEFAULT '{}',
    updated_at      TIMESTAMPTZ     NOT NULL DEFAULT now()
);
