-- Migration 036: strategy_definitions table
--
-- Stores UI-designed strategies (GenericRules) as first-class entities.
-- Each row holds the full Strategy JSON produced by StrategyDefinitionPage.
-- strategy_instances references a strategy_definition when using GenericRules.
--
-- Separation of concerns:
--   strategy_definitions — WHAT the strategy does (rules, indicators, conditions)
--   strategy_instances   — HOW it is deployed (broker, capital, schedule)
--   strategy_scenarios   — HOW it is tested (parameters, date range, backtest run)

CREATE TABLE IF NOT EXISTS strategy_definitions (
    id              UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    name            TEXT         NOT NULL,
    description     TEXT,
    trading_style   TEXT         NOT NULL DEFAULT 'Intraday',
    -- Full Strategy JSON from StrategyDefinitionPage (indicators + longEntry/Exit/shortEntry/Exit)
    definition_json JSONB        NOT NULL DEFAULT '{}',
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_strategy_definitions_name
    ON strategy_definitions (name);

-- Allow strategy_instances to optionally reference a definition
-- (existing rows retain null = classic hardcoded strategy)
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'strategy_instances'
          AND column_name = 'strategy_definition_id'
    ) THEN
        ALTER TABLE strategy_instances
            ADD COLUMN strategy_definition_id UUID
            REFERENCES strategy_definitions(id) ON DELETE SET NULL;
    END IF;
END$$;
