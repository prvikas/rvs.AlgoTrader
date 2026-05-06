using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Extensions;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.Infrastructure.Repositories;

public class OrderRepository(AlgoTraderDbContext db) : IOrderRepository
{

    public async Task<Order?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.Orders.FindAsync([id], ct);

    public async Task<Order?> GetByIdempotencyKeyAsync(string key, CancellationToken ct = default)
        => await db.Orders.FirstOrDefaultAsync(o => o.IdempotencyKey == key, ct);

    public async Task<Order?> GetByBrokerOrderIdAsync(string brokerOrderId, CancellationToken ct = default)
        => await db.Orders.FirstOrDefaultAsync(o => o.BrokerOrderId == brokerOrderId, ct);

    public async Task<IReadOnlyList<Order>> GetByStrategyRunAsync(Guid strategyRunId, CancellationToken ct = default)
        => await db.Orders
            .Include(o => o.Broker)
            .Include(o => o.Exchange)
            .Include(o => o.ProductType)
            .Where(o => o.StrategyRunId == strategyRunId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Order>> GetRecentAsync(int count, CancellationToken ct = default)
        => await db.Orders
            .Include(o => o.Broker)
            .Include(o => o.Exchange)
            .Include(o => o.ProductType)
            .OrderByDescending(o => o.PlacedAt)
            .Take(count)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Order>> GetOpenOrdersAsync(string brokerName, CancellationToken ct = default)
    {
        // Resolve broker name to broker ID first
        var broker = await db.Brokers.FirstOrDefaultAsync(b => b.Name == brokerName, ct);
        if (broker is null) return [];
        return await db.Orders
            .Include(o => o.Broker)
            .Include(o => o.Exchange)
            .Include(o => o.ProductType)
            .Where(o => o.BrokerId == broker.Id &&
                (o.Status == OrderStatus.Open || o.Status == OrderStatus.Pending || o.Status == OrderStatus.PartiallyFilled))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Order>> GetByDateRangeAsync(ZonedDateTime from, ZonedDateTime to, CancellationToken ct = default)
    {
        var fromUtc = from.ToInstant().ToDateTimeUtc();
        var toUtc = to.ToInstant().ToDateTimeUtc();
        return await db.Orders.Where(o =>
            EF.Property<DateTime>(o, "placed_at_utc") >= fromUtc &&
            EF.Property<DateTime>(o, "placed_at_utc") <= toUtc)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Order order, CancellationToken ct = default)
    {
        await db.Orders.AddAsync(order, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Order order, CancellationToken ct = default)
    {
        db.Orders.Update(order);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> ExistsAsync(string idempotencyKey, CancellationToken ct = default)
        => await db.Orders.AnyAsync(o => o.IdempotencyKey == idempotencyKey, ct);

    public async Task<int> CountTodayByRunIdsAsync(
        IEnumerable<Guid> strategyRunIds, LocalDate dateIst, CancellationToken ct = default)
    {
        var tz = DateTimeZoneProviders.Tzdb["Asia/Kolkata"];
        var startInstant = dateIst.AtStartOfDayInZone(tz).ToInstant();
        var endInstant   = dateIst.PlusDays(1).AtStartOfDayInZone(tz).ToInstant();
        var ids = strategyRunIds.ToList();

        return await db.Orders
            .CountAsync(o => o.StrategyRunId.HasValue
                          && ids.Contains(o.StrategyRunId!.Value)
                          && o.PlacedAt >= startInstant
                          && o.PlacedAt < endInstant, ct);
    }
}
