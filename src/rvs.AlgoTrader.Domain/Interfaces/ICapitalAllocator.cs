namespace rvs.AlgoTrader.Domain.Interfaces;

public interface ICapitalAllocator
{
    Task<bool> TryReserveAsync(Guid strategyInstanceId, decimal amount, CancellationToken ct);
    Task ReleaseAsync(Guid strategyInstanceId, decimal amount, CancellationToken ct);
    Task<decimal> GetAvailableAsync(Guid strategyInstanceId, CancellationToken ct);
    Task ReconcileFromPositionsAsync(CancellationToken ct);
}
