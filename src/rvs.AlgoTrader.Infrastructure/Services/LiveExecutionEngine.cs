using MassTransit;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Events;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Infrastructure.Constants;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Converts strategy signals into broker orders.
///
/// Gate sequence (in order):
///   0. Approval gate   — active live-trading approval required
///   1. Kill switch      — system-wide or strategy-wide halt
///   2. Idempotency      — de-duplicate signals within a short window
///   3. Position sizing  — compute quantity via IPositionSizingEngine (FixedFractional default)
///                         mirrors ForwardTestEngine exactly (AP-002 execution parity)
///   4. Per-strategy risk profile — MaxCapitalPerTradePct, MaxTradesPerDay,
///                         MaxOpenTradesPerSymbol (loaded once from IRiskProfileRepository)
///   5. Portfolio risk   — 6-control portfolio-level check via IPortfolioRiskManager
///   6. Capital reservation — atomic Redis Lua (AP-005)
///   7. Broker order     — Polly-wrapped HTTP via IBrokerClientFactory (AP-010)
///   8. Persist + event  — order record + OrderPlaced domain event
/// </summary>
public class LiveExecutionEngine(
    IBrokerClientFactory brokerFactory,
    ICapitalAllocator capitalAllocator,
    IIdempotencyService idempotency,
    IKillSwitchService killSwitch,
    IOrderRepository orderRepo,
    IPortfolioRiskManager portfolioRisk,
    IPositionSizingEngine sizingEngine,
    IRiskProfileRepository riskProfileRepo,
    IStrategyRunRepository strategyRunRepo,
    IPublishEndpoint bus,
    IClock clock,
    IApprovalService approvalService,
    ILogger<LiveExecutionEngine> logger) : ILiveExecutionEngine
{
    private const SizingModel DefaultSizingModel = SizingModel.FixedFractional;

    public async Task ExecuteSignalAsync(
        StrategyInstance instance,
        SignalResult signal,
        string correlationId,
        CancellationToken ct)
    {
        // 0. Approval gate — instance must have an active live-trading approval
        var activeApproval = await approvalService.GetActiveApprovalAsync(instance.Id, ct);
        if (activeApproval == null)
        {
            logger.LogWarning(
                "[LiveExecution] Signal suppressed — no active approval for instance {Instance} ({InstanceId}). " +
                "Run backtest + forward test and obtain manual approval before live deployment.",
                instance.Name, instance.Id);
            return;
        }

        // 1. Kill switch check
        if (await killSwitch.IsActiveAsync(ct))
        {
            logger.LogWarning("[LiveExecution] Kill switch active — signal suppressed for {Instance}", instance.Name);
            return;
        }

        // 2. Idempotency key = instance + signal direction + candle timestamp (ms)
        var idempotencyKey = $"{instance.Id}:{signal.Signal.ToString().ToUpperInvariant()}:{clock.NowInstant().ToUnixTimeMilliseconds()}";
        var idempotencyCheck = await idempotency.CheckAsync(idempotencyKey, ct);
        if (idempotencyCheck.IsDuplicate)
        {
            logger.LogDebug("[LiveExecution] Duplicate signal suppressed by idempotency: {Key}", idempotencyKey);
            return;
        }

        var direction   = signal.Signal == SignalType.Buy ? OrderDirection.Buy : OrderDirection.Sell;
        var credential  = instance.Credential ?? throw new InvalidOperationException(
            $"BrokerCredential not found for instance {instance.Id}");
        var entryPrice  = signal.EntryPrice ?? 0m;

        // 3. Position sizing — identical logic to ForwardTestEngine (AP-002 execution parity)
        //    FixedFractional: risk 1% of allocated capital per trade using signal.StopLoss as risk anchor.
        //    Falls back to FixedLots (credential.LotSize) when no stop-loss is provided.
        var (quantity, sizingRationale) = sizingEngine.Compute(
            DefaultSizingModel,
            instance.AllocatedCapital > 0 ? instance.AllocatedCapital : entryPrice,
            entryPrice,
            signal.StopLoss,
            atr: null,
            new PositionSizingConfig(FixedLots: credential.LotSize > 0 ? credential.LotSize : 1));

        quantity = Math.Max(1, quantity);
        logger.LogDebug("[LiveExecution] Sizing for {Instance}: {Rationale}", instance.Name, sizingRationale);

        var orderValue = entryPrice * quantity;

        // 4. Per-strategy risk profile checks
        if (instance.RiskProfileId.HasValue)
        {
            var riskCheck = await CheckRiskProfileAsync(instance, orderValue, ct);
            if (!riskCheck.Allowed)
            {
                logger.LogWarning("[LiveExecution] Risk profile blocked order for {Instance}: {Reason}",
                    instance.Name, riskCheck.BlockReason);
                return;
            }
        }

        // 5. Portfolio-level risk controls (6 controls: daily loss, positions, deployment, symbol, margin, delta)
        var portfolioCheck = await portfolioRisk.CheckOrderAsync(instance, orderValue, ct);
        if (!portfolioCheck.Allowed)
        {
            logger.LogWarning("[LiveExecution] Portfolio risk blocked order for {Instance}: {Reason}",
                instance.Name, portfolioCheck.BlockReason);
            return;
        }

        // 6. Capital reservation (atomic Redis Lua — AP-005)
        var reserved = await capitalAllocator.TryReserveAsync(instance.Id, orderValue, ct);
        if (!reserved)
        {
            logger.LogWarning("[LiveExecution] Insufficient capital for {Instance}", instance.Name);
            return;
        }

        // 7. Place order via broker
        var brokerClient = brokerFactory.GetOrderClient(instance.BrokerName ?? BrokerNames.Zerodha);
        var orderRequest = new OrderRequest(
            instance.InternalSymbol,
            credential.BrokerToken ?? instance.InternalSymbol,
            OrderType.Market.ToString().ToUpperInvariant(),
            direction.ToString().ToUpperInvariant(),
            quantity,
            signal.EntryPrice,
            null,
            credential.Exchange.ToString(),
            credential.ProductType.ToString(),
            idempotencyKey,
            instance.RuntimeState?.CurrentRunId,
            correlationId);

        var brokerResult = await brokerClient.PlaceOrderAsync(orderRequest, ct);
        var now = clock.NowInstant();

        // 8. Persist order record
        var order = Order.Create(
            instance.BrokerName ?? BrokerNames.Zerodha,
            instance.InternalSymbol,
            OrderType.Market,
            direction,
            quantity,
            signal.EntryPrice,
            null,
            idempotencyKey,
            correlationId,
            instance.RuntimeState?.CurrentRunId,
            now);

        if (brokerResult.Success && brokerResult.BrokerOrderId != null)
            order.MarkPlaced(brokerResult.BrokerOrderId, now);
        else
            order.MarkRejected(now);

        await orderRepo.AddAsync(order, ct);

        if (brokerResult.Success)
        {
            await idempotency.StoreAsync(idempotencyKey, brokerResult, ct);
            await bus.Publish(new OrderPlaced(
                order.Id, instance.BrokerName ?? BrokerNames.Zerodha, brokerResult.BrokerOrderId!,
                instance.InternalSymbol, OrderType.Market, direction,
                quantity, signal.EntryPrice,
                instance.RuntimeState?.CurrentRunId, correlationId, clock.NowIst()), ct);

            logger.LogInformation("[LiveExecution] Order placed: {OrderId} for {Instance} ({Signal}) qty={Qty}",
                brokerResult.BrokerOrderId, instance.Name,
                signal.Signal.ToString().ToUpperInvariant(), quantity);
        }
        else
        {
            // Release capital on rejection (AP-005 compensating release)
            await capitalAllocator.ReleaseAsync(instance.Id, orderValue, ct);
            logger.LogError("[LiveExecution] Order rejected for {Instance}: {Reason}",
                instance.Name, brokerResult.RejectionReason);
        }
    }

    // ── Per-strategy risk profile enforcement ────────────────────────────────

    private async Task<RiskCheckResult> CheckRiskProfileAsync(
        StrategyInstance instance, decimal orderValue, CancellationToken ct)
    {
        var profile = await riskProfileRepo.GetByIdAsync(instance.RiskProfileId!.Value, ct);
        if (profile == null) return new RiskCheckResult(true, null); // profile deleted — allow

        // MaxCapitalPerTradePct: order value must not exceed this % of allocated capital
        if (instance.AllocatedCapital > 0 && profile.MaxCapitalPerTradePct > 0)
        {
            var tradePct = orderValue / instance.AllocatedCapital * 100m;
            if (tradePct > profile.MaxCapitalPerTradePct)
                return new RiskCheckResult(false,
                    $"Trade size {tradePct:F1}% exceeds MaxCapitalPerTradePct {profile.MaxCapitalPerTradePct}%");
        }

        // MaxTradesPerDay: count today's orders for this instance's runs
        if (profile.MaxTradesPerDay > 0)
        {
            var runs = await strategyRunRepo.GetByInstanceAsync(instance.Id, ct);
            var runIds = runs.Select(r => r.Id);
            var todayIst = clock.NowInstant()
                .InZone(DateTimeZoneProviders.Tzdb["Asia/Kolkata"])
                .Date;
            var todayCount = await orderRepo.CountTodayByRunIdsAsync(runIds, todayIst, ct);
            if (todayCount >= profile.MaxTradesPerDay)
                return new RiskCheckResult(false,
                    $"MaxTradesPerDay {profile.MaxTradesPerDay} reached ({todayCount} placed today)");
        }

        return new RiskCheckResult(true, null);
    }
}
