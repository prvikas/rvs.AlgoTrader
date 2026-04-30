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
///   • client_secret is a short-lived JWT signed with ES256 using your Apple private key.
///   • Apple sends email/name only on the FIRST sign-in; subsequent sign-ins omit them.
///   • The form_post body may include a "user" JSON field (name, email) on first sign-in only.
///   • Emails may be Apple Private Relay addresses (xxxxxx@privaterelay.appleid.com).
///
/// Setup: Register a "Services ID" in the Apple Developer portal, enable "Sign in with Apple",
/// create a private key, and add the backend callback URL as a return URL.
/// </summary>
public sealed class AppleAuthProvider(
    IHttpClientFactory httpFactory,
    IOptions<OAuthOptions> opts)
    : IExternalAuthProvider
{
    private AppleOAuthOptions Cfg => opts.Value.Apple!;

    public string ProviderName => "Apple";

    public string GetAuthorizationUrl(string state, string redirectUri)
    {
        var q = new Dictionary<string, string>
        {
            ["response_type"]  = "code",
            ["response_mode"]  = "form_post",   // Apple POSTs to redirectUri
            ["client_id"]      = Cfg.ClientId,
            ["redirect_uri"]   = redirectUri,
            ["scope"]          = "openid email name",
            ["state"]          = state,
        };
        return "https://appleid.apple.com/auth/authorize?" + BuildQuery(q);
    }

    public async Task<ExternalUserInfo> ExchangeCodeAsync(
        string code, string redirectUri, string? rawIdToken, CancellationToken ct)
    {
        // Apple sends the id_token directly in the form_post body — decode it first
        // before making the token exchange, since email is only present in the form_post
        // id_token on the very first sign-in.
        ExternalUserInfo? fromIdToken = rawIdToken != null ? ParseIdToken(rawIdToken) : null;

        var clientSecret = BuildAppleClientSecret();
        var http         = httpFactory.CreateClient("OAuth");

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
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var exchangedIdToken = json.GetProperty("id_token").GetString()
            ?? throw new InvalidOperationException("Apple token response missing id_token");

        var fromExchange = ParseIdToken(exchangedIdToken);

        // Prefer values from form_post id_token (first login has email); fall back to exchange
        return new ExternalUserInfo(
            Sub:         fromExchange.Sub,
            Email:       fromIdToken?.Email ?? fromExchange.Email,
            DisplayName: fromIdToken?.DisplayName ?? fromExchange.DisplayName,
            AvatarUrl:   null);   // Apple does not provide avatar URLs
    }

    private static ExternalUserInfo ParseIdToken(string idToken)
    {
        using var doc = DecodeJwtPayload(idToken);
        var root      = doc.RootElement;
        return new ExternalUserInfo(
            Sub:         root.GetProperty("sub").GetString()!,
            Email:       root.TryGetProperty("email", out var e) ? e.GetString() : null,
            DisplayName: null,   // Apple does not include name in id_token; it's in the "user" form field
            AvatarUrl:   null);
    }

    /// <summary>
    /// Builds the ES256-signed client secret JWT required by Apple.
    /// Apple issues these for up to 6 months; we generate one per request (cheap).
    /// </summary>
    private string BuildAppleClientSecret()
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(Cfg.PrivateKeyPem);

        var now     = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var exp     = now + 15_777_000;   // ~6 months
        var header  = B64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", kid = Cfg.KeyId }));
        var payload = B64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = Cfg.TeamId,
            iat = now,
            exp,
            aud = "https://appleid.apple.com",
            sub = Cfg.ClientId,
        }));
        var toSign    = Encoding.UTF8.GetBytes($"{header}.{payload}");
        var signature = ecdsa.SignData(toSign, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return $"{header}.{payload}.{B64Url(signature)}";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static JsonDocument DecodeJwtPayload(string jwt)
    {
        var parts   = jwt.Split('.');
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        var padded  = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        return JsonDocument.Parse(Convert.FromBase64String(padded));
    }

    private static string B64Url(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string BuildQuery(Dictionary<string, string> d)
        => string.Join('&', d.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
}
