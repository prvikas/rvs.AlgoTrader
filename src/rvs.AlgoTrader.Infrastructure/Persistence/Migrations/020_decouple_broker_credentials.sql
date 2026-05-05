-- Migration 020: Decouple broker_credentials from strategy_instances
-- Problem: broker_credentials used strategy_instance_id as PK + FK, meaning credentials
--          were per-strategy-run, not per-broker.  A single broker (e.g. Zerodha) should
--          have one credential row reusable across many strategy instances.
-- Fix:     Add a standalone broker_name PK, drop the FK to strategy_instances,
--          keep strategy_instance_id as a nullable reference for backward compat.
-- All operations are idempotent.

-- 1. Add new surrogate PK column (broker_name) if not already present
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'broker_credentials' AND column_name = 'broker_name'
    ) THEN
        ALTER TABLE broker_credentials ADD COLUMN broker_name VARCHAR(50) NOT NULL DEFAULT '';
    END IF;
END $$;

-- 2. Back-fill broker_name from the linked strategy_instances row
UPDATE broker_credentials bc
SET    broker_name = si.broker_name
FROM   strategy_instances si
WHERE  bc.strategy_instance_id = si.id
  AND  bc.broker_name = '';

-- 3. Fallback: if no strategy instance linked, default to 'Unknown'
UPDATE broker_credentials
SET    broker_name = 'Unknown'
WHERE  broker_name = '';

-- 4. Drop the old FK constraint (strategy_instance_id → strategy_instances.id)
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

-- 5. Drop the old PK (was strategy_instance_id)
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

-- 6. Make strategy_instance_id nullable (it is now just an optional reference, not PK)
ALTER TABLE broker_credentials
    ALTER COLUMN strategy_instance_id DROP NOT NULL;

-- 7. Add new PK on broker_name
ALTER TABLE broker_credentials
    ADD CONSTRAINT pk_broker_credentials PRIMARY KEY (broker_name);

-- 8. Add index on strategy_instance_id for joins (optional lookup)
CREATE INDEX IF NOT EXISTS ix_broker_credentials_strategy_instance_id
    ON broker_credentials(strategy_instance_id)
    WHERE strategy_instance_id IS NOT NULL;
