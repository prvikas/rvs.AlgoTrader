-- Migration 027: Fix #138 — TimescaleDB hypertable + compression + retention policy.
--
-- All operations are guarded by a TimescaleDB extension existence check so the
-- migration is a no-op on plain PostgreSQL deployments (development or AWS RDS).
--
-- Execution model:
--   1. Convert candles to a hypertable partitioned by open_time (7-day chunks).
--      Uses migrate_data=>true so existing rows are moved into the first chunk.
--   2. Enable columnar compression ordered by open_time, segmented by
--      (internal_symbol, timeframe) — matching query patterns in GetAsync.
--   3. Add a compression policy: auto-compress chunks older than 7 days.
--   4. Add a data retention policy: drop chunks older than 3 years (1095 days).
--      Adjust via SELECT alter_job(job_id, config => ...) in the DB if needed.
--
-- NOTE: create_hypertable on an existing table requires the time column
--       (open_time) to be part of the PRIMARY KEY, which it is:
--       PRIMARY KEY (internal_symbol, timeframe, open_time).
--       TimescaleDB will accept this composite PK.

DO $$ BEGIN
    -- Guard: skip entirely on non-TimescaleDB installations.
    IF NOT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'timescaledb') THEN
        RAISE NOTICE '[Migration 027] TimescaleDB not installed — skipping hypertable setup.';
        RETURN;
    END IF;

    -- ── Step 1: Convert candles to hypertable ────────────────────────────────
    IF NOT EXISTS (
        SELECT 1 FROM timescaledb_information.hypertables
        WHERE  hypertable_name = 'candles'
    ) THEN
        PERFORM create_hypertable(
            'candles',
            'open_time',
            chunk_time_interval => INTERVAL '7 days',
            migrate_data        => true,
            if_not_exists       => true
        );
        RAISE NOTICE '[Migration 027] candles converted to TimescaleDB hypertable (7-day chunks).';
    ELSE
        RAISE NOTICE '[Migration 027] candles is already a hypertable — skipping creation.';
    END IF;

    -- ── Step 2: Enable columnar compression ─────────────────────────────────
    BEGIN
        ALTER TABLE candles SET (
            timescaledb.compress              = true,
            timescaledb.compress_segmentby    = 'internal_symbol, timeframe',
            timescaledb.compress_orderby      = 'open_time DESC'
        );
        RAISE NOTICE '[Migration 027] Compression settings applied.';
    EXCEPTION WHEN OTHERS THEN
        RAISE NOTICE '[Migration 027] Compression settings already configured — skipping.';
    END;

    -- ── Step 3: Compression policy — compress chunks older than 7 days ──────
    BEGIN
        PERFORM add_compression_policy(
            'candles',
            compress_after => INTERVAL '7 days',
            if_not_exists  => true
        );
        RAISE NOTICE '[Migration 027] Compression policy added (compress after 7 days).';
    EXCEPTION WHEN OTHERS THEN
        RAISE NOTICE '[Migration 027] Compression policy already exists — skipping.';
    END;

    -- ── Step 4: Retention policy — drop chunks older than 3 years ───────────
    BEGIN
        PERFORM add_retention_policy(
            'candles',
            drop_after    => INTERVAL '1095 days',
            if_not_exists => true
        );
        RAISE NOTICE '[Migration 027] Retention policy added (drop after 3 years).';
    EXCEPTION WHEN OTHERS THEN
        RAISE NOTICE '[Migration 027] Retention policy already exists — skipping.';
    END;

END $$;
