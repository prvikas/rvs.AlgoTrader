namespace rvs.AlgoTrader.Application.Options;

/// <summary>
/// Top-level OAuth config.  Set Auth:FrontendBaseUrl to your React dev server or production domain.
/// Each provider section is optional — omit or set Enabled=false to hide the button on the login page.
/// Values should come from environment variables or Vault, not appsettings.json.
/// </summary>
public sealed class OAuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Base URL of the React frontend — used when redirecting after OAuth callback.</summary>
    public string FrontendBaseUrl { get; init; } = "http://localhost:3000";

    public GoogleOAuthOptions?    Google    { get; init; }
    public MicrosoftOAuthOptions? Microsoft { get; init; }
    public AppleOAuthOptions?     Apple     { get; init; }
}

public sealed class GoogleOAuthOptions
{
    public bool   Enabled      { get; init; }
    public string ClientId     { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
}

public sealed class MicrosoftOAuthOptions
{
    public bool   Enabled      { get; init; }
    public string ClientId     { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    /// <summary>
    /// Azure AD tenant ID, or "common" for multi-tenant (personal + work accounts).
    /// Use "consumers" to restrict to personal Microsoft accounts only.
    /// </summary>
    public string TenantId     { get; init; } = "common";
}

public sealed class AppleOAuthOptions
{
    public bool   Enabled       { get; init; }
    /// <summary>Services ID registered in Apple Developer portal (e.g. "com.example.app.siwa").</summary>
    public string ClientId      { get; init; } = string.Empty;
    /// <summary>10-character Apple Team ID from developer.apple.com → Membership.</summary>
    public string TeamId        { get; init; } = string.Empty;
    /// <summary>10-character Key ID of the Sign in with Apple private key.</summary>
    public string KeyId         { get; init; } = string.Empty;
    /// <summary>
    /// PEM-encoded PKCS#8 private key downloaded from Apple Developer portal.
    /// Include the full -----BEGIN PRIVATE KEY----- / -----END PRIVATE KEY----- block.
    /// Store in Vault or environment variable — never commit to source.
    /// </summary>
    public string PrivateKeyPem { get; init; } = string.Empty;
}
