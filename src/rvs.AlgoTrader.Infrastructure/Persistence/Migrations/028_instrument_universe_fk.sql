-- Migration 028: Correct FK between instrument_universe and instruments
--
-- Design:
--   instruments        = full broker master list (superset, all downloadable symbols)
--   instrument_universe = trading whitelist (subset, symbols strategies are allowed to trade)
--
-- FK direction: instrument_universe.symbol → instruments.internal_symbol
--   "Every whitelisted symbol must exist in the instrument master."
--   ON DELETE CASCADE: removing an instrument from the master also removes it from the whitelist.
--
-- The daily RefreshAsync now saves all broker instruments unconditionally.
-- Strategies filter by instrument_universe at execution time, not at import time.

-- Pre-flight: drop the wrong-direction FK from migration 021 if it somehow got applied.
DO $$ BEGIN
    IF EXISTS (
        SELECT FROM information_schema.table_constraints
        WHERE constraint_name = 'fk_instrument_universe_instruments'
          AND table_name = 'instrument_universe'
    ) THEN
        ALTER TABLE instrument_universe DROP CONSTRAINT fk_instrument_universe_instruments;
    END IF;
END $$;

-- Pre-flight: remove whitelist entries whose symbol is not yet in the instruments master.
-- Guard: only run if instruments has rows — avoids wiping the entire whitelist on a
-- fresh install where the first broker refresh has not yet populated the instruments table.
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM instruments LIMIT 1) THEN
        DELETE FROM instrument_universe
        WHERE symbol NOT IN (SELECT internal_symbol FROM instruments);
    END IF;
END $$;

-- Add the correct FK.
DO $$ BEGIN
    IF NOT EXISTS (
        SELECT FROM information_schema.table_constraints
        WHERE constraint_name = 'fk_instrument_universe_instruments'
          AND table_name = 'instrument_universe'
    ) THEN
        ALTER TABLE instrument_universe
        ADD CONSTRAINT fk_instrument_universe_instruments
        FOREIGN KEY (symbol) REFERENCES instruments(internal_symbol) ON DELETE CASCADE;
    END IF;
END $$;
