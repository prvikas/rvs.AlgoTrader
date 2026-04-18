-- Migration 039: market_news table
-- Stores market news items (NSE announcements, manual entries).
-- External NSE feed ingestion is VERIFY_LIVE — manual inserts supported now.

CREATE TABLE IF NOT EXISTS market_news (
    id           UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    symbol       VARCHAR(50),                                   -- NULL = market-wide
    headline     TEXT        NOT NULL,
    summary      TEXT,
    source       VARCHAR(100) NOT NULL DEFAULT 'manual',        -- 'NSE' | 'BSE' | 'manual'
    url          TEXT,
    published_at TIMESTAMPTZ NOT NULL,
    fetched_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    sentiment    VARCHAR(10) CHECK (sentiment IN ('Positive','Negative','Neutral','Unknown')),
    tags         TEXT                                           -- comma-separated
);

CREATE INDEX IF NOT EXISTS idx_market_news_symbol_published
    ON market_news (symbol, published_at DESC);

CREATE INDEX IF NOT EXISTS idx_market_news_published
    ON market_news (published_at DESC);
