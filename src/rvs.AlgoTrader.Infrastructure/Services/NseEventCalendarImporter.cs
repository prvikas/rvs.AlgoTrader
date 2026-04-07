using System.Globalization;
using Microsoft.Extensions.Logging;
using NodaTime;
using NodaTime.Text;
using rvs.AlgoTrader.Application.DTOs.MarketData;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Imports market events from NSE corporate actions CSV into the market_events table.
///
/// Two paths:
///   1. CSV upload (primary): POST /api/events/import accepts a CSV file with the columns below.
///   2. NSE API (deferred): NSE corporate actions API requires a cookie session and is blocked
///      without a browser. Use Playwright or an mStock proxy — documented in DATA_SOURCES.md.
///
/// Expected CSV columns (case-insensitive, order-independent):
///   SYMBOL, PURPOSE, EX_DATE (yyyy-MM-dd), RECORD_DATE (optional)
///
/// PURPOSE is mapped to EventType:
///   Dividend → DIVIDEND | Bonus → BONUS | Split → SPLIT
///   Results  → EARNINGS | Rights → RIGHTS | Other → CORPORATE_ACTION
/// </summary>
public sealed class NseEventCalendarImporter(
    IEventCalendarService events,
    ILogger<NseEventCalendarImporter> logger) : INseEventCalendarImporter
{
    private static readonly LocalDatePattern DatePattern = LocalDatePattern.CreateWithInvariantCulture("yyyy-MM-dd");
    private static readonly LocalDatePattern DatePatternSlash = LocalDatePattern.CreateWithInvariantCulture("dd/MM/yyyy");

    /// <summary>
    /// Parses the CSV text and upserts all rows as market events.
    /// Returns the number of events created.
    /// Duplicate entries (same symbol + date + type) are created as separate records —
    /// the market_events table has no uniqueness constraint on this tuple.
    /// </summary>
    public async Task<int> ImportCsvAsync(string csvText, CancellationToken ct)
    {
        var lines = csvText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
        {
            logger.LogWarning("[NseEventCalendarImporter] CSV has no data rows");
            return 0;
        }

        // Build header index
        var headers = lines[0].Split(',');
        var idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Length; i++)
            idx[headers[i].Trim().Trim('"')] = i;

        string Get(string[] cols, string key) =>
            idx.TryGetValue(key, out var i) && i < cols.Length ? cols[i].Trim().Trim('"') : "";

        int created = 0;
        for (int row = 1; row < lines.Length; row++)
        {
            var cols = lines[row].Split(',');
            var symbol  = Get(cols, "SYMBOL");
            var purpose = Get(cols, "PURPOSE");
            var exDate  = Get(cols, "EX_DATE");
            if (string.IsNullOrEmpty(symbol) || string.IsNullOrEmpty(exDate)) continue;

            if (!TryParseDate(exDate, out var date)) continue;

            var (eventType, title, impact) = MapPurpose(purpose, symbol);
            try
            {
                await events.CreateAsync(new CreateMarketEventRequest(
                    EventDate:   date,
                    EventType:   eventType,
                    Title:       title,
                    Symbol:      $"NSE:{symbol}",
                    Source:      "NSE_CSV",
                    Impact:      impact), ct);
                created++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[NseEventCalendarImporter] Failed to create event for {Symbol} on {Date}", symbol, date);
            }
        }

        logger.LogInformation("[NseEventCalendarImporter] Imported {Count} events from CSV", created);
        return created;
    }

    private static bool TryParseDate(string raw, out LocalDate date)
    {
        var r1 = DatePattern.Parse(raw);
        if (r1.Success) { date = r1.Value; return true; }
        var r2 = DatePatternSlash.Parse(raw);
        if (r2.Success) { date = r2.Value; return true; }
        date = default;
        return false;
    }

    private static (string eventType, string title, string impact) MapPurpose(string purpose, string symbol)
    {
        var p = purpose.ToLowerInvariant();
        return p switch
        {
            _ when p.Contains("dividend") => ("DIVIDEND", $"{symbol} Dividend", "Medium"),
            _ when p.Contains("bonus")    => ("BONUS",    $"{symbol} Bonus Issue", "Medium"),
            _ when p.Contains("split")    => ("SPLIT",    $"{symbol} Stock Split", "Medium"),
            _ when p.Contains("result")   => ("EARNINGS", $"{symbol} Q Results", "High"),
            _ when p.Contains("rights")   => ("RIGHTS",   $"{symbol} Rights Issue", "Medium"),
            _                              => ("CORPORATE_ACTION", $"{symbol}: {purpose}", "Low"),
        };
    }
}

// INseEventCalendarImporter is defined in rvs.AlgoTrader.Application.Services
