-- rvs.AlgoTrader Initial Migration
-- PostgreSQL (TimescaleDB optional)
-- Idempotent — safe to run multiple times.

-- Enable TimescaleDB (optional - skip if not installed)
CREATE EXTENSION IF NOT EXISTS timescaledb;

-- instruments
CREATE TABLE IF NOT EXISTS instruments (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    internal_symbol VARCHAR(50) NOT NULL UNIQUE,
    trading_symbol VARCHAR(50) NOT NULL,
    exchange VARCHAR(10) NOT NULL,
    instrument_type VARCHAR(20) NOT NULL DEFAULT 'EQ',
    name VARCHAR(200),
    underlying VARCHAR(50),
    strike_price NUMERIC(18,4),
    option_type INTEGER,
    expiry DATE,
    lot_size INT DEFAULT 1,
    tick_size NUMERIC(18,4) DEFAULT 0.05,
    is_active BOOL NOT NULL DEFAULT true,
    zerodha_token VARCHAR(20),
    upstox_token VARCHAR(100),
    mstock_token VARCHAR(100),
    last_refreshed_at TIMESTAMPTZ,
    UNIQUE(trading_symbol, exchange)
);
CREATE INDEX IF NOT EXISTS idx_instruments_active ON instruments(is_active) WHERE is_active = true;

-- candles (TimescaleDB hypertable if available, else regular table)
CREATE TABLE IF NOT EXISTS candles (
    internal_symbol VARCHAR(50) NOT NULL,
    timeframe VARCHAR(10) NOT NULL,
    open_time TIMESTAMPTZ NOT NULL,
    close_time TIMESTAMPTZ NOT NULL,
    open NUMERIC(18,4) NOT NULL,
    high NUMERIC(18,4) NOT NULL,
    low NUMERIC(18,4) NOT NULL,
    close NUMERIC(18,4) NOT NULL,
    volume BIGINT NOT NULL DEFAULT 0,
    is_closed BOOL NOT NULL DEFAULT true,
    PRIMARY KEY (internal_symbol, timeframe, open_time)
);
CREATE INDEX IF NOT EXISTS idx_candles_symbol_tf ON candles(internal_symbol, timeframe, open_time DESC);

