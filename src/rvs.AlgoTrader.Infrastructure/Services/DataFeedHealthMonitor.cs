using System.Collections.Concurrent;
using NodaTime;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Thread-safe, singleton implementation of <see cref="IDataFeedHealthMonitor"/>.
/// State is held in-memory — survives the lifetime of the process but not restarts.
/// </summary>
public class DataFeedHealthMonitor(IClock clock) : IDataFeedHealthMonitor
{
    private readonly ConcurrentDictionary<string, FeedState> _states = new(StringComparer.OrdinalIgnoreCase);

    public void RecordTick(string brokerName)
    {
        var s = GetOrCreate(brokerName);
        s.LastTickAt    = clock.NowInstant();
        s.IsConnected   = true;
        s.State         = "Connected";
    }

    public void RecordDisconnect(string brokerName)
    {
        var s = GetOrCreate(brokerName);
        s.IsConnected = false;
        s.State       = "Disconnected";
        Interlocked.Increment(ref s.TotalDisconnects);
    }

    public void RecordReconnect(string brokerName)
    {
        var s = GetOrCreate(brokerName);
        s.IsConnected        = true;
        s.State              = "Connected";
        s.ReconnectAttempts  = 0;   // reset on success
    }

    public void RecordReconnectAttempt(string brokerName)
    {
        var s = GetOrCreate(brokerName);
        s.State = "Reconnecting";
        Interlocked.Increment(ref s.ReconnectAttempts);
    }

    public IReadOnlyList<FeedHealthStatus> GetAllStatuses()
        => _states.Values.Select(ToDto).ToList();

    public FeedHealthStatus? GetStatus(string brokerName)
        => _states.TryGetValue(brokerName, out var s) ? ToDto(s) : null;

    public bool IsStale(string brokerName, int maxStalenessSeconds = 60)
    {
        if (!_states.TryGetValue(brokerName, out var s)) return true;
        if (!s.IsConnected) return true;
        if (s.LastTickAt is null) return true;
        return (clock.NowInstant() - s.LastTickAt.Value).TotalSeconds > maxStalenessSeconds;
    }

    private FeedState GetOrCreate(string brokerName)
        => _states.GetOrAdd(brokerName, _ => new FeedState { BrokerName = brokerName });

    private static FeedHealthStatus ToDto(FeedState s) => new(
        s.BrokerName, s.IsConnected, s.LastTickAt,
        s.ReconnectAttempts, s.TotalDisconnects, s.State);

    private sealed class FeedState
    {
        public string   BrokerName        { get; set; } = "";
        public bool     IsConnected       { get; set; }
        public Instant? LastTickAt        { get; set; }
        public int      ReconnectAttempts;
        public int      TotalDisconnects;
        public string   State             { get; set; } = "Unknown";
    }
}
