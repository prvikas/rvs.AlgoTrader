using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NodaTime;
using rvs.AlgoTrader.API.Hubs;
using rvs.AlgoTrader.Domain.Events;
using rvs.AlgoTrader.API.Messaging;
using Xunit;
using FluentAssertions;
using MassTransit;

namespace rvs.AlgoTrader.Tests.Unit.Messaging;

public class SignalRHubConsumerTests
{
    private readonly Mock<IHubContext<StrategyHub>> _strategyHubMock = new();
    private readonly Mock<IHubContext<AlertHub>> _alertHubMock = new();
    private readonly Mock<IHubContext<QuoteHub>> _quoteHubMock = new();
    private readonly Mock<IHubClients> _strategyClientsMock = new();
    private readonly Mock<IClientProxy> _groupProxyMock = new();

    public SignalRHubConsumerTests()
    {
        _strategyHubMock.Setup(h => h.Clients).Returns(_strategyClientsMock.Object);
        _strategyClientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(_groupProxyMock.Object);
        _alertHubMock.Setup(h => h.Clients).Returns(new Mock<IHubClients>().Object);
    }

    [Fact]
    public async Task Consume_SignalGenerated_PushesToBothGroups()
    {
        // Arrange
        var consumer = new SignalRHubConsumer(
            _strategyHubMock.Object, _alertHubMock.Object,
            NullLogger<SignalRHubConsumer>.Instance);

        var instanceId = Guid.NewGuid();
        var @event = new SignalGenerated(instanceId, "PriceActionBreakout", "RELIANCE", "5m",
            "BUY", 2500m, 2450m, 2600m, "Breakout detected", "corr-1",
            Instant.FromUtc(2024, 1, 15, 9, 15, 0).InUtc());

        var contextMock = new Mock<ConsumeContext<SignalGenerated>>();
        contextMock.Setup(c => c.Message).Returns(@event);
        contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        // Act
        await consumer.Consume(contextMock.Object);

        // Assert — both groups receive the event
        _strategyClientsMock.Verify(c => c.Group($"strategy-{instanceId}"), Times.Once);
        _strategyClientsMock.Verify(c => c.Group("all-strategies"), Times.Once);
    }

    [Fact]
    public async Task Consume_ColdRestartPaused_PushesNotificationToAllStrategies()
    {
        // Arrange
        var consumer = new SignalRHubConsumer(
            _strategyHubMock.Object, _alertHubMock.Object,
            NullLogger<SignalRHubConsumer>.Instance);

        var instanceId = Guid.NewGuid();
        var @event = new ColdRestartPausedEvent(instanceId, "PriceActionBreakout",
            "auto_resume_on_restart=false — manual restart required",
            Instant.FromUtc(2024, 1, 14, 18, 30, 0).InZone(DateTimeZone.Utc),
            "corr-1", Instant.FromUtc(2024, 1, 15, 3, 30, 0).InUtc());

        var contextMock = new Mock<ConsumeContext<ColdRestartPausedEvent>>();
        contextMock.Setup(c => c.Message).Returns(@event);
        contextMock.Setup(c => c.CancellationToken).Returns(CancellationToken.None);

        // Act
        await consumer.Consume(contextMock.Object);

        // Assert
        _strategyClientsMock.Verify(c => c.Group("all-strategies"), Times.Once);
        _groupProxyMock.Verify(p => p.SendCoreAsync("ColdRestartPauseNotification",
            It.IsAny<object[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
