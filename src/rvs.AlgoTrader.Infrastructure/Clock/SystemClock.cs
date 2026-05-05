using NodaTime;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Infrastructure.Clock;

/// <summary>
/// Production clock implementation of <see cref="IClock"/>.
/// Registered as singleton in DI: services.AddSingleton&lt;IClock, SystemClock&gt;();
///
/// NowIst() and TodayIst() are kept for backward compatibility.
/// New broker-specific code should inject <see cref="IBrokerTimezoneResolver"/> instead.
/// </summary>
public sealed class SystemClock : IClock
{
    // Public parameterless constructor required for DI registration.
    public SystemClock() { }

    private static readonly DateTimeZone IstZone =
        DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    /// <inheritdoc />
    public Instant NowInstant() =>
        NodaTime.SystemClock.Instance.GetCurrentInstant();

    /// <inheritdoc />
    public ZonedDateTime NowIst() =>
        NowInstant().InZone(IstZone);

    /// <inheritdoc />
    public LocalDate TodayIst() =>
        NowIst().Date;
}
