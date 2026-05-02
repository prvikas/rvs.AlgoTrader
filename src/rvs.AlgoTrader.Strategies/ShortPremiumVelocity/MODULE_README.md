# Short Premium Velocity (STRAT-004) — Module Reference

## Overview

Short Premium Velocity is a systematic options premium-selling strategy that classifies the market into five velocity regimes and dynamically adapts structure selection, position sizing, hedge coverage, and risk controls to each regime.

**Implementation namespace:** `rvs.AlgoTrader.Strategies.ShortPremiumVelocity`
**Config type:** `ShortPremiumVelocityConfig` (in Application layer, to avoid circular deps)
**DI registration:** `services.AddShortPremiumVelocity()` in `ServiceCollectionExtensions.cs`

---

## Velocity Regimes

| Label | Conditions | Typical Structure |
|---|---|---|
| `VelocityLowVolCompression` | IndiaVIX < 16 AND trendR² ≥ 0.4 | ShortStraddleStrangle |
| `VelocityChoppyMeanReversion` | IndiaVIX [14,20] AND trendR² < 0.4 | IronCondor |
| `VelocityPostPanicNormalization` | IndiaVIX [18,24] AND VixRoC5d < 0 | VerticalCreditSpread |
| `VelocityHighVolExpansion` | IndiaVIX [20,28] AND VixRoC5d > 15% | CalendarSpread (never ShortStraddleStrangle) |
| `VelocityPanic` | IndiaVIX > 28 OR spot1dMove > 3σ | NO new entries — hedge-only mode |

Classification is first-match deterministic. The same inputs always produce the same label, which is required for backtest reproducibility.

---

## 17-Component Architecture

```
VelocityRegimeClassifier  (in QuantRegimeService partial — Infrastructure layer)
         │
         ▼
VelocityScreener          8 hard gates + 5 soft filters
         │
         ▼
VelocityIndicator         VelocityScore + OpportunityDensity + AggressionMultiplier
         │
         ▼
StructureSelector         Pure function: regime × VS → StructureChoice
         │
         ▼
DteOptimizer              6-bucket scoring matrix → optimal DTE per regime
         │
         ▼
HedgeEngine               M1/M2/M3 mandatory + C1/C2/C3 conditional hedges
         │
         ▼
HedgeEvaluator            Portfolio Greeks → hedge coverage ratio + publisher
         │
         ▼
RollDecisionEngine        Gamma-pin / profit target / stop-loss → Roll/Close/Hold
         │
         ▼
CircuitBreakerService     Normal → SoftStop → HardStop (publishes ActivateKillSwitchCommand)
         │
         ▼
MarginManager             NetShockedUtilization tracking + TrimToFit 3-step
         │
         ▼
JumpRiskMonitor           Event-driven intraday vol-spike detector (IBrokerStreamClient)
         │
         ▼
RecoveryManager           4-step Sharpe-gated sizing multiplier (0.50–1.25)
         │
         ▼
ReinvestmentEngine        Session P&L compounding with friction buffer + NAV ceiling
         │
         ▼
SyntheticOptionsPricer    BSM + skew + slippage (backtest / forward-test mode)
         │
         ▼
StressScenarioLibrary     Pre-built crash / VIX-spike / correlation-breakdown scenarios
         │
         ▼
ShortPremiumVelocityStrategy   IStrategy implementation — orchestrates all 17 steps
```

---

## Key Flows

### New position entry (EvaluateAsync steps 1–17)

1. Classify velocity regime (VelocityRegimeClassifier)
2. Check circuit-breaker + jump-risk soft-stops → early exit
3. Screen position (8 hard gates + 5 soft filters)
4. Score the environment (VelocityScore + OpportunityDensity)
5. Select structure (StructureSelector)
6. Select DTE bucket (DteOptimizer)
7. Execute mandatory hedges (M1 delta / M2 tail-risk / M3 vega)
8. Compute Kelly-Hat position size: `kelly × aggression × recovery_multiplier`
9. Check margin headroom (MarginManager)
10. Create position record + emit signal

### Intraday monitoring

- **Every bar**: RollDecisionEngine evaluates each open position
- **Gamma-pin check**: 0–1 DTE within `GammaPinExitMinutesBeforeClose` → forced close
- **CircuitBreakerService**: evaluated on every daily P&L update
- **JumpRiskMonitor**: event-driven on broker WebSocket tick (no polling)

### End-of-session

- RecoveryManager.EvaluateStepUpAsync updates sizing for the next session
- ReinvestmentEngine.ProcessSessionCloseAsync compounds net P&L into SPV capital pool

