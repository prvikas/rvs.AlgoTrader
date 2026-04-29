namespace rvs.AlgoTrader.Domain.Enums;

/// <summary>
/// Describes the authentication flow a broker requires.
/// Used by the generic auth endpoint to determine the credentials shape.
/// </summary>
public enum BrokerAuthFlowType
{
    /// <summary>
    /// Direct credentials: API key + password + optional TOTP.
    /// Example: MStock Type B, Dhan.
    /// Client receives: { apiKey, clientCode, password, totp? }
    /// </summary>
    DirectCredentials,

    /// <summary>
    /// OAuth / OAuth2 redirect: call GET /broker/{name}/login-url, redirect user,
    /// receive callback token, exchange via POST /broker/{name}/connect.
    /// Example: Zerodha Kite Connect, Upstox, RobinHood.
    /// Client receives: { code } or { requestToken }
    /// </summary>
    OAuth,

    /// <summary>
    /// Simple API key only — no interactive login.
    /// Example: Alpaca, some US brokers.
    /// Client receives: { apiKey, apiSecret }
    /// </summary>
    ApiKey,
}
