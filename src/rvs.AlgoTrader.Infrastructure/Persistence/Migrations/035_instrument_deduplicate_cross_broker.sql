-- Migration 035: Merge duplicate instrument rows caused by multi-broker refreshes.
--
-- Root cause: When Zerodha sends "NSE:IRFC" and MStock sends "NSE:IRFC-EQ",
-- both share (trading_symbol=IRFC, exchange=NSE) but different internal_symbol,
-- so the old single-phase upsert created two rows instead of merging tokens.
--
-- Fix: For each group of rows sharing (trading_symbol, exchange), keep the row
-- with the most broker tokens (tiebreak: lowest id), merge all broker tokens onto
-- it, then delete the orphaned duplicates.
--
-- The new two-phase upsert in InstrumentRefreshService prevents future duplicates.
-- This migration cleans up existing historical duplicates only.
--
-- Note: DatabaseMigrationRunner wraps each migration in its own transaction,
-- so no explicit BEGIN/COMMIT here.

-- Step 1: For each duplicate group, identify the winner (most tokens, lowest id)
-- and collect which broker tokens to merge from losers onto winners.
WITH ranked AS (
    SELECT
        id,
        internal_symbol,
        trading_symbol,
        exchange,
        zerodha_token,
        upstox_token,
        mstock_token,
        ROW_NUMBER() OVER (
            PARTITION BY trading_symbol, exchange
            ORDER BY
                (CASE WHEN zerodha_token IS NOT NULL THEN 1 ELSE 0 END +
                 CASE WHEN mstock_token  IS NOT NULL THEN 1 ELSE 0 END +
                 CASE WHEN upstox_token  IS NOT NULL THEN 1 ELSE 0 END) DESC,
                id ASC
        ) AS rn
    FROM instruments
),
winners AS (
    SELECT id AS winner_id, trading_symbol, exchange
    FROM ranked
    WHERE rn = 1
),
losers AS (
    SELECT
        r.id AS loser_id,
        r.zerodha_token AS loser_zerodha,
        r.upstox_token  AS loser_upstox,
        r.mstock_token  AS loser_mstock,
        w.winner_id
    FROM ranked r
    JOIN winners w ON r.trading_symbol = w.trading_symbol AND r.exchange = w.exchange
    WHERE r.rn > 1
),
-- Aggregate all tokens from all losers per winner
merged_tokens AS (
    SELECT
        winner_id,
        MAX(loser_zerodha) AS merged_zerodha,
        MAX(loser_upstox)  AS merged_upstox,
        MAX(loser_mstock)  AS merged_mstock
    FROM losers
    GROUP BY winner_id
)
-- Step 2: Apply merged tokens onto winners (only fill NULL slots)
UPDATE instruments i
SET
    zerodha_token = COALESCE(i.zerodha_token, mt.merged_zerodha),
    upstox_token  = COALESCE(i.upstox_token,  mt.merged_upstox),
    mstock_token  = COALESCE(i.mstock_token,  mt.merged_mstock)
FROM merged_tokens mt
WHERE i.id = mt.winner_id;

-- Step 3: Delete the loser rows (instrument_universe FK must be updated first)
-- Re-compute losers inline to avoid CTE reuse issues after the UPDATE above.
WITH ranked2 AS (
    SELECT
        id,
        trading_symbol,
        exchange,
        ROW_NUMBER() OVER (
            PARTITION BY trading_symbol, exchange
            ORDER BY
                (CASE WHEN zerodha_token IS NOT NULL THEN 1 ELSE 0 END +
                 CASE WHEN mstock_token  IS NOT NULL THEN 1 ELSE 0 END +
                 CASE WHEN upstox_token  IS NOT NULL THEN 1 ELSE 0 END) DESC,
                id ASC
        ) AS rn
    FROM instruments
),
winners2 AS (
    SELECT id AS winner_id, trading_symbol, exchange
    FROM ranked2
    WHERE rn = 1
),
losers2 AS (
    SELECT r.id AS loser_id, w.winner_id
    FROM ranked2 r
    JOIN winners2 w ON r.trading_symbol = w.trading_symbol AND r.exchange = w.exchange
    WHERE r.rn > 1
)
-- Remap instrument_universe references from losers to winners before delete
UPDATE instrument_universe iu
SET symbol = (
    SELECT i_win.internal_symbol
    FROM instruments i_win
    WHERE i_win.id = l2.winner_id
)
FROM losers2 l2
JOIN instruments i_loser ON i_loser.id = l2.loser_id
WHERE iu.symbol = i_loser.internal_symbol;

-- Step 4: Delete orphaned loser rows now that FK references are remapped
WITH ranked3 AS (
    SELECT
        id,
        trading_symbol,
        exchange,
        ROW_NUMBER() OVER (
            PARTITION BY trading_symbol, exchange
            ORDER BY
                (CASE WHEN zerodha_token IS NOT NULL THEN 1 ELSE 0 END +
                 CASE WHEN mstock_token  IS NOT NULL THEN 1 ELSE 0 END +
                 CASE WHEN upstox_token  IS NOT NULL THEN 1 ELSE 0 END) DESC,
                id ASC
        ) AS rn
    FROM instruments
)
DELETE FROM instruments
WHERE id IN (SELECT id FROM ranked3 WHERE rn > 1);

-- Step 5: Ensure the unique constraint exists (idempotent — already present in schema).
-- This is defensive: if somehow it was dropped, recreate it.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'uq_instruments_trading_symbol_exchange'
          AND conrelid = 'instruments'::regclass
    ) THEN
        ALTER TABLE instruments
            ADD CONSTRAINT uq_instruments_trading_symbol_exchange
            UNIQUE (trading_symbol, exchange);
    END IF;
END$$;
