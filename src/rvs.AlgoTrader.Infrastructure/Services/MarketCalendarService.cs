using NodaTime;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// NSE market calendar: Mon–Fri, 9:15–15:30 IST.
/// Excludes BSE/NSE holidays via a static set maintained annually (no live exchange API required).
/// </summary>
public sealed class MarketCalendarService : IMarketCalendarService
{
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];
    private static readonly LocalTime MarketOpen = new(9, 15, 0);
    private static readonly LocalTime MarketClose = new(15, 30, 0);

    // Known NSE holidays 2024–2025 (static list; replace with live API in production)
    private static readonly HashSet<LocalDate> NseHolidays =
    [
        new LocalDate(2024, 1, 26),  // Republic Day
        new LocalDate(2024, 3, 25),  // Holi
        new LocalDate(2024, 4, 14),  // Dr. Ambedkar Jayanti
        new LocalDate(2024, 4, 17),  // Ram Navami
        new LocalDate(2024, 4, 21),  // Mahavir Jayanti
        new LocalDate(2024, 4, 29),  // Good Friday (shifted)
        new LocalDate(2024, 5, 23),  // Buddha Purnima
        new LocalDate(2024, 6, 17),  // Eid ul-Adha
        new LocalDate(2024, 7, 17),  // Muharram
        new LocalDate(2024, 8, 15),  // Independence Day
        new LocalDate(2024, 10, 2),  // Gandhi Jayanti
        new LocalDate(2024, 11, 1),  // Diwali Laxmi Puja
        new LocalDate(2024, 11, 15), // Gurunanak Jayanti
        new LocalDate(2024, 12, 25), // Christmas
        new LocalDate(2025, 1, 26),  // Republic Day
        new LocalDate(2025, 2, 26),  // Mahashivaratri
        new LocalDate(2025, 3, 14),  // Holi
        new LocalDate(2025, 3, 31),  // Id ul fitr
        new LocalDate(2025, 4, 10),  // Shri Mahavir Jayanti
        new LocalDate(2025, 4, 14),  // Dr Baba Saheb Ambedkar Jayanti
        new LocalDate(2025, 4, 18),  // Good Friday
        new LocalDate(2025, 5, 12),  // Buddha Purnima
        new LocalDate(2025, 6, 7),   // Id ul Adha (Bakri Id)
        new LocalDate(2025, 7, 29),  // Moharram
        new LocalDate(2025, 8, 15),  // Independence Day
        new LocalDate(2025, 10, 2),  // Mahatma Gandhi Jayanti
        new LocalDate(2025, 10, 21), // Diwali-Laxmi Puja
        new LocalDate(2025, 11, 5),  // Prakash Gurpurb Sri Guru Nanak Dev ji
        new LocalDate(2025, 12, 25), // Christmas

        // 2026 NSE holidays (official NSE circular)
        new LocalDate(2026, 1, 26),  // Republic Day
        new LocalDate(2026, 3, 20),  // Holi
        new LocalDate(2026, 4, 2),   // Shri Ram Navami
        new LocalDate(2026, 4, 3),   // Good Friday
        new LocalDate(2026, 4, 14),  // Dr. Ambedkar Jayanti
        new LocalDate(2026, 5, 1),   // Maharashtra Day
        new LocalDate(2026, 5, 27),  // Buddha Purnima
        new LocalDate(2026, 6, 27),  // Id ul Adha (Bakri Id)
        new LocalDate(2026, 8, 17),  // Independence Day (observed, 15th falls Sunday)
        new LocalDate(2026, 9, 8),   // Ganesh Chaturthi
        new LocalDate(2026, 10, 2),  // Mahatma Gandhi Jayanti
        new LocalDate(2026, 10, 20), // Diwali-Laxmi Puja
        new LocalDate(2026, 10, 21), // Diwali Balipratipada
        new LocalDate(2026, 11, 24), // Gurunanak Jayanti
        new LocalDate(2026, 12, 25), // Christmas
    ];

    /// <summary>Synchronous trading-day check used by IStrategyScheduler (cached holiday set).</summary>
    public bool IsTradingDay(LocalDate date)
    {
        if (date.DayOfWeek is IsoDayOfWeek.Saturday or IsoDayOfWeek.Sunday)
            return false;
        return !NseHolidays.Contains(date);
    }

    public Task<bool> IsTradingDayAsync(DateOnly date, CancellationToken ct)
    {
        var localDate = new LocalDate(date.Year, date.Month, date.Day);
        var dayOfWeek = localDate.DayOfWeek;

        // Weekend check
        if (dayOfWeek is IsoDayOfWeek.Saturday or IsoDayOfWeek.Sunday)
            return Task.FromResult(false);

        // Holiday check
        return Task.FromResult(!NseHolidays.Contains(localDate));
    }

    public bool IsWithinMarketHours(ZonedDateTime time)
    {
        var istTime = time.WithZone(Ist);
        var localTime = istTime.TimeOfDay;
        var dayOfWeek = istTime.DayOfWeek;

        if (dayOfWeek is IsoDayOfWeek.Saturday or IsoDayOfWeek.Sunday)
            return false;

        if (NseHolidays.Contains(istTime.Date))
            return false;

        return localTime >= MarketOpen && localTime <= MarketClose;
    }
}
