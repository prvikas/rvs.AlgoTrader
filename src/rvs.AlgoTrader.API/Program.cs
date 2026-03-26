using Hangfire;
using Hangfire.Dashboard;
using MassTransit;
using Microsoft.EntityFrameworkCore;
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
builder.Host.UseSerilog((ctx, lc) => lc.ReadFrom.Configuration(ctx.Configuration));

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
