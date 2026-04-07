using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Brokers.Abstractions;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Infrastructure.Constants;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Places multi-leg option spreads atomically (best-effort).
/// Legs are submitted in parallel; if any leg is rejected the remaining open legs
/// are cancelled and the SpreadPosition is marked Failed.
/// Uses IOptionLegSelector to resolve each SpreadLeg to a concrete NFO instrument.
/// Uses IOrderManager to track individual leg order lifecycle.
/// </summary>
public class SpreadOrderManager(
    IOptionLegSelector legSelector,
    ISpreadPositionRepository spreadRepo,
    IBrokerClientFactory brokerFactory,
    IKillSwitchService killSwitch,
    IClock clock,
    ILogger<SpreadOrderManager> logger) : ISpreadOrderManager
{
    public async Task<Guid?> ExecuteSpreadAsync(
        StrategyInstance instance,
        SpreadSignalResult signal,
        decimal spotPrice,
        LocalDate expiryDate,
        string correlationId,
        CancellationToken ct)
    {
        if (await killSwitch.IsActiveAsync(ct))
        {
            logger.LogWarning("[SpreadOrderManager] Kill switch active — spread suppressed for {Instance}", instance.Name);
            return null;
        }

        var brokerName  = instance.BrokerName ?? BrokerNames.Default;
        var brokerClient = brokerFactory.GetOrderClient(brokerName);
        var now          = clock.NowInstant();

        // Resolve all legs to concrete instruments
        var resolvedLegs = new List<(SpreadLeg Spec, OptionLegResolution Resolution)>();
        foreach (var leg in signal.Legs)
        {
            var spec   = new OptionsLegSpec(leg.OptionType, leg.SelectionMode,
                leg.OtmPct, leg.OtmStrikes, leg.FixedStrike, leg.TargetDelta, leg.NearestWeekly);
            var resolution = await legSelector.ResolveAsync(
                instance.InternalSymbol, spec, spotPrice, expiryDate, brokerName, ct);

            if (resolution == null)
            {
                logger.LogError("[SpreadOrderManager] Could not resolve leg {OptionType}/{Mode} for {Instance}",
                    leg.OptionType, leg.SelectionMode, instance.Name);
                return null;
            }
            resolvedLegs.Add((leg, resolution));
        }

        // Create SpreadPosition record
        var spread = new SpreadPosition
        {
            Id               = Guid.NewGuid(),
            BrokerName       = brokerName,
            UnderlyingSymbol = instance.InternalSymbol,
            SpreadType       = signal.SpreadType,
            Status           = "Open",
            StrategyRunId    = instance.RuntimeState?.CurrentRunId,
            CorrelationId    = correlationId,
            OpenedAt         = now
        };

        // Submit all legs in parallel
        var legEntities = new List<SpreadPositionLeg>();
        var orderTasks  = new List<Task<(SpreadPositionLeg Leg, bool Success, string? BrokerOrderId)>>();

        foreach (var (spec, res) in resolvedLegs)
        {
            // SO-3: apply NSE lot size so each leg covers the correct number of contracts.
            // spec.Quantity is the number of lots (default 1); the credential LotSize is the
            // lot multiplier (e.g. 50 for NIFTY, 25 for BANKNIFTY). Default to 1 if not configured.
            var lotSize = instance.Credential?.LotSize > 0 ? instance.Credential.LotSize : 1;
            var legEntity = new SpreadPositionLeg
            {
                Id               = Guid.NewGuid(),
                SpreadPositionId = spread.Id,
                InternalSymbol   = res.InternalSymbol,
                BrokerToken      = res.BrokerToken,
                Direction        = spec.Direction,
                OptionType       = spec.OptionType,
                StrikePrice      = res.StrikePrice,
                Expiry           = res.Expiry,
                Quantity         = spec.Quantity * lotSize,
                Status           = "Pending"
            };
            legEntities.Add(legEntity);

            var idempKey = $"{spread.Id}:{res.InternalSymbol}:{spec.Direction}";
            orderTasks.Add(PlaceLegAsync(brokerClient, legEntity, instance, idempKey, correlationId, ct));
        }

        spread.Legs = legEntities;
        await spreadRepo.AddAsync(spread, ct);

        var results = await Task.WhenAll(orderTasks);

        bool anyRejected = results.Any(r => !r.Success);
        if (anyRejected)
        {
            // Reverse-out any legs that did get submitted — CancelOrder is a no-op for already-filled
            // market orders, so we place a market order in the opposite direction to flatten the position.
            foreach (var (leg, success, brokerId) in results.Where(r => r.Success && r.BrokerOrderId != null))
            {
                try
                {
                    var reverseDir = leg.Direction == OrderDirection.Buy ? OrderDirection.Sell : OrderDirection.Buy;
                    var reverseIdempKey = $"rollback:{spread.Id}:{leg.InternalSymbol}:{reverseDir}";
                    var reverseReq = new OrderRequest(
                        leg.InternalSymbol, leg.BrokerToken,
                        OrderType.Market.ToString().ToUpperInvariant(),
                        reverseDir.ToString().ToUpperInvariant(),
                        leg.Quantity, null, null,
                        Domain.Enums.Exchange.NFO.ToString(),
                        ProductType.NRML.ToString(),
                        reverseIdempKey,
                        instance.RuntimeState?.CurrentRunId,
                        correlationId);
                    await brokerClient.PlaceOrderAsync(reverseReq, ct);
                    leg.Status = "Cancelled";
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[SpreadOrderManager] Failed to reverse rollback leg {BrokerId}", brokerId);
                }
            }
            spread.Status   = "Failed";
            spread.ClosedAt = clock.NowInstant();
            await spreadRepo.UpdateAsync(spread, ct);

            logger.LogError("[SpreadOrderManager] Spread {SpreadId} failed — one or more legs rejected, rollback attempted",
                spread.Id);
            return null;
        }

        // Update leg broker order IDs
        for (int i = 0; i < results.Length; i++)
        {
            legEntities[i].BrokerOrderId = results[i].BrokerOrderId;
            legEntities[i].Status        = "Open";
        }
        await spreadRepo.UpdateAsync(spread, ct);

        logger.LogInformation("[SpreadOrderManager] Spread {SpreadId} ({Type}) opened with {N} legs",
            spread.Id, signal.SpreadType, legEntities.Count);
        return spread.Id;
    }

    public async Task CloseSpreadAsync(Guid spreadPositionId, string reason, string correlationId, CancellationToken ct)
    {
        var spread = await spreadRepo.GetByIdAsync(spreadPositionId, ct);
        if (spread == null || spread.Status != "Open") return;

        var brokerClient = brokerFactory.GetOrderClient(spread.BrokerName);
        foreach (var leg in spread.Legs.Where(l => l.Status == "Open" && l.BrokerOrderId != null))
        {
            try
            {
                // CancelOrder is a no-op on already-filled market orders. Place a reverse market
                // order to close the position (exit = opposite direction of entry).
                var reverseDir = leg.Direction == OrderDirection.Buy ? OrderDirection.Sell : OrderDirection.Buy;
                var idempKey = $"close:{spreadPositionId}:{leg.InternalSymbol}:{reverseDir}";
                var req = new OrderRequest(
                    leg.InternalSymbol, leg.BrokerToken,
                    OrderType.Market.ToString().ToUpperInvariant(),
                    reverseDir.ToString().ToUpperInvariant(),
                    leg.Quantity, null, null,
                    Domain.Enums.Exchange.NFO.ToString(),
                    ProductType.NRML.ToString(),
                    idempKey,
                    null,
                    correlationId);
                await brokerClient.PlaceOrderAsync(req, ct);
                leg.Status = "Cancelled";
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[SpreadOrderManager] Failed to close leg {BrokerId}", leg.BrokerOrderId);
            }
        }

        spread.Status   = "Closed";
        spread.ClosedAt = clock.NowInstant();
        await spreadRepo.UpdateAsync(spread, ct);
    }

    public Task<SpreadPosition?> GetSpreadAsync(Guid spreadPositionId, CancellationToken ct)
        => spreadRepo.GetByIdAsync(spreadPositionId, ct);

    private async Task<(SpreadPositionLeg Leg, bool Success, string? BrokerOrderId)> PlaceLegAsync(
        IBrokerOrderClient broker,
        SpreadPositionLeg leg,
        StrategyInstance instance,
        string idempotencyKey,
        string correlationId,
        CancellationToken ct)
    {
        try
        {
            var request = new OrderRequest(
                leg.InternalSymbol,
                leg.BrokerToken,
                OrderType.Market.ToString().ToUpperInvariant(),
                leg.Direction.ToString().ToUpperInvariant(),
                leg.Quantity,
                null, null,
                Domain.Enums.Exchange.NFO.ToString(),
                ProductType.NRML.ToString(),
                idempotencyKey,
                instance.RuntimeState?.CurrentRunId,
                correlationId);

            var result = await broker.PlaceOrderAsync(request, ct);
            return (leg, result.Success, result.BrokerOrderId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[SpreadOrderManager] Leg placement failed for {Symbol}", leg.InternalSymbol);
            return (leg, false, null);
        }
    }
}
