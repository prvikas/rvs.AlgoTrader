using System.Text.Json;
using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Brokers.Abstractions;

namespace rvs.AlgoTrader.Brokers.Upstox.Auth;

/// <summary>
/// Upstox OAuth2 auth. Token expires at 3:30 AM IST the following day (NOT 6h from issuance).
/// Full re-auth required on expiry — refresh_token is stored for this purpose.
/// </summary>
public class UpstoxAuth(HttpClient http, ILogger<UpstoxAuth> logger)
{
    private const string TokenUrl = "https://api.upstox.com/v2/login/authorization/token";

    public string GetLoginUrl(string apiKey, string redirectUri)
        => $"https://api.upstox.com/v2/login/authorization/dialog?response_type=code&client_id={apiKey}&redirect_uri={Uri.EscapeDataString(redirectUri)}";

    public async Task<LoginResult> ExchangeCodeAsync(BrokerCredentials creds, string redirectUri, CancellationToken ct)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = creds.RequestToken ?? "",
            ["client_id"] = creds.ApiKey,
            ["client_secret"] = creds.ApiSecret ?? "",
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code"
        });

        var response = await http.PostAsync(TokenUrl, form, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("[Upstox] Token exchange failed: {Body}", json);
            return new LoginResult(false, null, null, null, null, json, null);
        }

        var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");
        var accessToken = data.GetProperty("access_token").GetString()!;
        var refreshToken = data.TryGetProperty("extended_token", out var et) ? et.GetString() : null;

        // Token expires at 3:30 AM IST next day
        var istNow = TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"));
        var expiry = new DateTime(istNow.Year, istNow.Month, istNow.Day, 3, 30, 0).AddDays(1);
        var expiresAt = new DateTimeOffset(TimeZoneInfo.ConvertTime(expiry,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"), TimeZoneInfo.Utc));

        return new LoginResult(true, accessToken, refreshToken, expiresAt, null, null, null);
    }

    public async Task<LoginResult> RefreshTokenAsync(string refreshToken, string apiKey, string apiSecret, CancellationToken ct)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["extended_token"] = refreshToken,
            ["client_id"] = apiKey,
            ["client_secret"] = apiSecret,
            ["grant_type"] = "refresh_token"
        });

        var response = await http.PostAsync(TokenUrl, form, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            return new LoginResult(false, null, null, null, null, json, null);

        var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");
        var accessToken = data.GetProperty("access_token").GetString()!;

        var istNow = TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"));
        var expiry = new DateTime(istNow.Year, istNow.Month, istNow.Day, 3, 30, 0).AddDays(1);
        var expiresAt = new DateTimeOffset(TimeZoneInfo.ConvertTime(expiry,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"), TimeZoneInfo.Utc));

        return new LoginResult(true, accessToken, refreshToken, expiresAt, null, null, null);
    }
}
