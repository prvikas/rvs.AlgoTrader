using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using NodaTime.Serialization.SystemTextJson;
using rvs.AlgoTrader.API.Authorization;
using rvs.AlgoTrader.API.Services;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Backtesting.Engine;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Infrastructure.Repositories;
using rvs.AlgoTrader.Infrastructure.Services;
using rvs.AlgoTrader.Strategies;
using rvs.AlgoTrader.Strategies.ShortPremiumVelocity;
using System.Text;

namespace rvs.AlgoTrader.API.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration config)
    {
        services.AddControllers()
            .AddJsonOptions(opts =>
            {
                opts.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                opts.JsonSerializerOptions.DefaultIgnoreCondition =
                    System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
                opts.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
                // NodaTime types (LocalDate, Instant, ZonedDateTime, etc.) used in request/response DTOs
                opts.JsonSerializerOptions.ConfigureForNodaTime(NodaTime.DateTimeZoneProviders.Tzdb);
            });

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new() { Title = "rvs.AlgoTrader API", Version = "v1" });
            c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
                Scheme = "bearer", BearerFormat = "JWT"
            });
        });

        // SignalR: must use the same JSON options as AddControllers so that enum values
        // (BacktestJobStatus, etc.) are serialised as strings, not integers.
        // Without this, TypeScript receives { status: 2 } instead of { status: "Running" }
        // and all status comparisons in the frontend fail silently.
        services.AddSignalR()
            .AddJsonProtocol(opts =>
            {
                opts.PayloadSerializerOptions.PropertyNamingPolicy =
                    System.Text.Json.JsonNamingPolicy.CamelCase;
                opts.PayloadSerializerOptions.DefaultIgnoreCondition =
                    System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
                opts.PayloadSerializerOptions.Converters.Add(
                    new System.Text.Json.Serialization.JsonStringEnumConverter());
                // NodaTime types used in BacktestResultDto (dates/instants)
                opts.PayloadSerializerOptions.ConfigureForNodaTime(
                    NodaTime.DateTimeZoneProviders.Tzdb);
            });

        var jwtKey = config["JWT__SECRET"] ?? throw new InvalidOperationException("JWT__SECRET not configured");
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(opts =>
            {
                opts.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ClockSkew = TimeSpan.Zero
                };
                // Allow JWT in SignalR query string
                opts.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) &&
                            ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                            ctx.Token = token;
                        return Task.CompletedTask;
                    }
                };
            });

        // Strategy factory — registered here because only the API project references rvs.AlgoTrader.Strategies
        services.AddSingleton<IStrategyFactory, StrategyFactory>();

        // STRAT-004: Short Premium Velocity engine services (singleton; hold in-memory rolling state)
        services.AddShortPremiumVelocity();

        // BacktestEngine — registered here because only the API project references rvs.AlgoTrader.Backtesting.
        // Infrastructure defines IBacktestEngine (Application layer interface) but cannot reference
        // the Backtesting assembly directly (CLAUDE.md Rule #2 — no cross-context direct calls).
        services.AddScoped<IBacktestEngine, BacktestEngine>();
        services.AddScoped<IWalkForwardEngine, WalkForwardEngine>();
        services.AddSingleton<IMonteCarloSimulator, MonteCarloSimulator>();

        // ICurrentUser — reads JWT identity from HTTP context; falls back to "System" outside request scope.
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        // IBacktestProgressPusher — implemented here (API) because it requires IHubContext<BacktestHub>.
        // BacktestJobManager (Infrastructure) depends on IBacktestProgressPusher (Application interface)
        // to avoid a circular dependency (Infrastructure → API is not allowed).
        services.AddSingleton<IBacktestProgressPusher, SignalRBacktestProgressPusher>();

        // IBacktestJobManager — singleton so job state survives across HTTP request scopes.
        // Depends on IBacktestProgressPusher (registered above) and IBacktestEngine (scoped).
        // Uses IServiceScopeFactory to resolve scoped services per job run.
        services.AddSingleton<IBacktestJobManager, BacktestJobManager>();

        // ForwardTestEngine — must be Singleton: holds in-memory _activeStates per instance.
        // Scoped lifetime would lose all state on every new HTTP request scope.
        // IForwardTestEngine is defined in Application layer; ForwardTestEngine is in Backtesting.
        services.AddSingleton<IForwardTestEngine, ForwardTestEngine>();

        // #128: 6-tier RBAC. Each policy requires the "role" claim to be one of the
        // listed values. Higher tiers inherit lower-tier access (additive allowlist).
        services.AddAuthorization(opts =>
        {
            opts.AddPolicy(PolicyNames.Viewer, p => p.RequireAuthenticatedUser());
            opts.AddPolicy(PolicyNames.Analyst, p => p.RequireClaim("role",
                "Analyst", "Trader", "RiskManager", "Admin", "SuperAdmin"));
            opts.AddPolicy(PolicyNames.Trader, p => p.RequireClaim("role",
                "Trader", "RiskManager", "Admin", "SuperAdmin"));
            opts.AddPolicy(PolicyNames.RiskManager, p => p.RequireClaim("role",
                "RiskManager", "Admin", "SuperAdmin"));
            opts.AddPolicy(PolicyNames.Admin, p => p.RequireClaim("role",
                "Admin", "SuperAdmin"));
            opts.AddPolicy(PolicyNames.SuperAdmin, p => p.RequireClaim("role",
                "SuperAdmin"));
        });
        services.AddCors(opts =>
            opts.AddDefaultPolicy(p => p.WithOrigins(
                    config["CORS__ORIGINS"]?.Split(',') ?? ["http://localhost:3000"])
                .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

        services.AddRateLimiter(opts =>
        {
            opts.AddFixedWindowLimiter("api", lim =>
            {
                lim.Window = TimeSpan.FromMinutes(1);
                lim.PermitLimit = 300;
                lim.QueueLimit = 0;
            });
        });

        return services;
    }
}
