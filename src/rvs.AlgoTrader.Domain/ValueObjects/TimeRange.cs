using NodaTime;

namespace rvs.AlgoTrader.Domain.ValueObjects;

public record TimeRange(Instant Start, Instant End)
{
    public bool Contains(Instant instant) => instant >= Start && instant <= End;
    public Duration Duration => End - Start;
}
