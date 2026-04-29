-- Migration 050: Multi-user support + generic broker account registry
-- Creates: users, user_broker_accounts
-- Extends: strategy_definitions, backtest_runs, quant_conditions, monitoring_alert_rules with user_id
-- Existing rows are assigned to a placeholder "system" user (idempotent; re-runnable).

-- ── 1. users ──────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS users (
    id            UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    username      VARCHAR(100) NOT NULL,
    password_hash VARCHAR(200) NOT NULL,             -- BCrypt hash ($2a$...)
    role          VARCHAR(20)  NOT NULL DEFAULT 'Analyst', -- Admin | Trader | Analyst | RiskManager
    is_active     BOOLEAN      NOT NULL DEFAULT true,
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS uidx_users_username
    ON users (LOWER(username));

-- Seed a "system" user that owns all pre-existing rows.
-- The real admin is seeded by SeedAdminUserAsync at startup using LocalAuth config.
INSERT INTO users (id, username, password_hash, role)
VALUES ('00000000-0000-0000-0000-000000000001', 'system', '$system$', 'Admin')
ON CONFLICT DO NOTHING;

-- ── 2. user_broker_accounts ───────────────────────────────────────────────────
-- Declares which broker accounts each user has configured.
-- Credentials (API keys, secrets) are stored encrypted in Redis/Vault keyed by
-- broker:credentials:{userId}:{brokerName}:{field}.  This table is the registry.
CREATE TABLE IF NOT EXISTS user_broker_accounts (
    id           UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id      UUID        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    broker_name  VARCHAR(50) NOT NULL,   -- MStock | Zerodha | Upstox | Dhan | Alpaca | etc.
    market       VARCHAR(10) NOT NULL DEFAULT 'IN', -- IN | US | UK | SG | etc.
    display_name VARCHAR(100),           -- optional friendly label
    is_active    BOOLEAN     NOT NULL DEFAULT true,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_user_broker_market UNIQUE (user_id, broker_name, market)
);

CREATE INDEX IF NOT EXISTS idx_user_broker_accounts_user
    ON user_broker_accounts (user_id) WHERE is_active = true;

-- ── 3. Add user_id to core tables ────────────────────────────────────────────
-- Nullable initially; backfilled to system user; NOT NULL enforced post-backfill.

ALTER TABLE strategy_definitions
    ADD COLUMN IF NOT EXISTS user_id UUID REFERENCES users(id) ON DELETE SET NULL;

ALTER TABLE backtest_runs
    ADD COLUMN IF NOT EXISTS user_id UUID REFERENCES users(id) ON DELETE SET NULL;

ALTER TABLE quant_conditions
    ADD COLUMN IF NOT EXISTS user_id UUID REFERENCES users(id) ON DELETE SET NULL;

ALTER TABLE monitoring_alert_rules
    ADD COLUMN IF NOT EXISTS user_id UUID REFERENCES users(id) ON DELETE SET NULL;

-- Backfill existing rows to system user
UPDATE strategy_definitions    SET user_id = '00000000-0000-0000-0000-000000000001' WHERE user_id IS NULL;
UPDATE backtest_runs           SET user_id = '00000000-0000-0000-0000-000000000001' WHERE user_id IS NULL;
UPDATE quant_conditions        SET user_id = '00000000-0000-0000-0000-000000000001' WHERE user_id IS NULL;
UPDATE monitoring_alert_rules  SET user_id = '00000000-0000-0000-0000-000000000001' WHERE user_id IS NULL;

-- Indexes for per-user queries
CREATE INDEX IF NOT EXISTS idx_strategy_definitions_user ON strategy_definitions (user_id);
CREATE INDEX IF NOT EXISTS idx_backtest_runs_user        ON backtest_runs        (user_id);
CREATE INDEX IF NOT EXISTS idx_quant_conditions_user     ON quant_conditions     (user_id);
CREATE INDEX IF NOT EXISTS idx_alert_rules_user          ON monitoring_alert_rules (user_id);
