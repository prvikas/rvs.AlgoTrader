using Microsoft.Extensions.Logging;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// In-memory kill switch. Used when Redis is not available (local dev / single-instance).
/// State is lost on restart — kill switch defaults to inactive on startup.
/// </summary>
public sealed class InMemoryKillSwitchService(ILogger<InMemoryKillSwitchService> logger) : IKillSwitchService
{
    private volatile bool _isActive;
    private string? _activatedBy;
    private string? _reason;
    private DateTimeOffset? _activatedAt;

    public Task<bool> IsActiveAsync(CancellationToken ct)
        => Task.FromResult(_isActive);

    public Task ActivateAsync(string actor, string reason, string correlationId, CancellationToken ct)
    {
        _isActive = true;
        _activatedBy = actor;
        _reason = reason;
        _activatedAt = DateTimeOffset.UtcNow;
        logger.LogWarning("Kill switch ACTIVATED by {Actor}: {Reason} [{CorrelationId}]", actor, reason, correlationId);
        return Task.CompletedTask;
    }

    public Task DeactivateAsync(string actor, string correlationId, CancellationToken ct)
    {
        _isActive = false;
        logger.LogInformation("Kill switch DEACTIVATED by {Actor} [{CorrelationId}]", actor, correlationId);
        return Task.CompletedTask;
    }

    public Task<KillSwitchStatus> GetStatusAsync(CancellationToken ct)
        => Task.FromResult(new KillSwitchStatus(_isActive, _activatedBy, _reason, _activatedAt));
}
