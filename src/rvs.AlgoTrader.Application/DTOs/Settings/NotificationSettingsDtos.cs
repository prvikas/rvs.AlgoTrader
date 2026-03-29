namespace rvs.AlgoTrader.Application.DTOs.Settings;

/// <summary>Read-only view of notification settings. Bot token is masked for security.</summary>
public record NotificationSettingsDto(
    string? TelegramBotTokenMasked,
    string? TelegramChatId,
    decimal MaxDailyDrawdownPct,
    bool? AlertsEnabled);

/// <summary>Mutable notification settings. Null values leave the existing setting unchanged.</summary>
public record UpdateNotificationSettingsDto(
    string? TelegramBotToken,
    string? TelegramChatId,
    decimal? MaxDailyDrawdownPct,
    bool? AlertsEnabled);
