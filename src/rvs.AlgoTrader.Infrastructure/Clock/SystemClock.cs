using NodaTime;
using rvs.AlgoTrader.Domain.Interfaces;
namespace rvs.AlgoTrader.Infrastructure.Clock;

/// <summary>Production IClock implementation using NodaTime SystemClock. Registered as singleton.</summary>
public sealed class SystemClock : IClock
{
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];
    private readonly NodaTime.IClock _inner;

    public SystemClock() : this(NodaTime.SystemClock.Instance) { }
    public SystemClock(NodaTime.IClock inner) => _inner = inner;

    public Instant NowInstant() => _inner.GetCurrentInstant();
    public ZonedDateTime NowIst() => _inner.GetCurrentInstant().InZone(Ist);
    public LocalDate TodayIst() => NowIst().Date;
}
