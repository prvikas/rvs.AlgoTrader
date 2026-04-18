using Microsoft.Extensions.Configuration;
using Npgsql;
using rvs.AlgoTrader.Application.DTOs.News;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Reads and writes market news from the <c>market_news</c> table (migration 039).
/// External NSE announcements feed ingestion is VERIFY_LIVE — not implemented here.
/// </summary>
public class NewsService(IConfiguration config) : INewsService
{
    private string Cs => config.GetConnectionString("DefaultConnection")
        ?? "Host=localhost;Database=algotrader;Username=postgres;Password=postgres";

    public async Task<IReadOnlyList<NewsItemDto>> GetRecentAsync(
        int days = 7, string? symbol = null, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync(ct);

        var symbolFilter = symbol != null ? "AND (symbol = @sym OR symbol IS NULL)" : "";
        await using var cmd = new NpgsqlCommand($"""
            SELECT id, symbol, headline, summary, source, url,
                   published_at, sentiment, tags
            FROM market_news
            WHERE published_at >= NOW() - INTERVAL '{days} days'
              {symbolFilter}
            ORDER BY published_at DESC
            LIMIT 200
            """, conn);

        if (symbol != null) cmd.Parameters.AddWithValue("@sym", symbol);

        var list = new List<NewsItemDto>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(MapRow(reader));

        return list;
    }

    public async Task<Guid> CreateAsync(CreateNewsItemRequest request, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand("""
            INSERT INTO market_news (symbol, headline, summary, source, url, published_at, sentiment, tags)
            VALUES (@symbol, @headline, @summary, @source, @url, @publishedAt, @sentiment, @tags)
            RETURNING id
            """, conn);

        cmd.Parameters.AddWithValue("@symbol",      (object?)request.Symbol      ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@headline",    request.Headline);
        cmd.Parameters.AddWithValue("@summary",     (object?)request.Summary     ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@source",      request.Source);
        cmd.Parameters.AddWithValue("@url",         (object?)request.Url         ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@publishedAt", request.PublishedAt.UtcDateTime);
        cmd.Parameters.AddWithValue("@sentiment",   (object?)request.Sentiment   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tags",        (object?)request.Tags        ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync(ct);
        return (Guid)result!;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM market_news WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static NewsItemDto MapRow(NpgsqlDataReader r) => new(
        Id:          r.GetGuid(0),
        Symbol:      r.IsDBNull(1) ? null : r.GetString(1),
        Headline:    r.GetString(2),
        Summary:     r.IsDBNull(3) ? null : r.GetString(3),
        Source:      r.GetString(4),
        Url:         r.IsDBNull(5) ? null : r.GetString(5),
        // SCN-1 pattern: Npgsql 9 returns timestamptz as DateTime (UTC kind) on raw connections.
        PublishedAt: new DateTimeOffset(DateTime.SpecifyKind(r.GetDateTime(6), DateTimeKind.Utc)),
        Sentiment:   r.IsDBNull(7) ? null : r.GetString(7),
        Tags:        r.IsDBNull(8) ? null : r.GetString(8));
}
