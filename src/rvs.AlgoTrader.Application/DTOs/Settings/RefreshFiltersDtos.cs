namespace rvs.AlgoTrader.Application.DTOs.Settings;

/// <summary>Current state of all three refresh-scope filters plus metadata for the UI.</summary>
public record RefreshFiltersDto(
    string   IncludedExchanges,
    string   IncludedInstrumentTypes,
    string   IncludedEquityCategories,
    string[] KnownExchanges,
    string[] KnownInstrumentTypes,
    string[] KnownEquityCategories);

/// <summary>Partial-update request — null fields are left unchanged.</summary>
public record UpdateRefreshFiltersRequest(
    string? IncludedExchanges,
    string? IncludedInstrumentTypes,
    string? IncludedEquityCategories);
