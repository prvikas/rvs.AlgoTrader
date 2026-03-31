-- 017_QuantityBigInt.sql
-- INT overflows at ~2.1B which is reachable for high-frequency F&O contracts
-- (e.g. 100k lots × 50 lot-size = 5M units). Upgrade all quantity columns to BIGINT.
-- Idempotent — ALTER TYPE is a no-op when the column is already BIGINT.

ALTER TABLE orders   ALTER COLUMN quantity        TYPE BIGINT USING quantity::BIGINT;
ALTER TABLE orders   ALTER COLUMN filled_quantity TYPE BIGINT USING filled_quantity::BIGINT;

ALTER TABLE positions ALTER COLUMN quantity        TYPE BIGINT USING quantity::BIGINT;
