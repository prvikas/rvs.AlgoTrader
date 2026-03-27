using Hangfire;
using Hangfire.Dashboard;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using rvs.AlgoTrader.API.Extensions;
using rvs.AlgoTrader.API.Hubs;
using rvs.AlgoTrader.API.Messaging;
using rvs.AlgoTrader.API.Middleware;
using rvs.AlgoTrader.Infrastructure.Extensions;
using rvs.AlgoTrader.Infrastructure.Persistence;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Load secrets from Vault or environment
builder.Configuration.AddEnvironmentVariables();

// Bootstrap Serilog from appsettings.json (WriteTo: Console + File)
// Use AppContext.BaseDirectory for the file path so logs land in the output
// directory (bin/Debug/net9.0/logs/) regardless of VS working directory.
var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
Console.WriteLine($"[Startup] Log directory: {logDir}");
builder.Host.UseSerilog((ctx, lc) =>
    lc.ReadFrom.Configuration(ctx.Configuration)
      .WriteTo.File(
          Path.Combine(logDir, "algotrader-.log"),
          rollingInterval: Serilog.RollingInterval.Day,
          retainedFileCountLimit: 30,
          shared: true));

// NodaTime.IClock — registered here directly so SystemClock (which takes NodaTime.IClock
// in its constructor) resolves correctly before AddInfrastructureServices runs.
builder.Services.AddSingleton<IClock>(SystemClock.Instance);

// Infrastructure (EF Core, Redis, MassTransit, Hangfire, etc.)
// Pass SignalRHubConsumer so it is registered in the same MassTransit bus instance.
// It lives here (API) because it depends on SignalR Hub types unavailable in Infrastructure.
builder.Services.AddInfrastructureServices(builder.Configuration, cfg =>
{
    cfg.AddConsumer<SignalRHubConsumer>();
});

// API services (controllers, SignalR, JWT, CORS, rate limiting)
builder.Services.AddApiServices(builder.Configuration);

// MediatR — scans Application assembly
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(rvs.AlgoTrader.Application.Commands.Orders.PlaceOrderCommand).Assembly));

var app = builder.Build();

