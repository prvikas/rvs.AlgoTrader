-- rvs.AlgoTrader Initial Migration
-- PostgreSQL (TimescaleDB optional)

-- Enable TimescaleDB (optional - skip if not installed)
CREATE EXTENSION IF NOT EXISTS timescaledb;

-- instruments
CREATE TABLE instruments (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    internal_symbol VARCHAR(50) NOT NULL UNIQUE,
    trading_symbol VARCHAR(50) NOT NULL,
    exchange VARCHAR(10) NOT NULL,
    instrument_type VARCHAR(20) NOT NULL DEFAULT 'EQ',
    name VARCHAR(200),
    lot_size INT DEFAULT 1,
    tick_size NUMERIC(18,4) DEFAULT 0.05,
    is_active BOOL NOT NULL DEFAULT true,
    zerodha_token VARCHAR(20),
    upstox_token VARCHAR(100),
    mstock_token VARCHAR(100),
    last_refreshed_at TIMESTAMPTZ,
    UNIQUE(trading_symbol, exchange)
);
CREATE INDEX idx_instruments_active ON instruments(is_active) WHERE is_active = true;

-- candles (TimescaleDB hypertable if available, else regular table)
CREATE TABLE candles (
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
CREATE INDEX idx_candles_symbol_tf ON candles(internal_symbol, timeframe, open_time DESC);

-- risk_profiles
CREATE TABLE risk_profiles (
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
CREATE TABLE strategy_instances (
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
CREATE INDEX idx_strategy_instances_status ON strategy_instances(status);
CREATE INDEX idx_strategy_instances_symbol ON strategy_instances(internal_symbol, status);

-- strategy_runs
CREATE TABLE strategy_runs (
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
CREATE INDEX idx_strategy_runs_instance ON strategy_runs(strategy_instance_id);
CREATE INDEX idx_strategy_runs_status ON strategy_runs(status);

-- orders
CREATE TABLE orders (
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
CREATE INDEX idx_orders_broker_status ON orders(broker_name, status);
CREATE INDEX idx_orders_strategy_run ON orders(strategy_run_id);
CREATE INDEX idx_orders_broker_order_id ON orders(broker_order_id);
CREATE UNIQUE INDEX idx_orders_idempotency ON orders(idempotency_key);

-- positions
CREATE TABLE positions (
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
CREATE INDEX idx_positions_broker_open ON positions(broker_name, is_open);
CREATE INDEX idx_positions_strategy_run ON positions(strategy_run_id);
CREATE INDEX idx_positions_symbol_open ON positions(internal_symbol, is_open);

-- capital_allocations
CREATE TABLE capital_allocations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    strategy_instance_id UUID NOT NULL REFERENCES strategy_instances(id),
    broker_name VARCHAR(50) NOT NULL DEFAULT '',
    allocated_capital NUMERIC(18,4) NOT NULL,
    reserved_capital NUMERIC(18,4) NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(strategy_instance_id)
);

-- forward_test_sessions
CREATE TABLE forward_test_sessions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    strategy_instance_id UUID NOT NULL REFERENCES strategy_instances(id),
    started_at TIMESTAMPTZ NOT NULL,
    ended_at TIMESTAMPTZ,
    initial_capital NUMERIC(18,4) NOT NULL,
    final_capital NUMERIC(18,4) DEFAULT 0,
    final_pnl NUMERIC(18,4) DEFAULT 0,
    trade_count INT DEFAULT 0,
    win_rate NUMERIC(6,4) DEFAULT 0,
    status VARCHAR(30) DEFAULT 'Running'
);

-- forward_test_trades
CREATE TABLE forward_test_trades (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id UUID NOT NULL REFERENCES forward_test_sessions(id),
    internal_symbol VARCHAR(50) NOT NULL,
    direction VARCHAR(10) NOT NULL,
    quantity INT NOT NULL,
    entry_price NUMERIC(18,4) NOT NULL,
    exit_price NUMERIC(18,4),
    simulated_fill_price NUMERIC(18,4),
    slippage NUMERIC(18,4) DEFAULT 0,
    pnl NUMERIC(18,4) DEFAULT 0,
    realized_pnl NUMERIC(18,4),
    close_reason VARCHAR(100),
    entry_time TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    exit_time TIMESTAMPTZ,
    opened_at TIMESTAMPTZ,
    closed_at TIMESTAMPTZ
);

-- audit_log (append-only, SEBI compliance)
CREATE TABLE audit_log (
    id BIGSERIAL PRIMARY KEY,
    event_type VARCHAR(100) NOT NULL,
    entity_type VARCHAR(50),
    entity_id VARCHAR(100),
    actor VARCHAR(100),
    details JSONB,
    correlation_id VARCHAR(100),
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_audit_log_occurred ON audit_log(occurred_at DESC);
CREATE INDEX idx_audit_log_entity ON audit_log(entity_type, entity_id);
CREATE RULE no_update_audit AS ON UPDATE TO audit_log DO INSTEAD NOTHING;
CREATE RULE no_delete_audit AS ON DELETE TO audit_log DO INSTEAD NOTHING;

-- signal_journal
CREATE TABLE signal_journal (
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
CREATE INDEX idx_signal_journal_instance ON signal_journal(strategy_instance_id, occurred_at DESC);

-- app_config
CREATE TABLE app_config (
    key VARCHAR(200) PRIMARY KEY,
    value TEXT NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- monitoring_alert_rules
CREATE TABLE monitoring_alert_rules (
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
CREATE TABLE broker_latency_log (
    id BIGSERIAL PRIMARY KEY,
    broker_name VARCHAR(50) NOT NULL,
    p50_ms DOUBLE PRECISION NOT NULL,
    p95_ms DOUBLE PRECISION NOT NULL,
    p99_ms DOUBLE PRECISION NOT NULL,
    sample_count INT NOT NULL,
    measured_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- watchlists
CREATE TABLE watchlists (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(200) NOT NULL,
    created_by VARCHAR(100) NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- watchlist_symbols
CREATE TABLE watchlist_symbols (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    watchlist_id UUID NOT NULL REFERENCES watchlists(id) ON DELETE CASCADE,
    internal_symbol VARCHAR(50) NOT NULL,
    sort_order INT NOT NULL DEFAULT 0,
    added_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(watchlist_id, internal_symbol)
);

-- symbol_data_preferences
CREATE TABLE symbol_data_preferences (
    internal_symbol VARCHAR(50) PRIMARY KEY,
    preferred_broker VARCHAR(50),
    preferred_timeframes TEXT[] DEFAULT '{}',
    is_monitored BOOL NOT NULL DEFAULT false,
    notes TEXT,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
