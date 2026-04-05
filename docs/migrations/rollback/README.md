# Migration Rollback Scripts (#135)

Rollback scripts for each forward migration. Apply in **reverse numeric order** when rolling back multiple migrations.

## Procedure

```bash
# Roll back a single migration
psql $DATABASE_URL -f docs/migrations/rollback/rollback_NNN.sql

# Roll back multiple migrations (newest first)
psql $DATABASE_URL -f docs/migrations/rollback/rollback_027.sql
psql $DATABASE_URL -f docs/migrations/rollback/rollback_026.sql
psql $DATABASE_URL -f docs/migrations/rollback/rollback_025.sql
```

Each script:
1. Removes schema changes introduced by the forward migration
2. Deletes the row from `schema_migrations` so `DatabaseMigrationRunner` will re-apply the migration on the next startup if needed

## Rules

- **Always take a backup before rolling back** — see `docs/BACKUP_STRATEGY.md`
- Rollbacks for data-destructive migrations (e.g., `DROP TABLE`) cannot recover data
- Never modify an applied migration file — add a new numbered migration instead
- Test rollback scripts in a non-production environment first

## Available Rollbacks

| Rollback file          | Forward migration                        | Issues     |
|------------------------|------------------------------------------|------------|
| rollback_025.sql       | 025_fix_schema_conflicts.sql             | #170 #171 #147 |
| rollback_026.sql       | 026_fk_constraints_backtest_scenario.sql | #173 #174  |
| rollback_027.sql       | 027_timescaledb_compression.sql          | #138       |