// Database initialization — create database and apply migrations on startup
try
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AlgoTraderDbContext>();

        // Ensure database exists
        await dbContext.Database.EnsureCreatedAsync();
        Console.WriteLine("[Startup] Database checked/created.");

        // Execute initial migration SQL if database was just created
        var connection = dbContext.Database.GetDbConnection();
        await connection.OpenAsync();

        try
        {
            using var command = connection.CreateCommand();
            {
                // Check if tables exist
                command.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'strategy_instances';";
                var tableCount = (long?)await command.ExecuteScalarAsync() ?? 0;

                if (tableCount == 0)
                {
                    Console.WriteLine("[Startup] Tables not found. Executing InitialMigration.sql...");

                    // Find the migration SQL file
                    string? migrationPath = null;
                    var possiblePaths = new[]
                    {
                        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "rvs.AlgoTrader.Infrastructure", "Persistence", "Migrations", "InitialMigration.sql"),
                        Path.Combine(AppContext.BaseDirectory, "..", "..", "rvs.AlgoTrader.Infrastructure", "Persistence", "Migrations", "InitialMigration.sql"),
                        Path.Combine(AppContext.BaseDirectory, "Migrations", "InitialMigration.sql"),
                        @"C:\Users\prvik\Downloads\algotrader-claude-kit\src\rvs.AlgoTrader.Infrastructure\Persistence\Migrations\InitialMigration.sql"
                    };

                    foreach (var path in possiblePaths)
                    {
                        if (File.Exists(path))
                        {
                            migrationPath = path;
                            Console.WriteLine($"[Startup] Found migration file at: {path}");
                            break;
                        }
                    }

                    if (!string.IsNullOrEmpty(migrationPath))
                    {
                        var sqlScript = await File.ReadAllTextAsync(migrationPath);
                        Console.WriteLine($"[Startup] Migration file size: {sqlScript.Length} bytes");

                        // Split SQL statements properly (handle multi-line statements)
                        var statements = new List<string>();
                        var currentStatement = new System.Text.StringBuilder();

                        foreach (var line in sqlScript.Split('\n'))
                        {
                            var trimmedLine = line.Trim();

                            // Skip empty lines and comments
                            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("--"))
                                continue;

                            currentStatement.AppendLine(line);

                            // If line ends with semicolon, we have a complete statement
                            if (trimmedLine.EndsWith(';'))
                            {
                                var stmt = currentStatement.ToString().Trim();
                                if (!string.IsNullOrWhiteSpace(stmt))
                                {
                                    statements.Add(stmt);
                                }
                                currentStatement.Clear();
                            }
                        }

                        Console.WriteLine($"[Startup] Parsed {statements.Count} SQL statements");

                        int executedCount = 0;
                        foreach (var statement in statements)
                        {
                            if (!string.IsNullOrWhiteSpace(statement))
                            {
                                command.CommandText = statement;
                                try
                                {
                                    await command.ExecuteNonQueryAsync();
                                    executedCount++;
                                }
                                catch (Exception statementEx)
                                {
                                    // Log the error but continue (some statements like CREATE EXTENSION may fail)
                                    Console.WriteLine($"[Startup] Statement error (continuing): {statementEx.Message}");

                                    // Only re-throw if it's a critical table/schema error
                                    // Skip: function not found (TimescaleDB), extension not found, etc.
                                    if (statementEx.Message.Contains("42P01") || // relation does not exist
                                        (statementEx.Message.Contains("relation") && statementEx.Message.Contains("does not exist")))
                                        throw; // Re-throw if it's a missing table error

                                    // Continue for: function errors (42883), extension errors, etc.
                                }
                            }
                        }

                        Console.WriteLine($"[Startup] Successfully executed {executedCount} SQL statements.");
                    }
                    else
                    {
                        Console.WriteLine("[Startup] ERROR: InitialMigration.sql not found in any expected location!");
                        foreach (var path in possiblePaths)
                        {
                            Console.WriteLine($"  Checked: {path}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"[Startup] Database schema already initialized ({tableCount} tables found).");
                }

                // ── app_config defaults (only inserts missing keys) ──────────
                // app_config schema: key, value, updated_at — no actor/correlation_id columns.
                command.CommandText = """
                    INSERT INTO app_config (key, value, updated_at)
                    VALUES ('Brokers:Registered', 'MStock,Zerodha,Upstox', NOW())
                    ON CONFLICT (key) DO NOTHING;
                    """;
                try { await command.ExecuteNonQueryAsync(); Console.WriteLine("[Startup] app_config defaults seeded."); }
                catch (Exception acEx) { Console.WriteLine($"[Startup] WARNING: app_config seed failed: {acEx.Message}"); }

                command.CommandText = """
                    INSERT INTO app_config (key, value, updated_at)
                    VALUES ('InstrumentFilter:FuturesTypes', 'FUT,FUTIDX,FUTSTK,FUTURES,IF,SF,FUTCOM,FUTCUR,FUTIRD', NOW())
                    ON CONFLICT (key) DO NOTHING;
                    """;
                try { await command.ExecuteNonQueryAsync(); }
                catch (Exception ftEx) { Console.WriteLine($"[Startup] WARNING: Futures types seed failed: {ftEx.Message}"); }

                command.CommandText = """
                    INSERT INTO app_config (key, value, updated_at)
                    VALUES ('InstrumentFilter:OptionsTypes', 'OPT,OPTIDX,OPTSTK,OPTIONS,CE,PE,IO,SO', NOW())
                    ON CONFLICT (key) DO NOTHING;
                    """;
                try { await command.ExecuteNonQueryAsync(); }
                catch (Exception otEx) { Console.WriteLine($"[Startup] WARNING: Options types seed failed: {otEx.Message}"); }

                // ── instrument_universe seed (idempotent — run 002 migration if table is empty) ──
                command.CommandText = "SELECT COUNT(*) FROM instrument_universe;";
                long universeCount = 0;
                try { universeCount = (long?)await command.ExecuteScalarAsync() ?? 0; }
                catch { universeCount = -1; }

                if (universeCount == 0)
                {
                    Console.WriteLine("[Startup] instrument_universe is empty — searching for 002_InstrumentUniverse.sql...");
                    string? universeMigrationPath = null;
                    var universePaths = new[]
                    {
                        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "rvs.AlgoTrader.Infrastructure", "Persistence", "Migrations", "002_InstrumentUniverse.sql"),
                        Path.Combine(AppContext.BaseDirectory, "..", "..", "rvs.AlgoTrader.Infrastructure", "Persistence", "Migrations", "002_InstrumentUniverse.sql"),
                        Path.Combine(AppContext.BaseDirectory, "Migrations", "002_InstrumentUniverse.sql"),
                        @"C:\Users\prvik\Downloads\algotrader-claude-kit\src\rvs.AlgoTrader.Infrastructure\Persistence\Migrations\002_InstrumentUniverse.sql"
                    };
                    foreach (var p in universePaths)
                    {
                        if (File.Exists(p)) { universeMigrationPath = p; break; }
                    }

                    if (!string.IsNullOrEmpty(universeMigrationPath))
                    {
                        var sql002 = await File.ReadAllTextAsync(universeMigrationPath);
                        var stmts002 = new List<string>();
                        var sb002 = new System.Text.StringBuilder();
                        foreach (var line in sql002.Split('\n'))
                        {
                            var t = line.Trim();
                            if (string.IsNullOrWhiteSpace(t) || t.StartsWith("--")) continue;
                            sb002.AppendLine(line);
                            if (t.EndsWith(';'))
                            {
                                var s = sb002.ToString().Trim();
                                if (!string.IsNullOrWhiteSpace(s)) stmts002.Add(s);
                                sb002.Clear();
                            }
                        }
                        int seeded = 0;
                        foreach (var s in stmts002)
                        {
                            command.CommandText = s;
                            try { await command.ExecuteNonQueryAsync(); seeded++; }
                            catch (Exception sEx) { Console.WriteLine($"[Startup] 002 stmt skip: {sEx.Message[..Math.Min(120, sEx.Message.Length)]}"); }
                        }
                        Console.WriteLine($"[Startup] 002_InstrumentUniverse.sql executed ({seeded} statements).");
                    }
                    else
                    {
                        Console.WriteLine("[Startup] WARNING: 002_InstrumentUniverse.sql not found — instrument_universe will be empty (passthrough mode).");
                    }
                }
                else if (universeCount > 0)
                {
                    Console.WriteLine($"[Startup] instrument_universe already has {universeCount} rows — skipping seed.");
                }

                // ── 003_FixInstrumentColumns.sql (idempotent — always run) ───
                // Adds/renames derivative columns (underlying, strike_price, option_type, expiry)
                // to match snake_case InstrumentConfiguration mappings.
                {
                    string? fix003Path = null;
                    var fix003Paths = new[]
                    {
                        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "rvs.AlgoTrader.Infrastructure", "Persistence", "Migrations", "003_FixInstrumentColumns.sql"),
                        Path.Combine(AppContext.BaseDirectory, "..", "..", "rvs.AlgoTrader.Infrastructure", "Persistence", "Migrations", "003_FixInstrumentColumns.sql"),
                        Path.Combine(AppContext.BaseDirectory, "Migrations", "003_FixInstrumentColumns.sql"),
                        @"C:\Users\prvik\Downloads\algotrader-claude-kit\src\rvs.AlgoTrader.Infrastructure\Persistence\Migrations\003_FixInstrumentColumns.sql"
                    };
                    foreach (var p in fix003Paths) { if (File.Exists(p)) { fix003Path = p; break; } }

                    if (!string.IsNullOrEmpty(fix003Path))
                    {
                        var sql003 = await File.ReadAllTextAsync(fix003Path);
                        var stmts003 = new List<string>();
                        var sb003 = new System.Text.StringBuilder();
                        bool inDollarBlock = false;
                        foreach (var line in sql003.Split('\n'))
                        {
                            var t = line.Trim();
                            if (string.IsNullOrWhiteSpace(t) || t.StartsWith("--")) continue;
                            if (t.StartsWith("$$") || t.Contains("$$")) inDollarBlock = !inDollarBlock;
                            sb003.AppendLine(line);
                            if (!inDollarBlock && t.EndsWith(';'))
                            {
                                var s = sb003.ToString().Trim();
                                if (!string.IsNullOrWhiteSpace(s)) stmts003.Add(s);
                                sb003.Clear();
                            }
                        }
                        foreach (var s in stmts003)
                        {
                            command.CommandText = s;
                            try { await command.ExecuteNonQueryAsync(); }
                            catch (Exception s3Ex) { Console.WriteLine($"[Startup] 003 stmt skip: {s3Ex.Message[..Math.Min(120, s3Ex.Message.Length)]}"); }
                        }
                        Console.WriteLine("[Startup] 003_FixInstrumentColumns.sql applied.");
                    }
                }

                // ── 004_BacktestAndForwardTestTrades.sql (idempotent) ────────
                {
                    string? fix004Path = null;
                    var fix004Paths = new[]
                    {
                        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "rvs.AlgoTrader.Infrastructure", "Persistence", "Migrations", "004_BacktestAndForwardTestTrades.sql"),
                        Path.Combine(AppContext.BaseDirectory, "..", "..", "rvs.AlgoTrader.Infrastructure", "Persistence", "Migrations", "004_BacktestAndForwardTestTrades.sql"),
                        Path.Combine(AppContext.BaseDirectory, "Migrations", "004_BacktestAndForwardTestTrades.sql"),
                        @"C:\Users\prvik\Downloads\algotrader-claude-kit\src\rvs.AlgoTrader.Infrastructure\Persistence\Migrations\004_BacktestAndForwardTestTrades.sql"
                    };
                    foreach (var p in fix004Paths) { if (File.Exists(p)) { fix004Path = p; break; } }

                    if (!string.IsNullOrEmpty(fix004Path))
                    {
                        var sql004 = await File.ReadAllTextAsync(fix004Path);
                        var stmts004 = new List<string>();
                        var sb004 = new System.Text.StringBuilder();
                        foreach (var line in sql004.Split('\n'))
                        {
                            var t = line.Trim();
                            if (string.IsNullOrWhiteSpace(t) || t.StartsWith("--")) continue;
                            sb004.AppendLine(line);
                            if (t.EndsWith(';'))
                            {
                                var s = sb004.ToString().Trim();
                                if (!string.IsNullOrWhiteSpace(s)) stmts004.Add(s);
                                sb004.Clear();
                            }
                        }
                        foreach (var s in stmts004)
                        {
                            command.CommandText = s;
                            try { await command.ExecuteNonQueryAsync(); }
                            catch (Exception s4Ex) { Console.WriteLine($"[Startup] 004 stmt skip: {s4Ex.Message[..Math.Min(120, s4Ex.Message.Length)]}"); }
                        }
                        Console.WriteLine("[Startup] 004_BacktestAndForwardTestTrades.sql applied.");
                    }
                }

                // ── broker_sessions table (idempotent — always run) ──────────
                // Ensures the table exists regardless of when the DB was first
                // created, so DbBrokerSessionPersistence can write/read on startup.
                command.CommandText = """
                    CREATE TABLE IF NOT EXISTS broker_sessions (
                        broker_name   VARCHAR(50)  PRIMARY KEY,
                        access_token  TEXT         NOT NULL,
                        feed_token    TEXT,
                        refresh_token TEXT,
                        expires_at    TIMESTAMPTZ,
                        stored_at     TIMESTAMPTZ  NOT NULL DEFAULT NOW()
                    );
                    """;
                try { await command.ExecuteNonQueryAsync(); }
                catch (Exception bsEx)
                {
                    Console.WriteLine($"[Startup] WARNING: broker_sessions table check failed: {bsEx.Message}");
                }
            }
        }
        finally
        {
            await connection.CloseAsync();
        }
}
catch (Exception ex)
{
    Console.WriteLine($"[Startup] ERROR: Database initialization failed: {ex.Message}");
    Console.WriteLine($"[Startup] Exception type: {ex.GetType().Name}");

    // Only re-throw critical table/schema errors (42P01 = relation does not exist)
    // Skip non-critical errors like missing extensions or functions
    if (ex.Message.Contains("42P01") ||
        (ex.Message.Contains("relation") && ex.Message.Contains("does not exist") && !ex.Message.Contains("function")))
    {
        throw;
    }

    // Continue startup for non-critical errors (TimescaleDB, extensions, etc.)
    Console.WriteLine("[Startup] WARNING: Continuing with non-critical database initialization error.");
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<IdempotencyMiddleware>();

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<QuoteHub>("/hubs/quotes");
app.MapHub<StrategyHub>("/hubs/strategies");
app.MapHub<AlertHub>("/hubs/alerts");

// Hangfire dashboard (authenticated)
app.MapHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new LocalRequestsOnlyAuthorizationFilter()]
});

// Startup orchestration
using (var scope = app.Services.CreateScope())
{
    var orchestrator = scope.ServiceProvider
        .GetRequiredService<rvs.AlgoTrader.Application.Services.IStartupOrchestrator>();
    await orchestrator.RunAsync(app.Lifetime.ApplicationStopping);
}

app.Run();

// Make Program accessible to WebApplicationFactory in integration tests
public partial class Program { }
