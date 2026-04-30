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
/// All queries use {N} positional parameters so EF Core / Npgsql generates
/// proper $1/$2 placeholders — no string concatenation of user input.
/// </summary>
public class EventCalendarService(AlgoTraderDbContext db, IClock clock) : IEventCalendarService
{
    public async Task<IReadOnlyList<MarketEventDto>> GetByDateAsync(LocalDate date, CancellationToken ct = default)
    {
        // date comes from LocalDate (strongly typed) — safe to format directly
        var iso = date.ToString("yyyy-MM-dd", null);
        var rows = await db.Database
            .SqlQueryRaw<EventRow>(
                EventSelectSql + " WHERE event_date = {0}::date ORDER BY event_time NULLS LAST",
                iso)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<MarketEventDto>> GetRangeAsync(
        LocalDate from, LocalDate to, string? eventType = null, string? symbol = null,
        CancellationToken ct = default)
    {
        var isoFrom = from.ToString("yyyy-MM-dd", null);
        var isoTo   = to.ToString("yyyy-MM-dd", null);

        // Optional filters use the IS NULL OR col = param pattern so a single
        // parameterized query covers all four filter combinations.
        var rows = await db.Database
            .SqlQueryRaw<EventRow>(
                EventSelectSql +
                " WHERE event_date >= {0}::date AND event_date <= {1}::date" +
                " AND ({2}::text IS NULL OR event_type = {2})" +
                " AND ({3}::text IS NULL OR symbol = {3} OR symbol IS NULL)" +
                " ORDER BY event_date, event_time NULLS LAST",
                isoFrom, isoTo, eventType ?? (object)DBNull.Value, symbol ?? (object)DBNull.Value)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<bool> HasHighImpactEventAsync(
        LocalDate date, int windowDays = 1, string? symbol = null, CancellationToken ct = default)
    {
        var isoFrom = date.PlusDays(-windowDays).ToString("yyyy-MM-dd", null);
        var isoTo   = date.PlusDays(windowDays).ToString("yyyy-MM-dd", null);

        var row = await db.Database
            .SqlQueryRaw<CountRow>(
                "SELECT COUNT(*)::int AS value FROM market_events" +
                " WHERE event_date >= {0}::date AND event_date <= {1}::date" +
                " AND impact = 'High'" +
                " AND ({2}::text IS NULL OR symbol = {2} OR symbol IS NULL)",
                isoFrom, isoTo, symbol ?? (object)DBNull.Value)
            .FirstOrDefaultAsync(ct);
        return (row?.Value ?? 0) > 0;
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

        // All string values are passed as typed parameters — Npgsql handles type casting.
        var isoDate   = req.EventDate.ToString("yyyy-MM-dd", null);
        var timeStr   = eventTime.HasValue ? eventTime.Value.ToString("HH:mm:ss", null) : null;
        var impactStr = evt.Impact.ToString();
        var createdAt = evt.CreatedAt.ToDateTimeOffset();

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO market_events
              (id, event_date, event_time, event_type, title, description, impact, symbol, source, is_recurring, created_at)
            VALUES
              ({evt.Id}, {isoDate}::date, {timeStr}::time, {evt.EventType}, {evt.Title},
               {evt.Description}, {impactStr}, {evt.Symbol}, {evt.Source}, {evt.IsRecurring}, {createdAt})
            """, ct);
        return evt.Id;
    }

    public async Task UpdateAsync(Guid id, UpdateMarketEventRequest req, CancellationToken ct = default)
    {
        // Each field is only updated when provided (partial update).
        // Using separate ExecuteSqlInterpolated calls per field is simpler and fully safe —
        // the field names come from code (not user input), and values are parameterized.
        if (req.Title       is not null)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE market_events SET title = {req.Title} WHERE id = {id}", ct);
        if (req.Description is not null)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE market_events SET description = {req.Description} WHERE id = {id}", ct);
        if (req.Impact      is not null)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE market_events SET impact = {req.Impact} WHERE id = {id}", ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM market_events WHERE id = {id}", ct);
    }

    public async Task SeedFnoExpiriesAsync(int year, CancellationToken ct = default)
    {
        // NSE weekly expiry = every Thursday; monthly expiry = last Thursday of month.
        var thursday = new LocalDate(year, 1, 1);
        while (thursday.DayOfWeek != IsoDayOfWeek.Thursday) thursday = thursday.PlusDays(1);

        while (thursday.Year == year)
        {
            var isMonthlyExpiry = thursday.Month != thursday.PlusDays(7).Month;
            var isoDate = thursday.ToString("yyyy-MM-dd", null);
            var title   = isMonthlyExpiry
                ? $"NSE Monthly F&O Expiry {thursday:MMM yyyy}"
                : $"NSE Weekly F&O Expiry {thursday:dd MMM}";
            var impactStr = (isMonthlyExpiry ? EventImpact.High : EventImpact.Medium).ToString();

            // title is generated by code (no user input) — safe to pass as parameter
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO market_events (id, event_date, event_type, title, impact, is_recurring, source)
                VALUES (gen_random_uuid(), {isoDate}::date, 'FNO_EXPIRY', {title}, {impactStr}, TRUE, 'NSE')
                ON CONFLICT DO NOTHING
                """, ct);

            thursday = thursday.PlusDays(7);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private const string EventSelectSql =
        "SELECT id, " +
        "event_date    AS \"EventDate\", " +
        "event_time    AS \"EventTime\", " +
        "event_type    AS \"EventType\", " +
        "title, description, impact, symbol, source, " +
        "is_recurring  AS \"IsRecurring\", " +
        "created_at    AS \"CreatedAt\" " +
        "FROM market_events";

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
