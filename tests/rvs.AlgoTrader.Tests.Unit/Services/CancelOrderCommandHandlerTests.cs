using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NodaTime;
using rvs.AlgoTrader.Application.Commands.Orders;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using DomainClock = rvs.AlgoTrader.Domain.Interfaces.IClock;
using Xunit;

namespace rvs.AlgoTrader.Tests.Unit.Services;

/// <summary>
/// Unit tests for CancelOrderCommandHandler.
/// Verifies broker call routing, DB status update, and exception handling.
/// </summary>
public class CancelOrderCommandHandlerTests
{
    private readonly Mock<IBrokerClientFactory> _brokerFactory = new();
    private readonly Mock<IBrokerOrderClient> _orderClient = new();
    private readonly Mock<IOrderRepository> _orders = new();
    private readonly Mock<DomainClock> _clock = new();
    private readonly CancelOrderCommandHandler _sut;

    public CancelOrderCommandHandlerTests()
    {
        _clock.Setup(c => c.NowInstant()).Returns(Instant.FromUtc(2026, 4, 18, 10, 0));
        _brokerFactory.Setup(f => f.GetOrderClient("MStock")).Returns(_orderClient.Object);
        _sut = new CancelOrderCommandHandler(
            _brokerFactory.Object,
            _orders.Object,
            _clock.Object,
            NullLogger<CancelOrderCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_BrokerCancelsSuccessfully_ReturnsTrue()
    {
        _orderClient.Setup(c => c.CancelOrderAsync("ORD001", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(true);
        _orders.Setup(r => r.GetByBrokerOrderIdAsync("ORD001", It.IsAny<CancellationToken>()))
               .ReturnsAsync((Order?)null);

        var result = await _sut.Handle(new CancelOrderCommand("ORD001", "MStock"), CancellationToken.None);

        result.Should().BeTrue();
        _orderClient.Verify(c => c.CancelOrderAsync("ORD001", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_BrokerCancelsSuccessfully_AndOrderFoundInDb_MarksOrderCancelled()
    {
        var order = BuildOrder("ORD001");
        _orderClient.Setup(c => c.CancelOrderAsync("ORD001", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(true);
        _orders.Setup(r => r.GetByBrokerOrderIdAsync("ORD001", It.IsAny<CancellationToken>()))
               .ReturnsAsync(order);
        _orders.Setup(r => r.UpdateAsync(order, It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);

        var result = await _sut.Handle(new CancelOrderCommand("ORD001", "MStock"), CancellationToken.None);

        result.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
        _orders.Verify(r => r.UpdateAsync(order, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_BrokerReturnsFalse_ReturnsFalse_WithoutDbUpdate()
    {
        _orderClient.Setup(c => c.CancelOrderAsync("ORD001", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(false);

        var result = await _sut.Handle(new CancelOrderCommand("ORD001", "MStock"), CancellationToken.None);

        result.Should().BeFalse();
        _orders.Verify(r => r.GetByBrokerOrderIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_BrokerThrows_ReturnsFalse_DoesNotPropagate()
    {
        _orderClient.Setup(c => c.CancelOrderAsync("ORD001", It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new HttpRequestException("broker timeout"));

        var act = async () => await _sut.Handle(new CancelOrderCommand("ORD001", "MStock"), CancellationToken.None);

        // Must not throw — handler catches and returns false
        await act.Should().NotThrowAsync();
        var result = await _sut.Handle(new CancelOrderCommand("ORD001", "MStock"), CancellationToken.None);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_OrderNotFoundInDb_StillReturnsTrue()
    {
        // Broker confirms cancellation but the order doesn't exist in our DB (edge case: race).
        _orderClient.Setup(c => c.CancelOrderAsync("ORD999", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(true);
        _orders.Setup(r => r.GetByBrokerOrderIdAsync("ORD999", It.IsAny<CancellationToken>()))
               .ReturnsAsync((Order?)null);

        var result = await _sut.Handle(new CancelOrderCommand("ORD999", "MStock"), CancellationToken.None);

        result.Should().BeTrue("broker confirmed cancellation — DB miss is a non-fatal gap");
        _orders.Verify(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Order BuildOrder(string brokerOrderId)
    {
        var now = Instant.FromUtc(2026, 4, 18, 9, 30);
        var order = Order.Create(
            brokerName:      "MStock",
            internalSymbol:  "RELIANCE",
            orderType:       OrderType.Market,
            direction:       OrderDirection.Buy,
            quantity:        10,
            price:           null,
            triggerPrice:    null,
            idempotencyKey:  Guid.NewGuid().ToString(),
            correlationId:   Guid.NewGuid().ToString(),
            strategyRunId:   null,
            now:             now);
        // Simulate it being placed with a broker order ID via reflection (private setter)
        typeof(Order)
            .GetProperty("BrokerOrderId")!
            .SetValue(order, brokerOrderId);
        return order;
    }
}
