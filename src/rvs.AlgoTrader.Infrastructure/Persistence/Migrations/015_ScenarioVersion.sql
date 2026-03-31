-- Migration 015: Add version column to strategy_scenarios
-- Monotonically incremented each time parameters_json_override changes.
-- Default 1 so existing rows start at version 1.

ALTER TABLE strategy_scenarios
    ADD COLUMN IF NOT EXISTS version INTEGER NOT NULL DEFAULT 1;
