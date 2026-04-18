using rvs.AlgoTrader.Application.DTOs.News;

namespace rvs.AlgoTrader.Application.Services;

/// <summary>
/// Market news — stores and retrieves news items for symbols and the broader market.
/// External data ingestion (NSE announcements feed) is VERIFY_LIVE and left for a
/// future extension; for now items are created manually or via backfill scripts.
/// </summary>
public interface INewsService
{
    /// <summary>
    /// Get recent news items ordered by published_at descending.
    /// Pass <paramref name="symbol"/> to filter by symbol; null returns market-wide + all symbols.
    /// </summary>
    Task<IReadOnlyList<NewsItemDto>> GetRecentAsync(
        int days = 7,
        string? symbol = null,
        CancellationToken ct = default);

    /// <summary>Manually create a news item. Returns the new item's ID.</summary>
    Task<Guid> CreateAsync(CreateNewsItemRequest request, CancellationToken ct = default);

    /// <summary>Delete a news item by ID.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
