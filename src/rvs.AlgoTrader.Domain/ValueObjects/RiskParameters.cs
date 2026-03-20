namespace rvs.AlgoTrader.Domain.ValueObjects;

public record RiskParameters(
    decimal MaxCapitalPerTradePct,
    int MaxOpenTradesPerSymbol,
    decimal MaxDailyDrawdownPct,
    decimal MaxTotalCapitalDeployed,
    int MaxTradesPerDay
);
