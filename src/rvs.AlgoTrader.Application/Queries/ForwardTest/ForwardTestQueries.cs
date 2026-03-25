using MediatR;
using rvs.AlgoTrader.Application.DTOs.ForwardTest;

namespace rvs.AlgoTrader.Application.Queries.ForwardTest;

public record GetForwardTestSessionsQuery() : IRequest<IReadOnlyList<ForwardTestSessionDetailDto>>;
public record GetForwardTestSessionByIdQuery(Guid Id) : IRequest<ForwardTestSessionDetailDto?>;
