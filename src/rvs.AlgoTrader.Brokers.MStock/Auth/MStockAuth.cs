using System.Text.Json;
using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Brokers.Abstractions;

namespace rvs.AlgoTrader.Brokers.MStock.Auth;

/// <summary>
/// mStock Type B API — two-step authentication flow.
///
/// Step 1: POST /connect/login
///   Body (JSON): { clientcode, password, totp, state }
///   Headers: X-Mirae-Version: 1
///   Returns: refreshToken
///
/// Step 2: POST /session/verifytotp
///   Body (JSON): { refreshToken, totp }
///   Headers: X-Mirae-Version: 1, X-PrivateKey: api_key
///   Returns: jwtToken, refreshToken, feedToken
///
/// BOTH steps must complete within the same 30-second TOTP window.
/// Token expires at midnight IST (18:30 UTC) of the generated day.
/// No OAuth2 — re-run full two-step flow on 401/403.
/// </summary>
public class MStockAuth(HttpClient http, ILogger<MStockAuth> logger)
{
    private const string BaseUrl = "https://api.mstock.trade/openapi/typeb";

    public async Task<LoginResult> ExchangeSessionAsync(BrokerCredentials creds, CancellationToken ct)
    {
        // Validate required fields
        if (string.IsNullOrWhiteSpace(creds.ClientCode))
            return Fail("ClientCode is required for mStock login (e.g. 'AB1234')");
        if (string.IsNullOrWhiteSpace(creds.Password))
            return Fail("Password is required for mStock login");
        if (string.IsNullOrWhiteSpace(creds.Totp) || creds.Totp.Length != 6)
            return Fail("TOTP must be exactly 6 digits — use current code from your authenticator app");

        // ── Step 1: POST /connect/login ───────────────────────────────────────
        logger.LogInformation("[mStock] Step 1: POST /connect/login for ClientCode={ClientCode}", creds.ClientCode);

        var step1Body = JsonSerializer.Serialize(new
        {
            clientcode = creds.ClientCode,
            password   = creds.Password,
            totp       = creds.Totp,
            state      = creds.ApiKey   // use api_key as state identifier
        });

        var step1Request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/connect/login")
        {
            Content = new StringContent(step1Body, System.Text.Encoding.UTF8, "application/json")
        };
        step1Request.Headers.Add("X-Mirae-Version", "1");

        HttpResponseMessage step1Response;
        try { step1Response = await http.SendAsync(step1Request, ct); }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "[mStock] Step 1 network error");
            return Fail($"Network error (Step 1): {ex.Message}");
        }

        var step1Json = await step1Response.Content.ReadAsStringAsync(ct);
        if (!step1Response.IsSuccessStatusCode)
        {
            logger.LogError("[mStock] Step 1 failed: {Status} {Body}", step1Response.StatusCode, step1Json);
            return Fail($"Login failed (HTTP {(int)step1Response.StatusCode}): {step1Json}");
        }

        string refreshToken;
        try
        {
            var doc1 = JsonDocument.Parse(step1Json);
            var root1 = doc1.RootElement;

            // MStock returns {"data": null, "message": "..."} on auth failure —
            // TryGetProperty on a Null JsonElement throws InvalidOperationException,
            // so guard with ValueKind == Object before drilling in.
            if (!root1.TryGetProperty("data", out var data1) ||
                data1.ValueKind != JsonValueKind.Object ||
                !data1.TryGetProperty("refreshToken", out var rt))
            {
                var msg = root1.TryGetProperty("message", out var m) ? m.GetString() : step1Json;
                if (string.IsNullOrEmpty(msg))
                    msg = root1.TryGetProperty("errorcode", out var ec) ? ec.GetString() : step1Json;
                return Fail($"Step 1: refreshToken missing — {msg}");
            }
            refreshToken = rt.GetString()!;
        }
        catch (JsonException ex)
        {
            return Fail($"Step 1: invalid JSON response — {ex.Message}");
        }

        logger.LogInformation("[mStock] Step 1 success — refreshToken obtained. Proceeding to Step 2.");

        // ── Step 2: POST /session/verifytotp ──────────────────────────────────
        var step2Body = JsonSerializer.Serialize(new
        {
            refreshToken,                // IDE0037: member name can be simplified
            totp = creds.Totp           // same TOTP — must still be in same 30-second window
        });

        var step2Request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/session/verifytotp")
        {
            Content = new StringContent(step2Body, System.Text.Encoding.UTF8, "application/json")
        };
        step2Request.Headers.Add("X-Mirae-Version", "1");
        step2Request.Headers.Add("X-PrivateKey", creds.ApiKey);

        HttpResponseMessage step2Response;
        try { step2Response = await http.SendAsync(step2Request, ct); }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "[mStock] Step 2 network error");
            return Fail($"Network error (Step 2): {ex.Message}");
        }

        var step2Json = await step2Response.Content.ReadAsStringAsync(ct);
        if (!step2Response.IsSuccessStatusCode)
        {
            logger.LogError("[mStock] Step 2 failed: {Status} {Body}", step2Response.StatusCode, step2Json);
            return Fail($"TOTP verification failed (HTTP {(int)step2Response.StatusCode}): {step2Json}");
        }

        try
        {
            var doc2  = JsonDocument.Parse(step2Json);
            var root2 = doc2.RootElement;

            if (!root2.TryGetProperty("data", out var data2) ||
                data2.ValueKind != JsonValueKind.Object ||
                !data2.TryGetProperty("jwtToken", out var jwtEl))
            {
                var msg = root2.TryGetProperty("message", out var m) ? m.GetString() : step2Json;
                if (string.IsNullOrEmpty(msg))
                    msg = root2.TryGetProperty("errorcode", out var ec) ? ec.GetString() : step2Json;
                return Fail($"Step 2: jwtToken missing — {msg}");
            }

            var jwtToken   = jwtEl.GetString()!;
            var newRefresh = data2.TryGetProperty("refreshToken", out var nr) ? nr.GetString() : null;
            var feedToken  = data2.TryGetProperty("feedToken",    out var ft) ? ft.GetString() : null;
            var clientName = data2.TryGetProperty("ClientName",   out var cn) ? cn.GetString() : null;

            // Token expires at midnight IST = 18:30 UTC of the generated day
            var istNow = TimeZoneInfo.ConvertTime(DateTime.UtcNow,
                TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"));
            var midnightIst = new DateTime(istNow.Year, istNow.Month, istNow.Day, 0, 0, 0).AddDays(1);
            var expiresAt = new DateTimeOffset(TimeZoneInfo.ConvertTime(midnightIst,
                TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"), TimeZoneInfo.Utc));

            logger.LogInformation(
                "[mStock] Authentication successful. ClientName={ClientName}, ExpiresAt={ExpiresAt} UTC",
                clientName, expiresAt);

            return new LoginResult(true, jwtToken, newRefresh, expiresAt, null, null, feedToken);
        }
        catch (JsonException ex)
        {
            return Fail($"Step 2: invalid JSON response — {ex.Message}");
        }
    }

    private static LoginResult Fail(string message) =>
        new(false, null, null, null, null, message, null);
}
