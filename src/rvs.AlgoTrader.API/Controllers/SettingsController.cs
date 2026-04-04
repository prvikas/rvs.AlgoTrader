using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rvs.AlgoTrader.API.Authorization;
using rvs.AlgoTrader.Application.DTOs.Common;
using rvs.AlgoTrader.Application.DTOs.Settings;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.API.Controllers;

/// <summary>
/// Notification and monitoring settings.
/// All settings are DB-driven via IAppConfigService (no appsettings.json — CLAUDE.md Rule #20).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = PolicyNames.Admin)]
public class SettingsController(IAppConfigService configService) : ControllerBase
{
    // ─── Config keys ───────────────────────────────────────────────────────────────
    private const string TelegramBotTokenKey = "Notification:Telegram:BotToken";
    private const string TelegramChatIdKey = "Notification:Telegram:ChatId";
    private const string MaxDailyDrawdownPctKey = "Monitoring:MaxDailyDrawdownPct";
    private const string AlertsEnabledKey = "Monitoring:AlertsEnabled";

    /// <summary>Get current notification and monitoring settings.</summary>
    [HttpGet("notifications")]
    public async Task<ActionResult<ApiResponse<NotificationSettingsDto>>> GetNotificationSettings(
        CancellationToken ct)
    {
        var botToken = await configService.GetAsync<string>(TelegramBotTokenKey, ct);
        var chatId = await configService.GetAsync<string>(TelegramChatIdKey, ct);
        var maxDrawdownPct = await configService.GetAsync<decimal?>(MaxDailyDrawdownPctKey, ct);
        var alertsEnabled = await configService.GetAsync<bool>(AlertsEnabledKey, ct);

        var dto = new NotificationSettingsDto(
            // Mask the bot token — return only last 4 chars for display
            TelegramBotTokenMasked: MaskToken(botToken),
            TelegramChatId: chatId,
            MaxDailyDrawdownPct: maxDrawdownPct ?? 3.0m,
            AlertsEnabled: alertsEnabled);

        return Ok(ApiResponse<NotificationSettingsDto>.Ok(dto));
    }

    /// <summary>
    /// Update notification and monitoring settings.
    /// Passing null for a field leaves it unchanged.
    /// </summary>
    [HttpPut("notifications")]
    public async Task<ActionResult<ApiResponse<bool>>> UpdateNotificationSettings(
        [FromBody] UpdateNotificationSettingsDto request,
        CancellationToken ct)
    {
        var actor = User.Identity?.Name ?? "API";
        var correlationId = HttpContext.TraceIdentifier;

        if (request.TelegramBotToken is not null)
            await configService.SetAsync(TelegramBotTokenKey, request.TelegramBotToken, actor, correlationId, ct);

        if (request.TelegramChatId is not null)
            await configService.SetAsync(TelegramChatIdKey, request.TelegramChatId, actor, correlationId, ct);

        if (request.MaxDailyDrawdownPct.HasValue)
        {
            if (request.MaxDailyDrawdownPct.Value is < 0.1m or > 50m)
                return BadRequest(ApiResponse<bool>.Fail("MaxDailyDrawdownPct must be between 0.1 and 50."));
            await configService.SetAsync(MaxDailyDrawdownPctKey, request.MaxDailyDrawdownPct.Value, actor, correlationId, ct);
        }

        if (request.AlertsEnabled.HasValue)
            await configService.SetAsync(AlertsEnabledKey, request.AlertsEnabled.Value, actor, correlationId, ct);

        return Ok(ApiResponse<bool>.Ok(true));
    }

    private static string? MaskToken(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        if (token.Length <= 4) return "****";
        return $"****{token[^4..]}";
    }
}

// NotificationSettingsDto and UpdateNotificationSettingsDto moved to Application/DTOs/Settings/NotificationSettingsDtos.cs
