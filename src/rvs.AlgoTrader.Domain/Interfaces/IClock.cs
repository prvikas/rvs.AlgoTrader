namespace rvs.AlgoTrader.Domain.Interfaces;

/// <summary>
/// Abstraction over the system clock. All domain and application code must use this.
/// Never use DateTime.Now, DateTimeOffset.UtcNow, or NodaTime.SystemClock directly.
/// Production: SystemClock (NodaTime). Backtest/Test: SimulatedClock.
/// </summary>
public interface IClock
{
    /// <summary>Returns current UTC instant.</summary>
    NodaTime.Instant NowInstant();

    /// <summary>Returns current time in IST (Asia/Kolkata) as ZonedDateTime.</summary>
    NodaTime.ZonedDateTime NowIst();

    /// <summary>Returns current date in IST.</summary>
    NodaTime.LocalDate TodayIst();
}
