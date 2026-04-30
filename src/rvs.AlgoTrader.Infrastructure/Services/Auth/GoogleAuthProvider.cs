using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using rvs.AlgoTrader.Application.Options;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Infrastructure.Services.Auth;

/// <summary>
/// Google Sign-In via OAuth 2.0 Authorization Code flow.
/// Scopes: openid email profile — returns id_token (JWT) in the token response.
/// </summary>
public sealed class GoogleAuthProvider : OAuthProviderBase, IExternalAuthProvider
{
    private const string IssuerUrl = "https://accounts.google.com";

    private readonly IHttpClientFactory   _httpFactory;
    private readonly IOptions<OAuthOptions> _opts;

    public GoogleAuthProvider(IHttpClientFactory httpFactory, IOptions<OAuthOptions> opts)
    {
        _httpFactory = httpFactory;
        _opts        = opts;
    }

    private GoogleOAuthOptions Cfg => _opts.Value.Google
        ?? throw new InvalidOperationException("Auth:Google configuration section is missing.");

    public string ProviderName => "Google";
    public string DisplayName  => "Google";
    public string IconKey      => "google";
    public bool   IsEnabled    => _opts.Value.Google?.Enabled == true
                               && !string.IsNullOrEmpty(_opts.Value.Google.ClientId);

    public string GetAuthorizationUrl(string state, string redirectUri)
    {
        var q = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"]     = Cfg.ClientId,
            ["redirect_uri"]  = redirectUri,
            ["scope"]         = "openid email profile",
            ["state"]         = state,
            ["access_type"]   = "offline",        // request refresh_token
            ["prompt"]        = "select_account",
        };
        return $"https://accounts.google.com/o/oauth2/v2/auth?{BuildQuery(q)}";
    }

    public async Task<ExternalUserInfo> ExchangeCodeAsync(
        string code, string redirectUri, string? rawIdToken, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient("OAuth");

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
        var json    = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var idToken = json.GetProperty("id_token").GetString()
            ?? throw new InvalidOperationException("Google token response missing id_token.");

        return ParseIdToken(idToken, Cfg.ClientId);
    }

    private static ExternalUserInfo ParseIdToken(string idToken, string clientId)
    {
        using var doc = DecodeJwtPayload(idToken);
        var root = doc.RootElement;

        ValidateIssuer(root, IssuerUrl);
        ValidateAudience(root, clientId);

        return new ExternalUserInfo(
            Sub:         root.GetProperty("sub").GetString()!,
            Email:       root.TryGetProperty("email",   out var e) ? e.GetString() : null,
            DisplayName: root.TryGetProperty("name",    out var n) ? n.GetString() : null,
            AvatarUrl:   root.TryGetProperty("picture", out var p) ? p.GetString() : null);
    }
}
