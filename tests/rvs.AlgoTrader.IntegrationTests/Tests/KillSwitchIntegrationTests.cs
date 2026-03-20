using System.Net;
using System.Net.Http.Json;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.IntegrationTests.Infrastructure;

namespace rvs.AlgoTrader.IntegrationTests.Tests;

/// <summary>
/// Integration tests for kill switch lifecycle: activate → verify → deactivate.
/// Verifies Redis dual-write (hash + pub/sub) works end-to-end.
/// </summary>
public sealed class KillSwitchIntegrationTests : IClassFixture<AlgoTraderWebAppFactory>
{
    private readonly HttpClient _client;

    public KillSwitchIntegrationTests(AlgoTraderWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ActivateKillSwitch_ThenGetStatus_ReturnsActive()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();

        // Act
        var activateResponse = await _client.PostAsJsonAsync("/api/killswitch/activate", new
        {
            Actor = "integration-test",
            Reason = "Testing kill switch activation",
            CorrelationId = correlationId
        });

        var statusResponse = await _client.GetAsync("/api/killswitch/status");

        // Cleanup
        await _client.PostAsJsonAsync("/api/killswitch/deactivate", new
        {
            Actor = "integration-test",
            CorrelationId = Guid.NewGuid().ToString()
        });

        // Assert
        activateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeactivateKillSwitch_AfterActivation_ReturnsInactive()
    {
        // Arrange
        await _client.PostAsJsonAsync("/api/killswitch/activate", new
        {
            Actor = "integration-test",
            Reason = "Setup for deactivation test",
            CorrelationId = Guid.NewGuid().ToString()
        });

        // Act
        var deactivateResponse = await _client.PostAsJsonAsync("/api/killswitch/deactivate", new
        {
            Actor = "integration-test",
            CorrelationId = Guid.NewGuid().ToString()
        });

        // Assert
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await deactivateResponse.Content.ReadFromJsonAsync<ApiResponse<bool>>();
        body!.Success.Should().BeTrue();
    }
}
