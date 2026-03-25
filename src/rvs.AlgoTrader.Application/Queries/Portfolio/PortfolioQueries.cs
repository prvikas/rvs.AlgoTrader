using MediatR;
using rvs.AlgoTrader.Application.DTOs.Portfolio;

namespace rvs.AlgoTrader.Application.Queries.Portfolio;

public record GetPortfolioSummaryQuery() : IRequest<PortfolioSummaryDto>;
