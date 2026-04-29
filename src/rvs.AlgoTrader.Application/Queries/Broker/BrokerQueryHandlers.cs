using MediatR;
using rvs.AlgoTrader.Application.DTOs.Broker;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;
using IClock = rvs.AlgoTrader.Domain.Interfaces.IClock;

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

public class GetBrokerFundsHandler(
    IBrokerClientFactory brokerFactory,
    IAppBrokerSessionManager sessions,
    IClock clock)
    : IRequestHandler<GetBrokerFundsQuery, BrokerFundsDto?>
{
    public async Task<BrokerFundsDto?> Handle(GetBrokerFundsQuery request, CancellationToken ct)
    {
        // Guard: not authenticated → return null (controller returns 404)
        if (!await sessions.IsAuthenticatedAsync(request.BrokerName, ct)) return null;

        try
        {
            var client = brokerFactory.GetClient(request.BrokerName);
            var funds  = await client.GetFundsAsync(ct);
            return new BrokerFundsDto(
                BrokerName:      request.BrokerName,
                AvailableMargin: funds.AvailableBalance,
                UsedMargin:      funds.UsedMargin,
                TotalMargin:     funds.TotalBalance,
                Currency:        "INR",
                FetchedAt:       clock.NowInstant().ToDateTimeOffset());
        }
        catch { return null; }   // Not authenticated or broker API error → caller gets 404
    }
}

public class GetBrokerPositionsHandler(
    IBrokerClientFactory brokerFactory,
    IAppBrokerSessionManager sessions)
    : IRequestHandler<GetBrokerPositionsQuery, IReadOnlyList<BrokerPositionDto>>
{
    public async Task<IReadOnlyList<BrokerPositionDto>> Handle(GetBrokerPositionsQuery request, CancellationToken ct)
    {
        if (!await sessions.IsAuthenticatedAsync(request.BrokerName, ct))
            return [];

        try
        {
            var client    = brokerFactory.GetClient(request.BrokerName);
            var positions = await client.GetPositionsAsync(ct);
            return positions.Select(p => new BrokerPositionDto(
                TradingSymbol:   p.InternalSymbol,
                InstrumentType:  p.ProductType,
                Quantity:        p.Quantity,
                AverageBuyPrice: p.AveragePrice,
                LastTradedPrice: p.LastPrice,
                UnrealisedPnl:   p.PnL,
                RealisedPnl:     0m,    // Broker model carries only mark-to-market PnL
                Product:         p.ProductType)).ToList();
        }
        catch { return []; }
    }
}
