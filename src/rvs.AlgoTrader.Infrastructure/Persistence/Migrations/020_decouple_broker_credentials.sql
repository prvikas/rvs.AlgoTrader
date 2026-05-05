-- Migration 020: Decouple broker_credentials from strategy_instances
--               + Add per-broker market timezone
--
-- Problem 1: broker_credentials used strategy_instance_id as PK + FK.
--            Credentials must belong to the broker, not a strategy run.
-- Problem 2: Timezone was hardcoded as 'Asia/Kolkata' throughout the codebase.
--            A broker's market timezone is a BROKER property, not a config-file property.
--            UK/US/India markets can run simultaneously; the tz must be per-broker row.
-- All operations are idempotent.

-- ─── Step 1: Add broker_name column ───────────────────────────────────────────
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'broker_credentials' AND column_name = 'broker_name'
    ) THEN
        ALTER TABLE broker_credentials ADD COLUMN broker_name VARCHAR(50) NOT NULL DEFAULT '';
    END IF;
END $$;

-- ─── Step 2: Add market_timezone_id column (IANA tz, e.g. 'Asia/Kolkata') ────
-- Default = 'Asia/Kolkata' for backward compat with existing India-only rows.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'broker_credentials' AND column_name = 'market_timezone_id'
    ) THEN
        ALTER TABLE broker_credentials
            ADD COLUMN market_timezone_id VARCHAR(50) NOT NULL DEFAULT 'Asia/Kolkata';
    END IF;
END $$;

-- ─── Step 3: Back-fill broker_name from linked strategy_instances ────────────
UPDATE broker_credentials bc
SET    broker_name = si.broker_name
FROM   strategy_instances si
WHERE  bc.strategy_instance_id = si.id
  AND  bc.broker_name = '';

UPDATE broker_credentials
SET    broker_name = 'Unknown'
WHERE  broker_name = '';

-- ─── Step 4: Drop old FK (strategy_instance_id → strategy_instances.id) ──────
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'fk_broker_credentials_instance'
          AND table_name = 'broker_credentials'
    ) THEN
        ALTER TABLE broker_credentials DROP CONSTRAINT fk_broker_credentials_instance;
    END IF;
END $$;

-- ─── Step 5: Drop old PK (was strategy_instance_id) ─────────────────────────
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_type = 'PRIMARY KEY'
          AND table_name = 'broker_credentials'
    ) THEN
        ALTER TABLE broker_credentials DROP CONSTRAINT broker_credentials_pkey;
    END IF;
END $$;

-- ─── Step 6: Make strategy_instance_id nullable ──────────────────────────────
ALTER TABLE broker_credentials
    ALTER COLUMN strategy_instance_id DROP NOT NULL;

-- ─── Step 7: New PK on broker_name ──────────────────────────────────────────
ALTER TABLE broker_credentials
    ADD CONSTRAINT pk_broker_credentials PRIMARY KEY (broker_name);

-- ─── Step 8: Index on strategy_instance_id for optional diagnostic joins ─────
CREATE INDEX IF NOT EXISTS ix_broker_credentials_strategy_instance_id
    ON broker_credentials(strategy_instance_id)
    WHERE strategy_instance_id IS NOT NULL;

-- ─── Reference data: seed known brokers with correct timezones ───────────────
-- Upsert so re-running is safe. Add new brokers here as needed.
INSERT INTO broker_credentials (broker_name, market_timezone_id)
VALUES
    ('Zerodha',   'Asia/Kolkata'),
    ('Upstox',    'Asia/Kolkata'),
    ('MStock',    'Asia/Kolkata'),
    ('AngelOne',  'Asia/Kolkata'),
    ('IBKR',      'America/New_York'),
    ('Tradier',   'America/New_York'),
    ('AlphaVantage', 'America/New_York'),
    ('IGGroup',   'Europe/London'),
    ('Saxo',      'Europe/London'),
    ('MAS',       'Asia/Singapore')
ON CONFLICT (broker_name) DO UPDATE
    SET market_timezone_id = EXCLUDED.market_timezone_id
    WHERE broker_credentials.market_timezone_id = 'Asia/Kolkata';  -- only overwrite if still default
