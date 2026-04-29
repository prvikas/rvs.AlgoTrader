-- Migration 049: Link strategy_instances back to strategy_definition_scenarios
--
-- Adds definition_scenario_id to strategy_instances so the ScenariosTab in the UI
-- can find and stop/promote the running instance for a given UI scenario without
-- needing a separate lookup round-trip.
--
-- Set during PromoteBacktestToForwardTest and copied on PromoteForwardTestToLive.
-- Nullable: classic (non-GenericRules) strategy instances leave this NULL.

ALTER TABLE strategy_instances
  ADD COLUMN IF NOT EXISTS definition_scenario_id UUID
    REFERENCES strategy_definition_scenarios(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS idx_strategy_instances_definition_scenario
  ON strategy_instances (definition_scenario_id)
  WHERE definition_scenario_id IS NOT NULL;
