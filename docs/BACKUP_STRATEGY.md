# Database Backup Strategy (#133)

## Overview

| Component | Backup method | Frequency | Retention |
|-----------|---------------|-----------|-----------|
| PostgreSQL / TimescaleDB | `pg_dump` | Daily + WAL streaming | 30 days |
| Redis | RDB snapshot | Every 15 min | 7 days |
| Vault secrets | Vault snapshot | Daily | 90 days |
| Application config | Git (no secrets) | Per commit | Indefinite |

---

## PostgreSQL / TimescaleDB

### Daily logical backup (pg_dump)

```bash
#!/usr/bin/env bash
# scripts/backup/pg_backup.sh
# Run via cron: 0 2 * * * /opt/algotrader/scripts/backup/pg_backup.sh

set -euo pipefail

BACKUP_DIR="/var/backups/algotrader/postgres"
DATE=$(date +%Y%m%d_%H%M%S)
FILE="${BACKUP_DIR}/algotrader_${DATE}.dump"

mkdir -p "$BACKUP_DIR"

pg_dump \
  --format=custom \
  --compress=9 \
  --no-password \
  "$DATABASE_URL" \
  --file="$FILE"

echo "[Backup] PostgreSQL dump written: $FILE ($(du -sh "$FILE" | cut -f1))"

# Retain 30 days
find "$BACKUP_DIR" -name "*.dump" -mtime +30 -delete
echo "[Backup] Old backups pruned."
```

### Point-in-time recovery (WAL archiving)

For production, configure WAL archiving in `postgresql.conf`:

```ini
wal_level = replica
archive_mode = on
archive_command = 'cp %p /var/backups/algotrader/wal/%f'
```

Use `pg_basebackup` for base backups + WAL replay for PITR.

### TimescaleDB-specific

TimescaleDB hypertable chunks can be backed up with pg_dump (logical).
For large deployments use `timescaledb-backup` tool for chunk-level parallelism:

```bash
timescaledb-backup --database algotrader --output /var/backups/algotrader/tsdb/
```

### Restore

```bash
# Full restore from custom-format dump
pg_restore \
  --dbname algotrader \
  --clean \
  --if-exists \
  --no-owner \
  /var/backups/algotrader/postgres/algotrader_YYYYMMDD_HHMMSS.dump
```

---

## Redis

### RDB persistence (default enabled)

Add to `redis.conf` (or Docker environment):

```
save 900 1      # save after 900s if 1+ key changed
save 300 10     # save after 300s if 10+ keys changed
save 60 10000   # save after 60s if 10000+ keys changed
dir /var/lib/redis
dbfilename dump.rdb
```

### AOF (append-only file) — recommended for broker tokens

```
appendonly yes
appendfsync everysec
```

### Backup the RDB file

```bash
# scripts/backup/redis_backup.sh
REDIS_DIR="/var/lib/redis"
BACKUP_DIR="/var/backups/algotrader/redis"
DATE=$(date +%Y%m%d_%H%M%S)

mkdir -p "$BACKUP_DIR"
redis-cli BGSAVE
sleep 2
cp "${REDIS_DIR}/dump.rdb" "${BACKUP_DIR}/dump_${DATE}.rdb"

find "$BACKUP_DIR" -name "*.rdb" -mtime +7 -delete
```

---

## Recovery Runbook

### PostgreSQL failure

1. Stop the application: `systemctl stop algotrader`
2. Restore from latest dump (see above)
3. Re-run pending migrations: `DatabaseMigrationRunner` auto-applies on startup
4. Restart: `systemctl start algotrader`
5. Verify health: `curl http://localhost:62318/health`

### Redis failure

Redis stores only:
- Broker JWT tokens (encrypted, re-obtainable via re-login)
- Kill switch state
- Capital reservations (reconciled from DB on startup)

Redis failure is non-fatal: the application falls back to `InMemoryBrokerSessionManager`.
On Redis restart, re-authenticate brokers via `POST /api/broker/{broker}/login`.

### Candle data loss

Missing candle data can be re-downloaded:
```
POST /api/data-manager/download
{ "symbol": "NSE:RELIANCE", "timeframe": "1d", "from": "2020-01-01", "to": "2024-12-31" }
```

---

## Monitoring

Set up alerts for:
- Backup job failures (exit code != 0)
- Backup file age > 26 hours (daily backup missed)
- Disk usage > 80% on backup volume
