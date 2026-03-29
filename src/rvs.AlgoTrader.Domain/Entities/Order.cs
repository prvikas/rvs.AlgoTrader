using NodaTime;
using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Domain.Entities;

public class Order
{
    public Guid Id { get; private set; }
    public string BrokerName { get; private set; } = string.Empty;
    public string? BrokerOrderId { get; private set; }
    public string InternalSymbol { get; private set; } = string.Empty;
    public OrderType OrderType { get; private set; }
    public OrderDirection Direction { get; private set; }
    public int Quantity { get; private set; }
    public decimal? Price { get; private set; }
    public decimal? TriggerPrice { get; private set; }
    public OrderStatus Status { get; private set; }
    public Guid? StrategyRunId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;
    public decimal? FillPrice { get; private set; }
    public int FilledQuantity { get; private set; }
    public decimal? TrailingSl { get; private set; }
    public decimal? TrailingTp { get; private set; }
    public Exchange Exchange { get; set; } = Enums.Exchange.NSE;
    public ProductType ProductType { get; set; } = Enums.ProductType.MIS;
    public string? RejectionReason { get; set; }
    public Instant? PlacedAt { get; private set; }
    public Instant? FilledAt { get; private set; }
    public Instant CreatedAt { get; private set; }
    public Instant UpdatedAt { get; private set; }

    private Order() { }

    public static Order Create(
        string brokerName, string internalSymbol,
        OrderType orderType, OrderDirection direction,
        int quantity, decimal? price, decimal? triggerPrice,
        string idempotencyKey, string correlationId,
        Guid? strategyRunId, Instant now)
    {
        return new Order
        {
            Id = Guid.NewGuid(),
            BrokerName = brokerName,
            InternalSymbol = internalSymbol,
            OrderType = orderType,
            Direction = direction,
            Quantity = quantity,
            Price = price,
            TriggerPrice = triggerPrice,
            Status = OrderStatus.Pending,
            IdempotencyKey = idempotencyKey,
            CorrelationId = correlationId,
            StrategyRunId = strategyRunId,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void MarkPlaced(string brokerOrderId, Instant now)
    {
        BrokerOrderId = brokerOrderId;
        Status = OrderStatus.Open;
        PlacedAt = now;
        UpdatedAt = now;
    }

    public void MarkFilled(decimal fillPrice, int filledQty, Instant now)
    {
        FillPrice = fillPrice;
        FilledQuantity = filledQty;
        Status = filledQty >= Quantity ? OrderStatus.Complete : OrderStatus.Open;
        FilledAt = now;
        UpdatedAt = now;
    }

    public void MarkCancelled(Instant now)
    {
        Status = OrderStatus.Cancelled;
        UpdatedAt = now;
    }

    public void MarkRejected(Instant now)
    {
        Status = OrderStatus.Rejected;
        UpdatedAt = now;
    }

    public void UpdateTrailing(decimal? trailingSl, decimal? trailingTp, Instant now)
    {
        TrailingSl = trailingSl;
        TrailingTp = trailingTp;
        UpdatedAt = now;
    }
}
