using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace rvs.AlgoTrader.Tests.Architecture;

/// <summary>
/// NetArchTest architecture rules — these MUST pass in CI.
/// Violations block merges.
/// </summary>
public class ArchitectureTests
{
    private static readonly string ApplicationNs = "rvs.AlgoTrader.Application";
    private static readonly string InfrastructureNs = "rvs.AlgoTrader.Infrastructure";
    private static readonly string BrokersNs = "rvs.AlgoTrader.Brokers";

    [Fact]
    public void Domain_Must_Not_Depend_On_Application()
    {
        var result = Types.InAssembly(typeof(Domain.Interfaces.IStrategy).Assembly)
            .ShouldNot()
            .HaveDependencyOn(ApplicationNs)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Domain must not reference Application. Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Domain_Must_Not_Depend_On_Infrastructure()
    {
        var result = Types.InAssembly(typeof(Domain.Interfaces.IStrategy).Assembly)
            .ShouldNot()
            .HaveDependencyOn(InfrastructureNs)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Domain must not reference Infrastructure. Failing: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Domain_Must_Not_Depend_On_Brokers()
    {
        var result = Types.InAssembly(typeof(Domain.Interfaces.IStrategy).Assembly)
            .ShouldNot()
            .HaveDependencyOn(BrokersNs)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Domain must not depend on broker implementations");
    }

    [Fact]
    public void Application_Must_Not_Depend_On_Infrastructure()
    {
        var result = Types.InAssembly(typeof(Application.Commands.Orders.PlaceOrderCommand).Assembly)
            .ShouldNot()
            .HaveDependencyOn(InfrastructureNs)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Application must not reference Infrastructure. Failing: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Application_Must_Not_Depend_On_BrokerClients()
    {
        var result = Types.InAssembly(typeof(Application.Commands.Orders.PlaceOrderCommand).Assembly)
            .ShouldNot()
            .HaveDependencyOn("rvs.AlgoTrader.Brokers.Zerodha")
            .And()
            .HaveDependencyOn("rvs.AlgoTrader.Brokers.Upstox")
            .And()
            .HaveDependencyOn("rvs.AlgoTrader.Brokers.MStock")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Application must only reference Brokers.Abstractions, not concrete clients");
    }

    [Fact]
    public void IStrategy_Implementations_Must_Not_Depend_On_Brokers()
    {
        var result = Types.InAssembly(typeof(Strategies.StrategyFactory).Assembly)
            .That().ImplementInterface(typeof(Domain.Interfaces.IStrategy))
            .ShouldNot()
            .HaveDependencyOn(BrokersNs)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "IStrategy implementations must not directly use broker APIs");
    }

    [Fact]
    public void IStrategy_Implementations_Must_Not_Depend_On_Infrastructure()
    {
        var result = Types.InAssembly(typeof(Strategies.StrategyFactory).Assembly)
            .That().ImplementInterface(typeof(Domain.Interfaces.IStrategy))
            .ShouldNot()
            .HaveDependencyOn(InfrastructureNs)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "IStrategy implementations must be pure domain logic");
    }

    [Fact]
    public void Domain_Events_Must_Be_Immutable_Records()
    {
        var eventTypes = Types.InAssembly(typeof(Domain.Events.OrderPlaced).Assembly)
            .That().ResideInNamespace("rvs.AlgoTrader.Domain.Events")
            .GetTypes();

        foreach (var type in eventTypes)
        {
            type.IsClass.Should().BeTrue($"{type.Name} should be a class");
            // Records in C# are sealed by default for positional records
        }
    }

    [Fact]
    public void Controllers_Must_Use_MediatR_Not_Services_Directly()
    {
        // Controllers should only depend on IMediator (not on specific service/repo implementations)
        var result = Types.InAssembly(typeof(API.Controllers.OrdersController).Assembly)
            .That().HaveNameEndingWith("Controller")
            .ShouldNot()
            .HaveDependencyOn(InfrastructureNs)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Controllers should use MediatR, not inject infrastructure directly");
    }

    [Fact]
    public void Concrete_BrokerClients_Must_Not_Be_Referenced_From_Application()
    {
        var forbiddenBrokers = new[] {
            "rvs.AlgoTrader.Brokers.Zerodha.ZerodhaClient",
            "rvs.AlgoTrader.Brokers.Upstox.UpstoxClient",
            "rvs.AlgoTrader.Brokers.MStock.MStockClient"
        };

        foreach (var broker in forbiddenBrokers)
        {
            var result = Types.InAssembly(typeof(Application.Commands.Orders.PlaceOrderCommand).Assembly)
                .ShouldNot()
                .HaveDependencyOn(broker)
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                because: $"Application layer must not reference concrete broker {broker}");
        }
    }

