-- Migration 013: Currency fields on instruments + FX rates table

-- Add currency / price-multiplier columns to instruments
ALTER TABLE instruments
    ADD COLUMN IF NOT EXISTS quote_currency       TEXT         NOT NULL DEFAULT 'INR',
    ADD COLUMN IF NOT EXISTS settlement_currency  TEXT         NOT NULL DEFAULT 'INR',
    ADD COLUMN IF NOT EXISTS price_multiplier     NUMERIC(12,6) NOT NULL DEFAULT 1;

-- Set MCX instruments to USD quote currency (heuristic: exchange = 'MCX')
UPDATE instruments
   SET quote_currency = 'USD'
 WHERE exchange = 'MCX'
   AND quote_currency = 'INR';

-- FX rates — daily reference rates from RBI
CREATE TABLE IF NOT EXISTS fx_rates (
    id               UUID        NOT NULL DEFAULT gen_random_uuid(),
    base_currency    TEXT        NOT NULL,   -- e.g. 'USD'
    quote_currency   TEXT        NOT NULL,   -- e.g. 'INR'
    date             DATE        NOT NULL,
    rate             NUMERIC(14,6) NOT NULL,
    source           TEXT        NOT NULL DEFAULT 'RBI',
    recorded_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT pk_fx_rates PRIMARY KEY (id),
    CONSTRAINT uq_fx_rates UNIQUE (base_currency, quote_currency, date)
);

CREATE INDEX IF NOT EXISTS ix_fx_rates_lookup
    ON fx_rates (base_currency, quote_currency, date DESC);

-- Seed a default USD/INR rate so backtests don't fail before first RBI import
INSERT INTO fx_rates (id, base_currency, quote_currency, date, rate, source)
VALUES (gen_random_uuid(), 'USD', 'INR', CURRENT_DATE, 83.50, 'Seed')
ON CONFLICT (base_currency, quote_currency, date) DO NOTHING;
