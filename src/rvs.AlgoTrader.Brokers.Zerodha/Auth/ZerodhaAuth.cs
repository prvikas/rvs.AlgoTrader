using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Brokers.Abstractions;

namespace rvs.AlgoTrader.Brokers.Zerodha.Auth;

public class ZerodhaAuth(HttpClient http, ILogger<ZerodhaAuth> logger)
{
    private const string BaseUrl = "https://api.kite.trade";

    /// <summary>SHA-256(api_key + request_token + api_secret) — matches dotnetkiteconnect Utils.SHA256Hash</summary>
    public static string ComputeChecksum(string apiKey, string requestToken, string apiSecret)
    {
        var raw = $"{apiKey}{requestToken}{apiSecret}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public async Task<LoginResult> GenerateSessionAsync(BrokerCredentials creds, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(creds.RequestToken))
        {
            var loginUrl = $"https://kite.zerodha.com/connect/login?v=3&api_key={creds.ApiKey}";
            logger.LogInformation("[Zerodha] No request_token — manual login required: {Url}", loginUrl);
            return new LoginResult(false, null, null, null, loginUrl, "Manual login required", null);
        }

        var checksum = ComputeChecksum(creds.ApiKey, creds.RequestToken!, creds.ApiSecret!);
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["api_key"] = creds.ApiKey,
            ["request_token"] = creds.RequestToken!,
            ["checksum"] = checksum
        });

        var response = await http.PostAsync($"{BaseUrl}/session/token", form, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("[Zerodha] Session generation failed: {Status} {Body}", response.StatusCode, json);
            return new LoginResult(false, null, null, null, null, $"HTTP {response.StatusCode}: {json}", null);
        }

        var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");
        var accessToken = data.GetProperty("access_token").GetString()!;

        // Zerodha tokens expire at midnight IST
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var expiresAt = new DateTimeOffset(tomorrow.ToDateTime(new TimeOnly(18, 30)), TimeSpan.Zero); // midnight IST = 18:30 UTC

        return new LoginResult(true, accessToken, null, expiresAt, null, null, null);
    }

    public async Task InvalidateSessionAsync(string apiKey, string accessToken, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/session/token?api_key={apiKey}&access_token={accessToken}");
        await http.SendAsync(request, ct);
    }
}
