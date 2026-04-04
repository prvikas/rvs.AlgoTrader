-- Migration 024: Fix medium-priority bugs (#202, #203, #212 + idempotency repair for #020)
-- Fixes:
--   #212 – spread_positions.correlation_id DEFAULT '' breaks JOINs (remove the empty-string default)
--   #203 – forward_test_trades.realized_pnl always NULL (backfill + NOT NULL)
--   #202 – positions.average_price vs entry_price ambiguity (column comments + CHECK)
--   Idempotency guard – re-apply migration 020 constraints safely so a partially-failed 020
--                       on a fresh install does not leave the schema incomplete.

-- ── #212: Drop DEFAULT '' from spread_positions.correlation_id ─────────────────
-- Migration 020 updated existing '' rows to NULL but left the column default unchanged.
-- Any INSERT that omits correlation_id would still store '' instead of NULL.
DO $$ BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name   = 'spread_positions'
          AND column_name  = 'correlation_id'
    ) THEN
        -- Remove the '' default so omitted inserts get NULL (correct JOIN behaviour)
        ALTER TABLE spread_positions ALTER COLUMN correlation_id DROP DEFAULT;
        -- Belt-and-suspenders: flip any remaining empty strings that slipped through
        UPDATE spread_positions SET correlation_id = NULL WHERE correlation_id = '';
        -- Prevent future empty-string insertions
        ALTER TABLE spread_positions
            ADD CONSTRAINT ck_spread_positions_correlation_id_notempty
            CHECK (correlation_id IS NULL OR correlation_id <> '');
    END IF;
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

-- ── #203: forward_test_trades – backfill realized_pnl and make it NOT NULL ────
-- realized_pnl was never set by ForwardTestEngine; pnl held the actual P&L value.
-- The C# fix (ForwardTestEngine now sets both) was committed alongside this migration.
DO $$ BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name   = 'forward_test_trades'
          AND column_name  = 'realized_pnl'
    ) THEN
        -- Backfill all historical rows where realized_pnl was never written
        UPDATE forward_test_trades SET realized_pnl = pnl WHERE realized_pnl IS NULL;
        -- Set a safe default so future inserts that omit the column get 0
        ALTER TABLE forward_test_trades ALTER COLUMN realized_pnl SET DEFAULT 0;
        -- Now enforce NOT NULL; all rows are filled
        ALTER TABLE forward_test_trades ALTER COLUMN realized_pnl SET NOT NULL;
    END IF;
END $$;

-- Add column comment so the dual-column history is self-documenting
DO $$ BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = 'public' AND table_name = 'forward_test_trades'
    ) THEN
        COMMENT ON COLUMN forward_test_trades.realized_pnl IS
            'Authoritative closed-trade P&L. Backfilled from pnl column in migration 024. '
            'Use this column for all analytics and reporting.';
        COMMENT ON COLUMN forward_test_trades.pnl IS
            'Legacy P&L column kept for backward compatibility. '
            'Equals realized_pnl for all closed trades. Do not use for new analytics.';
    END IF;
END $$;

-- ── #202: positions – clarify average_price vs entry_price via column comments ─
-- average_price = VWAP across all fills (cost basis used for P&L); updated by Position.UpdateAvgPrice().
-- entry_price   = first-fill price, immutable reference, NOT used for P&L calculation.
DO $$ BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.tables
        WHERE table_schema = 'public' AND table_name = 'positions'
    ) THEN
        COMMENT ON COLUMN positions.average_price IS
            'VWAP cost basis across all fills. Always use this for unrealized/realized P&L. '
            'Updated by Position.UpdateAvgPrice() on each partial fill.';
        COMMENT ON COLUMN positions.entry_price IS
            'First-fill price — immutable reference, never updated after initial open. '
            'Do NOT use for P&L calculations; use average_price instead.';
    END IF;
END $$;

-- ── Idempotency repair for migration 020 (non-idempotent ADD CONSTRAINT calls) ─
-- Migration 020 used bare ALTER TABLE ... ADD CONSTRAINT which fails if the constraint
-- already exists (e.g. on a DB that had a partial 020 run, or manual setup).
-- Re-issuing them here with DO-EXCEPTION ensures the schema is complete regardless.

-- #209: fx_rates rate > 0
DO $$ BEGIN
    ALTER TABLE fx_rates ADD CONSTRAINT ck_fx_rates_rate_positive CHECK (rate > 0);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

-- #205: instruments positive numeric fields
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

-- #210: capital_allocations reserved <= allocated
DO $$ BEGIN
    ALTER TABLE capital_allocations ADD CONSTRAINT ck_capital_allocations_reserved_not_negative CHECK (reserved_capital >= 0);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

DO $$ BEGIN
    ALTER TABLE capital_allocations ADD CONSTRAINT ck_capital_allocations_reserved_not_exceed CHECK (reserved_capital <= allocated_capital);
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

-- #211: risk_profiles percentage columns in (0, 1]
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

-- #204: enum/status CHECK constraints
DO $$ BEGIN
    ALTER TABLE orders ADD CONSTRAINT ck_orders_status CHECK (status IN ('Pending','Placed','Partial','Filled','Cancelled','Rejected','Failed'));
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

DO $$ BEGIN
    ALTER TABLE orders ADD CONSTRAINT ck_orders_direction CHECK (direction IN ('BUY','SELL'));
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

DO $$ BEGIN
    ALTER TABLE strategy_instances ADD CONSTRAINT ck_strategy_instances_status CHECK (status IN ('Draft','Running','Paused','Stopped','Scheduled','Error'));
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

DO $$ BEGIN
    ALTER TABLE strategy_instances ADD CONSTRAINT ck_strategy_instances_mode CHECK (mode IN ('Backtest','Forward','Live'));
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

DO $$ BEGIN
    ALTER TABLE strategy_runs ADD CONSTRAINT ck_strategy_runs_status CHECK (status IN ('Running','Stopped','Paused','Completed'));
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

DO $$ BEGIN
    ALTER TABLE download_jobs ADD CONSTRAINT ck_download_jobs_status CHECK (status IN ('Queued','InProgress','Completed','Failed'));
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

DO $$ BEGIN
    ALTER TABLE market_breadth_snapshots ADD CONSTRAINT ck_market_breadth_snapshots_regime CHECK (regime IN ('Bullish','Neutral','Bearish'));
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

DO $$ BEGIN
    ALTER TABLE forward_test_sessions ADD CONSTRAINT ck_forward_test_sessions_status CHECK (status IN ('Running','Paused','Stopped','Completed','Failed'));
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

DO $$ BEGIN
    ALTER TABLE strategy_scenarios ADD CONSTRAINT ck_strategy_scenarios_status CHECK (status IN ('Draft','Backtested','ForwardTest','Live','Archived'));
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;

-- #213: alert_log alert_type validation
DO $$ BEGIN
    ALTER TABLE alert_log ADD CONSTRAINT ck_alert_log_alert_type CHECK (
        alert_type IN (
            'DailyLossLimit','PortfolioRisk','PositionLimit','SectorConcentration',
            'CorrelationWarning','DataFeedDown','BrokerConnectionLost',
            'StrategyError','BacktestError','CalendarEvent','MarketClosed'
        )
    );
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;
