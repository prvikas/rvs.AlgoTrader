namespace rvs.AlgoTrader.Domain.Constants;

/// <summary>
/// Named constants for all supported candle timeframes.
/// Use these instead of magic strings in switch expressions, arrays, and Redis keys.
/// </summary>
public static class Timeframes
{
    public const string OneMinute      = "1m";
    public const string ThreeMinute    = "3m";
    public const string FiveMinute     = "5m";
    public const string FifteenMinute  = "15m";
    public const string ThirtyMinute   = "30m";
    public const string SixtyMinute    = "60m";
    public const string Daily          = "1d";

    /// <summary>All supported timeframes in ascending granularity order.</summary>
    public static readonly IReadOnlyList<string> All =
        [OneMinute, ThreeMinute, FiveMinute, FifteenMinute, ThirtyMinute, SixtyMinute, Daily];
}
