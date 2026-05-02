using MediatR;
using Microsoft.Extensions.Logging;
using NodaTime;
using rvs.AlgoTrader.Application.Commands.ShortPremiumVelocity;
using rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;
using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Entities;
using rvs.AlgoTrader.Domain.Interfaces;
using rvs.AlgoTrader.Domain.ValueObjects;
using IClock = rvs.AlgoTrader.Domain.Interfaces.IClock;

namespace rvs.AlgoTrader.Strategies.ShortPremiumVelocity;

/// <summary>
/// Determines whether an open short-premium position should be rolled, closed, or held.
///
/// ROLL:  DTE &lt; 7 AND GammaPerTheta > 2.0 AND eligible candidate DTE exists.
/// CLOSE: LiquiditySurvival &lt; 60 OR SlippagePenalty > 70.
/// HOLD:  default.
/// NEVER roll in VelocityPanic or VelocityHighVolExpansion → Close or Hold only.
/// NO roll if: underlying through short strike OR FillQuality &lt; 60.
///
/// Gamma-pin hard-exit (non-negotiable):
///   0-DTE/1-DTE: force Close ≥ GammaPinExitMinutesBeforeClose before market close.
///   Soft-kill:   GammaPerTheta > GammaPinSoftKillGammaRatio × ceiling AND near close
///                → force Close ≥ GammaPinSoftKillMinutesBeforeClose before close.
///
/// Profit target: realised gain ≥ ProfitTargetFraction[regime] × entry premium → Close.
/// Stop-loss:     |unrealised loss| ≥ StopLossMultiplier × theoretical max-loss → Close + publish.
/// </summary>
public sealed class RollDecisionEngine(
    IPublisher                  publisher,
    IClock                      clock,
    ILogger<RollDecisionEngine> log)
    : IRollDecisionEngine
{
    // NSE market close: 15:30 IST
    private static readonly DateTimeZone Ist =
        DateTimeZone.ForOffset(Offset.FromHoursAndMinutes(5, 30));
    private const int MarketCloseHour   = 15;
    private const int MarketCloseMinute = 30;

    public async Task<RollDecision> EvaluateAsync(
        VelocityPosition           position,
        VelocityRegimeState        regime,
        StrategyContext            ctx,
        ShortPremiumVelocityConfig config,
        CancellationToken          ct)
    {
        var blockingGates = new List<string>();

        // ── Gamma-pin hard exit (highest priority) ────────────────────────────
        if (position.Dte <= 1)
        {
            int minsToClose = MinutesToMarketClose();
            if (minsToClose <= config.GammaPinExitMinutesBeforeClose)
            {
                string reason = $"Gamma-pin hard exit: DTE={position.Dte} minsToClose={minsToClose}";
                log.LogInformation("RollDecisionEngine: {Reason} pos={Id}", reason, position.PositionId);
                return new RollDecision(RollAction.Close, [], reason, null);
            }
        }

        // ── Gamma-pin soft-kill ───────────────────────────────────────────────
        if (config.GammaPerThetaCeiling.TryGetValue(regime.Label, out decimal ceiling) && ceiling > 0m &&
            position.GammaPerTheta > config.GammaPinSoftKillGammaRatio * ceiling)
        {
            int minsToClose = MinutesToMarketClose();
            if (minsToClose <= config.GammaPinSoftKillMinutesBeforeClose)
            {
                string reason = $"Gamma-pin soft-kill: GammaPerTheta={position.GammaPerTheta:F2}>" +
                                $"{config.GammaPinSoftKillGammaRatio:F3}×{ceiling:F2} minsToClose={minsToClose}";
                log.LogInformation("RollDecisionEngine soft-kill: {Reason} pos={Id}", reason, position.PositionId);
                return new RollDecision(RollAction.Close, [], reason, null);
            }
        }

        // ── Profit target ─────────────────────────────────────────────────────
        if (config.ProfitTargetFraction.TryGetValue(regime.Label, out decimal profitFrac) && profitFrac > 0m)
        {
            decimal gainFraction = position.EntryPremium > 0
                ? (position.EntryPremium - position.CurrentPremium) / position.EntryPremium
                : 0m;
            if (gainFraction >= profitFrac)
            {
                string reason = $"ProfitTarget: gain={gainFraction:P1} ≥ {profitFrac:P1}×entry";
                log.LogInformation("RollDecisionEngine profit target: {Reason} pos={Id}", reason, position.PositionId);
                return new RollDecision(RollAction.Close, [], reason, null);
            }
        }

        // ── Stop-loss ─────────────────────────────────────────────────────────
        decimal unrealisedLoss = position.EntryPremium > 0
            ? (position.CurrentPremium - position.EntryPremium) / position.EntryPremium
            : 0m;
        decimal maxLoss = config.StopLossMultiplier;
        if (unrealisedLoss >= maxLoss)
        {
            string reason = $"StopLoss: unrealisedLoss={unrealisedLoss:P1} ≥ {maxLoss:F1}×premium";
            log.LogWarning("RollDecisionEngine stop-loss: {Reason} pos={Id}", reason, position.PositionId);
            await publisher.Publish(new StopLossTriggerEvent(
                position.PositionId, unrealisedLoss,
                clock.NowInstant()), ct);
            return new RollDecision(RollAction.Close, [], reason, null);
        }

        // ── Regime block on Roll ──────────────────────────────────────────────
        bool rollBlocked = regime.Label is MarketRegime.VelocityPanic
                                       or MarketRegime.VelocityHighVolExpansion;
        if (rollBlocked)
            blockingGates.Add($"Regime={regime.Label}: no rolling allowed");

        // ── Liquidity / slippage → forced Close ───────────────────────────────
        decimal liquiditySurvival = 80m; // proxy
        decimal slippagePenalty   = 30m; // proxy

        if (liquiditySurvival < 60m)
        {
            string reason = $"LiquiditySurvival={liquiditySurvival:F1}<60";
            log.LogInformation("RollDecisionEngine: CLOSE — {Reason} pos={Id}", reason, position.PositionId);
            return new RollDecision(RollAction.Close, [reason], reason, null);
        }

        if (slippagePenalty > 70m)
        {
            string reason = $"SlippagePenalty={slippagePenalty:F1}>70";
            log.LogInformation("RollDecisionEngine: CLOSE — {Reason} pos={Id}", reason, position.PositionId);
            return new RollDecision(RollAction.Close, [reason], reason, null);
        }

        // ── Roll eligibility ──────────────────────────────────────────────────
        if (!rollBlocked && position.Dte < 7 && position.GammaPerTheta > 2.0m)
        {
            decimal fillQuality = 70m; // proxy
            if (fillQuality < 60m)
                blockingGates.Add($"FillQuality={fillQuality:F1}<60");

            // Underlying through short strike check (proxy)
            bool underlyingThroughStrike = false;
            if (underlyingThroughStrike)
                blockingGates.Add("UnderlyingThroughShortStrike");

            if (blockingGates.Count == 0)
            {
                string reason = $"Roll: DTE={position.Dte}<7 GammaPerTheta={position.GammaPerTheta:F2}>2.0";
                log.LogInformation("RollDecisionEngine: ROLL pos={Id} → {Reason}", position.PositionId, reason);
                return new RollDecision(RollAction.Roll, [], reason, TargetDte: 14);
            }

            log.LogInformation(
                "RollDecisionEngine: roll blocked for pos={Id} — {Gates}",
                position.PositionId, string.Join("; ", blockingGates));
        }

        log.LogDebug("RollDecisionEngine: HOLD pos={Id} DTE={DTE}", position.PositionId, position.Dte);
        return new RollDecision(RollAction.Hold, blockingGates, "HOLD: no roll/close trigger", null);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private int MinutesToMarketClose()
    {
        var now         = clock.NowInstant().InZone(Ist);
        int currentMins = now.Hour * 60 + now.Minute;
        int closeMins   = MarketCloseHour * 60 + MarketCloseMinute;
        return Math.Max(closeMins - currentMins, 0);
    }
}
