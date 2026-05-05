using NodaTime;
using rvs.AlgoTrader.Domain.Interfaces;

namespace rvs.AlgoTrader.Infrastructure.Clock;

/// <summary>
/// Production clock — delegates to NodaTime's SystemClock.
/// Timezone-specific helpers now live in <see cref="IMarketTimezone"/> / <see cref="MarketTimezoneService"/>;
/// this class only provides the raw UTC instant.
/// </summary>
public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();
    private SystemClock() { }

    public Instant GetCurrentInstant() => NodaTime.SystemClock.Instance.GetCurrentInstant();
}
