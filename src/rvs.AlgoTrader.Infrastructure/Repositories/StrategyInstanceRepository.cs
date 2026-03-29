using Microsoft.EntityFrameworkCore;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.Infrastructure.Repositories;

public class StrategyInstanceRepository(
    AlgoTraderDbContext db,
    IFieldEncryptionService encryption) : IStrategyInstanceRepository
{
    public async Task<StrategyInstance?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var instance = await db.StrategyInstances.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (instance != null) DecryptToken(instance);
        return instance;
    }

    public async Task<IReadOnlyList<StrategyInstance>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await db.StrategyInstances.ToListAsync(ct);
        foreach (var i in list) DecryptToken(i);
        return list;
    }

    public async Task<IReadOnlyList<StrategyInstance>> GetAllActiveAsync(CancellationToken ct = default)
    {
        var list = await db.StrategyInstances.Where(s => s.Status != StrategyStatus.Stopped).ToListAsync(ct);
        foreach (var i in list) DecryptToken(i);
        return list;
    }

    public async Task<IReadOnlyList<StrategyInstance>> GetRunningAsync(CancellationToken ct = default)
    {
        var list = await db.StrategyInstances.Where(s => s.Status == StrategyStatus.Running).ToListAsync(ct);
        foreach (var i in list) DecryptToken(i);
        return list;
    }

    public async Task AddAsync(StrategyInstance instance, CancellationToken ct = default)
    {
        var plain = instance.BrokerToken;
        if (plain != null) instance.BrokerToken = encryption.Encrypt(plain);
        await db.StrategyInstances.AddAsync(instance, ct);
        await db.SaveChangesAsync(ct);
        instance.BrokerToken = plain; // restore plaintext for caller
    }

    public async Task UpdateAsync(StrategyInstance instance, CancellationToken ct = default)
    {
        var plain = instance.BrokerToken;
        if (plain != null) instance.BrokerToken = encryption.Encrypt(plain);
        db.StrategyInstances.Update(instance);
        await db.SaveChangesAsync(ct);
        instance.BrokerToken = plain; // restore plaintext for caller
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var instance = await db.StrategyInstances.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (instance != null)
        {
            db.StrategyInstances.Remove(instance);
            await db.SaveChangesAsync(ct);
        }
    }

    private void DecryptToken(StrategyInstance instance)
    {
        if (instance.BrokerToken != null)
            instance.BrokerToken = encryption.Decrypt(instance.BrokerToken);
    }
}
