-- Migration 051: OAuth / social login provider support
-- Extends the users table to support OAuth-only accounts and adds
-- user_external_logins for provider-specific identities.
-- Adding a new OAuth provider (GitHub, LinkedIn, Twitter …) requires ZERO schema changes.

-- ── 1. Extend users table ─────────────────────────────────────────────────────
-- Make password_hash nullable (OAuth users have no local password).
ALTER TABLE users ALTER COLUMN password_hash DROP NOT NULL;

-- Make username nullable (OAuth users are identified by email / provider_sub).
ALTER TABLE users ALTER COLUMN username DROP NOT NULL;

-- Add profile columns populated from OAuth provider.
ALTER TABLE users ADD COLUMN IF NOT EXISTS email        VARCHAR(255);
ALTER TABLE users ADD COLUMN IF NOT EXISTS display_name VARCHAR(200);
ALTER TABLE users ADD COLUMN IF NOT EXISTS avatar_url   VARCHAR(500);

-- Unique index on email (case-insensitive, partial — allows rows with null email).
CREATE UNIQUE INDEX IF NOT EXISTS uidx_users_email
    ON users (LOWER(email))
    WHERE email IS NOT NULL;

-- ── 2. user_external_logins ───────────────────────────────────────────────────
-- One row per (user, provider) link.  A user may have Google + Microsoft + Apple.
-- provider_sub is the stable unique ID issued by the provider (never changes).
CREATE TABLE IF NOT EXISTS user_external_logins (
    id           UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id      UUID         NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    provider     VARCHAR(50)  NOT NULL,          -- 'Google' | 'Microsoft' | 'Apple'
    provider_sub VARCHAR(255) NOT NULL,           -- stable ID from provider
    email        VARCHAR(255),                    -- email at time of link (informational)
    created_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_external_login UNIQUE (provider, provider_sub)
);

CREATE INDEX IF NOT EXISTS idx_user_external_logins_user
    ON user_external_logins (user_id);
