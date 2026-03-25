# Skill: Testing Patterns — AlgoTrader

## Purpose
Testing conventions, fixtures, and patterns for the AlgoTrader test suite.
Load when writing any test file.

---

## Test Project Structure

```
tests/
├── rvs.AlgoTrader.UnitTests/
│   ├── Strategies/                   # Strategy signal tests
│   ├── Services/                     # Domain service tests
│   ├── Infrastructure/               # Infrastructure unit tests
│   ├── Architecture/                 # NetArchTest rules
│   └── Fixtures/                     # Shared test data factories
├── rvs.AlgoTrader.IntegrationTests/
│   ├── Api/                          # Controller integration tests
│   ├── Brokers/                      # Broker adapter tests (WireMock)
│   ├── Infrastructure/               # Redis, DB, RabbitMQ tests
│   └── Scenarios/                    # Full end-to-end scenarios
└── rvs.AlgoTrader.Tests.UI/
    ├── Pages/                        # Page Object Model classes
    └── Scenarios/                    # Playwright test scenarios
```

---

## Unit Test Conventions

### Naming
```csharp
// Format: MethodName_Scenario_ExpectedResult
[Fact]
public async Task EvaluateAsync_WhenVolumeAboveThresholdAndBreakout_ReturnsBuySignal() { }

[Fact]
public async Task TryReserveAsync_WhenTwoConcurrentRequests_OnlyOneSucceeds() { }

[Fact]
public void IsWithinScheduledSession_WhenMarketHoliday_ReturnsFalse() { }
```

### SimulatedClock — Always Use in Unit Tests
```csharp
// NEVER use SystemClock in unit tests
// ALWAYS create a SimulatedClock at a fixed IST time

// Standard trading day fixture times:
public static class TestClocks
{
    // 9:15 IST — market open
    public static SimulatedClock MarketOpen() =>
        new(Instant.FromUtc(2024, 1, 15, 3, 45, 0)); // 9:15 IST = 3:45 UTC
    
    // 10:30 IST — mid-morning
    public static SimulatedClock MidMorning() =>
        new(Instant.FromUtc(2024, 1, 15, 5, 0, 0));
    
    // 15:29 IST — just before close
    public static SimulatedClock BeforeClose() =>
        new(Instant.FromUtc(2024, 1, 15, 9, 59, 0));
    
    // 15:31 IST — after market close
    public static SimulatedClock AfterClose() =>
        new(Instant.FromUtc(2024, 1, 15, 10, 1, 0));
    
    // Saturday — market closed
    public static SimulatedClock Weekend() =>
        new(Instant.FromUtc(2024, 1, 20, 5, 0, 0)); // Saturday IST
}
```

### Candle Fixtures
```csharp
public static class CandleFixtures
{
    // Generates N candles starting at a given time
    public static IReadOnlyList<Candle> GenerateBullTrend(
        string symbol, string timeframe, int count,
        decimal startPrice = 1000m, Instant? startTime = null)
    {
        var candles = new List<Candle>();
        var time = startTime ?? Instant.FromUtc(2024, 1, 15, 3, 45, 0);
        var price = startPrice;
        var intervalMinutes = TimeframeToMinutes(timeframe);
        
        for (int i = 0; i < count; i++)
        {
            price += Random.Shared.NextSingle() * 5 - 1; // slight upward bias
            candles.Add(new Candle(
                symbol, timeframe, time,
                Open: price - 2, High: price + 3, Low: price - 4, Close: price,
                Volume: (long)(100000 + Random.Shared.Next(-10000, 10000))
            ));
            time += Duration.FromMinutes(intervalMinutes);
        }
        return candles;
    }

    // Creates a breakout candle above a swing high
    public static Candle BreakoutCandle(
        string symbol, Instant time, decimal swingHigh, 
        decimal volumeMultiplier = 2.0m, long baseVolume = 100000) =>
        new(symbol, "15m", time,
            Open: swingHigh - 2, 
            High: swingHigh + 5,  // clearly breaks above swing high
            Low: swingHigh - 3, 
            Close: swingHigh + 3, // strong close above
            Volume: (long)(baseVolume * volumeMultiplier)
        );
}
```

