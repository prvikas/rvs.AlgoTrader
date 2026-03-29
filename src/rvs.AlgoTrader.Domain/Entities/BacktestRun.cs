using NodaTime;

namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// Persisted record of a completed backtest run.
/// One row per successful BacktestEngine.RunAsync call.
/// Trades are serialised to TradesJson for simple storage without
/// a separate backtest_trades table.
/// </summary>
public class BacktestRun
{
    public Guid Id { get; set; }
    public string StrategyName { get; set; } = string.Empty;
    public string InternalSymbol { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public LocalDate FromDate { get; set; }
    public LocalDate ToDate { get; set; }
    public decimal InitialCapital { get; set; }
    public decimal FinalEquity { get; set; }
    public decimal TotalPnl { get; set; }
    public decimal TotalReturn { get; set; }
    public decimal MaxDrawdown { get; set; }
    public decimal SharpeRatio { get; set; }
    public decimal CalmarRatio { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal WinRate { get; set; }
    public int TotalTrades { get; set; }
    public int WinCount { get; set; }
    public int LossCount { get; set; }
    public decimal AvgWin { get; set; }
    public decimal AvgLoss { get; set; }
    public int MaxConsecutiveLosses { get; set; }
    public decimal ExpectancyPerTrade { get; set; }
    /// <summary>SHA-256 hash of candle data + parameters — ensures reproducibility.</summary>
    public string? DataHash { get; set; }

    /// <summary>Scenario that triggered this run, if any.</summary>
    public Guid? ScenarioId { get; set; }

    /// <summary>Effective merged parameters used for this run (base + scenario override).</summary>
    public string? EffectiveParametersJson { get; set; }
    /// <summary>JSON array of BacktestTradeDto objects.</summary>
    public string TradesJson { get; set; } = "[]";
    /// <summary>JSON-serialized extended stats (Sortino, Monthly/Yearly breakdown, etc.).</summary>
    public string? ExtendedStatsJson { get; set; }
    public Instant RanAt { get; set; }
}
