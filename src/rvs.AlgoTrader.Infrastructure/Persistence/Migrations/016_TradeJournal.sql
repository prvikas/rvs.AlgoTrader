-- 016_TradeJournal.sql
-- Trade Journal: per-trade R-multiple, MAE/MFE, notes, tags, tax classification.
-- Idempotent — safe to run multiple times.

CREATE TABLE IF NOT EXISTS trade_journal_entries (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    strategy_instance_id    UUID            NOT NULL REFERENCES strategy_instances(id) ON DELETE CASCADE,
    internal_symbol         VARCHAR(50)     NOT NULL,
    direction               VARCHAR(10)     NOT NULL,   -- BUY / SELL
    quantity                INTEGER         NOT NULL,
    entry_price             NUMERIC(18,4)   NOT NULL,
    exit_price              NUMERIC(18,4)   NOT NULL,
    stop_loss               NUMERIC(18,4),
    take_profit             NUMERIC(18,4),
    entry_time              TIMESTAMPTZ     NOT NULL,
    exit_time               TIMESTAMPTZ     NOT NULL,
    gross_pnl               NUMERIC(18,4)   NOT NULL DEFAULT 0,
    net_pnl                 NUMERIC(18,4)   NOT NULL DEFAULT 0,
    commission              NUMERIC(18,4)   NOT NULL DEFAULT 0,
    stt                     NUMERIC(18,4)   NOT NULL DEFAULT 0,
    r_multiple              NUMERIC(10,4),             -- net_pnl / initial_risk
    initial_risk            NUMERIC(18,4),             -- (entry - stop_loss) * qty
    mae                     NUMERIC(18,4),             -- Maximum Adverse Excursion
    mfe                     NUMERIC(18,4),             -- Maximum Favorable Excursion
    exit_reason             VARCHAR(50)     NOT NULL DEFAULT 'UNKNOWN',  -- STOP_LOSS, TAKE_PROFIT, TRAIL_STOP, MANUAL, END_OF_SESSION
    entry_reason            TEXT,
    notes                   TEXT,
    tags                    TEXT[],                    -- e.g. {'mistake','good-setup','whipsaw'}
    tax_classification      VARCHAR(30)     NOT NULL DEFAULT 'Speculative',  -- Speculative / ShortTermCapitalGain / LongTermCapitalGain
    holding_days            INTEGER         NOT NULL DEFAULT 0,
    source                  VARCHAR(20)     NOT NULL DEFAULT 'ForwardTest',  -- ForwardTest / Live / Backtest
    source_trade_id         UUID,                      -- FK to forward_test_trades.id or orders.id
    created_at              TIMESTAMPTZ     NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_trade_journal_instance
    ON trade_journal_entries (strategy_instance_id, entry_time DESC);

CREATE INDEX IF NOT EXISTS idx_trade_journal_symbol
    ON trade_journal_entries (internal_symbol, entry_time DESC);

CREATE INDEX IF NOT EXISTS idx_trade_journal_exit_reason
    ON trade_journal_entries (exit_reason);

-- EF compatibility
ALTER TABLE trade_journal_entries ALTER COLUMN id         SET DEFAULT gen_random_uuid();
ALTER TABLE trade_journal_entries ALTER COLUMN gross_pnl  SET DEFAULT 0;
ALTER TABLE trade_journal_entries ALTER COLUMN net_pnl    SET DEFAULT 0;
ALTER TABLE trade_journal_entries ALTER COLUMN commission  SET DEFAULT 0;
ALTER TABLE trade_journal_entries ALTER COLUMN stt         SET DEFAULT 0;
ALTER TABLE trade_journal_entries ALTER COLUMN exit_reason SET DEFAULT 'UNKNOWN';
ALTER TABLE trade_journal_entries ALTER COLUMN tax_classification SET DEFAULT 'Speculative';
ALTER TABLE trade_journal_entries ALTER COLUMN holding_days SET DEFAULT 0;
ALTER TABLE trade_journal_entries ALTER COLUMN source      SET DEFAULT 'ForwardTest';
ALTER TABLE trade_journal_entries ALTER COLUMN created_at  SET DEFAULT NOW();
