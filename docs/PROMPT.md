# PROMPT.md — Precision AI Coding Prompt

> Read this file only when explicitly requested via `docs/PROMPT.md`.
> Delete or stub each entry immediately after implementation is confirmed done.

---
## PROMPT-007 — DONE (2026-04-07)

Implemented: BJ-1 24h eviction, BJ-2 RunStartedAt, BJ-3 correct downloadTf,
PS-1 Kelly formula, PS-2 AtrMultiplier, BS-1 expired-option delta=0,
SO-1 rollback→reverse PlaceOrder, SO-2 CloseSpread→reverse PlaceOrder, SO-3 lot size,
IC-1 spread detection in BacktestEngine, IC-3 IronCondor AtmIv==0 guard,
CM-1 STT sell-side only, CM-2 options exchange fee 0.053%.

Deferred (need OptionLegSelector re-arch): IC-2/VS-1 FromStrike anchor, BS-2 atmIv param.

---
## PROMPT-008 — DONE (2026-04-07)

Implemented: SS-2 StrangleCallDelta/StranglePutDelta, SS-4 DiagnosticsJson typed dict,
FIB-1 fib1618 formula corrected (uptrend=swingHigh+range*0.618, downtrend=swingLow-range*0.618),
PAB-1 trendEma 0m warmup guard, EV-1 VWAP daily-TF detection (rolling cumulative, no session reset).

Deferred (larger scope): SS-1 MaxLossMultiple enforcement wiring, SS-3 DTE filter,
CS-1 dual-expiry StrategyContext, CS-2 IV term-structure slope filter,
FIB-2 UnderlyingStopLevel in SpreadSignalResult, FIB-3/FIB-4 BacktestEngine iv_history/event-calendar population,
ALL-SPREADS-1 SpreadBacktestEngine, IC-2/VS-1 FromStrike (deferred from P007), EV-3 PCR validation.
