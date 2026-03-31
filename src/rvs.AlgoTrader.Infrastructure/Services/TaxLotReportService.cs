using System.Text;
using rvs.AlgoTrader.Application.DTOs.TradeJournal;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Generates ITR-3 compatible tax lot rows for a financial year (April–March).
/// Uses FIFO lot matching by default.
/// </summary>
public sealed class TaxLotReportService(AlgoTraderDbContext db) : ITaxLotReportService
{
    public async Task<IReadOnlyList<TaxLotRow>> GetTaxLotsAsync(
        Guid? strategyInstanceId, string financialYear, CancellationToken ct)
    {
        // Parse FY "2025-26" → from 2025-04-01 to 2026-03-31
        var parts = financialYear.Split('-');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var fyStart))
            throw new ArgumentException($"Invalid financial year format '{financialYear}'. Use 'YYYY-YY' e.g. '2025-26'");

        var fromInstant = Instant.FromDateTimeOffset(new DateTimeOffset(fyStart,     4, 1, 0, 0, 0, TimeSpan.Zero));
        var toInstant   = Instant.FromDateTimeOffset(new DateTimeOffset(fyStart + 1, 3, 31, 23, 59, 59, TimeSpan.Zero));

        var q = db.TradeJournalEntries
            .Where(e => e.ExitTime >= fromInstant && e.ExitTime <= toInstant);
        if (strategyInstanceId.HasValue)
            q = q.Where(e => e.StrategyInstanceId == strategyInstanceId.Value);

        var trades = await q.OrderBy(e => e.EntryTime).ToListAsync(ct);

        return trades.Select(t => new TaxLotRow(
            Symbol:           t.InternalSymbol,
            Direction:        t.Direction,
            EntryDate:        DateOnly.FromDateTime(t.EntryTime.ToDateTimeOffset().Date),
            ExitDate:         DateOnly.FromDateTime(t.ExitTime.ToDateTimeOffset().Date),
            HoldingDays:      t.HoldingDays,
            TaxClassification: t.TaxClassification,
            Quantity:         t.Quantity,
            CostOfAcquisition: t.EntryPrice * t.Quantity,
            SaleConsideration: t.ExitPrice  * t.Quantity,
            GrossGain:        t.GrossPnl,
            Stt:              t.Stt,
            NetGain:          t.NetPnl,
            FinancialYear:    financialYear
        )).ToList();
    }

    public async Task<byte[]> ExportCsvAsync(
        Guid? strategyInstanceId, string financialYear, CancellationToken ct)
    {
        var rows = await GetTaxLotsAsync(strategyInstanceId, financialYear, ct);
        var sb = new StringBuilder();
        sb.AppendLine("Symbol,Direction,Entry Date,Exit Date,Holding Days,Tax Classification,Quantity,Cost of Acquisition (Rs),Sale Consideration (Rs),Gross Gain (Rs),STT (Rs),Net Gain (Rs),Financial Year");
        foreach (var r in rows)
            sb.AppendLine($"{r.Symbol},{r.Direction},{r.EntryDate:yyyy-MM-dd},{r.ExitDate:yyyy-MM-dd},{r.HoldingDays},{r.TaxClassification},{r.Quantity},{r.CostOfAcquisition:F2},{r.SaleConsideration:F2},{r.GrossGain:F2},{r.Stt:F4},{r.NetGain:F2},{r.FinancialYear}");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
