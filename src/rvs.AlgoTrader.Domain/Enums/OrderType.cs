namespace rvs.AlgoTrader.Domain.Enums;

public enum OrderType { Market, Limit, SL, SLM }
public enum OrderDirection { Buy, Sell }
public enum OrderStatus { Pending, Open, PartiallyFilled, Complete, Rejected, Cancelled }
public enum SignalType { Buy, Sell, Hold }
public enum StrategyStatus { Draft, Scheduled, Running, Paused, Stopped }
public enum StrategyMode { Backtest, Forward, Live }
public enum InstrumentType { Equity, Futures, Options, Index }
public enum OptionType { Call, Put }
public enum BrokerName { Zerodha, Upstox, MStock }
public enum ScheduleAction { AutoResume, Pause, Skip, StartLate, AlertOnly, Scheduled, NextDay, ManuallyPaused }
public enum MissedSessionBehavior { StartLate, Skip, AlertOnly }
public enum AlertSeverity { Info, Warn, Critical }
public enum AlertCategory { System, Strategy, Broker, DataQuality, Risk }
public enum SkippedReason { Throttled, MarketClosed, KillSwitch, RiskLimit, InsufficientCapital, Timeout, OutsideSchedule, InsufficientData, FilterFailed }
public enum StrategyRunStatus { Running, Stopped, Failed, Completed }
public enum ScenarioStatus { Draft, Backtested, ForwardTest, Live, Archived }
public enum BacktestJobStatus { Queued, Downloading, Running, Completed, Failed, Cancelled }
public enum Exchange { NSE, BSE, NFO, BFO, MCX, CDS }
public enum ProductType { MIS, CNC, NRML }

/// <summary>
/// Controls how IOptionLegSelector picks the concrete strike for an options leg.
/// Atm: nearest ATM strike.
/// OtmByPct: OTM by a percentage of spot (e.g. 2% OTM call).
/// OtmByStrike: OTM by a fixed number of strike intervals (e.g. 2 strikes OTM).
/// FixedStrike: exact strike price provided in OptionsLegSpec.
/// ByDelta: closest strike whose Black-Scholes delta matches TargetDelta.
/// </summary>
public enum StrikeSelectionMode { Atm, OtmByPct, OtmByStrike, FixedStrike, ByDelta }

/// <summary>
/// Lifecycle states tracked by IOrderManager.
/// Submitted → Open (ACK) → PartialFill / Filled / Cancelled / Rejected / Expired / Error
/// </summary>
public enum OrderManagerState
{
    Pending,
    Submitted,
    Open,
    PartialFill,
    Filled,
    Cancelled,
    Rejected,
    Expired,
    Error
}

/// <summary>Regime classification for implied volatility vs. 52-week history.</summary>
public enum IvRegime { Low, Normal, Elevated, Extreme }

/// <summary>
/// Position sizing algorithm applied before each order.
/// FixedLots:          always trade N lots regardless of price or volatility.
/// FixedFractional:    risk X% of current equity per trade (R-based: stops required).
/// AtrBased:           risk amount = equity × riskPct; lots = risk / (ATR × pointValue).
/// KellyCriterion:     half-Kelly: lots = 0.5 × (winRate/lossRate − (1−winRate)/avgWinLoss) × equity / price.
/// VolatilityTargeting: target X% annualised volatility; scale lots to hit that target.
/// </summary>
public enum SizingModel { FixedLots, FixedFractional, AtrBased, KellyCriterion, VolatilityTargeting }

/// <summary>Slippage model applied to fills in backtests and paper trading.</summary>
public enum SlippageModelType { None, Fixed, Percentage, BidAsk, AtrFraction, VolumeImpact }

/// <summary>Commission model for a specific broker.</summary>
public enum CommissionModelType { FlatPerOrder, PercentageOfValue, ZeroCommission }

/// <summary>
/// Controls whether a strategy instance runs against live broker data, paper-trades,
/// or is a historical backtest. Extends the existing StrategyMode with Paper.
/// </summary>
public enum ExecutionMode { Backtest, Paper, Live }

/// <summary>Stop-loss variant applied per trade.</summary>
public enum StopType
{
    Fixed,           // static price level set at entry
    TrailingFixed,   // trails by a fixed ₹ offset
    TrailingPercent, // trails by a % of current price
    TrailingAtr,     // trails by N × ATR
    BreakEven,       // slides to entry once 1R gained
    TimeStop,        // close if not in profit after N bars
    Chandelier       // highest high (long) / lowest low (short) − N × ATR
}

/// <summary>What triggers the next scale-in tranche.</summary>
public enum TrancheTrigger
{
    PriceMoveUp,    // spot rises by X% from last entry
    PriceMoveDown,  // spot falls by X% (for shorts)
    EmaBounce,      // price pulls back to EMA and bounces
    ManualOnly      // never auto-triggered; requires explicit call
}

/// <summary>Whether to scale into winners (pyramid) or out of them (de-risk).</summary>
public enum ScalingMode { None, PyramidIn, ScaleOut }
