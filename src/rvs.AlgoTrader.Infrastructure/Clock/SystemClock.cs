using NodaTime;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Infrastructure.Clock;

/// <summary>
/// Production clock implementation of <see cref="IClock"/>.
///
/// NowIst() and TodayIst() are kept for backward compatibility so existing
/// call-sites continue to compile.  New code should NOT call these directly;
/// instead inject <see cref="IBrokerTimezoneResolver"/> and use
/// <c>resolver.ResolveAsync(brokerName).Now</c> to get the correct
/// market time for the specific broker being traded.
/// </summary>
public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();
    private SystemClock() { }

    // Lazily resolved once — safe because TZDB is immutable at runtime.
    private static readonly DateTimeZone IstZone =
        DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    /// <inheritdoc />
    public Instant NowInstant() =>
        NodaTime.SystemClock.Instance.GetCurrentInstant();

    /// <inheritdoc />
    /// <remarks>
    /// Legacy helper retained for backward compatibility.
    /// Prefer <see cref="IBrokerTimezoneResolver"/> for broker-specific market time.
    /// </remarks>
    public ZonedDateTime NowIst() =>
        NowInstant().InZone(IstZone);

    /// <inheritdoc />
    /// <remarks>
    /// Legacy helper retained for backward compatibility.
    /// Prefer <see cref="IBrokerTimezoneResolver"/> for broker-specific market date.
    /// </remarks>
    public LocalDate TodayIst() =>
        NowIst().Date;
}
