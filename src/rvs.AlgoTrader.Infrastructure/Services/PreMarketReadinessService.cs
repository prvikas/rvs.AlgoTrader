using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Infrastructure.Persistence;
using StackExchange.Redis;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// #137: Implements pre-market readiness checks.
///
/// Checks (in order):
///  1. CRITICAL — Database reachable
///  2. CRITICAL — Redis reachable
///  3. CRITICAL — Today is a trading day (market calendar)
///  4. WARNING  — mStock broker token valid
///  5. WARNING  — At least one active strategy instance has allocated capital
///  6. INFO     — Capital is non-zero across active instances
/// </summary>
public sealed class PreMarketReadinessService(
    AlgoTraderDbContext db,
    IConnectionMultiplexer? redis,
    IMarketCalendarService calendar,
    IAppBrokerSessionManager sessionManager,
    IClock clock,
    ILogger<PreMarketReadinessService> logger) : IPreMarketReadinessService
{
    private static readonly DateTimeZone Ist = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];

    public async Task<PreMarketReadinessReport> CheckAsync(CancellationToken ct = default)
    {
        var now       = clock.NowInstant();
        var today     = now.InZone(Ist).Date;
        var checks    = new List<ReadinessCheck>();

        // ── 1. Database connectivity ─────────────────────────────────────────
        checks.Add(await CheckDbAsync(ct));

        // ── 2. Redis connectivity ────────────────────────────────────────────
        checks.Add(CheckRedis());

        // ── 3. Market calendar — is today a trading day? ─────────────────────
        checks.Add(await CheckTradingDayAsync(today, ct));

        // ── 4. Broker token validity ─────────────────────────────────────────
        checks.Add(await CheckBrokerTokenAsync(ct));

        // ── 5. Active strategy instances with capital ────────────────────────
        checks.Add(await CheckActiveInstancesAsync(ct));

        var isReady = checks.All(c => c.Severity != CheckSeverity.Critical || c.Passed);

        logger.LogInformation(
            "[PreMarket] Readiness check on {Date}: IsReady={IsReady}, " +
            "Checks={Passed}/{Total}",
            today, isReady,
            checks.Count(c => c.Passed), checks.Count);

        return new PreMarketReadinessReport(isReady, today, now, checks);
    }

    // ── Individual checks ────────────────────────────────────────────────────

    private async Task<ReadinessCheck> CheckDbAsync(CancellationToken ct)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
            return new ReadinessCheck("Database", true, CheckSeverity.Critical, "Connection OK");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[PreMarket] Database connectivity check failed");
            return new ReadinessCheck("Database", false, CheckSeverity.Critical,
                $"Database unreachable: {ex.Message}");
        }
    }

    private ReadinessCheck CheckRedis()
    {
        if (redis == null)
            return new ReadinessCheck("Redis", false, CheckSeverity.Warning, "Redis not configured");

        try
        {
            var pong = redis.GetDatabase().Ping();
            return new ReadinessCheck("Redis", true, CheckSeverity.Warning,
                $"PONG in {pong.TotalMilliseconds:F0}ms");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[PreMarket] Redis connectivity check failed");
            return new ReadinessCheck("Redis", false, CheckSeverity.Warning,
                $"Redis unreachable: {ex.Message}");
        }
    }

    private async Task<ReadinessCheck> CheckTradingDayAsync(LocalDate today, CancellationToken ct)
    {
        try
        {
            var isTradingDay = calendar.IsTradingDay(today);
            return new ReadinessCheck(
                "MarketCalendar",
                isTradingDay,
                CheckSeverity.Critical,
                isTradingDay ? $"{today} is a trading day" : $"{today} is NOT a trading day (holiday/weekend)");
        }
        catch (Exception ex)
        {
            return new ReadinessCheck("MarketCalendar", false, CheckSeverity.Warning,
                $"Calendar check failed: {ex.Message}");
        }
    }

    private async Task<ReadinessCheck> CheckBrokerTokenAsync(CancellationToken ct)
    {
        try
        {
            var isValid = await sessionManager.IsAuthenticatedAsync("MStock", ct);
            return new ReadinessCheck(
                "BrokerToken_MStock",
                isValid,
                CheckSeverity.Warning,
                isValid ? "mStock token valid" : "mStock token missing or expired — re-authenticate before trading");
        }
        catch (Exception ex)
        {
            return new ReadinessCheck("BrokerToken_MStock", false, CheckSeverity.Warning,
                $"Token check failed: {ex.Message}");
        }
    }

    private async Task<ReadinessCheck> CheckActiveInstancesAsync(CancellationToken ct)
    {
        try
        {
            var activeCount = await db.Set<Domain.Entities.StrategyInstance>()
                .Where(s => s.Status == StrategyStatus.Running || s.Status == StrategyStatus.Paused)
                .CountAsync(ct);

            var totalCapital = await db.Set<Domain.Entities.StrategyInstance>()
                .Where(s => s.Status == StrategyStatus.Running || s.Status == StrategyStatus.Paused)
                .SumAsync(s => (decimal?)s.AllocatedCapital ?? 0m, ct);

            return new ReadinessCheck(
                "ActiveInstances",
                activeCount > 0,
                CheckSeverity.Info,
                $"{activeCount} active instance(s), total allocated capital: ₹{totalCapital:N0}");
        }
        catch (Exception ex)
        {
            return new ReadinessCheck("ActiveInstances", false, CheckSeverity.Info,
                $"Instance check failed: {ex.Message}");
        }
    }
}
