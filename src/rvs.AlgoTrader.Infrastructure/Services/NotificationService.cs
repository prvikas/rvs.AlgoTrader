using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Application.Services;
using System.Net.Http.Json;
using System.Net.Mail;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Multi-channel notification service: Telegram + Email.
/// Channel values: "TELEGRAM", "EMAIL", "ALL".
/// </summary>
public sealed class NotificationService(IHttpClientFactory http, IConfiguration config, ILogger<NotificationService> logger) : INotificationService
{

    public async Task SendAsync(string channel, string severity, string message, CancellationToken ct)
    {
        var channels = channel.ToUpperInvariant();

        if (channels is "TELEGRAM" or "ALL")
        {
            await SendTelegramAsync(message, severity, ct);
        }

        if (channels is "EMAIL" or "ALL")
        {
            await SendEmailAsync(message, severity, ct);
        }
    }

    private async Task SendTelegramAsync(string message, string severity, CancellationToken ct)
    {
        try
        {
            var botToken = config["Notification:Telegram:BotToken"];
            var chatId = config["Notification:Telegram:ChatId"];

            if (string.IsNullOrEmpty(botToken) || string.IsNullOrEmpty(chatId))
            {
                logger.LogWarning("Telegram not configured — skipping notification");
                return;
            }

            var emoji = severity.ToUpper() switch
            {
                "CRITICAL" => "🚨",
                "ERROR" => "❌",
                "WARNING" => "⚠️",
                _ => "ℹ️"
            };

            var text = $"{emoji} *[{severity}]* {EscapeMarkdown(message)}";
            var client = http.CreateClient();
            var url = $"https://api.telegram.org/bot{botToken}/sendMessage";

            var payload = new { chat_id = chatId, text, parse_mode = "MarkdownV2" };
            var response = await client.PostAsJsonAsync(url, payload, ct);

            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Telegram send failed: {StatusCode}", response.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Telegram notification failed");
        }
    }

    private async Task SendEmailAsync(string message, string severity, CancellationToken ct)
    {
        try
        {
            var host = config["Notification:Email:SmtpHost"];
            var port = int.Parse(config["Notification:Email:SmtpPort"] ?? "587");
            var username = config["Notification:Email:Username"];
            var password = config["Notification:Email:Password"];
            var from = config["Notification:Email:FromAddress"] ?? "algotrader@rvs.in";

            if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(username))
            {
                logger.LogWarning("Email not configured — skipping notification");
                return;
            }

            using var smtp = new SmtpClient(host, port)
            {
                EnableSsl = true,
                Credentials = new System.Net.NetworkCredential(username, password)
            };

            var mail = new MailMessage(from, username)
            {
                Subject = $"[{severity}] rvs.AlgoTrader Alert",
                Body = message
            };

            await smtp.SendMailAsync(mail, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Email notification failed");
        }
    }

    private static string EscapeMarkdown(string text) =>
        text.Replace(".", "\\.").Replace("-", "\\-").Replace("(", "\\(").Replace(")", "\\)");
}
