-- Migration 059: Create broker metadata lookup tables (pure additive)
-- Creates: timezones, brokers, exchanges, product_types
-- No existing tables are modified; this migration is safe to re-run.

-- ── 1. timezones ─────────────────────────────────────────────────────────────
-- Seed IANA timezone IDs for all supported markets
CREATE TABLE IF NOT EXISTS timezones (
    id       SMALLINT PRIMARY KEY GENERATED ALWAYS AS IDENTITY,
    iana_id  VARCHAR(50) NOT NULL UNIQUE
);

INSERT INTO timezones (iana_id) VALUES
    ('Asia/Kolkata'),
    ('America/New_York'),
    ('Europe/London'),
    ('Asia/Singapore'),
    ('America/Chicago'),
    ('Asia/Tokyo'),
    ('Europe/Frankfurt')
ON CONFLICT DO NOTHING;

-- ── 2. brokers ──────────────────────────────────────────────────────────────
-- Master table of supported brokers with their default timezone
CREATE TABLE IF NOT EXISTS brokers (
    id           SMALLINT PRIMARY KEY GENERATED ALWAYS AS IDENTITY,
    name         VARCHAR(50) NOT NULL UNIQUE,
    display_name VARCHAR(100),
    timezone_id  SMALLINT NOT NULL REFERENCES timezones(id),
    is_active    BOOLEAN NOT NULL DEFAULT true
);

INSERT INTO brokers (name, display_name, timezone_id, is_active) VALUES
    ('Zerodha',  'Zerodha',  (SELECT id FROM timezones WHERE iana_id = 'Asia/Kolkata'), true),
    ('Upstox',   'Upstox',   (SELECT id FROM timezones WHERE iana_id = 'Asia/Kolkata'), true),
    ('MStock',   'MStock',   (SELECT id FROM timezones WHERE iana_id = 'Asia/Kolkata'), true),
    ('Dhan',     'Dhan',     (SELECT id FROM timezones WHERE iana_id = 'Asia/Kolkata'), true),
    ('IBKR',     'Interactive Brokers', (SELECT id FROM timezones WHERE iana_id = 'America/New_York'), true),
    ('Alpaca',   'Alpaca',   (SELECT id FROM timezones WHERE iana_id = 'America/New_York'), true),
    ('IGGroup',  'IG Group', (SELECT id FROM timezones WHERE iana_id = 'Europe/London'), true)
ON CONFLICT DO NOTHING;

-- ── 3. exchanges ────────────────────────────────────────────────────────────
-- Normalized exchange codes with market codes and timezones
CREATE TABLE IF NOT EXISTS exchanges (
    id          SMALLINT PRIMARY KEY GENERATED ALWAYS AS IDENTITY,
    code        VARCHAR(10) NOT NULL UNIQUE,
    market_code CHAR(2) NOT NULL,
    timezone_id SMALLINT NOT NULL REFERENCES timezones(id)
);

INSERT INTO exchanges (code, market_code, timezone_id) VALUES
    ('NSE',    'IN', (SELECT id FROM timezones WHERE iana_id = 'Asia/Kolkata')),
    ('BSE',    'IN', (SELECT id FROM timezones WHERE iana_id = 'Asia/Kolkata')),
    ('NFO',    'IN', (SELECT id FROM timezones WHERE iana_id = 'Asia/Kolkata')),
    ('MCX',    'IN', (SELECT id FROM timezones WHERE iana_id = 'Asia/Kolkata')),
    ('CDS',    'IN', (SELECT id FROM timezones WHERE iana_id = 'Asia/Kolkata')),
    ('NYSE',   'US', (SELECT id FROM timezones WHERE iana_id = 'America/New_York')),
    ('NASDAQ', 'US', (SELECT id FROM timezones WHERE iana_id = 'America/New_York')),
    ('LSE',    'UK', (SELECT id FROM timezones WHERE iana_id = 'Europe/London'))
ON CONFLICT DO NOTHING;

-- ── 4. product_types ────────────────────────────────────────────────────────
-- Normalized product type codes
CREATE TABLE IF NOT EXISTS product_types (
    id   SMALLINT PRIMARY KEY GENERATED ALWAYS AS IDENTITY,
    code VARCHAR(10) NOT NULL UNIQUE
);

INSERT INTO product_types (code) VALUES
    ('MIS'),
    ('NRML'),
    ('CNC'),
    ('BO'),
    ('CO'),
    ('DAY'),
    ('GTC')
ON CONFLICT DO NOTHING;
