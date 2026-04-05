using NodaTime;

namespace rvs.AlgoTrader.Application.Services;

/// <summary>
/// #137: Pre-market readiness check — validates that the system is safe to start
/// live/forward trading before market open.
///
/// Called by operators via GET /api/readiness/pre-market each morning (ideally as
/// part of a pre-open checklist or scheduled alert).  The engine blocks strategy
/// start if any CRITICAL check fails.
/// </summary>
public interface IPreMarketReadinessService
{
    Task<PreMarketReadinessReport> CheckAsync(CancellationToken ct = default);
}

public record PreMarketReadinessReport(
    bool         IsReady,
    LocalDate    CheckDate,
    Instant      CheckedAt,
    IReadOnlyList<ReadinessCheck> Checks)
{
    /// <summary>True only when all CRITICAL checks pass.</summary>
    public bool AllCriticalPass => Checks.All(c => c.Severity != CheckSeverity.Critical || c.Passed);
}

public record ReadinessCheck(
    string        Name,
    bool          Passed,
    CheckSeverity Severity,
    string?       Detail = null);

public enum CheckSeverity { Critical, Warning, Info }
