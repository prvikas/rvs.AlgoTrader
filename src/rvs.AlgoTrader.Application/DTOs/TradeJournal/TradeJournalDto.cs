namespace rvs.AlgoTrader.Application.DTOs.TradeJournal;

public record TradeJournalEntryDto(
    Guid     Id,
    Guid     StrategyInstanceId,
    string   InternalSymbol,
    string   Direction,
    int      Quantity,
    decimal  EntryPrice,
    decimal  ExitPrice,
    decimal? StopLoss,
    decimal? TakeProfit,
    DateTimeOffset EntryTime,
    DateTimeOffset ExitTime,
    decimal  GrossPnl,
    decimal  NetPnl,
    decimal  Commission,
    decimal  Stt,
    decimal? RMultiple,
    decimal? InitialRisk,
    decimal? Mae,
    decimal? Mfe,
    string   ExitReason,
    string?  EntryReason,
    string?  Notes,
    string[] Tags,
    string   TaxClassification,
    int      HoldingDays,
    string   Source,
    Guid?    SourceTradeId,
    DateTimeOffset CreatedAt);

public record UpdateTradeJournalNotesRequest(string? Notes, string[] Tags);

/// <summary>P&amp;L attribution breakdown across one or more dimensions.</summary>
public record PnlAttributionDto(
    IReadOnlyList<PnlByDimension> BySymbol,
    IReadOnlyList<PnlByDimension> ByMonth,
    IReadOnlyList<PnlByDimension> ByDayOfWeek,
    IReadOnlyList<PnlByDimension> ByExitType,
    IReadOnlyList<PnlByDimension> BySession);

public record PnlByDimension(
    string  Label,
    decimal TotalPnl,
    decimal WinPnl,
    decimal LossPnl,
    int     TradeCount,
    int     WinCount,
    decimal WinRate,
    decimal AvgRMultiple);

/// <summary>Tax lot report row for ITR-3 export.</summary>
public record TaxLotRow(
    string   Symbol,
    string   Direction,
    DateOnly EntryDate,
    DateOnly ExitDate,
    int      HoldingDays,
    string   TaxClassification,
    decimal  Quantity,
    decimal  CostOfAcquisition,
    decimal  SaleConsideration,
    decimal  GrossGain,
    decimal  Stt,
    decimal  NetGain,
    string   FinancialYear);
