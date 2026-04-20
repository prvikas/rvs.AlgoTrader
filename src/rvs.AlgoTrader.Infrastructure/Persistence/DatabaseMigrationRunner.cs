using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace rvs.AlgoTrader.Infrastructure.Persistence;

/// <summary>
/// Lightweight SQL migration runner.
///
/// How it works:
///   1. Ensures the PostgreSQL database exists.
///   2. Creates a `schema_migrations` tracking table if absent.
///   3. Repairs stale records — if a migration is marked applied but its columns are
///      missing (e.g. due to a previous crash mid-apply), the record is deleted so
///      the migration re-runs on this startup.
///   4. Discovers every *.sql file under the Migrations directory, sorted by filename.
///   5. For each file NOT in schema_migrations: executes it then records it.
///
/// Error policy:
///   - CREATE EXTENSION failures are silently skipped (TimescaleDB/PostGIS optional).
///   - ALL other statement failures are fatal — the migration is NOT marked applied,
///     and the app refuses to start. Fix the SQL, then restart.
///   - This means the app will never silently run against a stale schema.
///
/// Adding a new migration: drop a numbered *.sql file in the Migrations folder.
/// Program.cs never changes.
/// </summary>
public class DatabaseMigrationRunner(
    AlgoTraderDbContext db,
    ILogger<DatabaseMigrationRunner> logger)
{
    // ── Path resolution ────────────────────────────────────────────────────────

    private static readonly string[] _searchRoots =
    [
        // Published / Docker: SQL files copied to output via CopyToOutputDirectory
        Path.Combine(AppContext.BaseDirectory, "Migrations"),

        // Development: bin/Debug/net9.0/ → up 4 levels → src/ → Infrastructure project
        // AppContext.BaseDirectory = src/rvs.AlgoTrader.API/bin/Debug/net9.0/
        // ../../../../             = src/
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "rvs.AlgoTrader.Infrastructure", "Persistence", "Migrations"),

        // Fallback for non-standard output structures
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "rvs.AlgoTrader.Infrastructure", "Persistence", "Migrations"),
        Path.Combine(AppContext.BaseDirectory, "..", "..", "rvs.AlgoTrader.Infrastructure", "Persistence", "Migrations"),
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "rvs.AlgoTrader.Infrastructure", "Persistence", "Migrations"),
    ];

    private static string? FindMigrationsDirectory()
    {
        foreach (var candidate in _searchRoots)
        {
            var full = Path.GetFullPath(candidate);
            if (Directory.Exists(full)) return full;
        }
        return null;
    }

    // ── Public entry point ────────────────────────────────────────────────────

    public async Task RunAsync(CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);
        logger.LogInformation("[Migration] Database existence confirmed.");

        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);

        try
        {
            using var cmd = conn.CreateCommand();

            // ── schema_migrations tracking table ──────────────────────────────
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    name       TEXT        PRIMARY KEY,
                    applied_at TIMESTAMPTZ NOT NULL DEFAULT now()
                );
                """;
            await cmd.ExecuteNonQueryAsync(ct);

            // ── app_config seed ───────────────────────────────────────────────
            await SeedAppConfigAsync(cmd, ct);

            // ── Repair: remove stale schema_migration records ─────────────────
            // If a migration was recorded as applied but its DB objects are
            // missing (crash mid-apply, locked DLL, etc.), delete the record
            // so the migration re-runs on this startup.
            await RepairStaleRecordsAsync(cmd, ct);

            // ── Discover + apply SQL files ────────────────────────────────────
            var migrationsDir = FindMigrationsDirectory();
            if (migrationsDir == null)
            {
                // Log the paths that were tried to help diagnose deployment issues
                var tried = string.Join("\n  ", _searchRoots.Select(Path.GetFullPath));
                logger.LogError(
                    "[Migration] Migrations directory not found. Tried:\n  {Paths}\n" +
                    "Add SQL files to CopyToOutputDirectory in Infrastructure.csproj or check the output path.",
                    tried);
                throw new DirectoryNotFoundException(
                    $"Could not locate Migrations directory. AppContext.BaseDirectory={AppContext.BaseDirectory}. " +
                    "Ensure *.sql files are configured with <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory> " +
                    "in rvs.AlgoTrader.Infrastructure.csproj.");
            }

            var files = Directory.GetFiles(migrationsDir, "*.sql")
                .OrderBy(f =>
                {
                    var n = Path.GetFileName(f);
                    // Legacy: InitialMigration.sql (no prefix) sorts before 002_
                    return n.Equals("InitialMigration.sql", StringComparison.OrdinalIgnoreCase)
                        ? "001_" + n
                        : n;
                }, StringComparer.OrdinalIgnoreCase)
                .ToList();

            logger.LogInformation("[Migration] Found {Count} SQL file(s) in {Dir}", files.Count, migrationsDir);

            foreach (var file in files)
            {
                var name = Path.GetFileName(file);

                // Already applied?
                cmd.Parameters.Clear();
                cmd.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE name = @n;";
                var pCheck = cmd.CreateParameter();
                pCheck.ParameterName = "@n"; pCheck.Value = name;
                cmd.Parameters.Add(pCheck);

                var already = (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L);
                cmd.Parameters.Clear();
                if (already > 0)
                {
                    logger.LogDebug("[Migration] {Name} — already applied.", name);
                    continue;
                }

                logger.LogInformation("[Migration] Applying {Name}…", name);
                var sql      = await File.ReadAllTextAsync(file, ct);
                var executed = 0;
                var skipped  = 0;

                // Wrap each migration in a transaction so a mid-migration failure
                // rolls back all statements — the migration is never partially applied.
                using var tx = await conn.BeginTransactionAsync(ct);
                cmd.Transaction = (System.Data.Common.DbTransaction)tx;

                foreach (var stmt in SplitStatements(sql))
                {
                    cmd.CommandText = stmt;
                    var isExtension = stmt.Contains("CREATE EXTENSION", StringComparison.OrdinalIgnoreCase);
                    try
                    {
                        await cmd.ExecuteNonQueryAsync(ct);
                        executed++;
                    }
                    catch (Exception ex) when (isExtension)
                    {
                        // TimescaleDB / PostGIS are optional — skip silently
                        logger.LogWarning("[Migration] {Name} — optional extension skipped: {Msg}",
                            name, ex.Message[..Math.Min(120, ex.Message.Length)]);
                        skipped++;
                    }
                    // All other exceptions propagate — transaction rolls back automatically.
                }

                // Record as applied inside the same transaction
                cmd.Parameters.Clear();
                cmd.CommandText = "INSERT INTO schema_migrations (name) VALUES (@n) ON CONFLICT DO NOTHING;";
                var pApply = cmd.CreateParameter();
                pApply.ParameterName = "@n"; pApply.Value = name;
                cmd.Parameters.Add(pApply);
                await cmd.ExecuteNonQueryAsync(ct);
                cmd.Parameters.Clear();

                await tx.CommitAsync(ct);
                cmd.Transaction = null;

                logger.LogInformation("[Migration] {Name} — done ({Executed} executed, {Skipped} optional skipped).",
                    name, executed, skipped);
            }

            logger.LogInformation("[Migration] All migrations up to date.");
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    // ── Repair ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Checks whether columns/tables introduced by each migration actually exist in
    /// the database. If a migration is recorded in schema_migrations but its objects
    /// are absent (crash mid-apply, locked DLL on previous run, etc.), the record is
    /// deleted so the migration re-runs on this startup.
    ///
    /// Add a new entry here whenever a migration introduces a column that EF queries.
    /// </summary>
    private async Task RepairStaleRecordsAsync(DbCommand cmd, CancellationToken ct)
    {
        var checks = new (string Migration, string Sql, string Description)[]
        {
            // ── 002_InstrumentUniverse ─────────────────────────────────────────
            (
                "002_InstrumentUniverse.sql",
                """
                SELECT COUNT(*) FROM information_schema.tables
                WHERE table_schema = 'public'
                  AND table_name   = 'instrument_universe';
                """,
                "instrument_universe table"
            ),

            // ── 004_BacktestAndForwardTestTrades ──────────────────────────────
            (
                "004_BacktestAndForwardTestTrades.sql",
                """
                SELECT COUNT(*) FROM information_schema.tables
                WHERE table_schema = 'public'
                  AND table_name   = 'backtest_runs';
                """,
                "backtest_runs table"
            ),
            (
                "004_BacktestAndForwardTestTrades.sql",
                """
                SELECT COUNT(*) FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name   = 'forward_test_sessions'
                  AND column_name  = 'source_backtest_id';
                """,
                "forward_test_sessions.source_backtest_id"
            ),
            (
                "004_BacktestAndForwardTestTrades.sql",
                """
                SELECT COUNT(*) FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name   = 'forward_test_sessions'
                  AND column_name  = 'max_drawdown';
                """,
                "forward_test_sessions.max_drawdown"
            ),

            // ── 005_BacktestExtendedStats ──────────────────────────────────────
            (
                "005_BacktestExtendedStats.sql",
                """
                SELECT COUNT(*) FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name   = 'backtest_runs'
                  AND column_name  = 'extended_stats_json';
                """,
                "backtest_runs.extended_stats_json"
            ),

            // ── 006_StrategyInstancePnl ────────────────────────────────────────
            (
                "006_StrategyInstancePnl.sql",
                """
                SELECT COUNT(*) FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name   = 'strategy_instances'
                  AND column_name  = 'today_realized_pnl';
                """,
                "strategy_instances.today_realized_pnl"
            ),

            // ── 007_StrategyScenarios ──────────────────────────────────────────
            (
                "007_StrategyScenarios.sql",
                """
                SELECT COUNT(*) FROM information_schema.tables
                WHERE table_schema = 'public'
                  AND table_name   = 'strategy_scenarios';
                """,
                "strategy_scenarios table"
            ),
            (
                "007_StrategyScenarios.sql",
                """
                SELECT COUNT(*) FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name   = 'backtest_runs'
                  AND column_name  = 'scenario_id';
                """,
                "backtest_runs.scenario_id"
            ),
            (
                "007_StrategyScenarios.sql",
                """
                SELECT COUNT(*) FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name   = 'backtest_runs'
                  AND column_name  = 'effective_parameters_json';
                """,
                "backtest_runs.effective_parameters_json"
            ),

            // ── 008_BrokerSessions ─────────────────────────────────────────────
            (
                "008_BrokerSessions.sql",
                """
                SELECT COUNT(*) FROM information_schema.tables
                WHERE table_schema = 'public'
                  AND table_name   = 'broker_sessions';
                """,
                "broker_sessions table"
            ),

            // ── 009_ScenarioCapital ────────────────────────────────────────────
            (
                "009_ScenarioCapital.sql",
                """
                SELECT COUNT(*) FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name   = 'strategy_scenarios'
                  AND column_name  = 'allocated_capital';
                """,
                "strategy_scenarios.allocated_capital"
            ),

            // ── 016_TradeJournal ───────────────────────────────────────────────
            (
                "016_TradeJournal.sql",
                """
                SELECT COUNT(*) FROM information_schema.tables
                WHERE table_schema = 'public'
                  AND table_name   = 'trade_journal_entries';
                """,
                "trade_journal_entries table"
            ),

            // ── 017_QuantityBigInt ─────────────────────────────────────────────
            (
                "017_QuantityBigInt.sql",
                """
                SELECT COUNT(*) FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name   = 'orders'
                  AND column_name  = 'quantity'
                  AND data_type    = 'bigint';
                """,
                "orders.quantity BIGINT"
            ),

            // ── 018_StrategyApprovals ──────────────────────────────────────────
            (
                "018_StrategyApprovals.sql",
                """
                SELECT COUNT(*) FROM information_schema.tables
                WHERE table_schema = 'public'
                  AND table_name   = 'strategy_approvals';
                """,
                "strategy_approvals table"
            ),

            // ── 024_fix_medium_priority_bugs ──────────────────────────────────
            (
                "024_fix_medium_priority_bugs.sql",
                """
                SELECT COUNT(*) FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name   = 'forward_test_trades'
                  AND column_name  = 'realized_pnl'
                  AND is_nullable  = 'NO';
                """,
                "forward_test_trades.realized_pnl NOT NULL"
            ),

            // ── 029_restore_strategy_instance_columns ─────────────────────────
            (
                "029_restore_strategy_instance_columns.sql",
                """
                SELECT COUNT(*) FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name   = 'strategy_instances'
                  AND column_name  = 'is_active';
                """,
                "strategy_instances.is_active"
            ),
            (
                "029_restore_strategy_instance_columns.sql",
                """
                SELECT COUNT(*) FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name   = 'strategy_instances'
                  AND column_name  = 'config_json';
                """,
                "strategy_instances.config_json"
            ),
            (
                "029_restore_strategy_instance_columns.sql",
                """
                SELECT COUNT(*) FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name   = 'strategy_instances'
                  AND column_name  = 'allocated_capital';
                """,
                "strategy_instances.allocated_capital"
            ),

            // ── 040_app_config_and_symbol_prefs ───────────────────────────────
            // Migration 040 originally used CREATE TABLE IF NOT EXISTS which was a
            // no-op on existing DBs, leaving value_json and is_active absent.
            // The fixed 040 uses ALTER TABLE ADD COLUMN IF NOT EXISTS.
            // Re-run 040 if either critical column is still missing.
            (
                "040_app_config_and_symbol_prefs.sql",
                """
                SELECT COUNT(*) FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name   = 'app_config'
                  AND column_name  = 'value_json';
                """,
                "app_config.value_json"
            ),
            (
                "040_app_config_and_symbol_prefs.sql",
                """
                SELECT COUNT(*) FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name   = 'symbol_data_preferences'
                  AND column_name  = 'is_active';
                """,
                "symbol_data_preferences.is_active"
            ),

            // ── 041_app_config_schema_fix ──────────────────────────────────────
            // Backstop: re-run 041 if value_json is still absent after 040.
            (
                "041_app_config_schema_fix.sql",
                """
                SELECT COUNT(*) FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name   = 'app_config'
                  AND column_name  = 'value_json';
                """,
                "app_config.value_json (041 backstop)"
            ),
        };

        var toDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (migration, checkSql, description) in checks)
        {
            cmd.Parameters.Clear();
            cmd.CommandText = checkSql;
            var exists = (long)(await cmd.ExecuteScalarAsync(ct) ?? 0L) > 0;
            if (!exists)
            {
                logger.LogWarning(
                    "[Migration] REPAIR: {Desc} is missing — will re-run {Migration}.",
                    description, migration);
                toDelete.Add(migration);
            }
        }

        foreach (var migration in toDelete)
        {
            cmd.Parameters.Clear();
            cmd.CommandText = "DELETE FROM schema_migrations WHERE name = @n;";
            var p = cmd.CreateParameter();
            p.ParameterName = "@n"; p.Value = migration;
            cmd.Parameters.Add(p);
            await cmd.ExecuteNonQueryAsync(ct);
            cmd.Parameters.Clear();
            logger.LogWarning("[Migration] REPAIR: removed stale record for {Migration} — it will re-apply now.", migration);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Splits a SQL script into individual statements on ';', handling:
    ///   - Line comments (--)
    ///   - Dollar-quoted blocks ($$ … $$) used by PL/pgSQL
    /// </summary>
    private static IEnumerable<string> SplitStatements(string sql)
    {
        var sb        = new System.Text.StringBuilder();
        bool inDollar = false;

        foreach (var rawLine in sql.Split('\n'))
        {
            var line    = rawLine.TrimEnd('\r');
            var trimmed = line.Trim();

            if (!inDollar && (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("--")))
                continue;

            // Count $$ occurrences: each pair toggles in/out of dollar-quote mode
            var count = 0;
            var idx   = 0;
            while ((idx = trimmed.IndexOf("$$", idx, StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += 2;
            }
            if (count % 2 != 0) inDollar = !inDollar;

            sb.AppendLine(line);

            if (!inDollar && trimmed.EndsWith(';'))
            {
                var stmt = sb.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(stmt))
                    yield return stmt;
                sb.Clear();
            }
        }

        var tail = sb.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(tail))
            yield return tail;
    }

    private static async Task SeedAppConfigAsync(DbCommand cmd, CancellationToken ct)
    {
        var seeds = new[]
        {
            ("Brokers:Registered",              "MStock,Zerodha,Upstox"),
            ("InstrumentFilter:FuturesTypes",   "FUT,FUTIDX,FUTSTK,FUTURES,IF,SF,FUTCOM,FUTCUR,FUTIRD"),
            ("InstrumentFilter:OptionsTypes",   "OPT,OPTIDX,OPTSTK,OPTIONS,CE,PE,IO,SO"),
        };

        foreach (var (key, value) in seeds)
        {
            // Use parameterized query — never interpolate into SQL.
            // Seed into both value (original InitialMigration column) and value_json
            // (added by migration 041) so AppConfigService.GetAsync can read them.
            // value_json stores the value as a JSON string (double-quoted).
            var valueJson = System.Text.Json.JsonSerializer.Serialize(value);
            cmd.Parameters.Clear();
            cmd.CommandText = """
                INSERT INTO app_config (key, value, value_json, actor, updated_at)
                VALUES (@k, @v, @vj, 'system', NOW())
                ON CONFLICT (key) DO NOTHING;
                """;
            var pk  = cmd.CreateParameter(); pk.ParameterName  = "@k";  pk.Value  = key;       cmd.Parameters.Add(pk);
            var pv  = cmd.CreateParameter(); pv.ParameterName  = "@v";  pv.Value  = value;     cmd.Parameters.Add(pv);
            var pvj = cmd.CreateParameter(); pvj.ParameterName = "@vj"; pvj.Value = valueJson; cmd.Parameters.Add(pvj);
            try { await cmd.ExecuteNonQueryAsync(ct); }
            catch (NpgsqlException ex) when (ex.SqlState is "42P01" or "42703")
            {
                // 42P01 = table does not exist yet (first run before InitialMigration).
                // 42703 = value_json/actor column not yet added (before migration 041 runs).
                // In both cases, migrations will create/alter the table; seed succeeds on next startup.
            }
            cmd.Parameters.Clear();
        }
    }
}
