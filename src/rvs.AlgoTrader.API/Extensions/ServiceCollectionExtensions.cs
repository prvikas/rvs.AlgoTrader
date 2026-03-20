using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Strategies;

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

        services.AddSignalR();

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

        services.AddAuthorization();
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