---

## Integration Test Conventions

### Testcontainers Fixture
```csharp
// rvs.AlgoTrader.IntegrationTests/IntegrationTestFixture.cs
public class IntegrationTestFixture : IAsyncLifetime
{
    public PostgreSqlContainer PostgreSQL { get; } = new PostgreSqlBuilder()
        .WithImage("timescale/timescaledb:latest-pg16")
        .Build();
    
    public RedisContainer Redis { get; } = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();
    
    public RabbitMqContainer RabbitMQ { get; } = new RabbitMqBuilder()
        .WithImage("rabbitmq:3-management-alpine")
        .Build();
    
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    
    public async Task InitializeAsync()
    {
        await Task.WhenAll(PostgreSQL.StartAsync(), Redis.StartAsync(), RabbitMQ.StartAsync());
        
        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Override DB connection strings with container endpoints
                    services.Configure<DatabaseOptions>(o => 
                        o.ConnectionString = PostgreSQL.GetConnectionString());
                    services.Configure<RedisOptions>(o => 
                        o.ConnectionString = Redis.GetConnectionString());
                    services.Configure<RabbitMqOptions>(o => 
                        o.Host = RabbitMQ.Hostname);
                });
            });
        
        // Run migrations
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AlgoTraderDbContext>();
        await db.Database.MigrateAsync();
    }
    
    public async Task DisposeAsync()
    {
        await Task.WhenAll(PostgreSQL.DisposeAsync().AsTask(), 
            Redis.DisposeAsync().AsTask(), RabbitMQ.DisposeAsync().AsTask());
    }
}
```

### Respawn for DB Reset
```csharp
// Reset DB between tests
public class OrderPlacementTests : IClassFixture<IntegrationTestFixture>, IAsyncLifetime
{
    private Respawner _respawner = null!;
    
    public async Task InitializeAsync()
    {
        _respawner = await Respawner.CreateAsync(
            _fixture.PostgreSQL.GetConnectionString(),
            new RespawnerOptions
            {
                TablesToIgnore = ["__EFMigrationsHistory", "market_calendar"],
                SchemasToInclude = ["public"]
            });
    }
    
    public async Task DisposeAsync() => await _respawner.ResetAsync(/* connection */);
}
```

---

## NetArchTest — Architecture Tests

