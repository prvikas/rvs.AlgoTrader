namespace rvs.AlgoTrader.Application.DTOs.Auth;

/// <summary>Response returned on successful login — carries the JWT and broker context.</summary>
public record LoginResultDto(string Token, string BrokerName, DateTimeOffset ExpiresAt);

/// <summary>Response after a successful user registration.</summary>
public record RegisterResultDto(Guid UserId, string Username, string Role);

/// <summary>
/// Describes a broker registered in the system, including whether the calling user
/// currently has an active session.
/// </summary>
public record BrokerInfoDto(
    string BrokerName,
    string Market,
    string AuthFlowType,     // "DirectCredentials" | "OAuth" | "ApiKey"
    bool   IsConnected);

/// <summary>One of the calling user's configured broker accounts.</summary>
public record UserBrokerAccountDto(
    Guid    Id,
    string  BrokerName,
    string  Market,
    string? DisplayName,
    bool    IsActive,
    bool    IsConnected);   // true when Redis session exists for this user+broker
