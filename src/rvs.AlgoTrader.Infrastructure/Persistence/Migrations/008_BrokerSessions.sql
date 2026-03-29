-- 008_BrokerSessions.sql
-- Broker session token persistence table.
-- Stores access/feed/refresh tokens per broker so they survive app restarts.
-- Idempotent — safe to run multiple times.

CREATE TABLE IF NOT EXISTS broker_sessions (
    broker_name   VARCHAR(50) PRIMARY KEY,
    access_token  TEXT        NOT NULL,
    feed_token    TEXT,
    refresh_token TEXT,
    expires_at    TIMESTAMPTZ,
    stored_at     TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