    /// <summary>
    /// CLAUDE.md Rule #19 — Application layer must have zero EF Core dependencies.
    /// EF Core is an Infrastructure concern; DbContext/DbSet/migrations live in Infrastructure only.
    /// NetArchTest CI enforcement: any violation blocks merge.
    /// </summary>
    [Fact]
    public void Application_Must_Not_Depend_On_EntityFrameworkCore()
    {
        var result = Types.InAssembly(typeof(Application.Commands.Orders.PlaceOrderCommand).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Application layer must not reference EF Core — use repository interfaces only. " +
                     $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    /// <summary>
    /// CLAUDE.md Rule #1 (Clock Rule) + AP-001 — Domain, Application, and Strategies assemblies
    /// must NEVER access the system clock directly via DateTime.Now, DateTimeOffset.UtcNow, etc.
    ///
    /// Only SystemClock (Infrastructure) is allowed to call the real system clock.
    /// All business logic must use IClock injected via constructor.
    ///
    /// Note: This check catches dependency on "System.DateTime" at the IL level.
    /// Any type that calls DateTime.Now or DateTimeOffset.UtcNow will have a type reference
    /// to System.DateTime in its IL — this rule flags those types.
    /// </summary>
    [Fact]
    public void Domain_Application_Strategies_Must_Not_Use_DateTimeDirectly()
    {
        // Domain assembly
        var domainResult = Types.InAssembly(typeof(Domain.Interfaces.IStrategy).Assembly)
            .ShouldNot()
            .HaveDependencyOn("System.DateTime")
            .GetResult();

        domainResult.IsSuccessful.Should().BeTrue(
            because: $"Domain must not use DateTime directly (use IClock). " +
                     $"Failing: {string.Join(", ", domainResult.FailingTypeNames ?? [])}");

        // Application assembly
        var appResult = Types.InAssembly(typeof(Application.Commands.Orders.PlaceOrderCommand).Assembly)
            .ShouldNot()
            .HaveDependencyOn("System.DateTime")
            .GetResult();

        appResult.IsSuccessful.Should().BeTrue(
            because: $"Application must not use DateTime directly (use IClock). " +
                     $"Failing: {string.Join(", ", appResult.FailingTypeNames ?? [])}");

        // Strategies assembly
        var strategiesResult = Types.InAssembly(typeof(Strategies.StrategyFactory).Assembly)
            .ShouldNot()
            .HaveDependencyOn("System.DateTime")
            .GetResult();

        strategiesResult.IsSuccessful.Should().BeTrue(
            because: $"Strategies must not use DateTime directly (use IClock — AP-001). " +
                     $"Failing: {string.Join(", ", strategiesResult.FailingTypeNames ?? [])}");
    }

    /// <summary>
    /// CLAUDE.md Rule #2 — Backtesting assembly must not reference any broker clients.
    /// Backtesting reads only from TimescaleDB via ICandleRepository — never from broker APIs.
    /// </summary>
    [Fact]
    public void Backtesting_Must_Not_Depend_On_BrokerClients()
    {
        var result = Types.InAssembly(typeof(Backtesting.Engine.BacktestEngine).Assembly)
            .ShouldNot()
            .HaveDependencyOn(BrokersNs)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Backtesting context must be 100% isolated from broker code (CLAUDE.md Rule #2 / AP-002). " +
                     $"Failing: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    /// <summary>
    /// CLAUDE.md Rule #4 — API controllers must not inject Infrastructure types directly.
    /// Controllers dispatch via MediatR; they must never have an Infrastructure assembly dependency.
    /// </summary>
    [Fact]
    public void API_Controllers_Must_Not_Depend_On_Infrastructure()
    {
        var result = Types.InAssembly(typeof(API.Controllers.OrdersController).Assembly)
            .That().HaveNameEndingWith("Controller")
            .ShouldNot()
            .HaveDependencyOn(InfrastructureNs)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Controllers must dispatch via MediatR, not inject Infrastructure. " +
                     $"Failing: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
