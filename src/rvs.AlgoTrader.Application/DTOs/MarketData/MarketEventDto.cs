using NodaTime;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Application.DTOs.MarketData;

/// <summary>Read-only projection of a <see cref="MarketEvent"/>.</summary>
public record MarketEventDto(
    Guid        Id,
    LocalDate   EventDate,
    string?     EventTime,
    string      EventType,
    string      Title,
    string?     Description,
    string      Impact,
    string?     Symbol,
    string?     Source,
    bool        IsRecurring,
    DateTimeOffset CreatedAt)
{
    public static MarketEventDto From(MarketEvent e) => new(
        e.Id,
        e.EventDate,
        e.EventTime?.ToString("HH:mm", null),
        e.EventType,
        e.Title,
        e.Description,
        e.Impact.ToString(),
        e.Symbol,
        e.Source,
        e.IsRecurring,
        e.CreatedAt.ToDateTimeOffset());
}

/// <summary>Request to create a new market event.</summary>
public record CreateMarketEventRequest(
    LocalDate  EventDate,
    string     EventType,
    string     Title,
    string?    Description   = null,
    string     Impact        = "Medium",
    string?    EventTime     = null,
    string?    Symbol        = null,
    string?    Source        = null,
    bool       IsRecurring   = false);

/// <summary>Partial-update request — null fields are left unchanged.</summary>
public record UpdateMarketEventRequest(
    string?    Title,
    string?    Description,
    string?    Impact);
