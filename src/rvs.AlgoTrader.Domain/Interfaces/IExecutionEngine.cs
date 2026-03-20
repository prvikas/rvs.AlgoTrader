using rvs.AlgoTrader.Domain.ValueObjects;

namespace rvs.AlgoTrader.Domain.Interfaces;

public interface IExecutionEngine
{
    Task<ExecutionResult> ExecuteSignalAsync(SignalResult signal, StrategyContext context, CancellationToken ct);
}

public record ExecutionResult(
    bool OrderPlaced,
    Guid? OrderId,
    string? BrokerOrderId,
    string? RejectionReason
);
