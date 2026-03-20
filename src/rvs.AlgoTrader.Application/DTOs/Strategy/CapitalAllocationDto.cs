namespace rvs.AlgoTrader.Application.DTOs.Strategy;

public record CapitalAllocationDto(
    Guid Id, Guid StrategyInstanceId, string BrokerName,
    decimal AllocatedCapital, decimal UsedCapital, decimal AvailableCapital,
    DateTimeOffset UpdatedAt);

public record UpdateCapitalAllocationDto(decimal AllocatedCapital, string BrokerName);
public record CreateCapitalAllocationDto(Guid StrategyInstanceId, decimal AllocatedCapital, string BrokerName);
