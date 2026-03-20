namespace rvs.AlgoTrader.Application.DTOs.Strategy;

public record RiskProfileDto(
    Guid Id, string Name, decimal MaxCapitalPerTradePct, int MaxOpenTradesPerSymbol,
    decimal MaxDailyDrawdownPct, decimal MaxTotalCapitalDeployed, int MaxTradesPerDay,
    DateTimeOffset CreatedAt);

public record CreateRiskProfileDto(
    string Name, decimal MaxCapitalPerTradePct, int MaxOpenTradesPerSymbol,
    decimal MaxDailyDrawdownPct, decimal MaxTotalCapitalDeployed, int MaxTradesPerDay);

public record UpdateRiskProfileDto(
    string? Name, decimal? MaxCapitalPerTradePct, int? MaxOpenTradesPerSymbol,
    decimal? MaxDailyDrawdownPct, decimal? MaxTotalCapitalDeployed, int? MaxTradesPerDay);
