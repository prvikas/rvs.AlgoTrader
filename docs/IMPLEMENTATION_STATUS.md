## Status legend
DONE | PARTIAL | STUB | MISSING | NOT_REVIEWED

## Product areas
| Area | Status | Notes |
|---|---|---|
| Solution structure | PARTIAL | repo, backend, frontend, tests, Claude kit |
| Claude docs | DONE | compressed CLAUDE.md, ANTI_PATTERNS.md extracted |
| Strategy abstraction | DONE | StrategyFactory + GetSchema(); 10 strategies registered (3 equity + 7 options) |
| STRAT-001 VCP Swing | DONE | #77: SMA200 trend filter, pivot-based contraction detection, support/breakout entry, scaling-ready |
| STRAT-002 Fib Option Spread | DONE | #78: Fibonacci 0.618 zone entry, IV filter, put/call credit spread direction by trend |
| STRAT-003 Intraday PCR | DONE | #79: PCR bias, VWAP entry, session window 09:15–11:00, gap defer to 13:00, delta-targeted strike |
| Iron Condor | DONE | #80: 4-leg (short call spread + short put spread), IV range + Bollinger range-bound filter |
| Short Straddle/Strangle | DONE | #81: ATM straddle or OTM strangle, mandatory MaxLossMultiple safety gate per spec |
| Calendar Spread | DONE | #82: sell near-weekly + buy far-monthly ATM, IV filter, call/put by chain bias |
| Vertical Spreads | DONE | #83: all 4 types (BullCall/BearPut/BullPut/BearCall), delta-based legs, chain bias; SignalResult.SpreadEntry() routes to ISpreadOrderManager |
| Backtest engine | PARTIAL | async jobs, SignalR streaming, extended stats, chart markers, PDF report, fullscreen chart; **BUGS**: position sizing ignores entry price scale (over-leverage on tight stops); entry costs not deducted from equity; trailing stop can activate on bar 1; see REQUIREMENTS_DELTA |
| Scenarios | DONE | StrategyScenario entity, ScenarioStatus enum, partial override merge, parallel run, promotion gate, comparison grid, ScenariosPanel + ScenarioEditorDrawer, migration 007; Version field (migration 015) auto-increments on param change |
| DB migrations | DONE | DatabaseMigrationRunner auto-discovers *.sql; migrations 001–015 applied (012: option_iv_history + spread tables, 013: currency + fx_rates, 014: execution_mode column, 015: scenario version) |
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
| Standards cleanup | DONE | #21-#26 (prev); #27 BrokerNames.cs constants — 11 infra/API files updated; #28 TradingDefaults.EmptyJson; #29 EmaSmoothing const; #30 InMemoryKillSwitchService lock(_statusLock) thread safety |
| Performance analytics | DONE | #89: VaR95, CVaR95, OmegaRatio, Skewness, Kurtosis, DeploymentRating/Rationale in BacktestResult/Dto; MAE/MFE in BacktestTradeDto; persisted in ExtendedStatsJson; restored in ToDto; BacktestService.MapToDto synced (ChartSample, CircuitBreaker, all analytics) |
| Monte Carlo simulation | DONE | #97: IMonteCarloSimulator interface + MonteCarloSimulator (bootstrap resample, P5/P50/P95 drawdown+equity, ProbabilityOfRuin); POST /api/backtest/{id}/montecarlo endpoint |
| Multi-timeframe | DONE | #94: StrategyContext gains Candles15Min/Candles1Hour/CandlesDaily; StrategyEvaluationQueue pre-fetches higher-TF from ICandleCache; IsFinerThan guard |
| Strategy correlation | DONE | #95: IStrategyCorrelationAnalyser, StrategyCorrelationAnalyser (Pearson + Monte Carlo 10K), CorrelationController with /matrix /portfolio /check endpoints |
| Forward test engine | NOT_REVIEWED | |
| Live execution engine | NOT_REVIEWED | |
| Broker integrations | NOT_REVIEWED | |
| Candle pipeline | NOT_REVIEWED | |
| Scheduling | NOT_REVIEWED | |
| Position sizing | DONE | #87: IPositionSizingEngine, 5 models (FixedLots, FixedFractional, AtrBased, KellyCriterion half-Kelly, VolatilityTargeting), hard caps |
| Slippage & commission | DONE | #88: ISlippageModel (6 types incl. Almgren-Chriss VolumeImpact), ICommissionModel, IndianMarketCommissionModel (STT/GST/SEBI/stamp) |
| Portfolio risk manager | DONE | #85/#86: PortfolioRiskManager, 6 controls, auto kill-switch on daily loss limit, Redis-backed config |
| Paper trading | DONE | #92: ExecutionMode enum on StrategyInstance, IPaperOrderSimulator, 5bps slippage, persists to forward_test_trades |
| Trailing stop manager | DONE | #93: ITrailingStopManager, all 7 StopType variants, ratchet enforcement, TimeStop bar-counting |
| Scaling/pyramid manager | DONE | #100: IScalingManager, ScalingMode/TrancheTrigger enums, weighted avg price, EmaBounce/PriceMoveUp/Down triggers |
| Risk controls | PARTIAL | portfolio-level wired; strategy-level per-order check not yet integrated into LiveExecutionEngine |
| UI workflow | PARTIAL | top-nav layout, MetricCards, right-side drawers, backtest replay chart; strategy list dynamic from API; scenario list shows inline Return/Sharpe/Drawdown badges from compare endpoint; strategy creation form missing parameter schema fetch + default population |
| Master data refresh | PARTIAL | MStock parsing fixed, missing DB columns (003 migration), instrument seeding |
| Tests | PARTIAL | PriceActionBreakout unit tests pass; SignalType enum comparisons fixed; integration factory wired |

## Update rule
After each meaningful task: revise affected rows only, add evidence-based notes, do not mark DONE without code support.
