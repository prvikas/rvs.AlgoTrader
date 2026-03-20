using NodaTime;

namespace rvs.AlgoTrader.Domain.ValueObjects;

/// <summary>
/// Represents a fully closed OHLCV candle bar. Only closed candles are passed to IStrategy.
/// Open/partial bars are NEVER included in the strategy context.
/// </summary>
public record ClosedCandle(
    string InternalSymbol,
    string Timeframe,
    ZonedDateTime OpenTime,
    ZonedDateTime CloseTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume
)
{
    public bool IsValid =>
        High >= Low &&
        High >= Open && High >= Close &&
        Low <= Open && Low <= Close &&
        Volume >= 0;

    public decimal Body => Math.Abs(Close - Open);
    public decimal Range => High - Low;
    public decimal BodyRangePct => Range == 0 ? 0 : Body / Range;
    public bool IsBullish => Close >= Open;
}
