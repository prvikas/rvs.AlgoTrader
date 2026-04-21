## Status legend
DONE | PARTIAL | STUB | MISSING | NOT_REVIEWED

## Product areas
| Area | Status | Notes |
|---|---|---|
| Solution structure | PARTIAL | repo, backend, frontend, tests, Claude kit |
| Strategy abstraction | DONE | StrategyFactory+GetSchema(); 11 strategies; ExitLong/ExitShort; BacktestEngine re-evaluates while position open |
| Strategies (6) | DONE | STRAT-001 VCP (breadth≥40%); STRAT-002 Fib (1.618/0.786, IVP, events); STRAT-003 PCR intraday; +fib1618 fix; DTE filter; VWAP TF; AtmIv guard |
| Vertical Spreads | DONE | All 4 types (BullCall/BearPut/BullPut/BearCall), delta-based legs; SpreadEntry→ISpreadOrderManager |
| Backtest engine | DONE | async/SignalR; SharpeRatio daily; WarmupBars; spread B-S sim; synthetic option chain (BT-OPT-1/2); real EOD snapshots (FIB-5); SS-1/CS-1 |
| Scenarios | DONE | StrategyScenario, partial override, parallel run, promotion gate, comparison grid; Version auto-inc |
| DB migrations | DONE | 001–042; 027 TimescaleDB; 034 iv_history; 038 option snapshots; 039 market_news; 040 app_config+symbol_data_prefs; 042 FK backtest_runs→definition_scenarios |
| Trade Journal | DONE | TradeJournalEntry, P&L attribution, TaxLotReportService (ITR-3); TradeJournalPage+PortfolioAnalysisPage |
| Health checks | DONE | DbHealthCheck+RedisHealthCheck; /health /healthz; pre-market readiness; SLO registry+SloTracker |
| Infra quality | DONE | FluentValidation; MediatR ValidationBehavior; HTTP 422; SECURITY/BACKUP/SLO docs; Polly retry+CB |
| Market breadth/Events | DONE | MarketBreadthSnapshot CTE+job; MarketEvent F&O seeder; EventCalendarController |
| Historical data | DONE | IHistoricalDataManager, gap detection, CSV import, DataManagerController, DataFeedHealthMonitor |
| Options engines | DONE | IBlackScholesEngine; IOptionLegSelector (delta, FromStrike anchor); IOrderManager; IOptionIvRankService; BS-2 atmIv |
| Multi-leg spreads | DONE | SpreadLeg/SpreadSignalResult; ISpreadOrderManager; SpreadPosition; SO-1/SO-2/SO-3 |
| Execution engines | DONE | 8-gate live; SEQ-1 spread; SEQ-2 fwd always runs; ForwardTestSpreadState; IPaperOrderSimulator |
| Broker integrations | PARTIAL | Polly 3× retry+CB; RedisEncryptedTokenStore AES-256-GCM; BrokerRequired=false offline mode; LocalAuth JWT |
| Candle pipeline | PARTIAL | INSERT ON CONFLICT; BulkInsert Npgsql COPY binary; partial index is_closed=true |
| Scheduling | DONE | HangfireJobRegistry; 9 recurring jobs; StartupOrchestrator 11-step; IST session windows |
| Risk/sizing/stops | DONE | 5 position models (Kelly PS-1/PS-2); 6 slippage models; PortfolioRiskManager 6 controls+kill-switch; 7 stop types; ScalingManager |
| Approval Gate | DONE | strategy_approvals table; IApprovalService (CAGR/DD/fwd checks); LiveExecutionEngine guard; ApprovalDrawer |
| UI workflow | PARTIAL | 6-tier RBAC; GuidedDashboard; PayoffChart; StrategyLabPage wizard; Screener/News/Correlation/Risk pages; ByStrategy chart |
| Strategy UI (PROMPT-001/002) | DONE | StrategiesPage 4-tab (Deployments removed); ScenariosTab: lifecycle actions (Backtest/View Backtest/→Fwd Test/Deploy Live stubs), instruments+timeframe stacked in row, backtest wording clarified; 401 cached-data fallback in BacktestJobManager |
| Generic UI strategies | DONE | migrations 036-038; IStrategyDefinitionService; GenericRulesConfig; 6 option indicators; SpreadEntry 10 types |
| MCP/P8 | DONE | /mcp/strategy-status, /mcp/backtest-results/{id}, /mcp/kill-switch (RiskManager policy); JWT |
| P9 Screener | DONE | ScreenerService SQL CTE; GET /api/screener; ScreenerJob 5PM IST; ScreenerPage |
| P9 News | DONE | migration 039; NewsService CRUD; NewsController; NewsPage feed+create |
| P9 Events/Analytics | DONE | FnoExpirySeedJob; PnlAttributionDto.ByStrategy; PortfolioAnalysisPage date filter |
| Master data | PARTIAL | MStock parsing fixed; DB columns (003); instrument seeding |
| Tests | DONE | 241 passing (117 unit + 88 legacy + 14 arch + 22 frontend Vitest). Integration+E2E test projects compile and are wired in run-tests.sh; need Docker/browser to execute |
| listRuns wired | DONE | GET /api/strategy-definitions/{id}/backtests; GetByDefinitionAsync joins backtest_runs→strategy_definition_scenarios; backtestResultToRunResult mapper; MOCK_RUNS removed |
| Trade charts fixed | DONE | ResultsTab.TradeAnalysisSection simplified to real-only path; chartTrades mapped from BacktestTradeResult (MAE/MFE/R/exitReason) so scatter/histogram/heatmap/streak charts render for real runs |
| Backtest trade analysis | DONE | BacktestTradeDto all fields populated (StopLoss/TP/HoldingBars/RMultiple/TotalCost/Slippage); TradesTable 17-col sortable+help tooltips; ExitReason 'Strategy' category; IST heatmap; ChartSample persisted+loaded; ScenarioId upsert (re-run overwrites); SignalR JsonStringEnumConverter; scenario status uses IStrategyDefinitionScenarioService |
| PROMPT-011/012/013 | DONE | SCN-1/OCS-1 Npgsql 9 datetime fixes; AC-1 AppConfigService write-through→app_config+DB fallback; SDP-1 SymbolDataPrefs DB-backed; CAP-1/CAP-2 AllocateCapital/DeallocateCapital handlers now persist via ICapitalAllocationRepository; DEAD-1 SetAppConfigHandler→IAppConfigService; SDP-2 IClock in BuildDefault |
| Alert rules persistence | DONE | MonitoringAlertRule entity+EF config; IAlertRulesRepository+EfAlertRulesRepository; DbSet wired; Create/Delete/Get handlers use real DB; AlertsController return type updated |
| MarketCalendar 2026 | DONE | NSE holidays 2026 added to MarketCalendarService static set (Republic Day through Christmas) |
| CancelOrder wired | DONE | CancelOrderCommandHandler calls IBrokerClientFactory+CancelOrderAsync; MarkCancelled via GetByBrokerOrderIdAsync; IOrderRepository.GetByBrokerOrderIdAsync added |
| app_config schema | DONE | Migration 041 adds value_json+actor columns (040 was a CREATE TABLE IF NOT EXISTS no-op); backfills to_json(value); SeedAppConfigAsync updated to insert value_json; repair check added |
| AP-001 fixes | DONE | IClock injected everywhere; zero DateTime.Now/UtcNow/SystemClock.Instance left outside DI registrations; covers OrderManager, BrokerSessionManager(both), BacktestService, BacktestJobManager, InMemoryIdempotency/KillSwitch/BrokerSession, PositionReconciliation, RedisEncryptedTokenStore, AuthController, McpController, ZerodhaClient, MStockClient, UpstoxClient, Decorators.ReconnectingBrokerStreamClient |

## Update rule: revise affected rows only; do not mark DONE without code support.
