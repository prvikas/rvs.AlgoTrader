using NodaTime;

namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// Daily market-breadth snapshot computed from NSE Bhavcopy EOD data.
/// Used by VCP and other strategies to gauge broad market regime before signalling.
/// </summary>
public class MarketBreadthSnapshot
{
    public Guid Id { get; private set; }

    /// <summary>Date the snapshot represents (IST calendar date).</summary>
    public LocalDate SnapshotDate { get; private set; }

    /// <summary>Total number of NSE equity instruments in universe.</summary>
    public int TotalSymbols { get; private set; }

    /// <summary>Count of instruments that closed above their 200-day SMA.</summary>
    public int Above200Sma { get; private set; }

    /// <summary>Count of instruments that closed above their 50-day SMA.</summary>
    public int Above50Sma { get; private set; }

    /// <summary>Number of 52-week highs on this date.</summary>
    public int HighsCount { get; private set; }

    /// <summary>Number of 52-week lows on this date.</summary>
    public int LowsCount { get; private set; }

    /// <summary>Number of advancing instruments vs declining.</summary>
    public int AdvancingCount { get; private set; }

    /// <summary>Number of declining instruments.</summary>
    public int DecliningCount { get; private set; }

    /// <summary>Computed market regime: Bull, Bear, or Neutral.</summary>
    public MarketRegime Regime { get; private set; }

    /// <summary>Percentage of instruments above their 200-day SMA.</summary>
    public decimal Pct200Sma => TotalSymbols > 0 ? Math.Round((decimal)Above200Sma / TotalSymbols * 100, 2) : 0;

    /// <summary>Advance-decline ratio. >1 = more advancers than decliners.</summary>
    public decimal AdRatio => DecliningCount > 0 ? Math.Round((decimal)AdvancingCount / DecliningCount, 2) : AdvancingCount;

    public Instant ComputedAt { get; private set; }

    private MarketBreadthSnapshot() { }

    public static MarketBreadthSnapshot Create(
        LocalDate snapshotDate,
        int totalSymbols,
        int above200Sma,
        int above50Sma,
        int highs,
        int lows,
        int advancing,
        int declining,
        Instant computedAt)
    {
        var regime = ComputeRegime(totalSymbols, above200Sma, advancing, declining);
        return new MarketBreadthSnapshot
        {
            Id            = Guid.NewGuid(),
            SnapshotDate  = snapshotDate,
            TotalSymbols  = totalSymbols,
            Above200Sma   = above200Sma,
            Above50Sma    = above50Sma,
            HighsCount    = highs,
            LowsCount     = lows,
            AdvancingCount = advancing,
            DecliningCount = declining,
            Regime        = regime,
            ComputedAt    = computedAt,
        };
    }

    private static MarketRegime ComputeRegime(int total, int above200, int advancing, int declining)
    {
        if (total == 0) return MarketRegime.Neutral;
        var pct200 = (double)above200 / total;
        var adRatio = declining > 0 ? (double)advancing / declining : 2.0;

        if (pct200 >= 0.60 && adRatio >= 1.5) return MarketRegime.Bull;
        if (pct200 <= 0.35 || adRatio <= 0.67) return MarketRegime.Bear;
        return MarketRegime.Neutral;
    }
}

public enum MarketRegime
{
    Bull    = 0,
    Neutral = 1,
    Bear    = 2,

    // ── Short Premium Velocity regime labels ──────────────────────────────────
    // Start at 100 to avoid any future conflict with breadth-based values above.
    /// <summary>India VIX &lt; 16, high trend R² — ideal short-premium environment.</summary>
    VelocityLowVolCompression      = 100,
    /// <summary>VIX 14–20, low trend R² — choppy, mean-reverting; tighter structures.</summary>
    VelocityChoppyMeanReversion    = 101,
    /// <summary>VIX 18–24 declining — post-panic normalisation; reduce size, collect theta.</summary>
    VelocityPostPanicNormalization = 102,
    /// <summary>VIX 20–28 rising rapidly — vol expansion; widen strikes, mandatory hedge.</summary>
    VelocityHighVolExpansion       = 103,
    /// <summary>VIX &gt; 28 or spot 1-day move &gt; 3σ — capital preservation only.</summary>
    VelocityPanic                  = 104,
}
