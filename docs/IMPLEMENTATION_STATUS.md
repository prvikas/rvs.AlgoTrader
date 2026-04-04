## Status legend
DONE | PARTIAL | STUB | MISSING | NOT_REVIEWED

## Product areas
| Area | Status | Notes |
|---|---|---|
| Solution structure | PARTIAL | repo, backend, frontend, tests, Claude kit |
| Claude docs | DONE | compressed CLAUDE.md, ANTI_PATTERNS.md extracted |
| Strategy abstraction | DONE | StrategyFactory + GetSchema(); 10 strategies registered (3 equity + 7 options) |
| Strategies (6) | DONE | STRAT-001 VCP (#77), STRAT-002 Fib spread (#78), STRAT-003 PCR (#79), Iron Condor (#80), Straddle/Strangle (#81), Calendar (#82) |
| Vertical Spreads | DONE | #83: all 4 types (BullCall/BearPut/BullPut/BearCall), delta-based legs, chain bias; SignalResult.SpreadEntry() routes to ISpreadOrderManager |
| Backtest engine | DONE | async jobs, SignalR streaming, extended stats, chart markers, PDF report; position sizing fixed (risk/stop capped at 25% equity); entry+exit costs both deducted |
| Scenarios | DONE | StrategyScenario entity, ScenarioStatus enum, partial override merge, parallel run, promotion gate, comparison grid, ScenariosPanel + ScenarioEditorDrawer, migration 007; Version field (migration 015) auto-increments on param change |
| DB migrations | DONE | DatabaseMigrationRunner auto-discovers *.sql; migrations 001–018 applied (018: strategy_approvals table + approval_ready/approved_at on strategy_instances) |
| Trade Journal | DONE | TradeJournalEntry entity, EF config, EfTradeJournalRepository, P&L attribution by symbol/month/DOW/exit-type, TaxLotReportService (ITR-3), TradeJournalController; frontend TradeJournalPage + PortfolioAnalysisPage (bar charts + tax lot export) |
| Health checks | DONE | DbHealthCheck (SELECT 1), RedisHealthCheck (PING Degraded not Unhealthy), /health (JSON report) + /healthz (liveness) endpoints registered in Program.cs |
| Infra quality | DONE | FluentValidation (#17), DTOs extracted (#16), EF Core repos (#18), BrokerNames constants (#27) |
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
| Strategy correlation | DONE | #95: IStrategyCorrelationAnalyser, StrategyCorrelationAnalyser (Pearson + Monte Carlo 10K), CorrelationController with /matrix /portfolio /check endpoints; CorrelationPage (heatmap, risk warnings, efficient frontier, optimal weights) |
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
| Scaling/pyramid manager | DONE | IScalingManager, ScalingMode/TrancheTrigger enums, EmaBounce/PriceMoveUp/Down triggers |
| Risk controls | PARTIAL | portfolio-level wired; strategy-level per-order not integrated into LiveExecutionEngine |
| Approval Gate (P4) | DONE | migration 018, StrategyApproval entity, IApprovalService, ApprovalService (CAGR/DD/fwd checks), ApprovalController (checks/status/approve/revoke), LiveExecutionEngine guard, StrategyCard badge + ApprovalDrawer |
| UI workflow | PARTIAL | top-nav, drawers, backtest replay; Trade Journal + P&L Analysis + Risk Dashboard pages added |
| Master data refresh | PARTIAL | MStock parsing fixed, missing DB columns (003 migration), instrument seeding |
| Tests | PARTIAL | PriceActionBreakout unit tests pass; integration factory wired |

## Update rule: revise affected rows only; do not mark DONE without code support.
