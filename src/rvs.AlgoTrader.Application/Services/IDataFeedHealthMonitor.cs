using NodaTime;

namespace rvs.AlgoTrader.Application.Services;

/// <summary>
/// Monitors WebSocket data feed health per broker.
/// Tracks last-received tick timestamp, connection state, and reconnection attempts.
/// </summary>
public interface IDataFeedHealthMonitor
{
    /// <summary>Record that a tick was received from <paramref name="brokerName"/>.</summary>
    void RecordTick(string brokerName);

    /// <summary>Record a connection loss for <paramref name="brokerName"/>.</summary>
    void RecordDisconnect(string brokerName);

    /// <summary>Record a successful reconnection for <paramref name="brokerName"/>.</summary>
    void RecordReconnect(string brokerName);

    /// <summary>Record a failed reconnection attempt for <paramref name="brokerName"/>.</summary>
    void RecordReconnectAttempt(string brokerName);

    /// <summary>Get the current health status for all monitored brokers.</summary>
    IReadOnlyList<FeedHealthStatus> GetAllStatuses();

    /// <summary>Get the health status for a specific broker.</summary>
    FeedHealthStatus? GetStatus(string brokerName);

    /// <summary>
    /// Returns true if the feed for <paramref name="brokerName"/> is considered stale
    /// (no tick received within <paramref name="maxStalenessSeconds"/> seconds).
    /// </summary>
    bool IsStale(string brokerName, int maxStalenessSeconds = 60);
}

/// <summary>Snapshot of the data feed health for a single broker.</summary>
public record FeedHealthStatus(
    string   BrokerName,
    bool     IsConnected,
    Instant? LastTickAt,
    int      ReconnectAttempts,
    int      TotalDisconnects,
    string   State);