-- risk_profiles
CREATE TABLE IF NOT EXISTS risk_profiles (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(200) NOT NULL,
    max_position_size_pct NUMERIC(6,4) NOT NULL DEFAULT 0.02,
    max_daily_loss_pct NUMERIC(6,4) NOT NULL DEFAULT 0.05,
    max_open_positions INT NOT NULL DEFAULT 5,
    max_total_capital_deployed NUMERIC(18,4),
    max_trades_per_day INT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- strategy_instances
CREATE TABLE IF NOT EXISTS strategy_instances (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(200) NOT NULL,
    strategy_name VARCHAR(100) NOT NULL,
    internal_symbol VARCHAR(50) NOT NULL,
    timeframe VARCHAR(10) NOT NULL,
    broker_name VARCHAR(50) NOT NULL,
    mode VARCHAR(20) NOT NULL DEFAULT 'Live',
    status VARCHAR(30) NOT NULL DEFAULT 'Stopped',
    parameters_json JSONB NOT NULL DEFAULT '{}',
    schedule_json JSONB,
    failure_behavior_json JSONB,
    auto_resume_on_restart BOOL NOT NULL DEFAULT false,
    risk_profile_id UUID REFERENCES risk_profiles(id),
    allocated_capital NUMERIC(18,4),
    exchange VARCHAR(10),
    product_type VARCHAR(10),
    lot_size INT,
    broker_token VARCHAR(100),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_strategy_instances_status ON strategy_instances(status);
CREATE INDEX IF NOT EXISTS idx_strategy_instances_symbol ON strategy_instances(internal_symbol, status);

-- strategy_runs
CREATE TABLE IF NOT EXISTS strategy_runs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    strategy_instance_id UUID NOT NULL REFERENCES strategy_instances(id),
    broker_name VARCHAR(50),
    mode VARCHAR(20) NOT NULL,
    status VARCHAR(30) NOT NULL,
    started_at TIMESTAMPTZ NOT NULL,
    ended_at TIMESTAMPTZ,
    end_reason VARCHAR(500),
    total_pnl NUMERIC(18,4) DEFAULT 0,
    trade_count INT DEFAULT 0,
    win_count INT DEFAULT 0,
    loss_count INT DEFAULT 0,
    max_drawdown NUMERIC(18,4) DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_strategy_runs_instance ON strategy_runs(strategy_instance_id);
CREATE INDEX IF NOT EXISTS idx_strategy_runs_status ON strategy_runs(status);

-- orders
CREATE TABLE IF NOT EXISTS orders (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    broker_name VARCHAR(50) NOT NULL,
    broker_order_id VARCHAR(100),
    internal_symbol VARCHAR(50) NOT NULL,
    order_type VARCHAR(20) NOT NULL,
    direction VARCHAR(10) NOT NULL,
    status VARCHAR(30) NOT NULL DEFAULT 'Pending',
    quantity INT NOT NULL,
    filled_quantity INT DEFAULT 0,
    price NUMERIC(18,4),
    trigger_price NUMERIC(18,4),
    fill_price NUMERIC(18,4),
    trailing_sl NUMERIC(18,4),
    trailing_tp NUMERIC(18,4),
    exchange VARCHAR(10) NOT NULL DEFAULT 'NSE',
    product_type VARCHAR(10) NOT NULL DEFAULT 'MIS',
    idempotency_key VARCHAR(100) NOT NULL UNIQUE,
    strategy_run_id UUID REFERENCES strategy_runs(id),
    correlation_id VARCHAR(100),
    rejection_reason TEXT,
    placed_at TIMESTAMPTZ,
    filled_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_orders_broker_status ON orders(broker_name, status);
CREATE INDEX IF NOT EXISTS idx_orders_strategy_run ON orders(strategy_run_id);
CREATE INDEX IF NOT EXISTS idx_orders_broker_order_id ON orders(broker_order_id);
CREATE UNIQUE INDEX IF NOT EXISTS idx_orders_idempotency ON orders(idempotency_key);

-- positions
CREATE TABLE IF NOT EXISTS positions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    broker_name VARCHAR(50) NOT NULL,
    internal_symbol VARCHAR(50) NOT NULL,
    quantity INT NOT NULL,
    average_price NUMERIC(18,4) NOT NULL,
    entry_price NUMERIC(18,4),
    last_price NUMERIC(18,4),
    realized_pnl NUMERIC(18,4) DEFAULT 0,
    unrealized_pnl NUMERIC(18,4) DEFAULT 0,
    stop_loss NUMERIC(18,4),
    take_profit NUMERIC(18,4),
    product_type VARCHAR(10) NOT NULL DEFAULT 'MIS',
    is_open BOOL NOT NULL DEFAULT true,
    strategy_run_id UUID REFERENCES strategy_runs(id),
    correlation_id VARCHAR(100),
    close_reason VARCHAR(50),
    opened_at TIMESTAMPTZ,
    closed_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_positions_broker_open ON positions(broker_name, is_open);
CREATE INDEX IF NOT EXISTS idx_positions_strategy_run ON positions(strategy_run_id);
CREATE INDEX IF NOT EXISTS idx_positions_symbol_open ON positions(internal_symbol, is_open);

-- capital_allocations
CREATE TABLE IF NOT EXISTS capital_allocations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    strategy_instance_id UUID NOT NULL REFERENCES strategy_instances(id),
    broker_name VARCHAR(50) NOT NULL DEFAULT '',
    allocated_capital NUMERIC(18,4) NOT NULL,
    reserved_capital NUMERIC(18,4) NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(strategy_instance_id)
);

-- forward_test_sessions and forward_test_trades are defined in 004_BacktestAndForwardTestTrades.sql
-- which has the full schema including max_drawdown, sharpe_ratio, source_backtest_id.
-- Do not define them here to avoid duplicate IF NOT EXISTS silently masking missing columns.

-- audit_log (append-only, SEBI compliance)
CREATE TABLE IF NOT EXISTS audit_log (
    id BIGSERIAL PRIMARY KEY,
    event_type VARCHAR(100) NOT NULL,
    entity_type VARCHAR(50),
    entity_id VARCHAR(100),
    actor VARCHAR(100),
    details JSONB,
    correlation_id VARCHAR(100),
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_audit_log_occurred ON audit_log(occurred_at DESC);
CREATE INDEX IF NOT EXISTS idx_audit_log_entity ON audit_log(entity_type, entity_id);
-- DROP IF EXISTS before CREATE so re-runs are safe (no IF NOT EXISTS syntax for rules)
DROP RULE IF EXISTS no_update_audit ON audit_log;
CREATE RULE no_update_audit AS ON UPDATE TO audit_log DO INSTEAD NOTHING;
DROP RULE IF EXISTS no_delete_audit ON audit_log;
CREATE RULE no_delete_audit AS ON DELETE TO audit_log DO INSTEAD NOTHING;

-- signal_journal
CREATE TABLE IF NOT EXISTS signal_journal (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    strategy_instance_id UUID NOT NULL REFERENCES strategy_instances(id),
    strategy_name VARCHAR(100),
    internal_symbol VARCHAR(50) NOT NULL,
    timeframe VARCHAR(10) NOT NULL,
    signal VARCHAR(10) NOT NULL,
    entry_price NUMERIC(18,4),
    stop_loss NUMERIC(18,4),
    take_profit NUMERIC(18,4),
    reason TEXT,
    skipped_reason VARCHAR(50),
    correlation_id VARCHAR(100),
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_signal_journal_instance ON signal_journal(strategy_instance_id, occurred_at DESC);

-- app_config
CREATE TABLE IF NOT EXISTS app_config (
    key VARCHAR(200) PRIMARY KEY,
    value TEXT NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- monitoring_alert_rules
CREATE TABLE IF NOT EXISTS monitoring_alert_rules (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    alert_type VARCHAR(100) NOT NULL,
    metric_name VARCHAR(100) NOT NULL,
    operator VARCHAR(10) NOT NULL,
    threshold_value DOUBLE PRECISION NOT NULL,
    severity VARCHAR(20) NOT NULL,
    channels TEXT[] NOT NULL DEFAULT '{}',
    is_active BOOL NOT NULL DEFAULT true,
    message_template TEXT NOT NULL
);

-- broker_latency_log
CREATE TABLE IF NOT EXISTS broker_latency_log (
    id BIGSERIAL PRIMARY KEY,
    broker_name VARCHAR(50) NOT NULL,
    p50_ms DOUBLE PRECISION NOT NULL,
    p95_ms DOUBLE PRECISION NOT NULL,
    p99_ms DOUBLE PRECISION NOT NULL,
    sample_count INT NOT NULL,
    measured_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- watchlists
CREATE TABLE IF NOT EXISTS watchlists (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(200) NOT NULL,
    created_by VARCHAR(100) NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- watchlist_symbols
CREATE TABLE IF NOT EXISTS watchlist_symbols (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    watchlist_id UUID NOT NULL REFERENCES watchlists(id) ON DELETE CASCADE,
    internal_symbol VARCHAR(50) NOT NULL,
    sort_order INT NOT NULL DEFAULT 0,
    added_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(watchlist_id, internal_symbol)
);

-- symbol_data_preferences
CREATE TABLE IF NOT EXISTS symbol_data_preferences (
    internal_symbol VARCHAR(50) PRIMARY KEY,
    preferred_broker VARCHAR(50),
    preferred_timeframes TEXT[] DEFAULT '{}',
    is_monitored BOOL NOT NULL DEFAULT false,
    notes TEXT,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ── EF compatibility: restore DB-level defaults stripped by EnsureCreatedAsync ──
-- EF creates columns without DB-level defaults (it manages values in C#).
-- When migrations INSERT rows without specifying these columns, Postgres
-- returns NOT NULL violations unless the defaults are set here first.
-- All ALTER COLUMN SET DEFAULT calls are idempotent.

-- UUID primary keys
ALTER TABLE instruments              ALTER COLUMN id SET DEFAULT gen_random_uuid();
ALTER TABLE risk_profiles            ALTER COLUMN id SET DEFAULT gen_random_uuid();
ALTER TABLE strategy_instances       ALTER COLUMN id SET DEFAULT gen_random_uuid();
ALTER TABLE strategy_runs            ALTER COLUMN id SET DEFAULT gen_random_uuid();
ALTER TABLE orders                   ALTER COLUMN id SET DEFAULT gen_random_uuid();
ALTER TABLE positions                ALTER COLUMN id SET DEFAULT gen_random_uuid();
ALTER TABLE capital_allocations      ALTER COLUMN id SET DEFAULT gen_random_uuid();
ALTER TABLE signal_journal           ALTER COLUMN id SET DEFAULT gen_random_uuid();
ALTER TABLE monitoring_alert_rules   ALTER COLUMN id SET DEFAULT gen_random_uuid();
ALTER TABLE watchlists               ALTER COLUMN id SET DEFAULT gen_random_uuid();
ALTER TABLE watchlist_symbols        ALTER COLUMN id SET DEFAULT gen_random_uuid();
-- forward_test_sessions and forward_test_trades id defaults are set in 004

-- Timestamp defaults (EF sets these in C# via IClock; DB-level fallback needed for SQL inserts)
ALTER TABLE risk_profiles            ALTER COLUMN created_at           SET DEFAULT NOW();
ALTER TABLE risk_profiles            ALTER COLUMN updated_at           SET DEFAULT NOW();
ALTER TABLE strategy_instances       ALTER COLUMN created_at           SET DEFAULT NOW();
ALTER TABLE strategy_instances       ALTER COLUMN updated_at           SET DEFAULT NOW();
ALTER TABLE orders                   ALTER COLUMN created_at           SET DEFAULT NOW();
ALTER TABLE orders                   ALTER COLUMN updated_at           SET DEFAULT NOW();
ALTER TABLE positions                ALTER COLUMN created_at           SET DEFAULT NOW();
ALTER TABLE positions                ALTER COLUMN updated_at           SET DEFAULT NOW();
ALTER TABLE capital_allocations      ALTER COLUMN created_at           SET DEFAULT NOW();
ALTER TABLE capital_allocations      ALTER COLUMN updated_at           SET DEFAULT NOW();
ALTER TABLE watchlists               ALTER COLUMN created_at           SET DEFAULT NOW();
ALTER TABLE watchlists               ALTER COLUMN updated_at           SET DEFAULT NOW();
ALTER TABLE watchlist_symbols        ALTER COLUMN added_at             SET DEFAULT NOW();

-- Boolean defaults
ALTER TABLE instruments              ALTER COLUMN is_active            SET DEFAULT true;
ALTER TABLE watchlist_symbols        ALTER COLUMN sort_order           SET DEFAULT 0;
ALTER TABLE symbol_data_preferences  ALTER COLUMN is_monitored         SET DEFAULT false;
ALTER TABLE symbol_data_preferences  ALTER COLUMN preferred_timeframes SET DEFAULT '{}';
