using Microsoft.EntityFrameworkCore;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.Infrastructure.Repositories;

public class StrategyInstanceRepository(
    AlgoTraderDbContext db,
    IFieldEncryptionService encryption,
    Domain.Interfaces.IClock clock) : IStrategyInstanceRepository
{
    public async Task<StrategyInstance?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var instance = await db.StrategyInstances
            .Include(s => s.RuntimeState)
            .Include(s => s.Credential)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (instance?.Credential?.BrokerToken != null)
            instance.Credential.BrokerToken = encryption.Decrypt(instance.Credential.BrokerToken);
        return instance;
    }

    public async Task<IReadOnlyList<StrategyInstance>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await db.StrategyInstances
            .Include(s => s.RuntimeState)
            .Include(s => s.Credential)
            .ToListAsync(ct);
        foreach (var i in list)
            if (i.Credential?.BrokerToken != null)
                i.Credential.BrokerToken = encryption.Decrypt(i.Credential.BrokerToken);
        return list;
    }

    public async Task<IReadOnlyList<StrategyInstance>> GetAllActiveAsync(CancellationToken ct = default)
    {
        var list = await db.StrategyInstances
            .Include(s => s.RuntimeState)
            .Include(s => s.Credential)
            .Where(s => s.Status != StrategyStatus.Stopped)
            .ToListAsync(ct);
        foreach (var i in list)
            if (i.Credential?.BrokerToken != null)
                i.Credential.BrokerToken = encryption.Decrypt(i.Credential.BrokerToken);
        return list;
    }

    public async Task<IReadOnlyList<StrategyInstance>> GetRunningAsync(CancellationToken ct = default)
    {
        var list = await db.StrategyInstances
            .Include(s => s.RuntimeState)
            .Include(s => s.Credential)
            .Where(s => s.Status == StrategyStatus.Running)
            .ToListAsync(ct);
        foreach (var i in list)
            if (i.Credential?.BrokerToken != null)
                i.Credential.BrokerToken = encryption.Decrypt(i.Credential.BrokerToken);
        return list;
    }

    public async Task AddAsync(StrategyInstance instance, CancellationToken ct = default)
    {
        // Create related entities
        var runtimeState = StrategyRuntimeState.Create(instance.Id, clock.NowInstant());
        var credential = BrokerCredential.Create(instance.Id);

        // Encrypt broker token if present
        var plainToken = credential.BrokerToken;
        if (plainToken != null)
            credential.BrokerToken = encryption.Encrypt(plainToken);

        instance.RuntimeState = runtimeState;
        instance.Credential = credential;

        await db.StrategyInstances.AddAsync(instance, ct);
        await db.SaveChangesAsync(ct);

        // Restore plaintext for caller
        if (credential.BrokerToken != null && plainToken != null)
            credential.BrokerToken = plainToken;
    }

    public async Task UpdateAsync(StrategyInstance instance, CancellationToken ct = default)
    {
        // Encrypt broker token if present and changed
        if (instance.Credential?.BrokerToken != null)
        {
            var plain = instance.Credential.BrokerToken;
            instance.Credential.BrokerToken = encryption.Encrypt(plain);
        }

        db.StrategyInstances.Update(instance);
        await db.SaveChangesAsync(ct);

        // Restore plaintext for caller
        if (instance.Credential?.BrokerToken != null)
        {
            var encrypted = instance.Credential.BrokerToken;
            instance.Credential.BrokerToken = encryption.Decrypt(encrypted);
        }
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
}
