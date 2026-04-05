-- Migration 030: Add created_by to strategy_instances.
-- This column is mapped by EF Core (StrategyInstanceConfiguration) but was never
-- present in the original InitialMigration.sql schema.

ALTER TABLE strategy_instances
    ADD COLUMN IF NOT EXISTS created_by VARCHAR(200) NOT NULL DEFAULT '';
