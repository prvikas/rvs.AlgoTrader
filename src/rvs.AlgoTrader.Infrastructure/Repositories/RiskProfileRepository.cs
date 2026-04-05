using Microsoft.EntityFrameworkCore;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.Infrastructure.Repositories;

public class RiskProfileRepository(AlgoTraderDbContext db) : IRiskProfileRepository
{
    public async Task<RiskProfile?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.RiskProfiles.FindAsync([id], ct);
}
