using MediatR;
using rvs.AlgoTrader.Application.DTOs.Portfolio;

namespace rvs.AlgoTrader.Application.Queries.Portfolio;

public record GetPortfolioSummaryQuery() : IRequest<PortfolioSummaryDto>;

/// <summary>Returns all currently open positions, optionally filtered by broker or strategy run.</summary>
public record GetOpenPositionsQuery(string? BrokerName = null, Guid? StrategyRunId = null)
    : IRequest<IReadOnlyList<PositionDto>>;

/// <summary>Returns recent orders with optional filtering by strategy run, paginated.</summary>
public record GetRecentOrdersQuery(Guid? StrategyRunId = null, int Page = 1, int PageSize = 50)
    : IRequest<IReadOnlyList<OrderDto>>;
