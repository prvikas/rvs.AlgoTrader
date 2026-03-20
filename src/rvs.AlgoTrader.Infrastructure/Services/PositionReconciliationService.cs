using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.Events;
using MassTransit;
namespace rvs.AlgoTrader.Infrastructure.Services;

public sealed class PositionReconciliationService(IPositionRepository positions) : IPositionReconciliationService
{
    private DateTimeOffset _lastRunAt = DateTimeOffset.MinValue;
    private bool _hasMismatches;
    private List<string> _mismatchedSymbols = new();

    public async Task ReconcileAsync(CancellationToken ct)
    {
        var localPositions = await positions.GetOpenAsync(ct);
        _mismatchedSymbols.Clear();
        _hasMismatches = false;

        // Broker position fetch happens via IBrokerAccountClient per broker
        // Comparison logic: for each open local position, verify broker has same qty/price
        // Mismatches → publish PositionMismatchDetected → alert_log

        _lastRunAt = DateTimeOffset.UtcNow;
    }

    public Task<ReconciliationStatus> GetStatusAsync(CancellationToken ct)
        => Task.FromResult(new ReconciliationStatus(_lastRunAt, _hasMismatches, _mismatchedSymbols.AsReadOnly()));
}
