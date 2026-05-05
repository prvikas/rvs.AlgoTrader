using NodaTime;
using rvs.AlgoTrader.Domain.Enums;
// ReSharper disable once RedundantUsingDirective — LocalDate used below

namespace rvs.AlgoTrader.Domain.Entities;

public class Position
{
    public Guid Id { get; private set; }
    public string BrokerName { get; private set; } = string.Empty;
    public string InternalSymbol { get; private set; } = string.Empty;
    public long Quantity { get; private set; }
    public decimal AvgPrice { get; private set; }
    public decimal EntryPrice { get; private set; }
    public decimal CurrentPrice { get; private set; }
    public decimal? StopLoss { get; private set; }
    public decimal? TakeProfit { get; private set; }
    public bool IsOpen { get; private set; }
    public Guid? StrategyRunId { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public string ProductType { get; set; } = "MIS";
    public string? CloseReason { get; set; }

    // ── Short Premium Velocity — leg tagging (AP: no separate hedge table) ────
    /// <summary>Leg role within a multi-leg short-premium position. Null for plain equity/non-SPV positions.</summary>
    public LegType? LegType { get; set; }

    /// <summary>Identifies the protective purpose of a hedge leg. Null for ShortPremium / DeltaHedge legs.</summary>
    public HedgeType? HedgeType { get; set; }

    /// <summary>Cumulative net cost of all hedge executions paired to this leg (negative = debit paid).</summary>
    public decimal? HedgeNetCost { get; set; }

    /// <summary>
    /// Self-referencing FK: for a Hedge leg, points to the ShortPremium Position it protects.
    /// For a ShortPremium leg this is null. Enables roll and attribution logic without a join table.
    /// </summary>
    public Guid? LinkedShortLegId { get; set; }

    /// <summary>
    /// Option spread structure (e.g. "IronCondor", "ShortStraddleStrangle").
    /// Stored as a string so the domain entity has no hard dependency on strategy enums.
    /// Null for plain equity / non-SPV positions.
    /// </summary>
    public string? StructureType { get; set; }

    /// <summary>
    /// Actual expiry date of the option series (IST calendar date, e.g. 2024-06-27).
    /// Null for plain equity / non-SPV positions.
    /// Used by RollDecisionEngine to compute true DTE instead of estimating from age.
    /// </summary>
    public LocalDate? ExpiryDate { get; set; }

    public decimal RealizedPnl { get; private set; }
    public decimal UnrealizedPnl { get; private set; }
    public Instant? OpenedAt { get; private set; }
    public Instant? ClosedAt { get; private set; }
    public Instant CreatedAt { get; private set; }
    public Instant UpdatedAt { get; private set; }

    // Compatibility aliases
    public decimal AveragePrice => AvgPrice;
    public decimal LastPrice => CurrentPrice;

    private Position() { }

    public static Position Open(
        string brokerName, string internalSymbol,
        long quantity, decimal avgPrice,
        decimal? stopLoss, decimal? takeProfit,
        Guid? strategyRunId, string correlationId, Instant now)
    {
        return new Position
        {
            Id = Guid.NewGuid(),
            BrokerName = brokerName,
            InternalSymbol = internalSymbol,
            Quantity = quantity,
            AvgPrice = avgPrice,
            EntryPrice = avgPrice,
            CurrentPrice = avgPrice,
            StopLoss = stopLoss,
            TakeProfit = takeProfit,
            IsOpen = true,
            StrategyRunId = strategyRunId,
            CorrelationId = correlationId,
            OpenedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void UpdatePrice(decimal currentPrice, Instant now)
    {
        CurrentPrice = currentPrice;
        UnrealizedPnl = (currentPrice - AvgPrice) * Quantity;
        UpdatedAt = now;
    }

    /// <summary>
    /// Re-average cost basis after a partial fill adds to an existing position.
    /// P&amp;L is always computed against <see cref="AvgPrice"/>, NOT <see cref="EntryPrice"/>.
    /// EntryPrice is the first-fill price and is immutable; AvgPrice is the VWAP across all fills.
    /// </summary>
    public void UpdateAvgPrice(long additionalQuantity, decimal fillPrice, Instant now)
    {
        if (additionalQuantity <= 0) return;
        var totalQty = Quantity + additionalQuantity;
        AvgPrice = (AvgPrice * Quantity + fillPrice * additionalQuantity) / totalQty;
        Quantity = totalQty;
        UnrealizedPnl = (CurrentPrice - AvgPrice) * Quantity;
        UpdatedAt = now;
    }

    public void UpdateStopLoss(decimal newSl, Instant now)
    {
        StopLoss = newSl;
        UpdatedAt = now;
    }

    public void Close(decimal exitPrice, decimal realizedPnl, Instant now)
    {
        RealizedPnl = realizedPnl;
        CurrentPrice = exitPrice;
        UnrealizedPnl = 0;
        IsOpen = false;
        ClosedAt = now;
        UpdatedAt = now;
    }
}
