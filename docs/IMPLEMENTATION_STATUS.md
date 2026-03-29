## Status legend
DONE | PARTIAL | STUB | MISSING | NOT_REVIEWED

## Product areas
| Area | Status | Notes |
|---|---|---|
| Solution structure | PARTIAL | repo, backend, frontend, tests, Claude kit |
| Claude docs | DONE | compressed CLAUDE.md, ANTI_PATTERNS.md extracted |
| Strategy abstraction | DONE | StrategyFactory + GetSchema(); PriceActionBreakout, EmaVwapMomentum, AlertCandleShort |
| Backtest engine | DONE | async jobs, SignalR streaming, extended stats, chart markers, PDF report, fullscreen chart, GET /backtest/{id} |
| Scenarios | DONE | StrategyScenario entity, ScenarioStatus enum, partial override merge, parallel run, promotion gate, comparison grid, ScenariosPanel + ScenarioEditorDrawer, migration 007 |
| DB migrations | DONE | DatabaseMigrationRunner: schema_migrations tracking table, auto-discovers *.sql in order, idempotent, Program.cs is 6 lines — never touch again |
| Forward test engine | NOT_REVIEWED | |
| Live execution engine | NOT_REVIEWED | |
| Broker integrations | NOT_REVIEWED | |
| Candle pipeline | NOT_REVIEWED | |
| Scheduling | NOT_REVIEWED | |
| Risk controls | NOT_REVIEWED | |
| UI workflow | PARTIAL | top-nav layout, MetricCards, right-side drawers, backtest replay chart |
| Master data refresh | PARTIAL | MStock parsing fixed, missing DB columns (003 migration), instrument seeding |
| Tests | NOT_REVIEWED | |

## Update rule
After each meaningful task: revise affected rows only, add evidence-based notes, do not mark DONE without code support.
