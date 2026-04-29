using MediatR;
using rvs.AlgoTrader.Application.DTOs.Auth;
using rvs.AlgoTrader.Application.DTOs.Broker;

namespace rvs.AlgoTrader.Application.Commands.Broker;

/// <summary>
/// Generic broker connect — works for any broker regardless of AuthFlowType.
/// Credentials dict keys: see IBrokerAuthService.AuthenticateAsync documentation.
/// </summary>
public record ConnectBrokerCommand(
    string BrokerName,
    IReadOnlyDictionary<string, string> Credentials
) : IRequest<BrokerAuthResultDto>;

/// <summary>Returns the OAuth login URL for a broker (null for non-OAuth brokers).</summary>
public record GetBrokerLoginUrlQuery(string BrokerName) : IRequest<string?>;

/// <summary>Disconnects the current user from the named broker.</summary>
public record DisconnectBrokerCommand(string BrokerName) : IRequest<bool>;

/// <summary>Triggers reconciliation of positions between local DB and the live broker.</summary>
public record ReconcileBrokerPositionsCommand(string BrokerName) : IRequest<bool>;

// ── User management commands ──────────────────────────────────────────────────

public record RegisterUserCommand(
    string Username,
    string Password,
    string Role = "Analyst"
) : IRequest<RegisterResultDto>;

public record AddBrokerAccountCommand(
    string BrokerName,
    string Market,
    string? DisplayName
) : IRequest<UserBrokerAccountDto>;

public record RemoveBrokerAccountCommand(Guid AccountId) : IRequest<bool>;
