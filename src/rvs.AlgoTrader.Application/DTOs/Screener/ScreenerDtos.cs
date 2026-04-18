namespace rvs.AlgoTrader.Application.DTOs.Screener;

/// <summary>
/// Filter parameters for <see cref="Services.IScreenerService.ScanAsync"/>.
/// MinRsScore: 0–100 position within 52-week range (default 50).
/// Signal: VCP_BREAKOUT | NEAR_BREAKOUT | UPTREND | null (all).
/// MaxResults: capped at 200 server-side.
/// </summary>
public record ScreenerFilters(
    decimal MinRsScore = 50m,
    string? Signal     = null,
    int MaxResults     = 50);

/// <summary>
/// One row from the screener scan — key metrics for a symbol that passes the trend filter.
/// RsScore: (close−yearLow)/(yearHigh−yearLow) × 100; 100 = at 52-week high.
/// Signal: VCP_BREAKOUT | NEAR_BREAKOUT | UPTREND.
/// VolumeConfirmed: true when last session volume exceeded 20-session average.
/// </summary>
public record ScreenerResultDto(
    string          Symbol,
    decimal         LastClose,
    decimal         Sma200,
    decimal         Sma50,
    decimal         YearHigh,
    decimal         YearLow,
    decimal         RsScore,
    string          Signal,
    bool            VolumeConfirmed,
    DateTimeOffset  ScannedAt);
