namespace rvs.AlgoTrader.Application.Services;

/// <summary>
/// Parses an NSE corporate actions CSV and upserts rows into the market_events table.
/// </summary>
public interface INseEventCalendarImporter
{
    /// <summary>
    /// Imports market events from a CSV string.
    /// Expected columns: SYMBOL, PURPOSE, EX_DATE (yyyy-MM-dd or dd/MM/yyyy).
    /// Returns the count of events created or updated.
    /// </summary>
    Task<int> ImportCsvAsync(string csvText, CancellationToken ct);
}
