using NodaTime;

namespace rvs.AlgoTrader.Domain.Entities;

public class Watchlist
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string CreatedBy { get; private set; } = string.Empty;
    public List<WatchlistSymbol> Symbols { get; private set; } = new();
    public Instant CreatedAt { get; private set; }
    public Instant UpdatedAt { get; private set; }

    private Watchlist() { }
}

public class WatchlistSymbol
{
    public Guid Id { get; private set; }
    public Guid WatchlistId { get; private set; }
    public string InternalSymbol { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }
    public Instant AddedAt { get; private set; }

    private WatchlistSymbol() { }
}
