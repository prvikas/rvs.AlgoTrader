-- Migration 020: Fix critical database integrity issues (#209, #205, #210, #211, #212, #213, #204)
-- Phase 1: Data integrity and financial safeguards

-- #209: FX rates missing rate > 0 check
ALTER TABLE fx_rates
ADD CONSTRAINT ck_fx_rates_rate_positive CHECK (rate > 0);

-- #205: Instruments missing positive checks on numeric fields
ALTER TABLE instruments
ADD CONSTRAINT ck_instruments_price_multiplier_positive CHECK (price_multiplier > 0),
ADD CONSTRAINT ck_instruments_tick_size_positive CHECK (tick_size > 0),
ADD CONSTRAINT ck_instruments_lot_size_positive CHECK (lot_size > 0);

-- #210: Capital allocations - reserved_capital <= allocated_capital
ALTER TABLE capital_allocations
ADD CONSTRAINT ck_capital_allocations_reserved_not_negative CHECK (reserved_capital >= 0),
ADD CONSTRAINT ck_capital_allocations_reserved_not_exceed CHECK (reserved_capital <= allocated_capital);

-- #211: Risk profiles - percentage ranges enforced (0, 1]
ALTER TABLE risk_profiles
ADD CONSTRAINT ck_risk_profiles_max_position_pct CHECK (max_position_size_pct > 0 AND max_position_size_pct <= 1),
ADD CONSTRAINT ck_risk_profiles_daily_loss_pct CHECK (max_daily_loss_pct > 0 AND max_daily_loss_pct <= 1),
ADD CONSTRAINT ck_risk_profiles_max_sector_pct CHECK (max_sector_concentration_pct > 0 AND max_sector_concentration_pct <= 1);

-- #212: Fix correlation_id DEFAULT '' breaking JOINs - change to NULL
-- First, update existing empty strings to NULL
UPDATE spread_positions
SET correlation_id = NULL
WHERE correlation_id = '';

-- Then update the default constraint
ALTER TABLE spread_positions
DROP CONSTRAINT DF_spread_positions_correlation_id;

ALTER TABLE spread_positions
ADD CONSTRAINT DF_spread_positions_correlation_id DEFAULT NULL FOR correlation_id;

-- #204: Add enum validation across multiple tables and columns

-- orders table: status and direction
ALTER TABLE orders
ADD CONSTRAINT ck_orders_status CHECK (status IN ('Pending', 'Placed', 'Partial', 'Filled', 'Cancelled', 'Rejected', 'Failed')),
ADD CONSTRAINT ck_orders_direction CHECK (direction IN ('BUY', 'SELL'));

-- strategy_instances table: status and mode
ALTER TABLE strategy_instances
ADD CONSTRAINT ck_strategy_instances_status CHECK (status IN ('Draft', 'Running', 'Paused', 'Stopped', 'Scheduled', 'Error')),
ADD CONSTRAINT ck_strategy_instances_mode CHECK (mode IN ('Backtest', 'Forward', 'Live'));

-- strategy_runs table: status
ALTER TABLE strategy_runs
ADD CONSTRAINT ck_strategy_runs_status CHECK (status IN ('Running', 'Stopped', 'Paused', 'Completed'));

-- download_jobs table: status
ALTER TABLE download_jobs
ADD CONSTRAINT ck_download_jobs_status CHECK (status IN ('Queued', 'InProgress', 'Completed', 'Failed'));

-- market_breadth_snapshots table: regime
ALTER TABLE market_breadth_snapshots
ADD CONSTRAINT ck_market_breadth_snapshots_regime CHECK (regime IN ('Bullish', 'Neutral', 'Bearish'));

-- forward_test_sessions table: status
ALTER TABLE forward_test_sessions
ADD CONSTRAINT ck_forward_test_sessions_status CHECK (status IN ('Running', 'Paused', 'Stopped', 'Completed', 'Failed'));

-- strategy_scenarios table: status
ALTER TABLE strategy_scenarios
ADD CONSTRAINT ck_strategy_scenarios_status CHECK (status IN ('Draft', 'Backtested', 'ForwardTest', 'Live', 'Archived'));

-- #213: Alert type validation - create check constraint on valid alert type values
-- Alert types should match monitoring_alert_rules or be from a known set of system alerts
ALTER TABLE alert_log
ADD CONSTRAINT ck_alert_log_alert_type CHECK (
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
