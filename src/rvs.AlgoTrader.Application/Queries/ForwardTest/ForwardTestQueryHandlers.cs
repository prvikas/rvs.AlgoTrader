using MediatR;
using NodaTime;
using NodaTime.Text;
using rvs.AlgoTrader.Application.DTOs.ForwardTest;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Application.Queries.ForwardTest;

public class GetForwardTestSessionsHandler(
    IForwardTestSessionRepository sessions,
    IForwardTestTradeRepository trades,
    IStrategyInstanceRepository strategies,
    IBacktestRunRepository backtests)
    : IRequestHandler<GetForwardTestSessionsQuery, IReadOnlyList<ForwardTestSessionDetailDto>>
{
    public async Task<IReadOnlyList<ForwardTestSessionDetailDto>> Handle(
        GetForwardTestSessionsQuery request, CancellationToken ct)
    {
        var allInstances = await strategies.GetAllAsync(ct);
        var fwdInstances = allInstances
            .Where(i => string.Equals(i.Mode.ToString(), "Forward", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var result = new List<ForwardTestSessionDetailDto>();
        foreach (var instance in fwdInstances)
        {
            var instanceSessions = await sessions.GetByInstanceAsync(instance.Id, ct);
            foreach (var session in instanceSessions)
            {
                result.Add(await ForwardTestSessionMapper.BuildAsync(session, instance, trades, backtests, ct));
            }
        }
        return result.OrderByDescending(s => s.StartedAt).ToList();
    }
}

public class GetForwardTestSessionByIdHandler(
    IForwardTestSessionRepository sessions,
    IForwardTestTradeRepository trades,
    IStrategyInstanceRepository strategies,
    IBacktestRunRepository backtests)
    : IRequestHandler<GetForwardTestSessionByIdQuery, ForwardTestSessionDetailDto?>
{
    public async Task<ForwardTestSessionDetailDto?> Handle(
        GetForwardTestSessionByIdQuery request, CancellationToken ct)
    {
        var session = await sessions.GetByIdAsync(request.Id, ct);
        if (session is null) return null;
        var instance = await strategies.GetByIdAsync(session.StrategyInstanceId, ct);
        if (instance is null) return null;
        return await ForwardTestSessionMapper.BuildAsync(session, instance, trades, backtests, ct);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Shared mapper — builds enriched DTO from raw session + strategy + trades
// ─────────────────────────────────────────────────────────────────────────────

internal static class ForwardTestSessionMapper
{
    internal static async Task<ForwardTestSessionDetailDto> BuildAsync(
        Domain.Entities.ForwardTestSession session,
        Domain.Entities.StrategyInstance instance,
        IForwardTestTradeRepository tradesRepo,
        IBacktestRunRepository backtestsRepo,
        CancellationToken ct)
    {
        var tradeList = await tradesRepo.GetBySessionAsync(session.Id, ct);
        var openCount = tradeList.Count(t => !t.ExitTime.HasValue);

        // Build equity curve from closed trades in chronological order.
        // Use RealizedPnl (authoritative since migration 024) with Pnl as fallback for
        // rows inserted before migration 024 backfilled the column.
        var curve = tradeList
            .Where(t => t.ExitTime.HasValue)
            .OrderBy(t => t.ExitTime)
            .Aggregate(
                (equity: session.InitialCapital, list: new List<ForwardEquityPoint>()),
                (acc, t) =>
                {
                    var tradePnl = t.RealizedPnl ?? t.Pnl;
                    acc.equity += tradePnl;
                    acc.list.Add(new ForwardEquityPoint(
                        // AP-001: no DateTime — use NodaTime InstantPattern for ISO 8601 formatting
                        InstantPattern.ExtendedIso.Format(t.ExitTime!.Value),
                        acc.equity,
                        tradePnl));
                    return acc;
                }).list;

        decimal totalReturn = session.InitialCapital > 0
            ? session.FinalPnl / session.InitialCapital
            : 0m;

        BacktestSnapshotDto? sourceBt = null;
        if (session.SourceBacktestId.HasValue)
        {
            var bt = await backtestsRepo.GetByIdAsync(session.SourceBacktestId.Value, ct);
            if (bt is not null)
                sourceBt = new BacktestSnapshotDto(
                    bt.Id ?? session.SourceBacktestId.Value.ToString(),
                    bt.TotalPnl, bt.TotalReturn, bt.WinRate,
                    bt.MaxDrawdown, bt.SharpeRatio,
                    bt.ExpectancyPerTrade, bt.TotalTrades);
        }

        return new ForwardTestSessionDetailDto(
            InstanceId: instance.Id.ToString(),
            InstanceName: instance.Name,
            StrategyType: instance.StrategyType,
            InternalSymbol: instance.InternalSymbol,
            Timeframe: instance.Timeframe,
            BrokerName: instance.BrokerName,
            Status: session.Status,
            StartedAt: session.StartedAt.ToDateTimeOffset(),
            EndedAt: session.EndedAt?.ToDateTimeOffset(),
            InitialCapital: session.InitialCapital,
            CurrentEquity: session.InitialCapital + session.FinalPnl,
            TotalPnl: session.FinalPnl,
            TotalReturn: totalReturn,
            MaxDrawdown: session.MaxDrawdown,
            SharpeRatio: session.SharpeRatio,
            WinRate: session.WinRate,
            TotalTrades: session.TradeCount,
            OpenPositionCount: openCount,
            SourceBacktestId: session.SourceBacktestId?.ToString(),
            SourceBacktest: sourceBt,
            EquityCurvePoints: curve);
    }
}
