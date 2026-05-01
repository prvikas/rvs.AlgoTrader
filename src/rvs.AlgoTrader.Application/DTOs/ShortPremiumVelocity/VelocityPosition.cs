using NodaTime;
using rvs.AlgoTrader.Domain.Enums;

namespace rvs.AlgoTrader.Application.DTOs.ShortPremiumVelocity;

/// <summary>
/// Read-model projection of a single leg within a Short Premium Velocity position.
/// Hydrated by the SPV engine from the positions table; passed to roll / hedge / attribution services.
/// Not persisted separately — derived from Position entity with LegType tagging.
///
/// HedgeType:        Non-null for Hedge and DeltaHedge legs; null for ShortPremium legs.
/// EntryPremium:     Premium collected (short) or paid (long) at entry per unit.
/// CurrentPremium:   Current mark-to-market mid-price of the option leg.
/// GammaPerTheta:    Gamma per unit of Theta collected. High values indicate unfavourable risk/reward.
/// Dte:              Days to expiry as of the last bar close.
/// LinkedShortLegId: ID of the short-premium Position this hedge leg protects. Null for ShortPremium legs.
/// </summary>
public record VelocityPosition(
    Guid          PositionId,
    string        UnderlyingSymbol,
    StructureType StructureType,
    LegType       LegType,
    HedgeType?    HedgeType,
    decimal       EntryPremium,
    decimal       CurrentPremium,
    decimal       Delta,
    decimal       Gamma,
    decimal       Theta,
    decimal       Vega,
    decimal       GammaPerTheta,
    int           Dte,
    Guid?         LinkedShortLegId,
    Instant       EnteredAt
);