```csharp
// rvs.AlgoTrader.UnitTests/Architecture/ArchitectureTests.cs
public class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(rvs.AlgoTrader.Domain.Entities.Order).Assembly;
    private static readonly Assembly Application = typeof(rvs.AlgoTrader.Application.Commands.PlaceOrderCommand).Assembly;
    private static readonly Assembly Infrastructure = typeof(rvs.AlgoTrader.Infrastructure.AlgoTraderDbContext).Assembly;
    private static readonly Assembly Strategies = typeof(rvs.AlgoTrader.Strategies.PriceActionBreakoutStrategy).Assembly;
    private static readonly Assembly Backtesting = typeof(rvs.AlgoTrader.Backtesting.BacktestEngine).Assembly;

    [Fact]
    public void Domain_ShouldNotDependOnInfrastructure()
    {
        var result = Types.InAssembly(Domain)
            .Should().NotHaveDependencyOn("rvs.AlgoTrader.Infrastructure")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join("\n", result.FailingTypeNames));
    }

    [Fact]
    public void Domain_ShouldNotDependOnEntityFramework()
    {
        var result = Types.InAssembly(Domain)
            .Should().NotHaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join("\n", result.FailingTypeNames));
    }

    [Fact]
    public void Application_ShouldNotDependOnEntityFramework()
    {
        var result = Types.InAssembly(Application)
            .Should().NotHaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join("\n", result.FailingTypeNames));
    }

    [Fact]
    public void Strategies_ShouldNotDependOnBrokers()
    {
        var result = Types.InAssembly(Strategies)
            .That().ImplementInterface(typeof(IStrategy))
            .Should().NotHaveDependencyOn("rvs.AlgoTrader.Brokers")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join("\n", result.FailingTypeNames));
    }

    [Fact]
    public void Backtesting_ShouldNotDependOnBrokerImplementations()
    {
        foreach (var broker in new[] { "rvs.AlgoTrader.Brokers.Zerodha", "rvs.AlgoTrader.Brokers.Upstox", "rvs.AlgoTrader.Brokers.MStock" })
        {
            var result = Types.InAssembly(Backtesting)
                .Should().NotHaveDependencyOn(broker)
                .GetResult();
            Assert.True(result.IsSuccessful, $"Backtesting depends on {broker}: " + 
                string.Join("\n", result.FailingTypeNames));
        }
    }

    [Fact]
    public void IStrategyImplementations_ShouldNotDependOnExecutionEngines()
    {
        var result = Types.InAssembly(Strategies)
            .That().ImplementInterface(typeof(IStrategy))
            .Should().NotHaveDependencyOn("IExecutionEngine")
            .And().NotHaveDependencyOn("IBrokerOrderClient")
            .GetResult();
        Assert.True(result.IsSuccessful, string.Join("\n", result.FailingTypeNames));
    }

    /// <summary>
    /// Enforces CLAUDE.md Hard Rule #1 — no direct system clock access in Domain,
    /// Application, or Strategies. All time reads must go through IClock.
    /// Catches DateTime.Now, DateTime.UtcNow, DateTimeOffset.UtcNow in business logic.
    /// NodaTime.SystemClock.Instance is only allowed in rvs.AlgoTrader.Infrastructure (SystemClock impl).
    /// </summary>
    [Fact]
    public void Domain_ShouldNotUseSystemDateTime()
    {
        var result = Types.InAssembly(Domain)
            .Should().NotHaveDependencyOn("System.DateTime")
            .GetResult();
        Assert.True(result.IsSuccessful,
            "Domain uses DateTime directly. Use IClock instead:\n" +
            string.Join("\n", result.FailingTypeNames));
    }

    [Fact]
    public void Application_ShouldNotUseSystemDateTime()
    {
        var result = Types.InAssembly(Application)
            .Should().NotHaveDependencyOn("System.DateTime")
            .GetResult();
        Assert.True(result.IsSuccessful,
            "Application uses DateTime directly. Use IClock instead:\n" +
            string.Join("\n", result.FailingTypeNames));
    }

    [Fact]
    public void Strategies_ShouldNotUseSystemDateTime()
    {
        var result = Types.InAssembly(Strategies)
            .Should().NotHaveDependencyOn("System.DateTime")
            .GetResult();
        Assert.True(result.IsSuccessful,
            "Strategies use DateTime directly. Use IClock instead:\n" +
            string.Join("\n", result.FailingTypeNames));
    }
}
```

---

## Parity Test — Backtest vs Forward Test

```csharp
// Verifies backtest and forward test produce identical signals from same candles
[Fact]
public async Task BacktestAndForwardTest_SameCandles_ProduceIdenticalSignals()
{
    // Arrange
    var candles = CandleFixtures.GenerateBullTrend("RELIANCE", "15m", 200);
    var config = StrategyConfigFixture.PriceActionBreakoutDefault();
    var clock = new SimulatedClock(candles[0].Timestamp);
    
    var backtestEngine = new BacktestExecutionEngine(/* deps */);
    var forwardEngine = new SimulatedExecutionEngine(/* deps */);
    var strategy = new PriceActionBreakoutStrategy(config, clock, /* indicators */);
    
    // Act
    var backtestSignals = await RunThroughEngineAsync(strategy, backtestEngine, candles, clock);
    
    clock = new SimulatedClock(candles[0].Timestamp); // reset clock
    var forwardSignals = await RunThroughEngineAsync(strategy, forwardEngine, candles, clock);
    
    // Assert — identical signal sequences
    Assert.Equal(backtestSignals.Count, forwardSignals.Count);
    for (int i = 0; i < backtestSignals.Count; i++)
    {
        Assert.Equal(backtestSignals[i].Signal, forwardSignals[i].Signal);
        Assert.Equal(backtestSignals[i].EntryPrice, forwardSignals[i].EntryPrice);
        Assert.Equal(backtestSignals[i].StopLoss, forwardSignals[i].StopLoss);
    }
}
```

