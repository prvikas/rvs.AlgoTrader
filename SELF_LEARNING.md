\# SELF\_LEARNING.md

SL-NNN | bad: ... | fix: ... | rule: ...



SL-001 | bad: proposed full rewrite without inspection | fix: inspect code first | rule: always start from IMPLEMENTATION\_STATUS.md

SL-002 | bad: large startup memory | fix: keep CLAUDE.md < 200 lines | rule: detail goes in docs/ or skills

SL-003 | bad: repeated unchanged requirements | fix: deltas only | rule: use REQUIREMENTS\_DELTA.md

SL-004 | bad: strategy coded from prose | fix: spec first | rule: update STRATEGY\_SPECS.md before coding

SL-005 | bad: parity drift backtest vs forward | fix: same signal logic only | rule: parity review on any strategy change

SL-006 | bad: assumed mStock returns IV without verifying | fix: verify live response schema first | rule: unconfirmed API fields must be marked VERIFY\_LIVE

SL-007 | bad: secret or token echoed in output | fix: never log secrets | rule: AP-018 always applies

SL-008 | bad: placed INseEventCalendarImporter interface in Infrastructure; controller imported Infrastructure.Services to use it | fix: always define service interfaces in Application layer; Infrastructure only holds implementations | rule: API → Application ← Infrastructure; AP-003

SL-009 | bad: DateTime.UtcNow in Application handler; .ToDateTimeUtc() in Application mapper | fix: inject IClock (Domain.Interfaces.IClock); use InstantPattern.ExtendedIso.Format() for serialization | rule: AP-001 no DateTime.Now anywhere; NodaTime conversions that return System.DateTime also violate this rule

SL-010 | bad: controller directly enqueued Hangfire job using Infrastructure.Jobs type; controller directly queried DbContext from Infrastructure.Persistence | fix: create IBreadthJobDispatcher + IEnumValuesService interfaces in Application; implement in Infrastructure; inject via DI | rule: controllers must dispatch via abstractions not concrete Infrastructure types

SL-011 | bad: zero-guard placed AFTER decimal division — stopDistance==0 check on line after riskAmount/stopDistance throws DivideByZeroException at runtime | fix: always move guard before the division | rule: validate denominator before dividing; compiler does not warn on decimal÷0m

SL-012 | bad: DTO fields (TrailActivationR, TrailOffsetR, BreakEvenAt1R, CircuitBreakerPct) present in BacktestRequestDto but not forwarded in BacktestService.BuildRequest; circuit breaker silently disabled despite DTO default 0.5m | fix: explicitly name every non-trivial optional field in the request builder | rule: omitting a named optional C# record param silently applies the domain-object default, not the DTO default — these can differ

SL-013 | bad: Sortino denominator was negReturns.Length instead of returns.Length; metric also not annualised while Sharpe was — two compounding errors on one formula | fix: returns.Average(r => r<0 ? r*r : 0.0) then ×√252 | rule: always verify both the denominator and annualisation factor when implementing a risk ratio; check it produces the same scale as the related metric (Sharpe)

SL-014 | bad: ComputeGroupedSharpe normalised each period's P&L by a fixed InitialCapital; also used population variance (÷N) | fix: track runningEquity; divide by equity-at-start-of-period; use Sum÷(N-1) | rule: Sharpe/Sortino period returns must use running equity as denominator; always use Bessel correction (N-1) for sample variance

SL-015 | bad: MonteCarloSimulator.RunFromTrades MaxDrawdowns and FinalEquities lists filled with P50 copies — "approximate" placeholder shipped as output | fix: single combined simulation loop collecting all three metrics | rule: stub values in quantitative result structures are always bugs; never ship placeholder data in financial simulation output

SL-016 | bad: PriceSpreadSim used pos.EntryIvFraction (IV frozen at entry) for all subsequent bar re-pricings; IV changes during hold cause material mis-pricing | fix: thread currentIvFrac from per-bar barIvRank into TryCloseSpreadSim → PriceSpreadSim | rule: any B-S re-pricing call inside a bar loop must use current-bar market inputs, not entry-snapshot values

SL-017 | bad: DrawdownRecoveryBars returned i−troughIdx where i was a trades[] index, not allCandles[] bar index; for sparse strategies this is orders-of-magnitude wrong | fix: (recovTrade.EntryBarIndex + HoldingBars) − (troughTrade.EntryBarIndex + HoldingBars) | rule: any field named *Bars must be verified to use candle-bar indices; trade-sequence index and bar index are unrelated scales



\## Session tracker

| # | Date | Area | Lessons |

|---|---|---|---|

| 1 | 2026-03-25 | full docs baseline | SL-001..SL-007 |

| 2 | 2026-04-06 | PROMPT-003 arch fixes | SL-008..SL-010 |

| 3 | 2026-04-23 | backtest engine audit | SL-011..SL-017 |



