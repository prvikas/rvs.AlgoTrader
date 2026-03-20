using NodaTime;
using rvs.AlgoTrader.Domain.Interfaces;
namespace rvs.AlgoTrader.Infrastructure.Clock;

/// <summary>Deterministic clock for backtests and unit tests. NOT registered in production DI.</summary>
public sealed class SimulatedClock : IClock
{
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];
    private Instant _current;

    public SimulatedClock(Instant startTime) { _current = startTime; }

    public static SimulatedClock FromIst(int year, int month, int day, int hour, int minute)
    {
        var local = new LocalDateTime(year, month, day, hour, minute, 0);
        var zoned = local.InZoneStrictly(Ist);
        return new SimulatedClock(zoned.ToInstant());
    }

    public Instant NowInstant() => _current;
    public ZonedDateTime NowIst() => _current.InZone(Ist);
    public LocalDate TodayIst() => NowIst().Date;

    /// <summary>Advance clock by a duration.</summary>
    public void Advance(Duration duration) => _current = _current.Plus(duration);

    /// <summary>Advance clock to a specific instant.</summary>
    public void AdvanceTo(Instant instant)
    {
        if (instant < _current) throw new InvalidOperationException("Cannot advance backwards in simulated time");
        _current = instant;
    }

    /// <summary>Advance to next bar open time for a given timeframe.</summary>
    public void AdvanceToNextBar(string timeframe)
    {
        var duration = timeframe switch
        {
            "1m" => Duration.FromMinutes(1),
            "3m" => Duration.FromMinutes(3),
            "5m" => Duration.FromMinutes(5),
            "15m" => Duration.FromMinutes(15),
            "30m" => Duration.FromMinutes(30),
            "1h" => Duration.FromHours(1),
            "1d" => Duration.FromDays(1),
            _ => throw new ArgumentException($"Unknown timeframe: {timeframe}")
        };
        Advance(duration);
    }
}
