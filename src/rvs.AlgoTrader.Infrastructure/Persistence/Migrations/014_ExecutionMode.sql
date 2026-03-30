-- Migration 014: Add execution_mode column to strategy_instances
-- Supports ExecutionMode enum: Live (default), Paper, Backtest

ALTER TABLE strategy_instances
    ADD COLUMN IF NOT EXISTS execution_mode TEXT NOT NULL DEFAULT 'Live';
