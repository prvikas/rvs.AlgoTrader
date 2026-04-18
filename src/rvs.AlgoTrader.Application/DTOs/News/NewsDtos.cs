namespace rvs.AlgoTrader.Application.DTOs.News;

/// <summary>One market news item as returned to the frontend.</summary>
public record NewsItemDto(
    Guid            Id,
    string?         Symbol,          // null = market-wide
    string          Headline,
    string?         Summary,
    string          Source,          // NSE | BSE | manual
    string?         Url,
    DateTimeOffset  PublishedAt,
    string?         Sentiment,       // Positive | Negative | Neutral | Unknown
    string?         Tags);

/// <summary>Request body for POST /api/news.</summary>
public record CreateNewsItemRequest(
    string?  Symbol,
    string   Headline,
    string?  Summary,
    string   Source,
    string?  Url,
    DateTimeOffset PublishedAt,
    string?  Sentiment = "Unknown",
    string?  Tags      = null);
