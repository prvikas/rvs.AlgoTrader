using NodaTime;
using rvs.AlgoTrader.Application.DTOs.MarketData;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Application.Services;

/// <summary>
/// Economic and corporate event calendar — distinct from <see cref="IMarketCalendarService"/>
/// which handles market hours and holidays.
/// <para>
/// This service manages scheduled events (RBI policy, earnings, budget day, F&amp;O expiry)
/// that strategies can query to avoid entering positions during high-impact windows.
/// </para>
/// </summary>
public interface IEventCalendarService
{
    // ── Read ──────────────────────────────────────────────────────────────────

    /// <summary>Get all events on a specific date.</summary>
    Task<IReadOnlyList<MarketEventDto>> GetByDateAsync(LocalDate date, CancellationToken ct = default);

    /// <summary>Get events in a date range, optionally filtered by type and/or symbol.</summary>
    Task<IReadOnlyList<MarketEventDto>> GetRangeAsync(
        LocalDate from, LocalDate to,
        string? eventType = null,
        string? symbol    = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns true if there is a High-impact event within <paramref name="windowDays"/>
    /// calendar days of <paramref name="date"/> for the given symbol (or any market-wide event).
    /// Used by strategies before generating entry signals.
    /// </summary>
    Task<bool> HasHighImpactEventAsync(
        LocalDate date,
        int windowDays = 1,
        string? symbol = null,
        CancellationToken ct = default);

    /// <summary>Get upcoming events in the next <paramref name="days"/> calendar days.</summary>
    Task<IReadOnlyList<MarketEventDto>> GetUpcomingAsync(int days = 7, CancellationToken ct = default);

    // ── Write ─────────────────────────────────────────────────────────────────

    /// <summary>Create a new market event. Returns the new event's ID.</summary>
    Task<Guid> CreateAsync(CreateMarketEventRequest request, CancellationToken ct = default);

    /// <summary>Update title, description, and/or impact of an existing event.</summary>
    Task UpdateAsync(Guid id, UpdateMarketEventRequest request, CancellationToken ct = default);

    /// <summary>Delete an event by ID.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Seed recurring NSE F&amp;O expiry dates for a given year into the event store.
    /// Idempotent — safe to call multiple times.
    /// </summary>
    Task SeedFnoExpiriesAsync(int year, CancellationToken ct = default);
}
