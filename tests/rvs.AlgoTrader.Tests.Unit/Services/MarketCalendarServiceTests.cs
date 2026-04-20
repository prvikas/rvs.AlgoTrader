using FluentAssertions;
using NodaTime;
using rvs.AlgoTrader.Infrastructure.Services;
using Xunit;

namespace rvs.AlgoTrader.Tests.Unit.Services;

/// <summary>
/// Unit tests for MarketCalendarService concrete implementation.
/// Verifies the static NSE holiday list for 2024–2026 and the
/// weekend / market-hours logic.
/// </summary>
public class MarketCalendarServiceTests
{
    private readonly MarketCalendarService _sut = new();

    // ── IsTradingDay ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(2026, 1, 26)]   // Republic Day
    [InlineData(2026, 3, 20)]   // Holi
    [InlineData(2026, 4, 2)]    // Shri Ram Navami
    [InlineData(2026, 4, 3)]    // Good Friday
    [InlineData(2026, 4, 14)]   // Dr. Ambedkar Jayanti
    [InlineData(2026, 5, 1)]    // Maharashtra Day
    [InlineData(2026, 5, 27)]   // Buddha Purnima
    [InlineData(2026, 6, 27)]   // Id ul Adha (Bakri Id)
    [InlineData(2026, 8, 17)]   // Independence Day (observed)
    [InlineData(2026, 9, 8)]    // Ganesh Chaturthi
    [InlineData(2026, 10, 2)]   // Mahatma Gandhi Jayanti
    [InlineData(2026, 10, 20)]  // Diwali-Laxmi Puja
    [InlineData(2026, 10, 21)]  // Diwali Balipratipada
    [InlineData(2026, 11, 24)]  // Gurunanak Jayanti
    [InlineData(2026, 12, 25)]  // Christmas
    public void IsTradingDay_ReturnsFalse_For2026Holidays(int year, int month, int day)
    {
        var date = new LocalDate(year, month, day);
        _sut.IsTradingDay(date).Should().BeFalse(
            because: $"{year}-{month:D2}-{day:D2} is a 2026 NSE holiday");
    }

    [Theory]
    [InlineData(2025, 1, 26)]   // Republic Day
    [InlineData(2025, 2, 26)]   // Mahashivaratri
    [InlineData(2025, 3, 14)]   // Holi
    [InlineData(2025, 4, 18)]   // Good Friday
    [InlineData(2025, 8, 15)]   // Independence Day
    [InlineData(2025, 10, 2)]   // Gandhi Jayanti
    [InlineData(2025, 12, 25)]  // Christmas
    public void IsTradingDay_ReturnsFalse_For2025Holidays(int year, int month, int day)
    {
        var date = new LocalDate(year, month, day);
        _sut.IsTradingDay(date).Should().BeFalse(
            because: $"{year}-{month:D2}-{day:D2} is a 2025 NSE holiday");
    }

    [Theory]
    [InlineData(2026, 1, 5)]    // Monday — normal trading day
    [InlineData(2026, 4, 6)]    // Monday after Good Friday weekend
    [InlineData(2026, 8, 18)]   // Tuesday after Ind. Day (17th)
    [InlineData(2026, 12, 28)]  // Monday after Christmas
    public void IsTradingDay_ReturnsTrue_ForRegularWeekdaysIn2026(int year, int month, int day)
    {
        var date = new LocalDate(year, month, day);
        _sut.IsTradingDay(date).Should().BeTrue(
            because: $"{year}-{month:D2}-{day:D2} is not a weekend or known holiday");
    }

    [Theory]
    [InlineData(2026, 1, 3)]    // Saturday
    [InlineData(2026, 1, 4)]    // Sunday
    [InlineData(2026, 3, 21)]   // Saturday
    [InlineData(2026, 12, 27)]  // Sunday
    public void IsTradingDay_ReturnsFalse_ForWeekends(int year, int month, int day)
    {
        var date = new LocalDate(year, month, day);
        _sut.IsTradingDay(date).Should().BeFalse(
            because: $"{year}-{month:D2}-{day:D2} is a weekend");
    }

    // ── IsTradingDayAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task IsTradingDayAsync_ReturnsFalse_ForRepublicDay2026()
    {
        var result = await _sut.IsTradingDayAsync(new DateOnly(2026, 1, 26), CancellationToken.None);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsTradingDayAsync_ReturnsTrue_ForNormalWeekday()
    {
        var result = await _sut.IsTradingDayAsync(new DateOnly(2026, 2, 2), CancellationToken.None); // Monday
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsTradingDayAsync_ReturnsFalse_ForSaturday()
    {
        var result = await _sut.IsTradingDayAsync(new DateOnly(2026, 2, 7), CancellationToken.None); // Saturday
        result.Should().BeFalse();
    }

    // ── IsWithinMarketHours ──────────────────────────────────────────────────

    [Fact]
    public void IsWithinMarketHours_ReturnsTrue_ForMidSessionOnTradingDay()
    {
        // 2026-02-02 (Monday) at 12:00 IST — middle of session
        var ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];
        var time = new LocalDateTime(2026, 2, 2, 12, 0, 0).InZoneStrictly(ist);
        _sut.IsWithinMarketHours(time).Should().BeTrue();
    }

    [Fact]
    public void IsWithinMarketHours_ReturnsTrue_AtMarketOpen()
    {
        var ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];
        var time = new LocalDateTime(2026, 2, 2, 9, 15, 0).InZoneStrictly(ist);
        _sut.IsWithinMarketHours(time).Should().BeTrue();
    }

    [Fact]
    public void IsWithinMarketHours_ReturnsFalse_BeforeMarketOpen()
    {
        var ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];
        var time = new LocalDateTime(2026, 2, 2, 9, 14, 59).InZoneStrictly(ist);
        _sut.IsWithinMarketHours(time).Should().BeFalse();
    }

    [Fact]
    public void IsWithinMarketHours_ReturnsFalse_AfterMarketClose()
    {
        var ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];
        var time = new LocalDateTime(2026, 2, 2, 15, 30, 1).InZoneStrictly(ist);
        _sut.IsWithinMarketHours(time).Should().BeFalse();
    }

    [Fact]
    public void IsWithinMarketHours_ReturnsFalse_OnSaturday()
    {
        var ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];
        var time = new LocalDateTime(2026, 2, 7, 11, 0, 0).InZoneStrictly(ist);
        _sut.IsWithinMarketHours(time).Should().BeFalse();
    }

    [Fact]
    public void IsWithinMarketHours_ReturnsFalse_OnHoliday()
    {
        // 2026-01-26 Republic Day — should not be within market hours even at 12:00 IST
        var ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];
        var time = new LocalDateTime(2026, 1, 26, 12, 0, 0).InZoneStrictly(ist);
        _sut.IsWithinMarketHours(time).Should().BeFalse();
    }
}
