using MediatR;
using rvs.AlgoTrader.Application.DTOs.Broker;

namespace rvs.AlgoTrader.Application.Commands.Broker;

// ── mStock Type B ─────────────────────────────────────────────────────────────
/// <summary>
/// Initiates mStock session exchange.
/// TOTP must be the current 6-digit code from authenticator app — never stored.
/// </summary>
public record AuthenticateMStockCommand(
    string ApiKey,
    string ClientCode,
    string Password,
    string Totp
) : IRequest<BrokerAuthResultDto>;

// ── Zerodha ───────────────────────────────────────────────────────────────────
/// <summary>Returns the Kite Connect OAuth login URL for browser redirect.</summary>
public record GetZerodhaLoginUrlQuery() : IRequest<string>;

/// <summary>
/// Exchanges Zerodha request_token (from OAuth redirect) for access_token.
/// Computes SHA-256(api_key + request_token + api_secret) checksum internally.
/// </summary>
public record AuthenticateZerodhaCommand(
    string RequestToken
) : IRequest<BrokerAuthResultDto>;

// ── Upstox ────────────────────────────────────────────────────────────────────
/// <summary>Returns the Upstox OAuth2 authorization URL for browser redirect.</summary>
public record GetUpstoxLoginUrlQuery() : IRequest<string>;

/// <summary>
/// Exchanges Upstox auth_code (from OAuth2 redirect) for access_token + extended_token.
/// </summary>
public record AuthenticateUpstoxCommand(
    string AuthCode
) : IRequest<BrokerAuthResultDto>;

/// <summary>Triggers reconciliation of positions between local DB and the live broker.</summary>
public record ReconcileBrokerPositionsCommand(string BrokerName) : IRequest<bool>;
