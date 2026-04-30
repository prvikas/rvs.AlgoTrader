using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using rvs.AlgoTrader.Application.Options;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Infrastructure.Services.Auth;

/// <summary>
/// Google Sign-In via OAuth 2.0 Authorization Code flow.
/// Scopes: openid email profile — returns id_token (JWT) in token response.
/// </summary>
public sealed class GoogleAuthProvider(
    IHttpClientFactory httpFactory,
    IOptions<OAuthOptions> opts)
    : IExternalAuthProvider
{
    private GoogleOAuthOptions Cfg => opts.Value.Google!;

    public string ProviderName => "Google";

    public string GetAuthorizationUrl(string state, string redirectUri)
    {
        var q = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"]     = Cfg.ClientId,
            ["redirect_uri"]  = redirectUri,
            ["scope"]         = "openid email profile",
            ["state"]         = state,
            ["access_type"]   = "offline",   // request refresh_token
            ["prompt"]        = "select_account",
        };
        return "https://accounts.google.com/o/oauth2/v2/auth?" + BuildQuery(q);
    }

    public async Task<ExternalUserInfo> ExchangeCodeAsync(
        string code, string redirectUri, string? rawIdToken, CancellationToken ct)
    {
        var http = httpFactory.CreateClient("OAuth");

        // Exchange code for tokens
        var resp = await http.PostAsync("https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"]    = "authorization_code",
                ["code"]          = code,
                ["redirect_uri"]  = redirectUri,
                ["client_id"]     = Cfg.ClientId,
                ["client_secret"] = Cfg.ClientSecret,
            }), ct);

        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var idToken = json.GetProperty("id_token").GetString()
            ?? throw new InvalidOperationException("Google token response missing id_token");

        return ParseIdToken(idToken);
    }

    private static ExternalUserInfo ParseIdToken(string idToken)
    {
        using var doc = DecodeJwtPayload(idToken);
        var root = doc.RootElement;
        return new ExternalUserInfo(
            Sub:         root.GetProperty("sub").GetString()!,
            Email:       root.TryGetProperty("email", out var e)       ? e.GetString() : null,
            DisplayName: root.TryGetProperty("name", out var n)        ? n.GetString() : null,
            AvatarUrl:   root.TryGetProperty("picture", out var p)     ? p.GetString() : null);
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
