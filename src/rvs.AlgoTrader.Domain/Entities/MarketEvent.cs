using NodaTime;

namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// A scheduled market event that may affect trading decisions.
/// Examples: RBI policy meeting, earnings releases, budget day, F&amp;O expiry.
/// Used by strategies to avoid entering positions during high-impact event windows.
/// </summary>
public class MarketEvent
{
    public Guid Id { get; private set; }

    /// <summary>Calendar date of the event (IST).</summary>
    public LocalDate EventDate { get; private set; }

    /// <summary>Time of the event (IST). Null for all-day or when time is unknown.</summary>
    public LocalTime? EventTime { get; private set; }

    /// <summary>Taxonomy type — e.g. RBI_POLICY, EARNINGS, BUDGET, FNO_EXPIRY, IPO_ALLOTMENT.</summary>
    public string EventType { get; private set; } = string.Empty;

    /// <summary>Short title shown in the calendar UI.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Optional longer description.</summary>
    public string? Description { get; private set; }

    /// <summary>Expected market impact: Low, Medium, High.</summary>
    public EventImpact Impact { get; private set; }

    /// <summary>Specific symbol affected. Null = market-wide event.</summary>
    public string? Symbol { get; private set; }

    /// <summary>Data source (e.g. "NSE", "RBI", "manual").</summary>
    public string? Source { get; private set; }

    /// <summary>True if this event recurs on a predictable schedule (e.g. monthly expiry).</summary>
    public bool IsRecurring { get; private set; }

    public Instant CreatedAt { get; private set; }

    private MarketEvent() { }

    public static MarketEvent Create(
        LocalDate eventDate,
        string eventType,
        string title,
        EventImpact impact,
        LocalTime? eventTime = null,
        string? description = null,
        string? symbol = null,
        string? source = null,
        bool isRecurring = false,
        Instant? createdAt = null)
    {
        if (string.IsNullOrWhiteSpace(eventType)) throw new ArgumentException("EventType is required.", nameof(eventType));
        if (string.IsNullOrWhiteSpace(title))     throw new ArgumentException("Title is required.", nameof(title));

        return new MarketEvent
        {
            Id          = Guid.NewGuid(),
            EventDate   = eventDate,
            EventTime   = eventTime,
            EventType   = eventType.ToUpperInvariant(),
            Title       = title,
            Description = description,
            Impact      = impact,
            Symbol      = symbol,
            Source      = source,
            IsRecurring = isRecurring,
            CreatedAt   = createdAt ?? SystemClock.Instance.GetCurrentInstant(),
        };
    }

    public void Update(string title, string? description, EventImpact impact)
    {
        Title       = title;
        Description = description;
        Impact      = impact;
    }
}

public enum EventImpact { Low, Medium, High }
