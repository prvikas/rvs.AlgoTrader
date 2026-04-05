-- Rollback for migration 027_timescaledb_compression.sql
-- Removes TimescaleDB compression and retention policies from candles.
-- The table remains a hypertable after rollback (reverting that is destructive
-- and not recommended; contact the DBA for hypertable-to-plain-table conversion).

DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'timescaledb') THEN
        RAISE NOTICE '[Rollback 027] TimescaleDB not installed — nothing to roll back.';
        RETURN;
    END IF;

    -- Remove retention policy
    BEGIN
        PERFORM remove_retention_policy('candles', if_not_exists => true);
        RAISE NOTICE '[Rollback 027] Retention policy removed.';
    EXCEPTION WHEN OTHERS THEN
        RAISE NOTICE '[Rollback 027] No retention policy to remove.';
    END;

    -- Remove compression policy
    BEGIN
        PERFORM remove_compression_policy('candles', if_not_exists => true);
        RAISE NOTICE '[Rollback 027] Compression policy removed.';
    EXCEPTION WHEN OTHERS THEN
        RAISE NOTICE '[Rollback 027] No compression policy to remove.';
    END;

    -- Decompress any already-compressed chunks before disabling compression
    BEGIN
        SELECT decompress_chunk(c)
        FROM show_chunks('candles') c
        WHERE c IN (SELECT chunk_name::regclass FROM chunk_compression_stats('candles') WHERE compression_status = 'Compressed');
        ALTER TABLE candles SET (timescaledb.compress = false);
        RAISE NOTICE '[Rollback 027] Compression disabled on candles hypertable.';
    EXCEPTION WHEN OTHERS THEN
        RAISE NOTICE '[Rollback 027] Could not disable compression: %', SQLERRM;
    END;
END $$;

DELETE FROM schema_migrations WHERE file_name = '027_timescaledb_compression.sql';
