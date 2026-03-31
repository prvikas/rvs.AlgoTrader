using rvs.AlgoTrader.Application.DTOs.TradeJournal;

namespace rvs.AlgoTrader.Application.Services;

public interface ITaxLotReportService
{
    Task<IReadOnlyList<TaxLotRow>> GetTaxLotsAsync(
        Guid? strategyInstanceId, string financialYear, CancellationToken ct);

    Task<byte[]> ExportCsvAsync(
        Guid? strategyInstanceId, string financialYear, CancellationToken ct);
}
