namespace rvs.AlgoTrader.Application.DTOs.Watchlist;

public record WatchlistDto(
    Guid Id, string Name, string CreatedBy,
    IReadOnlyList<WatchlistSymbolDto> Symbols,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public record WatchlistSymbolDto(
    Guid Id, string InternalSymbol, int SortOrder, DateTimeOffset AddedAt);

public record CreateWatchlistDto(string Name);
public record UpdateWatchlistDto(string Name);
