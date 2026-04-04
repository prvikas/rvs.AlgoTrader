# Database Issues Fixes Roadmap (#188-#213)

## Overview
Comprehensive database integrity fixes addressing 25 issues (#188-#213) covering:
- Data integrity constraints (CHECK constraints)
- Referential integrity (foreign keys)
- Schema normalization (column naming, duplicate cleanup)
- Performance optimization (indexes)

---

## Phase 1: CRITICAL (Migration 020) - Financial Data Integrity
**Status:** Ready for deployment

### #209: FX Rates Validation
- **Issue:** `fx_rates.rate` allows zero/negative values, corrupting multi-currency PnL
- **Fix:** ADD CHECK (rate > 0)
- **Impact:** Prevents invalid FX rates from being inserted

### #205: Instrument Field Validation
- **Issue:** `price_multiplier`, `tick_size`, `lot_size` allow zero values, breaking PnL/order calculations
- **Fix:** ADD CHECK constraints enforcing all three > 0
- **Impact:** Prevents malformed instruments from entering system

### #210: Capital Reservation Overflow
- **Issue:** `reserved_capital` can exceed `allocated_capital`, invalidating risk calculations
- **Fix:** ADD CHECK (reserved_capital >= 0 AND reserved_capital <= allocated_capital)
- **Impact:** Enforces valid capital reservation state

### #211: Risk Profile Percentage Validation
- **Issue:** Risk profile percentages (max_position_size_pct, etc.) have no range guard
- **Fix:** ADD CHECK constraints ensuring (0, 1] range
- **Impact:** Prevents misconfigured risk limits from bypassing safeguards

### #212: Correlation ID NULL Handling
- **Issue:** `spread_positions.correlation_id` DEFAULT '' breaks JOINs
- **Fix:** Change DEFAULT to NULL; backfill existing empty strings
- **Impact:** Enables proper NULL-based query filtering

### #213: Alert Type Validation
- **Issue:** `alert_log.alert_type` free-form text with no constraints
- **Fix:** ADD CHECK constraint restricting to known alert types
- **Impact:** Prevents invalid alert types from polluting logs

### #204: Enum Validation Across Tables
- **Issue:** Status/enum columns (orders, strategy_instances, etc.) accept any value
- **Fix:** ADD CHECK constraints on 9 columns across 7 tables
- **Impact:** Enforces valid state transitions throughout system

---

## Phase 2: HIGH PRIORITY (Migration 021) - Referential Integrity & Schema Conflicts
**Status:** Ready for deployment

### #201: internal_symbol NOT NULL Conflict
- **Issue:** NOT NULL constraint incompatible with watchlist mode (multi-symbol strategies)
- **Fix:** Make nullable; add CHECK enforcing (internal_symbol NOT NULL) OR (watchlist_id NOT NULL)
- **Impact:** Enables proper watchlist-based strategy support

### #197: Missing Foreign Keys
- **Issue:** No FK from backtest_runs to strategy_instances; no FK from instrument_universe to instruments
- **Fix:** ADD FKs with appropriate ON DELETE behavior
- **Impact:** Prevents orphaned records; enables traceability

### #192: 14 Missing Foreign Key Constraints
- **Issue:** No FKs between core entities (orders→strategy_runs, positions→strategy_runs, etc.)
- **Fix:** ADD 14 FK relationships across 9 tables
- **Impact:** Enforces referential integrity; prevents orphaned records

### #203: Overlapping PnL Columns
- **Issue:** `forward_test_trades` has both `realized_pnl` and `pnl` columns with unclear semantics
- **Fix:** Rename `pnl` to `gross_pnl` for clarity; enforce `realized_pnl` at exit
- **Status:** Requires manual code review; documented for Phase 2

### #202: Price Column Ambiguity
- **Issue:** `average_price` vs `entry_price` used inconsistently
- **Fix:** Audit all PnL code for consistency; add documentation/validation
- **Status:** Requires code review; documented for Phase 2

---

## Phase 3: MEDIUM PRIORITY (Migration 022) - Performance & Uniqueness
**Status:** Ready for deployment

### #208: Broker Session Expiry
- **Issue:** No CHECK on expiry dates; no index on expires_at
- **Fix:** ADD CHECK (expires_at > stored_at); CREATE INDEX on expires_at
- **Impact:** Prevents invalid sessions; improves token lookup performance

### #207: Scenario Name Uniqueness
- **Issue:** Duplicate scenario names possible per strategy instance
- **Fix:** CREATE UNIQUE INDEX on (strategy_instance_id, name)
- **Impact:** Prevents duplicate scenarios; enables name-based lookups

### #206: Backtest Run Deduplication
- **Issue:** Identical scenario runs create duplicates; no dedup mechanism
- **Fix:** CREATE UNIQUE INDEX on (scenario_id, data_hash)
- **Impact:** Prevents duplicate backtest runs; reduces data inflation

---

## Phase 4: LOW-MEDIUM PRIORITY (Migration 023) - Schema Cleanup
**Status:** Ready for review; some manual steps required

### #191: Orphaned Column
- **Issue:** Unused UUID column `id` in candles table
- **Fix:** DROP COLUMN "Id"
- **Impact:** Saves 16 bytes per row; removes legacy artifact

### #190: Column Naming Normalization
- **Issue:** PascalCase columns (WatchlistId, IsActive, ConfigJson, etc.) violate conventions
- **Fix:** Rename 6 columns to snake_case
- **Impact:** Standardizes schema naming

### #189: Duplicate Trailing Stop Columns
- **Issue:** PascalCase `TrailingSl`/`TrailingTp` duplicate snake_case versions
- **Fix:** DROP COLUMN "TrailingSl", "TrailingTp"
- **Impact:** Removes legacy columns

### #188: Duplicate Columns (instruments)
- **Issue:** 4 PascalCase columns duplicate snake_case versions
- **Fix:** DROP COLUMN "Underlying", "StrikePrice", "OptionType", "Expiry"
- **Impact:** Removes legacy EF Core artifacts

### #200: Overlapping Unique Constraints
- **Issue:** 3 overlapping unique constraints on `idempotency_key`
- **Fix:** Drop duplicate indexes; retain canonical constraint
- **Impact:** Reduces constraint overhead

### #199: Duplicate Candles Index
- **Issue:** Duplicate index on (internal_symbol, timeframe)
- **Fix:** DROP INDEX idx_candles_symbol_tf
- **Impact:** Saves storage; improves INSERT performance

### #193: Duplicate Indexes (13 pairs)
- **Issue:** 13 pairs of duplicate indexes with inconsistent naming
- **Fix:** Drop duplicates; retain properly-named canonical versions
- **Impact:** Reclaims significant storage

### #195: Redundant Timestamp Columns
- **Issue:** `entry_time`/`opened_at` and `exit_time`/`closed_at` duplicates
- **Fix:** Backfill; drop redundant columns
- **Impact:** Simplifies schema; reduces confusion

### #196: JSON Column Standardization
- **Issue:** 7 JSON columns stored as text; no validation/indexing
- **Fix:** Ensure NVARCHAR(MAX) with JSON_VALUE validation
- **Impact:** Enables JSON validation; improves queries

### #194: Table Consolidation
- **Issue:** `signal_journal` and `signal_journal_entries` duplicated data
- **Fix:** Migrate to single table; add missing columns and FKs
- **Status:** Requires manual data migration; complex operation

---

## Deployment Strategy

### Pre-Deployment
1. **Backup database** before running any migrations
2. **Review Phase 1** for environment-specific constraints
3. **Test migrations** on staging environment first

### Deployment Order
```
1. Migration 020 (Phase 1: Critical) - Financial safety constraints
2. Migration 021 (Phase 2: High Priority) - Referential integrity
3. Migration 022 (Phase 3: Medium) - Performance & uniqueness
4. Migration 023 (Phase 4: Cleanup) - Schema normalization
```

### Post-Deployment
1. Run data validation queries to verify integrity
2. Monitor for constraint violations (may indicate app bugs)
3. Update application code to handle new constraints
4. Update ORM mappings if column names changed

---

## Testing Checklist

- [ ] All migrations execute without errors
- [ ] No data loss from constraint additions
- [ ] Existing valid data passes new constraints
- [ ] Application tests pass with new constraints
- [ ] Performance impact measured (indexes added)
- [ ] Foreign key relationships verified with data samples

---

## Files Created
- Migration 020: `020_fix_critical_db_integrity.sql` - CHECK constraints for financial data
- Migration 021: `021_fix_high_priority_schema.sql` - Foreign keys and schema conflicts
- Migration 022: `022_fix_medium_priority_constraints.sql` - Uniqueness and performance
- Migration 023: `023_cleanup_schema_normalization.sql` - Column cleanup and normalization
