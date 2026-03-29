namespace rvs.AlgoTrader.Application.DTOs.Broker;

/// <summary>Request body for mStock API-key login.</summary>
public record MStockLoginRequest(string ApiKey, string ClientCode, string Password, string Totp);

/// <summary>Request body for Zerodha OAuth callback — carries the request token from the redirect.</summary>
public record ZerodhaCallbackRequest(string RequestToken);

/// <summary>Request body for Upstox OAuth callback — carries the auth code from the redirect.</summary>
public record UpstoxCallbackRequest(string AuthCode);
