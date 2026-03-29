using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;
using rvs.AlgoTrader.Application.DTOs.MarketData;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// EF Core + raw SQL implementation of <see cref="IEventCalendarService"/>.
/// Events are stored in the <c>market_events</c> table (migration 011).
/// </summary>
public class EventCalendarService(AlgoTraderDbContext db, IClock clock) : IEventCalendarService
{
    public async Task<IReadOnlyList<MarketEventDto>> GetByDateAsync(LocalDate date, CancellationToken ct = default)
    {
        var iso = date.ToString("yyyy-MM-dd", null);
        var rows = await db.Database
            .SqlQueryRaw<EventRow>(EventSelectSql + $" WHERE event_date = '{iso}' ORDER BY event_time NULLS LAST")
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<MarketEventDto>> GetRangeAsync(
        LocalDate from, LocalDate to, string? eventType = null, string? symbol = null,
        CancellationToken ct = default)
    {
        var isoFrom = from.ToString("yyyy-MM-dd", null);
        var isoTo   = to.ToString("yyyy-MM-dd", null);
        var where   = $"WHERE event_date >= '{isoFrom}' AND event_date <= '{isoTo}'";
        if (eventType is not null) where += $" AND event_type = '{Esc(eventType)}'";
        if (symbol    is not null) where += $" AND (symbol = '{Esc(symbol)}' OR symbol IS NULL)";

        var rows = await db.Database
            .SqlQueryRaw<EventRow>(EventSelectSql + $" {where} ORDER BY event_date, event_time NULLS LAST")
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<bool> HasHighImpactEventAsync(
        LocalDate date, int windowDays = 1, string? symbol = null, CancellationToken ct = default)
    {
        var isoFrom = date.PlusDays(-windowDays).ToString("yyyy-MM-dd", null);
        var isoTo   = date.PlusDays(windowDays).ToString("yyyy-MM-dd", null);
        var symbolFilter = symbol is not null
            ? $" AND (symbol = '{Esc(symbol)}' OR symbol IS NULL)"
            : " AND symbol IS NULL";

        var rows = await db.Database
            .SqlQueryRaw<CountRow>(
                $"SELECT COUNT(*)::int AS value FROM market_events " +
                $"WHERE event_date >= '{isoFrom}' AND event_date <= '{isoTo}' " +
                $"AND impact = 'High'{symbolFilter}")
            .FirstOrDefaultAsync(ct);
        return (rows?.Value ?? 0) > 0;
    }

    public async Task<IReadOnlyList<MarketEventDto>> GetUpcomingAsync(int days = 7, CancellationToken ct = default)
    {
        var today = clock.NowInstant()
            .InZone(DateTimeZoneProviders.Tzdb["Asia/Kolkata"])
            .Date;
        return await GetRangeAsync(today, today.PlusDays(days), ct: ct);
    }

    public async Task<Guid> CreateAsync(CreateMarketEventRequest req, CancellationToken ct = default)
    {
        if (!Enum.TryParse<EventImpact>(req.Impact, true, out var impact))
            impact = EventImpact.Medium;

        LocalTime? eventTime = null;
        if (req.EventTime is not null)
        {
            var tResult = LocalTimePattern.ExtendedIso.Parse(req.EventTime);
            if (tResult.Success) eventTime = tResult.Value;
        }

        var evt = MarketEvent.Create(
            req.EventDate, req.EventType, req.Title, impact,
            eventTime, req.Description, req.Symbol, req.Source, req.IsRecurring,
            clock.NowInstant());

        var isoDate = req.EventDate.ToString("yyyy-MM-dd", null);
        var timeVal = eventTime.HasValue
            ? $"'{eventTime.Value.ToString("HH:mm:ss", null)}'::time"
            : "NULL";
        var descVal   = evt.Description is null ? "NULL" : $"'{Esc(evt.Description)}'";
        var symbolVal = evt.Symbol      is null ? "NULL" : $"'{Esc(evt.Symbol)}'";
        var sourceVal = evt.Source      is null ? "NULL" : $"'{Esc(evt.Source)}'";

        await db.Database.ExecuteSqlRawAsync(
            $"INSERT INTO market_events " +
            $"(id, event_date, event_time, event_type, title, description, impact, symbol, source, is_recurring, created_at) " +
            $"VALUES ('{evt.Id}', '{isoDate}'::date, {timeVal}, '{Esc(evt.EventType)}', '{Esc(evt.Title)}', " +
            $"{descVal}, '{evt.Impact}', {symbolVal}, {sourceVal}, {evt.IsRecurring.ToString().ToLowerInvariant()}, " +
            $"'{evt.CreatedAt.ToDateTimeOffset():O}')", ct);
        return evt.Id;
    }

    public async Task UpdateAsync(Guid id, UpdateMarketEventRequest req, CancellationToken ct = default)
    {
        var parts = new List<string>();
        if (req.Title       is not null) parts.Add($"title = '{Esc(req.Title)}'");
        if (req.Description is not null) parts.Add($"description = '{Esc(req.Description)}'");
        if (req.Impact      is not null) parts.Add($"impact = '{Esc(req.Impact)}'");

        if (parts.Count == 0) return;

#pragma warning disable EF1002 // dynamic SET clause — all inputs sanitised via Esc()
        await db.Database.ExecuteSqlRawAsync(
            $"UPDATE market_events SET {string.Join(", ", parts)} WHERE id = '{id}'", ct);
#pragma warning restore EF1002
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        // id is a Guid — no sanitisation needed; EF1002 suppressed for clarity
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync($"DELETE FROM market_events WHERE id = '{id}'", ct);
#pragma warning restore EF1002
    }

    public async Task SeedFnoExpiriesAsync(int year, CancellationToken ct = default)
    {
        // NSE weekly expiry = every Thursday; monthly expiry = last Thursday of month.
        // Monthly expiry is higher impact; weekly is medium.
        var thursday = new LocalDate(year, 1, 1);
        while (thursday.DayOfWeek != IsoDayOfWeek.Thursday) thursday = thursday.PlusDays(1);

        var events = new List<(LocalDate Date, EventImpact Impact, string Title)>();
        while (thursday.Year == year)
        {
            var isMonthlyExpiry = thursday.Month != thursday.PlusDays(7).Month;
            events.Add((thursday,
                isMonthlyExpiry ? EventImpact.High : EventImpact.Medium,
                isMonthlyExpiry ? $"NSE Monthly F&O Expiry {thursday:MMM yyyy}" : $"NSE Weekly F&O Expiry {thursday:dd MMM}"));
            thursday = thursday.PlusDays(7);
        }

        foreach (var (evtDate, impact, title) in events)
        {
            var iso = evtDate.ToString("yyyy-MM-dd", null);
            await db.Database.ExecuteSqlRawAsync(
                $"INSERT INTO market_events (id, event_date, event_type, title, impact, is_recurring, source) " +
                $"VALUES (gen_random_uuid(), '{iso}'::date, 'FNO_EXPIRY', '{Esc(title)}', '{impact}', TRUE, 'NSE') " +
                $"ON CONFLICT DO NOTHING", ct);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private const string EventSelectSql =
        "SELECT id, event_date, event_time, event_type, title, description, impact, " +
        "symbol, source, is_recurring, created_at FROM market_events";

    private static string Esc(string s) => s.Replace("'", "''");

    private static MarketEventDto ToDto(EventRow r) => new(
        r.Id,
        LocalDate.FromDateTime(r.EventDate.DateTime),
        r.EventTime?.ToString(),
        r.EventType,
        r.Title,
        r.Description,
        r.Impact,
        r.Symbol,
        r.Source,
        r.IsRecurring,
        r.CreatedAt);

    private sealed class EventRow
    {
        public Guid Id { get; set; }
        public DateTimeOffset EventDate { get; set; }
        public TimeSpan? EventTime { get; set; }
        public string EventType { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public string Impact { get; set; } = "Medium";
        public string? Symbol { get; set; }
        public string? Source { get; set; }
        public bool IsRecurring { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    private sealed class CountRow { public int Value { get; set; } }
}
