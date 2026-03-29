namespace rvs.AlgoTrader.Application.DTOs.Auth;

/// <summary>Response returned on successful login — carries the JWT and broker context.</summary>
public record LoginResultDto(string Token, string BrokerName, DateTimeOffset ExpiresAt);
