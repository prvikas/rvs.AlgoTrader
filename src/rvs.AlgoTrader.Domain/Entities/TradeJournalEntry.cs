using NodaTime;

namespace rvs.AlgoTrader.Domain.Entities;

/// <summary>
/// Persistent per-trade journal entry for closed trades.
/// Populated from ForwardTestTrade or Order after a position closes.
/// Used for R-multiple analysis, P&amp;L attribution, and ITR-3 tax export.
/// </summary>
public class TradeJournalEntry
{
    public Guid     Id                   { get; set; }
    public Guid     StrategyInstanceId   { get; set; }
    public string   InternalSymbol       { get; set; } = string.Empty;
    public string   Direction            { get; set; } = string.Empty;   // BUY / SELL
    public int      Quantity             { get; set; }
    public decimal  EntryPrice           { get; set; }
    public decimal  ExitPrice            { get; set; }
    public decimal? StopLoss             { get; set; }
    public decimal? TakeProfit           { get; set; }
    public Instant  EntryTime            { get; set; }
    public Instant  ExitTime             { get; set; }
    public decimal  GrossPnl            { get; set; }
    public decimal  NetPnl              { get; set; }
    public decimal  Commission          { get; set; }
    public decimal  Stt                 { get; set; }
    public decimal? RMultiple           { get; set; }
    public decimal? InitialRisk         { get; set; }
    public decimal? Mae                 { get; set; }
    public decimal? Mfe                 { get; set; }
    public string   ExitReason          { get; set; } = "UNKNOWN";
    public string?  EntryReason         { get; set; }
    public string?  Notes               { get; set; }
    public string[] Tags                { get; set; } = [];
    public string   TaxClassification   { get; set; } = "Speculative";
    public int      HoldingDays         { get; set; }
    public string   Source              { get; set; } = "ForwardTest";
    public Guid?    SourceTradeId       { get; set; }
    public Instant  CreatedAt           { get; set; }

    // EF Core parameterless constructor
    public TradeJournalEntry() { }

    /// <summary>Factory — compute derived fields automatically.</summary>
    public static TradeJournalEntry Create(
        Guid strategyInstanceId, string symbol, string direction,
        int quantity, decimal entryPrice, decimal exitPrice,
        decimal? stopLoss, decimal? takeProfit,
        Instant entryTime, Instant exitTime,
        decimal grossPnl, decimal netPnl, decimal commission, decimal stt,
        decimal? mae, decimal? mfe, string exitReason, string? entryReason,
        string source, Guid? sourceTradeId, Instant createdAt)
    {
        var holdingDays = (int)(exitTime - entryTime).TotalDays;
        decimal? initialRisk = stopLoss.HasValue
            ? Math.Abs(entryPrice - stopLoss.Value) * quantity
            : null;
        decimal? rMultiple = initialRisk is > 0
            ? netPnl / initialRisk.Value
            : null;

        // Indian tax classification: Equity delivery > 1 year = LTCG, else Speculative intraday / STCG
        var taxClass = holdingDays == 0
            ? "Speculative"
            : holdingDays < 365
                ? "ShortTermCapitalGain"
                : "LongTermCapitalGain";

        return new TradeJournalEntry
        {
            Id                 = Guid.NewGuid(),
            StrategyInstanceId = strategyInstanceId,
            InternalSymbol     = symbol,
            Direction          = direction,
            Quantity           = quantity,
            EntryPrice         = entryPrice,
            ExitPrice          = exitPrice,
            StopLoss           = stopLoss,
            TakeProfit         = takeProfit,
            EntryTime          = entryTime,
            ExitTime           = exitTime,
            GrossPnl           = grossPnl,
            NetPnl             = netPnl,
            Commission         = commission,
            Stt                = stt,
            RMultiple          = rMultiple,
            InitialRisk        = initialRisk,
            Mae                = mae,
            Mfe                = mfe,
            ExitReason         = exitReason,
            EntryReason        = entryReason,
            Notes              = null,
            Tags               = [],
            TaxClassification  = taxClass,
            HoldingDays        = holdingDays,
            Source             = source,
            SourceTradeId      = sourceTradeId,
            CreatedAt          = createdAt,
        };
    }
}
