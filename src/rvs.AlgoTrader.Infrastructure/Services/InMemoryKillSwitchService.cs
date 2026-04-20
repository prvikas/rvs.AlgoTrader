using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// In-memory kill switch. Used when Redis is not available (local dev / single-instance).
/// State is lost on restart — kill switch defaults to inactive on startup.
/// </summary>
public sealed class InMemoryKillSwitchService(IClock clock, ILogger<InMemoryKillSwitchService> logger) : IKillSwitchService
{
    // All 4 fields are written together — a lock ensures an atomic snapshot on read.
    // volatile alone would only guarantee visibility of _isActive but not the other fields.
    private readonly object _statusLock = new();
    private bool _isActive;
    private string? _activatedBy;
    private string? _reason;
    private DateTimeOffset? _activatedAt;

    public Task<bool> IsActiveAsync(CancellationToken ct)
    {
        lock (_statusLock) { return Task.FromResult(_isActive); }
    }

    public Task ActivateAsync(string actor, string reason, string correlationId, CancellationToken ct)
    {
        lock (_statusLock)
        {
            _isActive = true;
            _activatedBy = actor;
            _reason = reason;
            _activatedAt = clock.NowInstant().ToDateTimeOffset();
        }
        logger.LogWarning("Kill switch ACTIVATED by {Actor}: {Reason} [{CorrelationId}]", actor, reason, correlationId);
        return Task.CompletedTask;
    }

    public Task DeactivateAsync(string actor, string correlationId, CancellationToken ct)
    {
        lock (_statusLock)
        {
            _isActive = false;
            _activatedBy = null;
            _reason = null;
            _activatedAt = null;
        }
        logger.LogInformation("Kill switch DEACTIVATED by {Actor} [{CorrelationId}]", actor, correlationId);
        return Task.CompletedTask;
    }

    public Task<KillSwitchStatus> GetStatusAsync(CancellationToken ct)
    {
        lock (_statusLock) { return Task.FromResult(new KillSwitchStatus(_isActive, _activatedBy, _reason, _activatedAt)); }
    }
}
