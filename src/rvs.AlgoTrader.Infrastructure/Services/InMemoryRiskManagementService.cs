using rvs.AlgoTrader.Application.Services;
using Microsoft.Extensions.Logging;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Permissive risk management service used when Redis is not available (local dev / paper trading).
/// Always allows orders — use only for paper trading / forward testing, never for live.
/// </summary>
public sealed class InMemoryRiskManagementService(ILogger<InMemoryRiskManagementService> logger) : IRiskManagementService
{
    public Task<RiskCheckResult> CheckAsync(Guid strategyInstanceId, object orderRequest, CancellationToken ct)
    {
        logger.LogDebug("InMemoryRiskManagementService: permissive risk check passed for instance {Id}", strategyInstanceId);
        return Task.FromResult(new RiskCheckResult(true, null));
    }
}
