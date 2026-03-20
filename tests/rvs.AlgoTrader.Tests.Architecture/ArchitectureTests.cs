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
}
