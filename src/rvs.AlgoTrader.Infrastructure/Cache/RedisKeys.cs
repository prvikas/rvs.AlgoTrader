namespace rvs.AlgoTrader.Infrastructure.Cache;

/// <summary>
/// Central registry for all Redis key patterns used across Infrastructure services.
/// Use these factory methods/constants instead of constructing key strings inline.
/// Prefix: "algotrader:" — all keys are namespaced to avoid collisions in shared Redis.
/// </summary>
public static class RedisKeys
{
    // ── Kill switch ───────────────────────────────────────────────────────────
    /// <summary>Hash key: is_active, activated_by, reason, activated_at, correlation_id</summary>
    public const string KillSwitch = "algotrader:kill_switch";

    /// <summary>Pub/sub channel for kill-switch fan-out to all connected instances.</summary>
    public const string KillSwitchChannel = "algotrader:kill_switch:events";

    // ── Per-instance capital allocation ───────────────────────────────────────
    /// <summary>Hash key: available, reserved, allocated — guarded by Lua CAS script.</summary>
    public static string Capital(Guid instanceId) => $"algotrader:capital:{instanceId}";

    // ── Broker session ────────────────────────────────────────────────────────
    /// <summary>Stores broker access token; expires after session TTL.</summary>
    public static string BrokerSession(string brokerName) => $"algotrader:broker_session:{brokerName}";

    // ── Candle cache ──────────────────────────────────────────────────────────
    /// <summary>Redis sorted set: closed candles keyed by close timestamp (score).</summary>
    public static string CandleCache(string symbol, string timeframe) => $"algotrader:candle:{symbol}:{timeframe}";

    /// <summary>Partial (open) candle state during an active bar.</summary>
    public static string CandlePartial(string symbol, string timeframe) => $"algotrader:candle_partial:{symbol}:{timeframe}";

    // ── Idempotency ───────────────────────────────────────────────────────────
    /// <summary>Set member / string key for deduplicating order placement requests.</summary>
    public static string Idempotency(string key) => $"algotrader:idempotency:{key}";
}
