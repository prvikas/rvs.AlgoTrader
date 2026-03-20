using MediatR;
using rvs.AlgoTrader.Application.DTOs.Broker;

namespace rvs.AlgoTrader.Application.Queries.Broker;

public record GetBrokerLatencyQuery(string? BrokerName = null) : IRequest<IReadOnlyList<BrokerLatencyDto>>;
public record GetBrokerConnectionStatusQuery() : IRequest<IReadOnlyList<BrokerConnectionStatusDto>>;
public record GetBrokerFundsQuery(string BrokerName) : IRequest<BrokerFundsDto?>;
public record GetBrokerPositionsQuery(string BrokerName) : IRequest<IReadOnlyList<BrokerPositionDto>>;
