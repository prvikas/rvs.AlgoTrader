# PROMPT.md — Precision AI Coding Prompt

> Read this file only when explicitly requested via `docs/PROMPT.md`.
> Delete or stub each entry immediately after implementation is confirmed done.

---

## PROMPT-003 — DONE — DB Integrity + Backtest Engine Fixes + Data Services

Completed 2026-04-06. All checklist items verified:
- Migrations 020–034 applied (A1–A4 covered by 020–023; 034 adds iv_history)
- Backtest commission fix: entry deducted at open, exit at close, NetPnl = GrossPnl − both (B2)
- FromJson() validation throws ArgumentException for all 3 strategy configs (B3)
- Frontend: schema fetched on type change, defaults populated, empty-params guard (C1–C3)
- NseBhavcopyCandleSource + BreadthCalculatorJob wired (D1)
- NseEventCalendarImporter + POST /api/event-calendar/import (D2)
- IvHistoryService + migration 034 + SQL PERCENT_RANK() (D3)
- mStock field mappings in DATA_SOURCES.md (D4)
- ZerodhaClient fully implemented with Polly (E1 — was already done)
- CommissionModelTests + StrategyFromJsonTests in Tests.Unit (F1)
- ArchitectureTests.cs already comprehensive (F2)
- docs/MCP_DESIGN.md created (G)
- docs/PLAN.md P7→DONE, P8→SCOPED, P9→SCOPED with 4 items (H)
