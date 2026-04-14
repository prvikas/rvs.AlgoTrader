using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using rvs.AlgoTrader.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Testcontainers.RabbitMq;

namespace rvs.AlgoTrader.IntegrationTests.Infrastructure;

/// <summary>
/// Custom WebApplicationFactory that spins up real Testcontainer instances:
///   - TimescaleDB-compatible PostgreSQL 16
///   - Redis 7 (AOF enabled)
///   - RabbitMQ 3.13
/// Each test class that uses this factory gets isolated containers via IClassFixture.
/// </summary>
public class AlgoTraderWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("timescale/timescaledb:latest-pg16")
        .WithDatabase("algotrader_test")
        .WithUsername("postgres")
        .WithPassword("postgres_test")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private readonly RabbitMqContainer _rabbitmq = new RabbitMqBuilder()
        .WithImage("rabbitmq:3.13-management-alpine")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Provide a fixed 32-byte test key so IFieldEncryptionService resolves in tests.
        builder.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // 32 bytes of 0x41 ('A') — valid 256-bit key for testing only
                ["FieldEncryption:Key"] = "QUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUE="
            }));

        builder.ConfigureServices(services =>
        {
            // Replace DB context with test container connection
            services.RemoveAll<DbContextOptions<AlgoTraderDbContext>>();
            services.AddDbContext<AlgoTraderDbContext>(opts =>
                opts.UseNpgsql(_postgres.GetConnectionString(),
                    npgsql => npgsql.UseNodaTime()));

            // Override Redis connection string
            services.RemoveAll<StackExchange.Redis.IConnectionMultiplexer>();
            services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ =>
                StackExchange.Redis.ConnectionMultiplexer.Connect(_redis.GetConnectionString()));

            // Override RabbitMQ host
            services.AddMassTransitTestHarness();
        });

        builder.UseEnvironment("Testing");
    }

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync(), _rabbitmq.StartAsync());

        // Apply EF migrations / SQL schema
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlgoTraderDbContext>();
        await db.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask(), _rabbitmq.DisposeAsync().AsTask());
    }
}
