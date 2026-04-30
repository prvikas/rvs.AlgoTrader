using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using rvs.AlgoTrader.Application.Options;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Infrastructure.Services.Auth;

/// <summary>
/// Microsoft identity platform (personal + Azure AD) via OAuth 2.0 Authorization Code flow.
/// TenantId defaults to "common" (accepts personal + any Azure AD tenant).
/// Uses the "oid" claim as the stable provider_sub — "sub" is per-application and can change.
/// </summary>
public sealed class MicrosoftAuthProvider : OAuthProviderBase, IExternalAuthProvider
{
    private readonly IHttpClientFactory     _httpFactory;
    private readonly IOptions<OAuthOptions> _opts;

    public MicrosoftAuthProvider(IHttpClientFactory httpFactory, IOptions<OAuthOptions> opts)
    {
        _httpFactory = httpFactory;
        _opts        = opts;
    }

    private MicrosoftOAuthOptions Cfg => _opts.Value.Microsoft
        ?? throw new InvalidOperationException("Auth:Microsoft configuration section is missing.");

    private string AuthBase => $"https://login.microsoftonline.com/{Cfg.TenantId}/oauth2/v2.0";

    public string ProviderName => "Microsoft";
    public string DisplayName  => "Microsoft";
    public string IconKey      => "microsoft";
    public bool   IsEnabled    => _opts.Value.Microsoft?.Enabled == true
                               && !string.IsNullOrEmpty(_opts.Value.Microsoft.ClientId);

    public string GetAuthorizationUrl(string state, string redirectUri)
    {
        var q = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"]     = Cfg.ClientId,
            ["redirect_uri"]  = redirectUri,
            ["scope"]         = "openid email profile",
            ["state"]         = state,
            ["prompt"]        = "select_account",
        };
        return $"{AuthBase}/authorize?{BuildQuery(q)}";
    }

    public async Task<ExternalUserInfo> ExchangeCodeAsync(
        string code, string redirectUri, string? rawIdToken, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient("OAuth");

        var resp = await http.PostAsync($"{AuthBase}/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"]    = "authorization_code",
                ["code"]          = code,
                ["redirect_uri"]  = redirectUri,
                ["client_id"]     = Cfg.ClientId,
                ["client_secret"] = Cfg.ClientSecret,
                ["scope"]         = "openid email profile",
            }), ct);

        resp.EnsureSuccessStatusCode();
        var json    = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var idToken = json.GetProperty("id_token").GetString()
            ?? throw new InvalidOperationException("Microsoft token response missing id_token.");

        return ParseIdToken(idToken, Cfg.ClientId);
    }

    private static ExternalUserInfo ParseIdToken(string idToken, string clientId)
    {
        using var doc = DecodeJwtPayload(idToken);
        var root = doc.RootElement;

        // Microsoft issues iss per-tenant: https://login.microsoftonline.com/{tenantId}/v2.0
        // For "common" / "consumers" tenants the iss contains the actual tenant UUID after login,
        // so we validate only the suffix pattern rather than an exact string.
        if (root.TryGetProperty("iss", out var issProp))
        {
            var iss = issProp.GetString() ?? "";
            if (!iss.StartsWith("https://login.microsoftonline.com/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Microsoft id_token issuer unexpected: '{iss}'.");
        }

        ValidateAudience(root, clientId);

        // Prefer "oid" (Object ID) as the stable identifier — "sub" is per-application.
        var sub = root.TryGetProperty("oid", out var oid)
            ? oid.GetString()
            : root.GetProperty("sub").GetString();

        return new ExternalUserInfo(
            Sub:         sub!,
            Email:       root.TryGetProperty("email", out var e)              ? e.GetString()
                       : root.TryGetProperty("preferred_username", out var u) ? u.GetString()
                       : null,
            DisplayName: root.TryGetProperty("name", out var n)               ? n.GetString() : null,
            AvatarUrl:   null);  // Microsoft Graph API required for photo — not fetched here
    }
}
