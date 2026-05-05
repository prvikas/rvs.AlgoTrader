-- Migration 060: Create broker-exchange configuration table
-- Defines which exchanges/product-types each broker supports and default lot sizes
-- Pure additive — no existing tables modified

CREATE TABLE IF NOT EXISTS broker_exchange_configs (
    id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    broker_id        SMALLINT NOT NULL REFERENCES brokers(id),
    exchange_id      SMALLINT NOT NULL REFERENCES exchanges(id),
    product_type_id  SMALLINT NOT NULL REFERENCES product_types(id),
    default_lot_size INT NOT NULL DEFAULT 1,
    is_active        BOOLEAN NOT NULL DEFAULT true,
    CONSTRAINT uq_broker_exchange_product UNIQUE (broker_id, exchange_id, product_type_id)
);

CREATE INDEX IF NOT EXISTS idx_broker_exchange_configs_broker
    ON broker_exchange_configs (broker_id);
CREATE INDEX IF NOT EXISTS idx_broker_exchange_configs_exchange
    ON broker_exchange_configs (exchange_id);

-- ── Seed reasonable defaults ─────────────────────────────────────────────────
-- Zerodha
INSERT INTO broker_exchange_configs (broker_id, exchange_id, product_type_id, default_lot_size) VALUES
    ((SELECT id FROM brokers WHERE name = 'Zerodha'), (SELECT id FROM exchanges WHERE code = 'NSE'), (SELECT id FROM product_types WHERE code = 'MIS'), 1),
    ((SELECT id FROM brokers WHERE name = 'Zerodha'), (SELECT id FROM exchanges WHERE code = 'NSE'), (SELECT id FROM product_types WHERE code = 'CNC'), 1),
    ((SELECT id FROM brokers WHERE name = 'Zerodha'), (SELECT id FROM exchanges WHERE code = 'BSE'), (SELECT id FROM product_types WHERE code = 'MIS'), 1),
    ((SELECT id FROM brokers WHERE name = 'Zerodha'), (SELECT id FROM exchanges WHERE code = 'NFO'), (SELECT id FROM product_types WHERE code = 'NRML'), 50),
    ((SELECT id FROM brokers WHERE name = 'Zerodha'), (SELECT id FROM exchanges WHERE code = 'MCX'), (SELECT id FROM product_types WHERE code = 'NRML'), 100)
ON CONFLICT DO NOTHING;

-- Upstox
INSERT INTO broker_exchange_configs (broker_id, exchange_id, product_type_id, default_lot_size) VALUES
    ((SELECT id FROM brokers WHERE name = 'Upstox'), (SELECT id FROM exchanges WHERE code = 'NSE'), (SELECT id FROM product_types WHERE code = 'MIS'), 1),
    ((SELECT id FROM brokers WHERE name = 'Upstox'), (SELECT id FROM exchanges WHERE code = 'NSE'), (SELECT id FROM product_types WHERE code = 'CNC'), 1),
    ((SELECT id FROM brokers WHERE name = 'Upstox'), (SELECT id FROM exchanges WHERE code = 'BSE'), (SELECT id FROM product_types WHERE code = 'MIS'), 1),
    ((SELECT id FROM brokers WHERE name = 'Upstox'), (SELECT id FROM exchanges WHERE code = 'NFO'), (SELECT id FROM product_types WHERE code = 'NRML'), 50)
ON CONFLICT DO NOTHING;

-- MStock
INSERT INTO broker_exchange_configs (broker_id, exchange_id, product_type_id, default_lot_size) VALUES
    ((SELECT id FROM brokers WHERE name = 'MStock'), (SELECT id FROM exchanges WHERE code = 'NSE'), (SELECT id FROM product_types WHERE code = 'MIS'), 1),
    ((SELECT id FROM brokers WHERE name = 'MStock'), (SELECT id FROM exchanges WHERE code = 'NSE'), (SELECT id FROM product_types WHERE code = 'CNC'), 1),
    ((SELECT id FROM brokers WHERE name = 'MStock'), (SELECT id FROM exchanges WHERE code = 'NFO'), (SELECT id FROM product_types WHERE code = 'NRML'), 50)
ON CONFLICT DO NOTHING;

-- IBKR
INSERT INTO broker_exchange_configs (broker_id, exchange_id, product_type_id, default_lot_size) VALUES
    ((SELECT id FROM brokers WHERE name = 'IBKR'), (SELECT id FROM exchanges WHERE code = 'NYSE'), (SELECT id FROM product_types WHERE code = 'DAY'), 1),
    ((SELECT id FROM brokers WHERE name = 'IBKR'), (SELECT id FROM exchanges WHERE code = 'NASDAQ'), (SELECT id FROM product_types WHERE code = 'DAY'), 1)
ON CONFLICT DO NOTHING;

-- Alpaca
INSERT INTO broker_exchange_configs (broker_id, exchange_id, product_type_id, default_lot_size) VALUES
    ((SELECT id FROM brokers WHERE name = 'Alpaca'), (SELECT id FROM exchanges WHERE code = 'NYSE'), (SELECT id FROM product_types WHERE code = 'DAY'), 1),
    ((SELECT id FROM brokers WHERE name = 'Alpaca'), (SELECT id FROM exchanges WHERE code = 'NASDAQ'), (SELECT id FROM product_types WHERE code = 'DAY'), 1)
ON CONFLICT DO NOTHING;
