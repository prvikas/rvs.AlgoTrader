namespace rvs.AlgoTrader.Application.DTOs.Backtest;

public record BacktestRequestDto(
    Guid StrategyInstanceId, string InternalSymbol,
    DateOnly FromDate, DateOnly ToDate, string Timeframe,
    Guid? CostProfileId, WalkForwardConfigDto? WalkForward);

public record WalkForwardConfigDto(int InSampleDays, int OutOfSampleDays, int StepDays);

public record BacktestResultDto(
    Guid RunId, string Status, decimal InitialCapital, decimal FinalCapital,
    decimal GrossPnl, decimal NetPnl, decimal TotalReturn, decimal MaxDrawdown,
    decimal SharpeRatio, decimal CalmarRatio, decimal WinRate, int TotalTrades,
    int WinningTrades, int LosingTrades, string DataIntegrityHash,
    DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, object ResultMetrics);

public record BacktestCostProfileDto(
    Guid Id, string Name, decimal BrokeragePct, decimal SttPct, decimal GstPct,
    decimal SebiChargesPct, decimal StampDutyPct, decimal SlippagePct,
    string Description, DateTimeOffset CreatedAt);

public record CreateBacktestCostProfileDto(
    string Name, decimal BrokeragePct, decimal SttPct, decimal GstPct,
    decimal SebiChargesPct, decimal StampDutyPct, decimal SlippagePct, string Description);
