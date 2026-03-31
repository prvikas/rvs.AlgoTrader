import axios from 'axios'
import { useAppStore } from '../stores/appStore'

// All timestamps from API are UTC ISO strings — display in IST (UTC+5:30)
export const apiClient = axios.create({
  baseURL: '/api',
  headers: { 'Content-Type': 'application/json' },
})

// Attach JWT token from Zustand store (persisted across sessions)
apiClient.interceptors.request.use(config => {
  const token = useAppStore.getState().jwtToken
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

apiClient.interceptors.response.use(
  res => res,
  err => {
    if (err.response?.status === 401) {
      // Clear the token in the Zustand store (also clears persisted localStorage entry)
      useAppStore.getState().setJwtToken(null)
      window.location.href = '/login'
    }
    return Promise.reject(err)
  }
)

export interface ApiResponse<T> {
  success: boolean
  data?: T
  error?: string
}

// Orders
export const ordersApi = {
  list: (params?: { broker?: string; status?: string }) =>
    apiClient.get<ApiResponse<Order[]>>('/orders', { params }),
  place: (cmd: PlaceOrderCommand) =>
    apiClient.post<ApiResponse<string>>('/orders', cmd),
  cancel: (brokerId: string, broker: string) =>
    apiClient.delete<ApiResponse<boolean>>(`/orders/${brokerId}`, { params: { broker } }),
}

// Strategies
export const strategiesApi = {
  list: () => apiClient.get<ApiResponse<StrategyInstance[]>>('/strategies'),
  get: (id: string) => apiClient.get<ApiResponse<StrategyInstance>>(`/strategies/${id}`),
  create: (cmd: CreateStrategyCommand) =>
    apiClient.post<ApiResponse<string>>('/strategies', cmd),
  start: (id: string) => apiClient.post<ApiResponse<string>>(`/strategies/${id}/start`),
  pause: (id: string, reason: string) =>
    apiClient.post<ApiResponse<boolean>>(`/strategies/${id}/pause`, { instanceId: id, reason }),
  stop: (id: string, reason: string) =>
    apiClient.post<ApiResponse<boolean>>(`/strategies/${id}/stop`, { instanceId: id, reason }),
  signals: (id: string, limit = 100) =>
    apiClient.get<ApiResponse<SignalJournalEntry[]>>(`/strategies/${id}/signals`, { params: { limit } }),
  update: (id: string, cmd: Partial<CreateStrategyCommand>) =>
    apiClient.put<ApiResponse<boolean>>(`/strategies/${id}`, cmd),
  delete: (id: string) => apiClient.delete<ApiResponse<boolean>>(`/strategies/${id}`),
  /** Fetches the full parameter schema for a strategy (single source of truth on backend). */
  getSchema: (strategyName: string) =>
    apiClient.get<ApiResponse<StrategyParamDef[]>>(`/strategies/schema/${encodeURIComponent(strategyName)}`),
  /** Returns all registered strategy names. */
  getRegisteredNames: () =>
    apiClient.get<ApiResponse<string[]>>('/strategies/registered'),
}

// Kill switch
export const killSwitchApi = {
  status: () => apiClient.get<ApiResponse<boolean>>('/kill-switch/status'),
  activate: (reason: string) =>
    apiClient.post<ApiResponse<boolean>>('/kill-switch/activate', { reason }),
  deactivate: (reason: string) =>
    apiClient.post<ApiResponse<boolean>>('/kill-switch/deactivate', { reason }),
}

// Broker
export const brokerApi = {
  status: () => apiClient.get<ApiResponse<BrokerStatus[]>>('/broker/status'),
  latency: () => apiClient.get<ApiResponse<BrokerLatency[]>>('/broker/latency'),
  funds: (brokerName: string) =>
    apiClient.get<ApiResponse<BrokerFunds>>(`/broker/${brokerName}/funds`),
  positions: (brokerName: string) =>
    apiClient.get<ApiResponse<BrokerPosition[]>>(`/broker/${brokerName}/positions`),
  // Auth flows
  mstockLogin: (apiKey: string, clientCode: string, password: string, totp: string) =>
    apiClient.post<ApiResponse<BrokerAuthResult>>('/broker/mstock/login', { apiKey, clientCode, password, totp }),
  zerodhaLoginUrl: () =>
    apiClient.get<ApiResponse<string>>('/broker/zerodha/login-url'),
  zerodhaCallback: (requestToken: string) =>
    apiClient.post<ApiResponse<BrokerAuthResult>>('/broker/zerodha/callback', { requestToken }),
  upstoxLoginUrl: () =>
    apiClient.get<ApiResponse<string>>('/broker/upstox/login-url'),
  upstoxCallback: (authCode: string) =>
    apiClient.post<ApiResponse<BrokerAuthResult>>('/broker/upstox/callback', { authCode }),
}

// Instruments
export interface InstrumentsListParams {
  search?: string
  exchange?: string
  instrumentType?: string
  active?: boolean
  sortBy?: string    // "symbol" | "name" | "exchange" | "type" | "trading"
  sortDir?: string   // "asc" | "desc"
  page?: number
  pageSize?: number
}

// ── Refresh preview / commit DTOs ─────────────────────────────────────────

export interface TypeBucketRow {
  /** "Equity" | "Futures" | "Options" | "Index" | "Other" */
  bucket: string
  count: number
  /** Raw broker type codes inside this bucket, e.g. ["EQ","BE"] or ["FUT","FUTIDX"] */
  typeCodes: string[]
}

export interface ExchangePreviewGroup {
  exchange: string
  total: number
  types: TypeBucketRow[]
}

export interface CategoryPreviewRow {
  category: string   // e.g. "LARGE_CAP"
  label: string      // e.g. "Large-cap"
  matchCount: number // how many downloaded equity symbols are in this category
}

export interface RefreshPreviewDto {
  stagingToken: string
  brokerName: string
  totalDownloaded: number
  stagedAt: string         // ISO UTC
  expiresInMinutes: number
  exchanges: ExchangePreviewGroup[]
  equityCategories: CategoryPreviewRow[]
}

export interface RefreshCommitRequest {
  stagingToken: string
  includedExchanges: string[]
  /** "Equity" | "Futures" | "Options" | "Index" */
  includedInstrumentTypes: string[]
  /** Universe category codes, e.g. ["NSE_EQUITY","LARGE_CAP"] */
  includedEquityCategories: string[]
}

export interface RefreshCommitResult {
  brokerName: string
  saved: number
  skipped: number
  newCount: number
  updatedCount: number
}

// ──────────────────────────────────────────────────────────────────────────

export const instrumentsApi = {
  list: (params?: InstrumentsListParams) =>
    apiClient.get<ApiResponse<PagedResult<Instrument>>>('/instruments', { params }),
  search: (query: string) =>
    apiClient.get<ApiResponse<PagedResult<Instrument>>>('/instruments', {
      params: { search: query, pageSize: 20 },
    }),
  /** Legacy / scheduled-job path: download + immediately save using stored filter config. */
  refresh: (brokerName = 'all') =>
    apiClient.post<ApiResponse<boolean>>(`/instruments/refresh?brokerName=${brokerName}`),

  /**
   * Wizard Step 1 — download instruments from the broker, stage them in memory,
   * and return a preview with counts so the user can decide what to save.
   */
  preview: (brokerName: string) =>
    apiClient.post<ApiResponse<RefreshPreviewDto>>(`/instruments/preview?brokerName=${encodeURIComponent(brokerName)}`),

  /**
   * Wizard Step 2 — apply the user's filter selections to the staged data and write to the DB.
   */
  commit: (req: RefreshCommitRequest) =>
    apiClient.post<ApiResponse<RefreshCommitResult>>('/instruments/commit', req),
}

// Historical Data
export const historicalApi = {
  /** Triggers a Hangfire job to download candle history for one instrument.
   *  fromDate format: "YYYY-MM-DD". Returns a job ID or status message. */
  downloadHistory: (internalSymbol: string, timeframe: string, fromDate: string) =>
    apiClient.post<ApiResponse<string>>('/historical/download', { internalSymbol, timeframe, fromDate }),
  /** List recent download jobs and their status. */
  listJobs: () =>
    apiClient.get<ApiResponse<HistoricalDownloadJob[]>>('/historical/jobs'),
}

export interface HistoricalDownloadJob {
  id: string
  internalSymbol: string
  timeframe: string
  fromDate: string
  status: string   // "Pending" | "InProgress" | "Succeeded" | "Failed"
  createdAt: string
  completedAt?: string
  error?: string
}

// Backtest
export const backtestApi = {
  /** Synchronous run — blocks until complete. Use `start` for long backtests. */
  run: (req: BacktestRequest) =>
    apiClient.post<ApiResponse<BacktestResult>>('/backtest/run', req),
  /** Async run — returns jobId immediately (202). Subscribe to /hubs/backtest or poll status. */
  start: (req: BacktestRequest) =>
    apiClient.post<ApiResponse<{ jobId: string }>>('/backtest/start', req),
  /** Poll progress/result of an async backtest job. */
  status: (jobId: string) =>
    apiClient.get<ApiResponse<BacktestJobStatus>>(`/backtest/${jobId}/status`),
  /** Cancel a running job. */
  cancel: (jobId: string) =>
    apiClient.post<ApiResponse<unknown>>(`/backtest/${jobId}/cancel`),
  /** List all active (Queued/Running) job IDs. */
  activeJobs: () =>
    apiClient.get<ApiResponse<string[]>>('/backtest/active'),
  /** List saved backtest runs from DB (paged). Primary source for the Previous Runs panel. */
  list: (strategyName?: string, page = 1, pageSize = 50) =>
    apiClient.get<ApiResponse<PagedResult<BacktestResult>>>('/backtest', { params: { strategyName, page, pageSize } }),
  /** Fetch a single saved result by ID (includes chartSample). */
  get: (id: string) =>
    apiClient.get<ApiResponse<BacktestResult>>(`/backtest/${id}`),
  report: (id: string) =>
    apiClient.get(`/backtest/${id}/report`, { responseType: 'blob' }),
}

// --- Type Definitions ---
export interface Order {
  id: string
  brokerName: string
  brokerOrderId?: string
  internalSymbol: string
  orderType: string
  direction: string
  status: string
  quantity: number
  filledQuantity: number
  price?: number
  fillPrice?: number
  placedAt: string  // UTC ISO
  filledAt?: string // UTC ISO
}

export interface StrategyInstance {
  id: string
  name: string
  strategyType: string    // maps to StrategyInstanceDto.StrategyType (e.g. "PriceActionBreakout")
  internalSymbol: string
  timeframe: string
  mode: string            // maps to StrategyInstanceDto.Mode (e.g. "Forward", "Live")
  brokerName: string
  status: string
  allocatedCapital?: number
  parametersJson?: string
  createdAt: string
  /** Today's realised P&L for this instance (net of brokerage + taxes). */
  todayRealizedPnl?: number
  /** Current mark-to-market unrealised P&L on open positions. */
  todayUnrealizedPnl?: number
  /** Number of currently open positions held by this instance. */
  openPositionCount?: number
}

export interface SignalJournalEntry {
  id: string
  strategyName: string
  internalSymbol: string
  timeframe: string
  signal: string
  entryPrice?: number
  stopLoss?: number
  takeProfit?: number
  reason?: string
  occurredAt: string
}

export interface BrokerAuthResult {
  success: boolean
  brokerName: string
  message?: string
  expiresAt?: string
}

export interface BrokerStatus {
  brokerName: string
  isConnected: boolean
  isAuthenticated: boolean
  lastHeartbeatAt?: string       // matches BrokerConnectionStatusDto.LastHeartbeatAt
  reconnectAttempts?: number
  lastDisconnectReason?: string
  sessionExpiresAt?: string
  lastCheckedAt?: string         // alias for display fallback
}

export interface BrokerLatency {
  brokerName: string
  p50Ms: number
  p95Ms: number
  p99Ms: number
  sampleCount: number
  measuredAt: string
}

export interface BrokerFunds {
  brokerName: string
  availableBalance: number
  usedMargin: number
  totalBalance: number
  fetchedAt: string
}

export interface BrokerPosition {
  brokerName: string
  internalSymbol: string
  quantity: number
  averagePrice: number
  lastPrice: number
  pnl: number
  productType: string
}

/** 0=NextBarOpen (default), 1=NextBarOpenPlusSlippage, 2=SignalBarClose */
export type FillModel = 0 | 1 | 2

export interface BacktestRequest {
  strategyName: string
  parametersJson: string
  internalSymbol: string
  timeframe: string
  fromDate: string
  toDate: string
  initialCapital: number
  riskPerTradePercent?: number
  /** Fill model. Default = 0 (NextBarOpen). */
  fillModel?: FillModel
  /** Slippage in basis points applied when fillModel = 1. Default = 5 bps. */
  slippageBasisPoints?: number
  /** Flat brokerage per order leg in INR. Default = 20 (Zerodha/Upstox model). */
  brokerageFlatPerSide?: number
  /** Trailing stop: R-multiple gain required before trail activates. 0 = disabled. */
  trailActivationR?: number
  /** Trailing stop: offset behind best price in R multiples. Default = 0.5. */
  trailOffsetR?: number
  /** Slide SL to break-even once 1R is gained. Default = false. */
  breakEvenAt1R?: boolean
  /** Stop backtest when equity < initialCapital × circuitBreakerPct. 0 = disabled. Default = 0.5 (50%). */
  circuitBreakerPct?: number
}

export interface BacktestMonthlyBreakdown {
  year: number
  month: number
  pnl: number
  trades: number
  winRate: number
}

export interface BacktestYearlyBreakdown {
  year: number
  pnl: number
  return: number
  trades: number
  winRate: number
}

export interface BacktestResult {
  id?: string
  success: boolean
  strategyName: string
  symbol: string
  timeframe: string
  fromDate?: string
  toDate?: string
  initialCapital?: number
  finalEquity?: number
  totalPnl: number
  totalReturn: number
  maxDrawdown: number
  sharpeRatio: number
  calmarRatio?: number
  profitFactor?: number
  winRate: number
  totalTrades: number
  winCount?: number
  lossCount?: number
  avgWin?: number
  avgLoss?: number
  maxConsecutiveLosses?: number
  expectancyPerTrade?: number
  // Extended stats
  sortinoRatio?: number
  dailySharpe?: number
  monthlySharpe?: number
  monthlyWinRate?: number
  drawdownRecoveryBars?: number
  maxLots?: number
  dataHash?: string
  startedAt?: string   // UTC ISO — display in IST
  trades?: BacktestTradeResult[]
  monthlyBreakdown?: BacktestMonthlyBreakdown[]
  yearlyBreakdown?: BacktestYearlyBreakdown[]
  // Downsampled (≤ 2000 bars) candlestick + indicator data for the full replay chart
  chartSample?: BacktestChartBar[]
  error?: string
  /** True when the backtest was stopped early by the circuit breaker. */
  circuitBreakerHit?: boolean
  /** Human-readable reason for the circuit breaker trigger. */
  circuitBreakerReason?: string
}

export interface BacktestTradeResult {
  direction: string
  entryPrice: number
  exitPrice: number
  quantity: number
  exitReason: string
  grossPnl: number
  netPnl: number
  entryTime: string  // UTC ISO
  exitTime: string   // UTC ISO
}

/**
 * A single OHLCV bar with optional indicator values and signal marker.
 * Received during the run (BacktestChartUpdate SignalR event, rolling 200 bars)
 * and after completion (BacktestResult.chartSample, downsampled ≤ 2000 bars).
 */
export interface BacktestChartBar {
  timeMs: number                          // Unix epoch milliseconds
  open: number
  high: number
  low: number
  close: number
  volume: number
  signal?: 'BUY' | 'SELL' | null         // trade signal on this bar
  signalPrice?: number
  stopLoss?: number
  takeProfit?: number
  indicators?: Record<string, number>     // e.g. { ema5: 452.3, vwap: 453.1 }
}

/** Status of an async backtest job (from GET /backtest/{jobId}/status). */
export interface BacktestJobStatus {
  jobId: string
  status: 'Queued' | 'Downloading' | 'Running' | 'Completed' | 'Failed' | 'Cancelled'
  progressPct: number    // 0–100
  currentBar: number
  totalBars: number
  tradesSoFar: number
  currentEquity: number
  error?: string
  result?: BacktestResult
}

// ── Scenarios ─────────────────────────────────────────────────────────────────

export interface StrategyScenario {
  id: string
  strategyInstanceId: string
  name: string
  description?: string
  /** Partial JSON — only keys that differ from the strategy's base params, e.g. {"LookbackBars":30}. Use as a diff label; never pass directly to the engine. */
  parametersJsonOverride?: string
  /** Fully-merged parameters (base + override applied). This is what the backtest/forward-test engine actually uses. */
  effectiveParametersJson: string
  /** Capital allocated to this scenario. If null, the strategy-level capital is used. */
  allocatedCapital?: number
  status: 'Draft' | 'Backtested' | 'ForwardTest' | 'Live' | 'Archived'
  lastBacktestRunId?: string
  createdAt: string
  updatedAt: string
}

export interface ScenarioComparisonRow {
  scenarioId: string
  scenarioName: string
  /** Fully-merged parameters this scenario uses (base + override applied). */
  effectiveParametersJson: string
  allocatedCapital?: number
  totalReturn?: number
  maxDrawdown?: number
  sharpeRatio?: number
  winRate?: number
  totalTrades?: number
  profitFactor?: number
  expectancyPerTrade?: number
  status: string
  lastBacktestRunId?: string
}

export interface ScenarioJobResult {
  scenarioId: string
  jobId: string
}

export interface CreateScenarioRequest {
  name: string
  description?: string
  parametersJsonOverride?: string
  allocatedCapital?: number
}

export interface UpdateScenarioRequest {
  name?: string
  description?: string
  parametersJsonOverride?: string
  allocatedCapital?: number
}

export interface RunScenariosRequest {
  internalSymbol: string
  timeframe: string
  fromDate: string
  toDate: string
  initialCapital?: number
  riskPerTradePercent?: number
  scenarioIds?: string[]
}

export const scenariosApi = {
  list: (instanceId: string) =>
    apiClient.get<ApiResponse<StrategyScenario[]>>(`/strategies/${instanceId}/scenarios`),
  get: (instanceId: string, scenarioId: string) =>
    apiClient.get<ApiResponse<StrategyScenario>>(`/strategies/${instanceId}/scenarios/${scenarioId}`),
  compare: (instanceId: string) =>
    apiClient.get<ApiResponse<ScenarioComparisonRow[]>>(`/strategies/${instanceId}/scenarios/compare`),
  create: (instanceId: string, req: CreateScenarioRequest) =>
    apiClient.post<ApiResponse<string>>(`/strategies/${instanceId}/scenarios`, req),
  update: (instanceId: string, scenarioId: string, req: UpdateScenarioRequest) =>
    apiClient.put<ApiResponse<boolean>>(`/strategies/${instanceId}/scenarios/${scenarioId}`, req),
  delete: (instanceId: string, scenarioId: string) =>
    apiClient.delete<ApiResponse<boolean>>(`/strategies/${instanceId}/scenarios/${scenarioId}`),
  promote: (instanceId: string, scenarioId: string, targetStatus: string) =>
    apiClient.post<ApiResponse<boolean>>(
      `/strategies/${instanceId}/scenarios/${scenarioId}/promote`,
      { targetStatus }),
  run: (instanceId: string, req: RunScenariosRequest) =>
    apiClient.post<ApiResponse<ScenarioJobResult[]>>(`/strategies/${instanceId}/scenarios/run`, req),
}

// Forward Test
export const forwardTestApi = {
  list: () => apiClient.get<ApiResponse<ForwardTestSession[]>>('/forward-test'),
  get: (id: string) => apiClient.get<ApiResponse<ForwardTestSession>>(`/forward-test/${id}`),
  /** @deprecated use promoteToLive instead */
  promote: (id: string) => apiClient.post<ApiResponse<boolean>>(`/forward-test/${id}/promote`),
  promoteFromBacktest: (req: PromoteFromBacktestRequest) =>
    apiClient.post<ApiResponse<string>>('/forward-test/from-backtest', req),
  promoteToLive: (instanceId: string, req: PromoteToLiveRequest) =>
    apiClient.post<ApiResponse<PromoteToLiveResult>>(`/forward-test/${instanceId}/promote-to-live`, req),
}

export interface BacktestSnapshot {
  backtestId: string
  totalPnl: number
  totalReturn: number
  winRate: number
  maxDrawdown: number
  sharpeRatio: number
  expectancyPerTrade: number
  totalTrades: number
}

export interface ForwardTestSession {
  instanceId: string
  instanceName: string
  strategyType: string
  internalSymbol: string
  timeframe: string
  brokerName?: string
  status: string
  startedAt: string
  endedAt?: string
  initialCapital: number
  currentEquity: number
  totalPnl: number
  totalReturn: number
  maxDrawdown: number
  sharpeRatio: number
  winRate: number
  totalTrades: number
  openPositionCount: number
  sourceBacktestId?: string
  sourceBacktest?: BacktestSnapshot    // present if promoted from a backtest
  equityCurvePoints?: EquityCurvePoint[]
}

export interface EquityCurvePoint {
  time: string   // UTC ISO
  equity: number
  pnl: number
}

export interface PromoteFromBacktestRequest {
  backtestId: string
  instanceName: string
  brokerName: string
  initialCapital: number
  scheduleJson?: string
}

export interface PromoteToLiveRequest {
  brokerName: string
  allocatedCapital: number
  scheduleJson?: string
}

export interface PreFlightCheck {
  name: string
  passed: boolean
  reason?: string
}

export interface PromoteToLiveResult {
  success: boolean
  newStrategyInstanceId?: string
  checks: PreFlightCheck[]
  error?: string
}

// Portfolio
export const portfolioApi = {
  summary: () => apiClient.get<ApiResponse<PortfolioSummary>>('/portfolio/summary'),
}

export interface StrategyPnlRow {
  instanceId: string
  name: string
  strategyType: string
  internalSymbol: string
  mode: string
  status: string
  allocatedCapital: number
  todayRealizedPnl: number
  todayUnrealizedPnl: number
  todayTotalPnl: number
  pnlPercent: number
}

export interface PortfolioSummary {
  todayTotalRealizedPnl: number
  todayTotalUnrealizedPnl: number
  todayTotalPnl: number
  totalAllocatedCapital: number
  runningCount: number
  pausedCount: number
  stoppedCount: number
  forwardTestCount: number
  byStrategy: StrategyPnlRow[]
}

// Notification Settings
// ── Instrument Universe ────────────────────────────────────────────────────

export interface InstrumentUniverseEntry {
  id: string
  symbol: string
  exchange: string
  /** NSE_EQUITY | OPTIONS_UNDERLYING */
  category: string
  isActive: boolean
  createdAt: string
}

export interface CreateUniverseEntryRequest {
  symbol: string
  exchange: string
  category: string
}

export interface UpdateUniverseEntryRequest {
  symbol?: string
  exchange?: string
  category?: string
  isActive?: boolean
}

export const universeApi = {
  list: (params?: { category?: string; active?: boolean; page?: number; pageSize?: number }) =>
    apiClient.get<ApiResponse<PagedResult<InstrumentUniverseEntry>>>('/universe', { params }),

  create: (req: CreateUniverseEntryRequest) =>
    apiClient.post<ApiResponse<InstrumentUniverseEntry>>('/universe', req),

  update: (id: string, req: UpdateUniverseEntryRequest) =>
    apiClient.put<ApiResponse<InstrumentUniverseEntry>>(`/universe/${id}`, req),

  delete: (id: string) =>
    apiClient.delete<ApiResponse<boolean>>(`/universe/${id}`),

  seedDefaults: () =>
    apiClient.post<ApiResponse<number>>('/universe/seed-defaults'),
}

export const instrumentTypesApi = {
  getFuturesTypes: () =>
    apiClient.get<ApiResponse<string>>('/instrument-types/futures'),

  getOptionsTypes: () =>
    apiClient.get<ApiResponse<string>>('/instrument-types/options'),

  updateFuturesTypes: (types: string) =>
    apiClient.put<ApiResponse<string>>('/instrument-types/futures', { types }),

  updateOptionsTypes: (types: string) =>
    apiClient.put<ApiResponse<string>>('/instrument-types/options', { types }),

  resetDefaults: () =>
    apiClient.post<ApiResponse<string>>('/instrument-types/reset-defaults'),
}

// ── Refresh Filters ────────────────────────────────────────────────────────

export interface RefreshFiltersDto {
  /** Comma-separated exchanges to include, e.g. "NSE,NFO" */
  includedExchanges: string
  /** Comma-separated instrument types to include, e.g. "Equity,Futures,Options,Index" */
  includedInstrumentTypes: string
  /** Comma-separated equity universe categories to include, e.g. "NSE_EQUITY" */
  includedEquityCategories: string
  /** All known exchange values (for checkboxes) */
  knownExchanges: string[]
  /** All known instrument type values (for checkboxes) */
  knownInstrumentTypes: string[]
  /** All known equity category values (for checkboxes) */
  knownEquityCategories: string[]
}

export interface UpdateRefreshFiltersRequest {
  includedExchanges?: string
  includedInstrumentTypes?: string
  includedEquityCategories?: string
}

export const refreshFiltersApi = {
  get: () =>
    apiClient.get<ApiResponse<RefreshFiltersDto>>('/refresh-filters'),

  update: (req: UpdateRefreshFiltersRequest) =>
    apiClient.put<ApiResponse<RefreshFiltersDto>>('/refresh-filters', req),

  resetDefaults: () =>
    apiClient.post<ApiResponse<RefreshFiltersDto>>('/refresh-filters/reset-defaults'),
}

export const settingsApi = {
  getNotifications: () =>
    apiClient.get<ApiResponse<NotificationSettings>>('/settings/notifications'),
  updateNotifications: (req: UpdateNotificationSettings) =>
    apiClient.put<ApiResponse<boolean>>('/settings/notifications', req),
}

export interface NotificationSettings {
  telegramBotTokenMasked?: string
  telegramChatId?: string
  maxDailyDrawdownPct: number
  alertsEnabled?: boolean
}

export interface UpdateNotificationSettings {
  telegramBotToken?: string
  telegramChatId?: string
  maxDailyDrawdownPct?: number
  alertsEnabled?: boolean
}

export interface Instrument {
  id: string
  internalSymbol: string
  tradingSymbol: string
  name: string
  exchange: string
  instrumentType: string
  underlying?: string
  strikePrice?: number
  optionType?: string
  expiry?: string
  lotSize: number
  tickSize: number
  isActive: boolean
  /** Broker-native tokens keyed by broker name. Only brokers that have been refreshed appear here.
   *  e.g. { "Zerodha": "738561", "Upstox": "NSE_EQ|INE002A01018", "MStock": "3045" }
   *  Use `instrument.brokerTokens["Zerodha"]` to get the token for a specific broker.
   */
  brokerTokens: Record<string, string>
}

export interface PagedResult<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
}

export interface PlaceOrderCommand {
  brokerName: string
  internalSymbol: string
  brokerToken: string
  orderType: string
  direction: string
  quantity: number
  price?: number
  exchange: string
  productType: string
}

export interface CreateStrategyCommand {
  name: string
  strategyType: string      // maps to backend CreateStrategyInstanceCommand.StrategyType
  internalSymbol: string
  timeframe: string
  brokerName: string
  mode: string
  parametersJson: string
  scheduleJson?: string     // JSON-serialised ScheduleConfig — session times, days, auto-resume, etc.
  failureBehaviorJson?: string  // JSON-serialised FailureBehaviorConfig
  allocatedCapital?: number
}

// ── Strategy Parameter Schema ────────────────────────────────────────────────
// These types mirror rvs.AlgoTrader.Domain.Interfaces.StrategyParamDef.
// The backend is the single source of truth — fetched via GET /strategies/schema/{name}.
// The frontend never hardcodes parameter definitions; it renders them dynamically.

export interface StrategyParamOption {
  value: string
  label: string
}

export interface StrategyParamDef {
  /** Property name on the backend Config class — used as the JSON key in parametersJson */
  key: string
  /** Human-readable label for the UI */
  label: string
  /** Input type: "int" | "decimal" | "bool" | "select" */
  type: 'int' | 'decimal' | 'bool' | 'select'
  /** Default value from the backend Config class */
  default: number | boolean | string | null
  min?: number
  max?: number
  step?: number
  hint?: string
  options?: StrategyParamOption[]
}
