using NodaTime;

namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>Persisted OHLCV bar. All timestamps are UTC Instant.</summary>
public class Candle
{
    public Guid Id { get; set; }
    public string InternalSymbol { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public Instant OpenTime { get; set; }
    public Instant CloseTime { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }
    public string BrokerName { get; set; } = string.Empty;
    public bool IsClosed { get; set; }

    /// <summary>Backwards-compat alias — same as OpenTime.</summary>
    public Instant Timestamp => OpenTime;

    public bool IsValid => High >= Low && High >= Open && High >= Close && Low <= Open && Low <= Close && Volume >= 0;

    // EF Core requires parameterless constructor
    public Candle() { }
}