---

## Hard Guards (APX rules enforced by this module)

| Rule | Enforcement |
|---|---|
| No naked short options | StructureSelector only selects defined-risk structures in HighVol/Panic |
| No new positions in HardStop | CircuitBreakerService gate in VelocityScreener |
| Panic → hedge-only | VelocityPanic returns `StructureType.None`; HedgeEngine still runs |
| IClock (AP-001) | All classes use `IClock` alias; zero `DateTime.Now` |
| KillSwitch dual-write (AP-015) | CircuitBreakerService publishes `ActivateKillSwitchCommand` on HardStop |

---

## Configuration

All parameters live in `ShortPremiumVelocityConfig` with per-regime dictionaries keyed on `MarketRegime`.

Key config groups:
- **Per-regime dicts**: `TailRiskScoreCeiling`, `GammaPerThetaCeiling`, `ProfitTargetFraction`, `RecoveryMultiplierMin/Max`, `DtePreferenceWeights`
- **Sizing**: `VelocityScoreWeights[5]`, `OpportunityDensityWeights[5]`, `AggressionMultiplierMin/Max`
- **Circuit breaker**: `SoftStopLossPct=1.5%`, `HardStopLossPct=2.5%`
- **Reinvestment**: `MaxVelocityLayerPctOfAccount=87.5%`, `FrictionBufferPercent=12%`
- **Gamma-pin**: `GammaPinExitMinutesBeforeClose=60`, `StopLossMultiplier=2.0`

Override any value via `StrategyScenario.ParametersJsonOverride` (ScenarioParamMerger at runtime).

---

## Database Migrations

| Migration | Purpose |
|---|---|
| `052_spv_position_legs.sql` | `positions` table leg-tagging columns (leg_type, hedge_type, hedge_net_cost, linked_short_leg_id) |
| `053_spv_circuit_breaker_state.sql` | `velocity_circuit_breaker_state` table for CB state persistence across restarts |
| `054_spv_velocity_session_log.sql` | Per-session summary (P&L, regime, recovery step, CB state) |
| `055_spv_velocity_roll_log.sql` | Per-position roll/close/hold decision audit trail |
| `056_spv_velocity_reinvestment_log.sql` | Session compounding computation log |

---

## Testing

Unit tests live in `tests/rvs.AlgoTrader.Tests.Unit/Strategies/ShortPremiumVelocity/`.

| Test file | Class under test | Key scenarios |
|---|---|---|
| `VelocityRegimeClassifierTests` | `QuantRegimeService` | 5-rule classification, IsResultsSeason |
| `VelocityScreenerTests` | `VelocityScreener` | HardStop gate, TailRiskScore ceiling, RequiresMandatoryHedge |
| `VelocityIndicatorTests` | `VelocityIndicator` | VS/OD in [0,100], Panic tilt, SoftStop aggression cap |
| `StructureSelectorTests` | `StructureSelector` | Panic=None, HighVol never ShortStraddle |
| `DteOptimizerTests` | `DteOptimizer` | Panic=None, weekly bucket gate, valid bucket output |
| `RollDecisionEngineTests` | `RollDecisionEngine` | Gamma-pin, profit target, Panic/HighVol no roll |
| `HedgeEngineTests` | `HedgeEngine` | M1/M2/M3 triggers, C1/C2 conditional triggers |
| `CircuitBreakerServiceTests` | `CircuitBreakerService` | SoftStop/HardStop transitions, KillSwitch publish |
| `MarginManagerTests` | `MarginManager` | GetCurrentState, TrimToFit no-op below hard cap |
| `JumpRiskMonitorTests` | `JumpRiskMonitor` | Initial state, StartAsync non-blocking |
| `RecoveryManagerTests` | `RecoveryManager` | Panic lock, step 1 multiplier, EvaluateStepUp |
| `ReinvestmentEngineTests` | `ReinvestmentEngine` | Net P&L, friction buffer, NAV ceiling cap |
| `SyntheticOptionsPricerTests` | `SyntheticOptionsPricer` | BSM properties, Greeks bounds, expiry intrinsic |

---

## Phase Roadmap

| Phase | Status | Scope |
|---|---|---|
| Phase 1 | DONE | Domain types, interfaces, config, events, DTOs |
| Phase 2 | DONE | All 17 engine components, build 0 errors 0 warnings |
| Phase 3 | DONE | Migrations 053–056, 13 unit test files, this README |
| Phase 4 | PLANNED | Live broker integration, position lifecycle persistence |
