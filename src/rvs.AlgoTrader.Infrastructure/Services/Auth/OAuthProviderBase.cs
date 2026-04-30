using System.Text.Json;

namespace rvs.AlgoTrader.Infrastructure.Services.Auth;

/// <summary>
/// Shared helpers for all OAuth provider implementations.
/// Eliminates code duplication across GoogleAuthProvider, MicrosoftAuthProvider, AppleAuthProvider.
/// Each concrete provider inherits this class for JWT decoding and query-string building only —
/// all OAuth protocol logic stays in the concrete class.
/// </summary>
public abstract class OAuthProviderBase
{
    // ── JWT payload decoding ──────────────────────────────────────────────────

    /// <summary>
    /// Decodes the payload segment of a JWT (no signature verification — relies on TLS
    /// transport to the provider's token endpoint).
    /// Validates minimum structure (3 segments) before decoding.
    /// Callers must dispose the returned JsonDocument.
    /// </summary>
    protected static JsonDocument DecodeJwtPayload(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 3)
            throw new InvalidOperationException(
                $"Malformed id_token: expected 3 JWT segments, got {parts.Length}.");

        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        var padded  = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        return JsonDocument.Parse(Convert.FromBase64String(padded));
    }

    /// <summary>
    /// Validates the 'iss' (issuer) claim against an expected value.
    /// Throws if missing or mismatched — prevents token confusion attacks.
    /// </summary>
    protected static void ValidateIssuer(JsonElement root, string expectedIss)
    {
        var iss = root.TryGetProperty("iss", out var issProp) ? issProp.GetString() : null;
        if (string.IsNullOrEmpty(iss))
            throw new InvalidOperationException("id_token is missing the 'iss' claim.");
        if (!iss.Equals(expectedIss, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"id_token issuer mismatch: expected '{expectedIss}', got '{iss}'.");
    }

    /// <summary>
    /// Validates the 'aud' (audience) claim against the application's client_id.
    /// The aud claim may be a string or an array of strings.
    /// </summary>
    protected static void ValidateAudience(JsonElement root, string expectedClientId)
    {
        if (!root.TryGetProperty("aud", out var audProp)) return; // some providers omit it

        var audValues = audProp.ValueKind == JsonValueKind.Array
            ? audProp.EnumerateArray().Select(a => a.GetString()).OfType<string>().ToList()
            : new List<string> { audProp.GetString() ?? "" };

        if (!audValues.Any(a => a.Equals(expectedClientId, StringComparison.Ordinal)))
            throw new InvalidOperationException(
                $"id_token audience mismatch: client_id '{expectedClientId}' not found in aud claim.");
    }

    // ── Query string builder ──────────────────────────────────────────────────

    protected static string BuildQuery(Dictionary<string, string> parameters)
        => string.Join('&', parameters.Select(
            kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
}
