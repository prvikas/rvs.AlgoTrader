using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using rvs.AlgoTrader.Application.Options;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Infrastructure.Services.Auth;

/// <summary>
/// Sign in with Apple — OAuth 2.0 Authorization Code flow with form_post response mode.
///
/// Key differences from Google / Microsoft:
///   • response_mode=form_post → Apple POSTs to the callback URL (not a GET redirect).
///   • client_secret is a short-lived ES256-signed JWT built from your Apple private key.
///   • Apple sends email/name ONLY on the first sign-in; subsequent sign-ins omit them.
///   • Emails may be Apple Private Relay addresses (xxxxxx@privaterelay.appleid.com).
///
/// Setup: Register a Services ID in the Apple Developer portal, enable "Sign in with Apple",
/// create a private key, and register the backend callback URL as a return URL.
/// </summary>
public sealed class AppleAuthProvider : OAuthProviderBase, IExternalAuthProvider
{
    private const string AppleIssuer = "https://appleid.apple.com";

    private readonly IHttpClientFactory     _httpFactory;
    private readonly IOptions<OAuthOptions> _opts;

    public AppleAuthProvider(IHttpClientFactory httpFactory, IOptions<OAuthOptions> opts)
    {
        _httpFactory = httpFactory;
        _opts        = opts;
    }

    private AppleOAuthOptions Cfg => _opts.Value.Apple
        ?? throw new InvalidOperationException("Auth:Apple configuration section is missing.");

    public string ProviderName => "Apple";
    public string DisplayName  => "Apple";
    public string IconKey      => "apple";
    public bool   IsEnabled    => _opts.Value.Apple?.Enabled == true
                               && !string.IsNullOrEmpty(_opts.Value.Apple.ClientId);

    public string GetAuthorizationUrl(string state, string redirectUri)
    {
        var q = new Dictionary<string, string>
        {
            ["response_type"]  = "code",
            ["response_mode"]  = "form_post",    // Apple POSTs the callback — not a GET redirect
            ["client_id"]      = Cfg.ClientId,
            ["redirect_uri"]   = redirectUri,
            ["scope"]          = "openid email name",
            ["state"]          = state,
        };
        return $"https://appleid.apple.com/auth/authorize?{BuildQuery(q)}";
    }

    public async Task<ExternalUserInfo> ExchangeCodeAsync(
        string code, string redirectUri, string? rawIdToken, CancellationToken ct)
    {
        // Apple sends the id_token in the form_post body on first sign-in — it contains the email.
        // Decode it first so we can preserve the email even after the token exchange,
        // because the exchange response id_token omits email on repeat logins.
        ExternalUserInfo? fromFormPost = rawIdToken is not null
            ? ParseIdToken(rawIdToken, Cfg.ClientId, validateAudience: true)
            : null;

        var clientSecret = BuildAppleClientSecret();
        var http         = _httpFactory.CreateClient("OAuth");

        var resp = await http.PostAsync("https://appleid.apple.com/auth/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"]    = "authorization_code",
                ["code"]          = code,
                ["redirect_uri"]  = redirectUri,
                ["client_id"]     = Cfg.ClientId,
                ["client_secret"] = clientSecret,
            }), ct);

        resp.EnsureSuccessStatusCode();
        var json           = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var exchangedToken = json.GetProperty("id_token").GetString()
            ?? throw new InvalidOperationException("Apple token response missing id_token.");

        var fromExchange = ParseIdToken(exchangedToken, Cfg.ClientId, validateAudience: true);

        // Prefer form_post values (first login has email); fall back to exchange response.
        return new ExternalUserInfo(
            Sub:         fromExchange.Sub,
            Email:       fromFormPost?.Email    ?? fromExchange.Email,
            DisplayName: fromFormPost?.DisplayName ?? fromExchange.DisplayName,
            AvatarUrl:   null);   // Apple does not provide avatar URLs
    }

    private static ExternalUserInfo ParseIdToken(string idToken, string clientId, bool validateAudience)
    {
        using var doc = DecodeJwtPayload(idToken);
        var root = doc.RootElement;

        ValidateIssuer(root, AppleIssuer);
        if (validateAudience) ValidateAudience(root, clientId);

        return new ExternalUserInfo(
            Sub:         root.GetProperty("sub").GetString()!,
            Email:       root.TryGetProperty("email", out var e) ? e.GetString() : null,
            DisplayName: null,   // Apple does not include name in id_token; comes from form "user" field
            AvatarUrl:   null);
    }

    /// <summary>
    /// Builds the ES256-signed client secret JWT required by Apple.
    /// Apple's client_secret is a JWT signed with your EC private key (not a static string).
    /// We generate one per request — generation is cheap (microseconds).
    /// </summary>
    private string BuildAppleClientSecret()
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(Cfg.PrivateKeyPem);

        var now     = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var exp     = now + 15_777_000;   // 6 months (Apple maximum)
        var header  = B64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", kid = Cfg.KeyId }));
        var payload = B64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = Cfg.TeamId,
            iat = now,
            exp,
            aud = AppleIssuer,
            sub = Cfg.ClientId,
        }));
        var toSign    = Encoding.UTF8.GetBytes($"{header}.{payload}");
        var signature = ecdsa.SignData(toSign, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return $"{header}.{payload}.{B64Url(signature)}";
    }

    private static string B64Url(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
