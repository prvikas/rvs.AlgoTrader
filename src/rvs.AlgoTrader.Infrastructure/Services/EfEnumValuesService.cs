using Microsoft.EntityFrameworkCore;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// EF Core implementation of <see cref="IEnumValuesService"/>.
/// Reads the enum_values table — cached by the controller for 300s.
/// </summary>
public sealed class EfEnumValuesService(AlgoTraderDbContext db) : IEnumValuesService
{
    public async Task<Dictionary<string, List<EnumOptionDto>>> GetAllAsync(CancellationToken ct)
    {
        var rows = await db.EnumValues
            .Where(e => e.IsActive)
            .OrderBy(e => e.Domain)
            .ThenBy(e => e.SortOrder)
            .ToListAsync(ct);

        return rows
            .GroupBy(e => e.Domain)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => new EnumOptionDto(e.Value, e.Label)).ToList());
    }
}
