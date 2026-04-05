-- Migration 020: Fix critical database integrity issues (#209, #205, #210, #211, #212, #213, #204)
-- Phase 1: Data integrity and financial safeguards
-- PostgreSQL version
--
-- All ADD CONSTRAINT statements use DO $$ EXCEPTION WHEN duplicate_object THEN NULL $$
-- so this migration is idempotent and safe to re-run after a partial failure.

-- #209: FX rates missing rate > 0 check
DO $$ BEGIN
    ALTER TABLE fx_rates ADD CONSTRAINT ck_fx_rates_rate_positive CHECK (rate > 0);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

-- #205: Instruments missing positive checks on numeric fields
DO $$ BEGIN
    ALTER TABLE instruments ADD CONSTRAINT ck_instruments_price_multiplier_positive CHECK (price_multiplier > 0);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;
DO $$ BEGIN
    ALTER TABLE instruments ADD CONSTRAINT ck_instruments_tick_size_positive CHECK (tick_size > 0);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;
DO $$ BEGIN
    ALTER TABLE instruments ADD CONSTRAINT ck_instruments_lot_size_positive CHECK (lot_size > 0);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

-- #210: Capital allocations - reserved_capital <= allocated_capital
DO $$ BEGIN
    ALTER TABLE capital_allocations ADD CONSTRAINT ck_capital_allocations_reserved_not_negative CHECK (reserved_capital >= 0);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;
DO $$ BEGIN
    ALTER TABLE capital_allocations ADD CONSTRAINT ck_capital_allocations_reserved_not_exceed CHECK (reserved_capital <= allocated_capital);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

-- #211: Risk profiles - percentage ranges enforced (0, 1]
-- Add missing column first (omitted from InitialMigration.sql)
ALTER TABLE risk_profiles
ADD COLUMN IF NOT EXISTS max_sector_concentration_pct NUMERIC(6,4) NOT NULL DEFAULT 0.25;

DO $$ BEGIN
    ALTER TABLE risk_profiles ADD CONSTRAINT ck_risk_profiles_max_position_pct CHECK (max_position_size_pct > 0 AND max_position_size_pct <= 1);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;
DO $$ BEGIN
    ALTER TABLE risk_profiles ADD CONSTRAINT ck_risk_profiles_daily_loss_pct CHECK (max_daily_loss_pct > 0 AND max_daily_loss_pct <= 1);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;
DO $$ BEGIN
    ALTER TABLE risk_profiles ADD CONSTRAINT ck_risk_profiles_max_sector_pct CHECK (max_sector_concentration_pct > 0 AND max_sector_concentration_pct <= 1);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

-- #212: Fix correlation_id DEFAULT '' breaking JOINs - change to NULL
UPDATE spread_positions
SET correlation_id = NULL
WHERE correlation_id = '';

-- #204: Add enum validation across multiple tables and columns

-- orders table: status and direction
DO $$ BEGIN
    ALTER TABLE orders ADD CONSTRAINT ck_orders_status CHECK (status IN ('Pending', 'Placed', 'Partial', 'Filled', 'Cancelled', 'Rejected', 'Failed'));
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;
DO $$ BEGIN
    ALTER TABLE orders ADD CONSTRAINT ck_orders_direction CHECK (direction IN ('BUY', 'SELL'));
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

-- strategy_instances table: status and mode
DO $$ BEGIN
    ALTER TABLE strategy_instances ADD CONSTRAINT ck_strategy_instances_status CHECK (status IN ('Draft', 'Running', 'Paused', 'Stopped', 'Scheduled', 'Error'));
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;
DO $$ BEGIN
    ALTER TABLE strategy_instances ADD CONSTRAINT ck_strategy_instances_mode CHECK (mode IN ('Backtest', 'Forward', 'Live'));
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

-- strategy_runs table: status
DO $$ BEGIN
    ALTER TABLE strategy_runs ADD CONSTRAINT ck_strategy_runs_status CHECK (status IN ('Running', 'Stopped', 'Paused', 'Completed'));
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

-- download_jobs table: status
DO $$ BEGIN
    ALTER TABLE download_jobs ADD CONSTRAINT ck_download_jobs_status CHECK (status IN ('Queued', 'InProgress', 'Completed', 'Failed'));
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

-- market_breadth_snapshots table: regime
DO $$ BEGIN
    ALTER TABLE market_breadth_snapshots ADD CONSTRAINT ck_market_breadth_snapshots_regime CHECK (regime IN ('Bullish', 'Neutral', 'Bearish'));
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

-- forward_test_sessions table: status
DO $$ BEGIN
    ALTER TABLE forward_test_sessions ADD CONSTRAINT ck_forward_test_sessions_status CHECK (status IN ('Running', 'Paused', 'Stopped', 'Completed', 'Failed'));
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

-- strategy_scenarios table: status
DO $$ BEGIN
    ALTER TABLE strategy_scenarios ADD CONSTRAINT ck_strategy_scenarios_status CHECK (status IN ('Draft', 'Backtested', 'ForwardTest', 'Live', 'Archived'));
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

-- #213: Alert type validation
DO $$ BEGIN
    ALTER TABLE alert_log ADD CONSTRAINT ck_alert_log_alert_type CHECK (
        alert_type IN (
            'DailyLossLimit',
            'PortfolioRisk',
            'PositionLimit',
            'SectorConcentration',
            'CorrelationWarning',
            'DataFeedDown',
            'BrokerConnectionLost',
            'StrategyError',
            'BacktestError',
            'CalendarEvent',
            'MarketClosed'
        )
    );
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;
