using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using rvs.AlgoTrader.Application.Options;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Infrastructure.Services.Auth;

/// <summary>
/// Microsoft identity platform (personal accounts + Azure AD) via OAuth 2.0 Authorization Code flow.
/// TenantId defaults to "common" (accepts personal Microsoft accounts and any Azure AD tenant).
/// Scopes: openid email profile — returns id_token containing oid + email.
/// Uses "oid" claim as the stable provider_sub (the sub claim changes per application registration;
/// oid is the immutable Object ID of the user in Azure AD).
/// </summary>
public sealed class MicrosoftAuthProvider(
    IHttpClientFactory httpFactory,
    IOptions<OAuthOptions> opts)
    : IExternalAuthProvider
{
    private MicrosoftOAuthOptions Cfg => opts.Value.Microsoft!;

    public string ProviderName => "Microsoft";

    private string AuthBase => $"https://login.microsoftonline.com/{Cfg.TenantId}/oauth2/v2.0";

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
        return AuthBase + "/authorize?" + BuildQuery(q);
    }

    public async Task<ExternalUserInfo> ExchangeCodeAsync(
        string code, string redirectUri, string? rawIdToken, CancellationToken ct)
    {
        var http = httpFactory.CreateClient("OAuth");

        var resp = await http.PostAsync(AuthBase + "/token",
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
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var idToken = json.GetProperty("id_token").GetString()
            ?? throw new InvalidOperationException("Microsoft token response missing id_token");

        return ParseIdToken(idToken);
    }

    private static ExternalUserInfo ParseIdToken(string idToken)
    {
        using var doc  = DecodeJwtPayload(idToken);
        var root       = doc.RootElement;

        // Prefer "oid" (Object ID) as the stable sub — "sub" is per-app and changes
        // if the user's tenant is migrated or the app registration changes.
        var sub = root.TryGetProperty("oid", out var oid) ? oid.GetString()
                : root.GetProperty("sub").GetString();

        return new ExternalUserInfo(
            Sub:         sub!,
            Email:       root.TryGetProperty("email", out var e)             ? e.GetString()
                       : root.TryGetProperty("preferred_username", out var u) ? u.GetString()
                       : null,
            DisplayName: root.TryGetProperty("name", out var n)              ? n.GetString() : null,
            AvatarUrl:   null);  // Microsoft Graph required for photo — not fetched here
    }

    private static JsonDocument DecodeJwtPayload(string jwt)
    {
        var parts   = jwt.Split('.');
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        var padded  = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        return JsonDocument.Parse(Convert.FromBase64String(padded));
    }

    private static string BuildQuery(Dictionary<string, string> d)
        => string.Join('&', d.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
}
