using NodaTime;
using rvs.AlgoTrader.Application.Services;
using System.Text.Json;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Writes to the append-only audit_log table via IAuditLogRepository.
/// The PostgreSQL table has rules blocking UPDATE and DELETE (SEBI compliance).
/// </summary>
public sealed class AuditService(IAuditLogRepository repo, Domain.Interfaces.IClock clock) : IAuditService
{
    public Task LogAsync(string action, string actor, string entityType, string entityId,
        object? details, string correlationId, CancellationToken ct)
    {
        return repo.AppendAsync(action, actor, entityType, entityId, details, correlationId, clock.NowInstant(), ct);
    }
}
