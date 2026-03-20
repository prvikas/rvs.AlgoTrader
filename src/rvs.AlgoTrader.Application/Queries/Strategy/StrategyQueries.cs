using MediatR;
using rvs.AlgoTrader.Application.DTOs.Strategy;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Application.Queries.Strategy;

public record GetStrategyInstanceByIdQuery(Guid Id) : IRequest<StrategyInstanceDto?>;
public record GetAllStrategyInstancesQuery : IRequest<IReadOnlyList<StrategyInstanceDto>>;
public record GetStrategyInstancesQuery(int Page = 1, int PageSize = 20) : IRequest<PagedResult<StrategyInstanceDto>>;
public record GetSignalJournalQuery(Guid? StrategyId, string? Symbol, string? Signal, string? SkippedReason, int Page = 1, int PageSize = 50) : IRequest<PagedResult<SignalJournalEntryDto>>;
public record GetCapitalAllocationQuery(Guid StrategyInstanceId) : IRequest<CapitalAllocationDto?>;