---

## Playwright Test Conventions

### Page Object Model
```csharp
// rvs.AlgoTrader.Tests.UI/Pages/DashboardPage.cs
public class DashboardPage(IPage page)
{
    public async Task<bool> IsKillSwitchVisibleAsync() =>
        await page.Locator("[data-testid='kill-switch-btn']").IsVisibleAsync();
    
    public async Task ClickKillSwitchAsync()
    {
        await page.Locator("[data-testid='kill-switch-btn']").ClickAsync();
        await page.Locator("[data-testid='kill-switch-confirm']").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='kill-switch-active-badge']");
    }
    
    public async Task<string> GetActiveBrokerBadgeAsync(string broker) =>
        await page.Locator($"[data-testid='broker-badge-{broker.ToLower()}']").TextContentAsync() ?? "";
}

// rvs.AlgoTrader.Tests.UI/Pages/OrderFormPage.cs
public class OrderFormPage(IPage page)
{
    public async Task<string> GetIdempotencyKeyAsync() =>
        await page.InputValue("[data-testid='idempotency-key-input']");
    
    public async Task SubmitOrderAsync(OrderFormData data)
    {
        await page.FillAsync("[data-testid='symbol-input']", data.Symbol);
        await page.SelectOptionAsync("[data-testid='order-type-select']", data.OrderType);
        await page.FillAsync("[data-testid='quantity-input']", data.Quantity.ToString());
        await page.ClickAsync("[data-testid='submit-order-btn']");
    }
}
```

### React data-testid Conventions
```tsx
// EVERY interactive element must have a data-testid
// Format: kebab-case, descriptive

<button data-testid="kill-switch-btn" onClick={handleKillSwitch}>
  Kill Switch
</button>

<input data-testid="idempotency-key-input" type="hidden" value={idempotencyKey} />

<span data-testid={`broker-badge-${broker.toLowerCase()}`} 
      className={cn("badge", isConnected ? "badge-green" : "badge-red")}>
  {broker}
</span>

<div data-testid="partial-bar-label" className="text-yellow-500 text-xs">
  ⚠ In progress — not used for signals
</div>
```

---

## Test Data Factories

```csharp
// Shared factories — DRY principle
public static class StrategyConfigFixture
{
    public static PriceActionBreakoutConfig PriceActionBreakoutDefault() => new()
    {
        SwingLookback = 5,
        VolumeSmaperiod = 10,
        VolumeMultiplier = 1.5m,
        UseEmaFilter = false, // disabled for simple fixture tests
        RRRatio = 2.0m,
        SlBufferPercent = 0.001m
    };
}

public static class RiskProfileFixture
{
    public static RiskProfile Conservative() => new()
    {
        MaxCapitalPerTradePct = 2.0m,
        MaxOpenTradesPerSymbol = 1,
        MaxDailyDrawdownPct = 3.0m,
        MaxTotalCapitalDeployed = 50000m,
        MaxTradesPerDay = 5
    };
}

public static class CapitalAllocationFixture
{
    public static CapitalAllocation Standard(Guid strategyId) => new()
    {
        StrategyInstanceId = strategyId,
        BrokerName = "Zerodha",
        AllocatedCapital = 100000m,
        UsedCapital = 0m
    };
}
```
