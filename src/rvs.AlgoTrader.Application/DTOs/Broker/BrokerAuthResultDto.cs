namespace rvs.AlgoTrader.Application.DTOs.Broker;

public record BrokerAuthResultDto(
    bool Success,
    string BrokerName,
    string? Message,
    DateTimeOffset? ExpiresAt
);
