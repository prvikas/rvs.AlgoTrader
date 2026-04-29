using Microsoft.EntityFrameworkCore;
using NodaTime;
using rvs.AlgoTrader.Application.DTOs.QuantIntelligence;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Infrastructure.Persistence;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// EF Core implementation for indicator intelligence cards.
/// All writes stamp UpdatedAt from IClock (AP-001).
/// </summary>
public sealed class IndicatorIntelligenceService(AlgoTraderDbContext db, IClock clock)
    : IIndicatorIntelligenceService
{
    public async Task<IReadOnlyList<IndicatorIntelligenceDto>> GetAllAsync(CancellationToken ct)
    {
        var rows = await db.IndicatorIntelligence
            .OrderBy(e => e.DisplayName)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<IndicatorIntelligenceDto?> GetByKeyAsync(string indicatorKey, CancellationToken ct)
    {
        var row = await db.IndicatorIntelligence
            .FirstOrDefaultAsync(e => e.IndicatorKey == indicatorKey, ct);
        return row is null ? null : ToDto(row);
    }

    public async Task<IndicatorIntelligenceDto> UpdateAsync(
        string indicatorKey, UpdateIndicatorIntelligenceRequest req, CancellationToken ct)
    {
        var row = await db.IndicatorIntelligence
            .FirstOrDefaultAsync(e => e.IndicatorKey == indicatorKey, ct)
            ?? throw new KeyNotFoundException($"Indicator intelligence card '{indicatorKey}' not found.");

        row.WhatItMeasures       = req.WhatItMeasures;
        row.CommonMistake        = req.CommonMistake;
        row.PositiveEvConditions = req.PositiveEvConditions;
        row.IgnoreConditions     = req.IgnoreConditions;
        row.BestPairedWith       = req.BestPairedWith;
        row.SizingImplications   = req.SizingImplications;
        row.UserNotes            = req.UserNotes;
        row.UpdatedAt            = clock.NowInstant();

        await db.SaveChangesAsync(ct);
        return ToDto(row);
    }

    private static IndicatorIntelligenceDto ToDto(IndicatorIntelligence e) => new(
        e.Id,
        e.IndicatorKey,
        e.DisplayName,
        e.WhatItMeasures,
        e.CommonMistake,
        e.PositiveEvConditions,
        e.IgnoreConditions,
        e.BestPairedWith,
        e.SizingImplications,
        e.UserNotes,
        e.UpdatedAt.ToDateTimeOffset());
}

/// <summary>
/// EF Core implementation for options metrics intelligence cards.
/// All writes stamp UpdatedAt from IClock (AP-001).
/// </summary>
public sealed class GreeksIntelligenceService(AlgoTraderDbContext db, IClock clock)
    : IGreeksIntelligenceService
{
    public async Task<IReadOnlyList<GreeksIntelligenceDto>> GetAllAsync(CancellationToken ct)
    {
        var rows = await db.GreeksIntelligence
            .OrderBy(e => e.DisplayName)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<GreeksIntelligenceDto?> GetByKeyAsync(string metricKey, CancellationToken ct)
    {
        var row = await db.GreeksIntelligence
            .FirstOrDefaultAsync(e => e.MetricKey == metricKey, ct);
        return row is null ? null : ToDto(row);
    }

    public async Task<GreeksIntelligenceDto> UpdateAsync(
        string metricKey, UpdateGreeksIntelligenceRequest req, CancellationToken ct)
    {
        var row = await db.GreeksIntelligence
            .FirstOrDefaultAsync(e => e.MetricKey == metricKey, ct)
            ?? throw new KeyNotFoundException($"Greeks intelligence card '{metricKey}' not found.");

        row.WhatItMeasures       = req.WhatItMeasures;
        row.WhyItMatters         = req.WhyItMatters;
        row.CommonMisuse         = req.CommonMisuse;
        row.PositiveEvConditions = req.PositiveEvConditions;
        row.RegimeContext        = req.RegimeContext;
        row.SizingImplications   = req.SizingImplications;
        row.PortfolioImpact      = req.PortfolioImpact;
        row.UserNotes            = req.UserNotes;
        row.UpdatedAt            = clock.NowInstant();

        await db.SaveChangesAsync(ct);
        return ToDto(row);
    }

    private static GreeksIntelligenceDto ToDto(GreeksIntelligence e) => new(
        e.Id,
        e.MetricKey,
        e.DisplayName,
        e.WhatItMeasures,
        e.WhyItMatters,
        e.CommonMisuse,
        e.PositiveEvConditions,
        e.RegimeContext,
        e.SizingImplications,
        e.PortfolioImpact,
        e.UserNotes,
        e.UpdatedAt.ToDateTimeOffset());
}
