using NodaTime;
using rvs.AlgoTrader.Domain.Entities;

namespace rvs.AlgoTrader.Application.DTOs.MarketData;

/// <summary>Read-only projection of a <see cref="MarketBreadthSnapshot"/> for API responses.</summary>
public record MarketBreadthDto(
    Guid       Id,
    LocalDate  SnapshotDate,
    int        TotalSymbols,
    int        Above200Sma,
    int        Above50Sma,
    decimal    Pct200Sma,
    int        HighsCount,
    int        LowsCount,
    int        AdvancingCount,
    int        DecliningCount,
    decimal    AdRatio,
    string     Regime,
    DateTimeOffset ComputedAt)
{
    /// <summary>Map domain entity → DTO.</summary>
    public static MarketBreadthDto From(MarketBreadthSnapshot s) => new(
        s.Id, s.SnapshotDate, s.TotalSymbols,
        s.Above200Sma, s.Above50Sma, s.Pct200Sma,
        s.HighsCount, s.LowsCount,
        s.AdvancingCount, s.DecliningCount, s.AdRatio,
        s.Regime.ToString(), s.ComputedAt.ToDateTimeOffset());
}

/// <summary>Summary widget data — latest snapshot + 30-day trend.</summary>
public record MarketBreadthSummaryDto(
    MarketBreadthDto? Latest,
    IReadOnlyList<MarketBreadthDto> History);
