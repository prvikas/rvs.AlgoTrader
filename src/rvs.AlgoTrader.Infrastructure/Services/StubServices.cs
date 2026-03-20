using rvs.AlgoTrader.Application.DTOs.Backtest;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Infrastructure.Services;

// ─────────────────────────────────────────────────────────────────────────────
// Stub service implementations for interfaces that require full integration
// with the Backtesting project or external systems.
// Replace with real implementations as those systems are wired up.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Stub backtest service — returns a not-implemented error result.
/// Replace with a real implementation that wraps BacktestEngine.
/// </summary>
public class BacktestService : IBacktestService
{
    public Task<BacktestResultDto> RunAsync(BacktestRequestDto request, CancellationToken ct)
        => Task.FromResult(new BacktestResultDto(
            Guid.NewGuid(), "ERROR", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            string.Empty, DateTimeOffset.UtcNow, null,
            new { Error = "BacktestService not yet implemented" }));

    public Task<object> RunWalkForwardAsync(BacktestRequestDto request, CancellationToken ct)
        => Task.FromResult<object>(new { Error = "WalkForward not yet implemented" });
}

/// <summary>
/// Stub backtest reproduction service.
/// </summary>
public class BacktestReproductionService : IBacktestReproductionService
{
    public Task<BacktestResultDto?> ReproduceAsync(BacktestResultDto original, CancellationToken ct)
        => Task.FromResult<BacktestResultDto?>(null);
}
