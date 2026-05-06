using System.Collections.Concurrent;
using MassTransit;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Events;
using rvs.AlgoTrader.Domain.Constants;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Tracks broker order lifecycle from submission through terminal states.
/// Replaces the fire-and-forget in LiveExecutionEngine with a state machine backed by
/// increasing-interval polling on GetOrderBookAsync (1s → 2s → 4s … capped at 30s).
/// WebSocket fills via RecordFill short-circuit polling immediately.
/// Singleton: all tracking state is in-memory; lost on restart (acceptable — orders are
/// independently persisted in the orders table).
/// </summary>
public sealed class OrderManager(
    IBrokerClientFactory brokerFactory,
    IOrderRepository orderRepo,
    IPublishEndpoint bus,
    IClock clock,
    ILogger<OrderManager> logger) : IOrderManager
{
    private static readonly TimeSpan TrackingTimeout = TimeSpan.FromMinutes(5);
    private static readonly int[] PollIntervalsMs = [1_000, 2_000, 4_000, 8_000, 16_000, 30_000];

    private readonly ConcurrentDictionary<Guid, TrackedOrder> _entries = new();

    public async Task<Guid> SubmitAsync(
        Order order,
        StrategyInstance instance,
        string correlationId,
        CancellationToken ct)
    {
        var brokerName = instance.BrokerAccount?.Broker?.Name ?? BrokerNames.Default;

        var entry = new TrackedOrder(
            orderId:       order.Id,
            brokerOrderId: order.BrokerOrderId,
            brokerName:    brokerName,
            state:         order.BrokerOrderId != null ? OrderManagerState.Submitted : OrderManagerState.Pending,
            correlationId: correlationId,
            strategyRunId: instance.RuntimeState?.CurrentRunId,
            submittedAt:   clock.NowInstant().ToDateTimeOffset());

        _entries[order.Id] = entry;

        // Fire-and-forget polling loop — does not block the caller
        _ = Task.Run(() => PollUntilTerminalAsync(entry, brokerName, ct), CancellationToken.None);

        return order.Id;
    }

    public void RecordFill(string brokerOrderId, decimal fillPrice, int filledQty, bool isPartial)
    {
        var entry = _entries.Values.FirstOrDefault(e => e.BrokerOrderId == brokerOrderId);
        if (entry == null) return;

        entry.State      = isPartial ? OrderManagerState.PartialFill : OrderManagerState.Filled;
        entry.FillPrice  = fillPrice;
        entry.FilledQty  = filledQty;
        entry.UpdatedAt  = clock.NowInstant().ToDateTimeOffset();

        logger.LogInformation("[OrderManager] Fill via WebSocket: {BrokerOrderId} qty={Qty} @ {Price} partial={Partial}",
            brokerOrderId, filledQty, fillPrice, isPartial);
    }

    public OrderManagerEntry? GetEntry(Guid orderId)
    {
        if (!_entries.TryGetValue(orderId, out var e)) return null;
        return ToPublic(e);
    }

    public IReadOnlyList<OrderManagerEntry> GetActive()
        => _entries.Values
            .Where(e => !IsTerminal(e.State))
            .Select(ToPublic)
            .ToList();

    // ── Polling loop ────────────────────────────────────────────────────────

    private async Task PollUntilTerminalAsync(TrackedOrder entry, string brokerName, CancellationToken ct)
    {
        var deadline = clock.NowInstant() + Duration.FromTimeSpan(TrackingTimeout);
        int pollIdx  = 0;

        while (clock.NowInstant() < deadline && !ct.IsCancellationRequested)
        {
            if (IsTerminal(entry.State)) break;

            await Task.Delay(PollIntervalsMs[Math.Min(pollIdx, PollIntervalsMs.Length - 1)], ct)
                      .ContinueWith(_ => { });

            try
            {
                var brokerClient = brokerFactory.GetOrderClient(brokerName);
                var book         = await brokerClient.GetOrderBookAsync(ct);
                var brokerOrder  = book.FirstOrDefault(o => o.BrokerOrderId == entry.BrokerOrderId);

                if (brokerOrder != null)
                    await ApplyBrokerStatusAsync(entry, brokerOrder, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "[OrderManager] Poll failed for {OrderId}", entry.OrderId);
                entry.State      = OrderManagerState.Error;
                entry.ErrorReason = ex.Message;
                entry.UpdatedAt  = clock.NowInstant().ToDateTimeOffset();
            }

            // Increase poll interval up to cap
            pollIdx = Math.Min(pollIdx + 1, PollIntervalsMs.Length - 1);
        }

        if (!IsTerminal(entry.State))
        {
            entry.State      = OrderManagerState.Expired;
            entry.ErrorReason = "Tracking timeout";
            entry.UpdatedAt  = clock.NowInstant().ToDateTimeOffset();
            logger.LogWarning("[OrderManager] Order {OrderId} expired before reaching terminal state", entry.OrderId);
        }

        // Remove from active tracking after a grace period
        _ = Task.Delay(TimeSpan.FromMinutes(30))
                .ContinueWith(t => { _entries.TryRemove(entry.OrderId, out _); });
    }

    private async Task ApplyBrokerStatusAsync(TrackedOrder entry, BrokerOrder brokerOrder, CancellationToken ct)
    {
        var prev = entry.State;
        entry.State     = MapBrokerStatus(brokerOrder.Status);
        entry.UpdatedAt = clock.NowInstant().ToDateTimeOffset();

        // Detect new fill
        bool newFill = entry.State is OrderManagerState.Filled or OrderManagerState.PartialFill
                       && (entry.FillPrice == null || brokerOrder.FilledQuantity > entry.FilledQty);

        if (newFill && brokerOrder.Price.HasValue)
        {
            entry.FillPrice = brokerOrder.Price;
            entry.FilledQty = brokerOrder.FilledQuantity;

            // Persist fill + publish event
            var order = await orderRepo.GetByIdAsync(entry.OrderId, ct);
            if (order != null)
            {
                var now = clock.NowInstant();
                order.MarkFilled(brokerOrder.Price!.Value, brokerOrder.FilledQuantity, now);
                await orderRepo.UpdateAsync(order, ct);

                await bus.Publish(new OrderFilled(
                    order.Id, brokerOrder.BrokerOrderId, brokerOrder.BrokerOrderId,
                    brokerOrder.InternalSymbol,
                    brokerOrder.Direction.Equals("BUY", StringComparison.OrdinalIgnoreCase)
                        ? OrderDirection.Buy : OrderDirection.Sell,
                    brokerOrder.FilledQuantity, brokerOrder.Price!.Value,
                    entry.State == OrderManagerState.PartialFill,
                    entry.StrategyRunId, entry.CorrelationId, clock.NowIst()), ct);
            }
        }

        if (entry.State != prev)
            logger.LogInformation("[OrderManager] {OrderId} {BrokerOrderId}: {Prev} → {New}",
                entry.OrderId, entry.BrokerOrderId, prev, entry.State);
    }

    private static OrderManagerState MapBrokerStatus(string brokerStatus)
        => brokerStatus.ToUpperInvariant() switch
        {
            "COMPLETE" or "FILLED"    => OrderManagerState.Filled,
            "OPEN" or "TRIGGER PENDING" => OrderManagerState.Open,
            "OPEN PENDING"            => OrderManagerState.Submitted,
            "CANCELLED"               => OrderManagerState.Cancelled,
            "REJECTED"                => OrderManagerState.Rejected,
            "MODIFY PENDING"          => OrderManagerState.Open,
            _                         => OrderManagerState.Open
        };

    private static bool IsTerminal(OrderManagerState s)
        => s is OrderManagerState.Filled or OrderManagerState.Cancelled
              or OrderManagerState.Rejected or OrderManagerState.Expired
              or OrderManagerState.Error;

    private static OrderManagerEntry ToPublic(TrackedOrder e) => new(
        e.OrderId, e.BrokerOrderId, e.State,
        e.FillPrice, e.FilledQty,
        e.SubmittedAt, e.UpdatedAt, e.ErrorReason);

    // Mutable internal state — only mutated by polling loop (one goroutine per order)
    // and RecordFill (lock-free; last-writer-wins is acceptable for fill events).
    private sealed class TrackedOrder(
        Guid orderId, string? brokerOrderId, string brokerName,
        OrderManagerState state, string correlationId, Guid? strategyRunId,
        DateTimeOffset submittedAt)
    {
        public Guid             OrderId       { get; } = orderId;
        public string?          BrokerOrderId { get; } = brokerOrderId;
        public string           BrokerName    { get; } = brokerName;
        public OrderManagerState State         { get; set; } = state;
        public string           CorrelationId { get; } = correlationId;
        public Guid?            StrategyRunId { get; } = strategyRunId;
        public DateTimeOffset   SubmittedAt   { get; } = submittedAt;
        public DateTimeOffset   UpdatedAt     { get; set; } = submittedAt;
        public decimal?         FillPrice     { get; set; }
        public int              FilledQty     { get; set; }
        public string?          ErrorReason   { get; set; }
    }
}
