using NodaTime;
using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Domain.Entities;

public class Order
{
    public Guid Id { get; private set; }
    public short BrokerId { get; private set; }
    public string? BrokerOrderId { get; private set; }
    public string InternalSymbol { get; private set; } = string.Empty;
    public OrderType OrderType { get; private set; }
    public OrderDirection Direction { get; private set; }
    public long Quantity { get; private set; }
    public decimal? Price { get; private set; }
    public decimal? TriggerPrice { get; private set; }
    public OrderStatus Status { get; private set; }
    public Guid? StrategyRunId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;
    public decimal? FillPrice { get; private set; }
    public long FilledQuantity { get; private set; }
    public decimal? TrailingSl { get; private set; }
    public decimal? TrailingTp { get; private set; }
    public short ExchangeId { get; set; }
    public short ProductTypeId { get; set; }
    public string? RejectionReason { get; set; }
    public Instant? PlacedAt { get; private set; }
    public Instant? FilledAt { get; private set; }
    public Instant CreatedAt { get; private set; }
    public Instant UpdatedAt { get; private set; }

    // Navigation
    public virtual Broker? Broker { get; set; }
    public virtual Exchange? Exchange { get; set; }
    public virtual ProductType? ProductType { get; set; }

    private Order() { }

    public static Order Create(
        short brokerId, string internalSymbol,
        OrderType orderType, OrderDirection direction,
        long quantity, decimal? price, decimal? triggerPrice,
        short exchangeId, short productTypeId,
        string idempotencyKey, string correlationId,
        Guid? strategyRunId, Instant now)
    {
        return new Order
        {
            Id = Guid.NewGuid(),
            BrokerId = brokerId,
            InternalSymbol = internalSymbol,
            OrderType = orderType,
            Direction = direction,
            Quantity = quantity,
            Price = price,
            TriggerPrice = triggerPrice,
            Status = OrderStatus.Pending,
            ExchangeId = exchangeId,
            ProductTypeId = productTypeId,
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

    public void MarkFilled(decimal fillPrice, long filledQty, Instant now)
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
