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
