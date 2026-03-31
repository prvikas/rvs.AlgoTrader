using Hangfire;
using Hangfire.Dashboard;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using rvs.AlgoTrader.API.Extensions;
using rvs.AlgoTrader.API.Hubs;
using rvs.AlgoTrader.API.Messaging;
using rvs.AlgoTrader.API.Middleware;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Infrastructure.Extensions;
using rvs.AlgoTrader.Infrastructure.Persistence;
using rvs.AlgoTrader.Infrastructure.Repositories;
using rvs.AlgoTrader.Infrastructure.Services;
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

// Health checks — DB and Redis liveness/readiness probes
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<rvs.AlgoTrader.API.HealthChecks.DbHealthCheck>("postgres", tags: ["db", "ready"])
    .AddCheck<rvs.AlgoTrader.API.HealthChecks.RedisHealthCheck>("redis", tags: ["cache", "ready"]);

// API services (controllers, SignalR, JWT, CORS, rate limiting)
builder.Services.AddApiServices(builder.Configuration);

// MediatR — scans Application assembly
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(rvs.AlgoTrader.Application.Commands.Orders.PlaceOrderCommand).Assembly));

var app = builder.Build();

// ── Database migrations — auto-apply all numbered SQL files on startup ────────
// DatabaseMigrationRunner tracks applied files in schema_migrations table.
// To add a new migration: drop a new 00N_Name.sql in the Migrations folder.
// Program.cs never needs to change again.
try
{
    using var scope  = app.Services.CreateScope();
    var runner = scope.ServiceProvider.GetRequiredService<rvs.AlgoTrader.Infrastructure.Persistence.DatabaseMigrationRunner>();
    await runner.RunAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"[Startup] FATAL: Database migration failed: {ex.Message}");
    throw; // Always fatal — the app cannot run against an out-of-date schema
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
app.MapHub<BacktestHub>("/hubs/backtest");

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

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            status  = report.Status.ToString(),
            checks  = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString(), description = e.Value.Description }),
            elapsed = report.TotalDuration.TotalMilliseconds
        });
        await ctx.Response.WriteAsync(result);
    }
});
app.MapHealthChecks("/healthz", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = _ => false });

app.Run();

// Make Program accessible to WebApplicationFactory in integration tests
public partial class Program { }
