using MediatR;
using rvs.AlgoTrader.Application.DTOs.Broker;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;

namespace rvs.AlgoTrader.Application.Queries.Broker;

public class GetBrokerLatencyHandler(IBrokerLatencyRepository repo) : IRequestHandler<GetBrokerLatencyQuery, IReadOnlyList<BrokerLatencyDto>>
{
    public async Task<IReadOnlyList<BrokerLatencyDto>> Handle(GetBrokerLatencyQuery request, CancellationToken ct)
    {
        var reports = await repo.GetLatestAsync(request.BrokerName, ct);
        return [.. reports.Select(r => new BrokerLatencyDto(r.BrokerName, r.P50Ms, r.P95Ms, r.P99Ms, r.SampleCount, r.MeasuredAt.ToDateTimeOffset()))];
    }
}

public class GetBrokerConnectionStatusHandler(IAppBrokerSessionManager sessions) : IRequestHandler<GetBrokerConnectionStatusQuery, IReadOnlyList<BrokerConnectionStatusDto>>
{
    private static readonly string[] Brokers = ["Zerodha", "Upstox", "MStock"];

    public async Task<IReadOnlyList<BrokerConnectionStatusDto>> Handle(GetBrokerConnectionStatusQuery request, CancellationToken ct)
    {
        var results = new List<BrokerConnectionStatusDto>();
        foreach (var broker in Brokers)
        {
            var authenticated = await sessions.IsAuthenticatedAsync(broker, ct);
            results.Add(new BrokerConnectionStatusDto(
                BrokerName: broker,
                IsConnected: authenticated,
                IsAuthenticated: authenticated,
                LastHeartbeatAt: null,
                ReconnectAttempts: 0,
                LastDisconnectReason: null,
                SessionExpiresAt: null));
        }
        return results;
    }
}

public class GetBrokerFundsHandler : IRequestHandler<GetBrokerFundsQuery, BrokerFundsDto?>
{
    public GetBrokerFundsHandler() { }

    public Task<BrokerFundsDto?> Handle(GetBrokerFundsQuery request, CancellationToken ct)
    {
        // Broker fund fetching requires live broker integration — returns null until implemented
        return Task.FromResult<BrokerFundsDto?>(null);
    }
}

public class GetBrokerPositionsHandler : IRequestHandler<GetBrokerPositionsQuery, IReadOnlyList<BrokerPositionDto>>
{
    public GetBrokerPositionsHandler() { }

    public Task<IReadOnlyList<BrokerPositionDto>> Handle(GetBrokerPositionsQuery request, CancellationToken ct)
    {
        // Broker position fetching requires live broker integration — returns empty until implemented
        return Task.FromResult<IReadOnlyList<BrokerPositionDto>>([]);
    }
}
