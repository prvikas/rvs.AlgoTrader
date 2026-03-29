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
| DB migrations | DONE | DatabaseMigrationRunner auto-discovers *.sql; migrations 001–013 applied (012: option_iv_history + spread tables, 013: currency + fx_rates) |
| FluentValidation | DONE | #17: ScenarioCommandValidators — Create/Update/Promote with JSON, capital, length rules |
| DTOs in controllers | DONE | #16: inline DTOs moved to Application/DTOs/{Auth,Broker,Settings,HistoricalData,MarketData} |
| Stub repositories | DONE | #18: 7 real EF Core repos; stubs removed from production DI; field-encryption service wired |
| Market breadth | DONE | #99: MarketBreadthSnapshot entity, IMarketBreadthService, single-SQL CTE computation, BreadthCalculatorJob, MarketBreadthController |
| Event calendar | DONE | #90: MarketEvent entity, IEventCalendarService, F&O expiry seeder, EventCalendarController |
| Historical data mgr | DONE | #91: IHistoricalDataManager, quality reports, gap detection, CSV bulk import, DataManagerController |
| Data-feed health | DONE | #96: IDataFeedHealthMonitor singleton, ConcurrentDictionary FeedState, exponential backoff in CandleAggregatorService, DataFeedController |
| Greeks engine | DONE | #68: IBlackScholesEngine, BlackScholesEngine (B-S with Abramowitz normal approx), GreeksSnapshot value object |
| Option leg selector | DONE | #65: IOptionLegSelector, OptionsLegSpec on SignalResult, StrikeSelectionMode enum, delta-based selection via BS engine |
| Order state machine | DONE | #73: IOrderManager, OrderManager with increasing-interval polling + WebSocket fill integration, OrderManagerState enum |
| IV Rank service | DONE | #72: IOptionIvRankService, OptionIvHistory entity, IvRankSnapshot value object, IvRegime enum, migration 012 |
| Currency on instrument | DONE | #64: QuoteCurrency/SettlementCurrency/PriceMultiplier on Instrument, IFxRateProvider, FxRate entity, migration 013 |
| Multi-leg spreads | DONE | #84: SpreadLeg/SpreadSignalResult types, ISpreadOrderManager, SpreadOrderManager, SpreadPosition entities, migration 012 |
| Forward test engine | NOT_REVIEWED | |
| Live execution engine | NOT_REVIEWED | |
| Broker integrations | NOT_REVIEWED | |
| Candle pipeline | NOT_REVIEWED | |
| Scheduling | NOT_REVIEWED | |
| Risk controls | NOT_REVIEWED | |
| UI workflow | PARTIAL | top-nav layout, MetricCards, right-side drawers, backtest replay chart |
| Master data refresh | PARTIAL | MStock parsing fixed, missing DB columns (003 migration), instrument seeding |
| Tests | PARTIAL | PriceActionBreakout unit tests pass; SignalType enum comparisons fixed; integration factory wired |

## Update rule
After each meaningful task: revise affected rows only, add evidence-based notes, do not mark DONE without code support.
