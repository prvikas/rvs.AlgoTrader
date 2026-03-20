namespace rvs.AlgoTrader.Application.DTOs.Orders;

public record OrderDto(
    Guid Id, string BrokerName, string? BrokerOrderId, string InternalSymbol,
    string OrderType, string Direction, int Quantity, decimal? Price, decimal? TriggerPrice,
    string Status, Guid? StrategyRunId, DateTimeOffset? PlacedAt, DateTimeOffset? FilledAt,
    decimal? FillPrice, decimal? TrailingSl, decimal? TrailingTp, DateTimeOffset CreatedAt);

public record CreateOrderDto(
    string BrokerName, string InternalSymbol, string OrderType, string Direction,
    int Quantity, decimal? Price, decimal? TriggerPrice, Guid? StrategyRunId);

public record ModifyOrderDto(int? Quantity, decimal? Price, decimal? TriggerPrice);
