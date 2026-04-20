-- Migration 041: Backstop for app_config value_json column.
--
-- Migration 040 now adds value_json and actor via ALTER TABLE ADD COLUMN IF NOT EXISTS.
-- This migration is a safety net in case the database was in a state where 040's
-- CREATE TABLE IF NOT EXISTS ran but the ALTER TABLE portion did not (e.g., partial
-- failure before the fix). All statements are fully idempotent.

ALTER TABLE app_config
    ADD COLUMN IF NOT EXISTS value_json  TEXT,
    ADD COLUMN IF NOT EXISTS actor       VARCHAR(200) NOT NULL DEFAULT 'system';

-- Back-fill: wrap existing plain-text values in JSON quotes so
-- AppConfigService.GetAsync<string> can deserialise them correctly.
UPDATE app_config
   SET value_json = to_json(value)::text
 WHERE value_json IS NULL AND value IS NOT NULL;
