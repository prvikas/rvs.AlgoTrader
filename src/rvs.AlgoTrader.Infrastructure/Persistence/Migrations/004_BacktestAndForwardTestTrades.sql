-- Migration 004: backtest_runs + forward_test_sessions + forward_test_trades
-- Applied automatically by Program.cs startup migration runner.

-- ── backtest_runs ────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS backtest_runs (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    strategy_name           VARCHAR(100)    NOT NULL,
    internal_symbol         VARCHAR(50)     NOT NULL,
    timeframe               VARCHAR(10)     NOT NULL,
    from_date               DATE            NOT NULL,
    to_date                 DATE            NOT NULL,
    initial_capital         NUMERIC(18,2)   NOT NULL,
    final_equity            NUMERIC(18,2)   NOT NULL DEFAULT 0,
    total_pnl               NUMERIC(18,2)   NOT NULL DEFAULT 0,
    total_return            NUMERIC(10,6)   NOT NULL DEFAULT 0,
    max_drawdown            NUMERIC(10,6)   NOT NULL DEFAULT 0,
    sharpe_ratio            NUMERIC(10,4)   NOT NULL DEFAULT 0,
    calmar_ratio            NUMERIC(10,4)   NOT NULL DEFAULT 0,
    profit_factor           NUMERIC(10,4)   NOT NULL DEFAULT 0,
    win_rate                NUMERIC(6,4)    NOT NULL DEFAULT 0,
    total_trades            INTEGER         NOT NULL DEFAULT 0,
    win_count               INTEGER         NOT NULL DEFAULT 0,
    loss_count              INTEGER         NOT NULL DEFAULT 0,
    avg_win                 NUMERIC(18,2)   NOT NULL DEFAULT 0,
    avg_loss                NUMERIC(18,2)   NOT NULL DEFAULT 0,
    max_consecutive_losses  INTEGER         NOT NULL DEFAULT 0,
    expectancy_per_trade    NUMERIC(18,2)   NOT NULL DEFAULT 0,
    data_hash               VARCHAR(64),
    trades_json             TEXT            NOT NULL DEFAULT '[]',
    ran_at                  TIMESTAMPTZ     NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_backtest_runs_strategy_symbol
    ON backtest_runs (strategy_name, internal_symbol, timeframe);

CREATE INDEX IF NOT EXISTS idx_backtest_runs_data_hash
    ON backtest_runs (data_hash);

-- ── forward_test_sessions ────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS forward_test_sessions (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    strategy_instance_id    UUID            NOT NULL,
    started_at              TIMESTAMPTZ     NOT NULL,
    ended_at                TIMESTAMPTZ,
    initial_capital         NUMERIC(18,2)   NOT NULL DEFAULT 0,
    final_capital           NUMERIC(18,2)   NOT NULL DEFAULT 0,
    final_pnl               NUMERIC(18,2)   NOT NULL DEFAULT 0,
    trade_count             INTEGER         NOT NULL DEFAULT 0,
    win_rate                NUMERIC(6,4)    NOT NULL DEFAULT 0,
    max_drawdown            NUMERIC(10,6)   NOT NULL DEFAULT 0,
    sharpe_ratio            NUMERIC(10,4)   NOT NULL DEFAULT 0,
    source_backtest_id      UUID,
    status                  VARCHAR(20)     NOT NULL DEFAULT 'Running'
);

CREATE INDEX IF NOT EXISTS idx_fwd_sessions_instance
    ON forward_test_sessions (strategy_instance_id);

-- ── forward_test_trades ──────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS forward_test_trades (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id              UUID            NOT NULL REFERENCES forward_test_sessions(id),
    internal_symbol         VARCHAR(50)     NOT NULL,
    direction               VARCHAR(4)      NOT NULL,
    quantity                INTEGER         NOT NULL,
    entry_price             NUMERIC(18,4)   NOT NULL,
    exit_price              NUMERIC(18,4),
    simulated_fill_price    NUMERIC(18,4),
    slippage                NUMERIC(10,4),
    realized_pnl            NUMERIC(18,2),
    pnl                     NUMERIC(18,2)   NOT NULL DEFAULT 0,
    close_reason            VARCHAR(20),
    entry_time              TIMESTAMPTZ     NOT NULL,
    exit_time               TIMESTAMPTZ,
    opened_at               TIMESTAMPTZ,
    closed_at               TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_fwd_trades_session
    ON forward_test_trades (session_id);
