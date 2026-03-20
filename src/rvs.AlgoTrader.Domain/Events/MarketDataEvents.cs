using NodaTime;
using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Domain.Events;

/// <summary>
/// Published ONLY for fully closed candles. Partial/open bars NEVER trigger this event.
/// </summary>
public record CandleClosedEvent(
    string InternalSymbol, string Timeframe,
    ClosedCandle ClosedCandle, string CorrelationId, ZonedDateTime OccurredAt);
