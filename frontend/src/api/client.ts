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

// On 401: clear token and redirect to /login.
// Both broker mode and local-auth mode use explicit login — no silent re-auth needed.
apiClient.interceptors.response.use(
  res => res,
  err => {
    if (err.response?.status === 401) {
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
      params: { search: query, pageSize: 20, universeOnly: true },
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
  /**
   * Enqueues a Hangfire job to download candle history for one instrument.
   * Routes to POST /api/data-manager/download (DataManagerController).
   * fromDate / toDate format: "YYYY-MM-DD". brokerName: "MStock" | "Zerodha" | "Upstox".
   */
  downloadHistory: (
    internalSymbol: string,
    timeframe: string,
    fromDate: string,
    toDate: string,
    brokerName: string,
  ) =>
    apiClient.post<ApiResponse<string>>('/data-manager/download', {
      internalSymbol,
      timeframe,
      fromDate,
      toDate,
      brokerName,
    }),
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

// Data Manager — quality reports
export interface DataQualityReport {
  internalSymbol: string
  timeframe: string
  firstCandle?: string   // "YYYY-MM-DD"
  lastCandle?:  string   // "YYYY-MM-DD"
  totalCandles: number
  gapCount: number
  duplicateCount: number
  hasData: boolean
}

export const dataManagerApi = {
  /** Per-symbol quality report (firstCandle, lastCandle, gaps, duplicates). */
  quality: (symbol: string, timeframe = '1D') =>
    apiClient.get<ApiResponse<DataQualityReport>>(`/data-manager/quality/${encodeURIComponent(symbol)}/${timeframe}`),
  /** Quality summary for ALL symbols at a given timeframe — use for bulk table display. */
  qualityAll: (timeframe = '1D') =>
    apiClient.get<ApiResponse<DataQualityReport[]>>(`/data-manager/quality?timeframe=${timeframe}`),
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
  /** Fetch full per-trade breakdown for a saved run (entry/exit, brokerage, R, legs, etc.). */
  trades: (id: string) =>
    apiClient.get<ApiResponse<BacktestTradeResult[]>>(`/backtest/${id}/trades`),
  report: (id: string) =>
    apiClient.get(`/backtest/${id}/report`, { responseType: 'blob' }),
  /** All backtest runs for all scenarios of a strategy definition (Previous Runs panel). */
  byDefinition: (definitionId: string, page = 1, pageSize = 100) =>
    apiClient.get<ApiResponse<BacktestResult[]>>(
      `/strategy-definitions/${definitionId}/backtests`,
      { params: { page, pageSize } }),
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
  scheduleJson?: string
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
  /** GUID string — links this job to a strategy definition scenario so the backend
   *  auto-updates scenario.Status → Backtested and saves metrics on completion. */
  scenarioId?: string
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
  /** Strategy definition scenario that triggered this run, if any. */
  scenarioId?: string
}

export interface BacktestTradeLeg {
  strike:    number
  type:      'CE' | 'PE'
  direction: 'BUY' | 'SELL'
  premium:   number   // entry premium per contract
  expiry:    string   // "YYYY-MM-DD"
  brokerage: number   // flat brokerage for this leg
}

export interface BacktestTradeResult {
  direction:       string
  entryPrice:      number
  exitPrice:       number
  quantity:        number
  exitReason:      string
  grossPnl:        number
  netPnl:          number
  entryTime:       string  // UTC ISO
  exitTime:        string  // UTC ISO
  // ── Excursion ──────────────────────────────────────────────────────────
  mae?:            number  // Maximum Adverse Excursion (price units)
  mfe?:            number  // Maximum Favorable Excursion (price units)
  // ── Cost breakdown ─────────────────────────────────────────────────────
  entryCommission?: number
  exitCommission?:  number
  totalCost?:       number  // entryCommission + exitCommission
  slippageAmount?:  number  // ₹ slippage cost at entry
  // ── Risk levels ────────────────────────────────────────────────────────
  stopLoss?:        number  // initial stop-loss price
  takeProfit?:      number  // take-profit / target
  // ── Duration & R ───────────────────────────────────────────────────────
  holdingBars?:     number  // candle bars from entry to exit
  rMultiple?:       number  // NetPnl ÷ initial risk (R-multiple)
  // ── Spread legs ────────────────────────────────────────────────────────
  legsJson?:        string  // JSON array of BacktestTradeLeg; parse to show per-leg table
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
  /**
   * Logical section heading (e.g. "Trend Template", "Price Structure").
   * The frontend groups all params with the same section under one collapsible card.
   */
  section?: string
  /**
   * Key of the bool param that gates this param — the input is dimmed/hidden
   * when the referenced bool param is false.
   */
  enabledBy?: string
}

// ── Trade Journal ────────────────────────────────────────────────────────────

export interface TradeJournalEntry {
  id: string
  strategyInstanceId?: string
  sessionId?: string
  symbol: string
  direction: string
  quantity: number
  entryPrice: number
  exitPrice?: number
  entryTime: string
  exitTime?: string
  grossPnl?: number
  netPnl?: number
  initialRisk?: number
  rMultiple?: number
  mae?: number
  mfe?: number
  holdingDays?: number
  taxClassification?: string
  exitReason?: string
  entryReason?: string
  notes?: string
  tags?: string[]
  source?: string
  createdAt: string
}

export interface PnlByDimension {
  dimension: string
  label: string
  grossPnl: number
  netPnl: number
  tradeCount: number
  winRate: number
}

export interface PnlAttribution {
  bySymbol: PnlByDimension[]
  byMonth: PnlByDimension[]
  byDayOfWeek: PnlByDimension[]
  byExitType: PnlByDimension[]
  bySession?: PnlByDimension[]
  byStrategy?: PnlByDimension[]  // P9: cross-strategy breakdown, present when no strategyInstanceId filter
}

export interface TaxLotRow {
  symbol: string
  openDate: string
  closeDate: string
  quantity: number
  buyPrice: number
  sellPrice: number
  grossPnl: number
  classification: string
  holdingDays: number
}

export const tradeJournalApi = {
  list: (params?: { page?: number; pageSize?: number; symbol?: string; source?: string; from?: string; to?: string }) =>
    apiClient.get<ApiResponse<PagedResult<TradeJournalEntry>>>('/tradejournal', { params }),
  get: (id: string) =>
    apiClient.get<ApiResponse<TradeJournalEntry>>(`/tradejournal/${id}`),
  updateNotes: (id: string, notes: string, tags?: string[]) =>
    apiClient.patch<ApiResponse<boolean>>(`/tradejournal/${id}/notes`, { notes, tags }),
  getAttribution: (params?: { from?: string; to?: string; strategyInstanceId?: string }) =>
    apiClient.get<ApiResponse<PnlAttribution>>('/tradejournal/attribution', { params }),
  getTaxLots: (fy: string) =>
    apiClient.get<ApiResponse<TaxLotRow[]>>('/tradejournal/tax-lots', { params: { fy } }),
  exportTaxLots: (fy: string) =>
    apiClient.get(`/tradejournal/tax-lots/export`, { params: { fy }, responseType: 'blob' }),
}

// ── P4 Approval Gate ─────────────────────────────────────────────────────────

export interface ApprovalCheckResult {
  automatedChecksPassed: boolean
  cagr?: number
  drawdown?: number
  sharpe?: number
  forwardTestDays?: number
  forwardWinRate?: number
  failedChecks: string[]
}

export interface ApprovalStatus {
  id: string
  strategyInstanceId: string
  approvedBy: string
  approvalNotes?: string
  cagrAtApproval?: number
  drawdownAtApproval?: number
  sharpeAtApproval?: number
  forwardTestDays?: number
  forwardWinRate?: number
  automatedChecksPassed: boolean
  isActive: boolean
  invalidatedAt?: string
  invalidationReason?: string
  createdAt: string
}

export const approvalApi = {
  getChecks: (instanceId: string) =>
    apiClient.get<ApiResponse<ApprovalCheckResult>>(
      `/v1/strategy-instances/${instanceId}/approval-checks`),
  getStatus: (instanceId: string) =>
    apiClient.get<ApiResponse<ApprovalStatus | null>>(
      `/v1/strategy-instances/${instanceId}/approval-status`),
  getHistory: (instanceId: string) =>
    apiClient.get<ApiResponse<ApprovalStatus[]>>(
      `/v1/strategy-instances/${instanceId}/approval-history`),
  approve: (instanceId: string, notes?: string) =>
    apiClient.post<ApiResponse<ApprovalStatus>>(
      `/v1/strategy-instances/${instanceId}/approve`, { notes }),
  revoke: (instanceId: string, approvalId: string, reason: string) =>
    apiClient.post<ApiResponse<boolean>>(
      `/v1/strategy-instances/${instanceId}/approvals/${approvalId}/revoke`, { reason }),
}

// ── Correlation & Portfolio Construction (#95) ─────────────────────────────

export interface StrategyReturnSeries {
  strategyInstanceId: string
  strategyName: string
  dailyReturns: number[]   // fractional daily returns (0.01 = 1%), chronological
}

export interface HighCorrelationPair {
  strategyA: string
  strategyB: string
  correlation: number
}

export interface CorrelationMatrix {
  strategyNames: string[]
  coefficients: number[][]          // symmetric n×n — coefficients[i][j] = Pearson r
  highCorrelationPairs: HighCorrelationPair[]
}

export interface MonteCarloPortfolio {
  weights: Record<string, number>
  annualReturn: number
  annualVolatility: number
  sharpeRatio: number
}

export interface PortfolioConstructionResult {
  optimalWeights: Record<string, number>
  portfolioSharpe: number
  portfolioAnnualReturn: number
  portfolioAnnualVolatility: number
  maxDrawdown: number
  diversificationRatio: number
  efficientFrontierSamples: MonteCarloPortfolio[]
}

export const correlationApi = {
  matrix: (series: StrategyReturnSeries[], highCorrThreshold = 0.7) =>
    apiClient.post<ApiResponse<CorrelationMatrix>>('/correlation/matrix', {
      series, highCorrThreshold,
    }),
  portfolio: (series: StrategyReturnSeries[], portfolioCount = 10_000, riskFreeRate = 0.065) =>
    apiClient.post<ApiResponse<PortfolioConstructionResult>>('/correlation/portfolio', {
      series, portfolioCount, riskFreeRate,
    }),
  check: (candidateSeries: StrategyReturnSeries, liveStrategySeries: StrategyReturnSeries[], highCorrThreshold = 0.7) =>
    apiClient.post<ApiResponse<HighCorrelationPair[]>>('/correlation/check', {
      candidateSeries, liveStrategySeries, highCorrThreshold,
    }),
}

// ── Strategy domain API (PROMPT-001) ─────────────────────────────────────────
// Strategy CRUD calls the real /api/strategy-definitions backend.
// Strategy CRUD + Scenarios backed by real /api/strategy-definitions backend.
import type {
  Strategy, Scenario, Deployment, RunResult, ParameterSweep, TradeRecord,
} from '../types/strategy'
import {
  Timeframe, TradingStyle, StrategyStatus,
  ScenarioStatus, Broker, DeploymentMode, DeploymentStatus, OverrideSection,
  ExitCombineLogic, StopType, TrailingType, StartCondition, DayOfWeek, RunMode,
} from '../types/strategy'

// ── Backend DTO shape (mirrors Application/DTOs/Strategy/StrategyDefinitionDtos.cs) ──
interface StrategyDefinitionDto {
  id: string
  name: string
  description?: string
  tradingStyle: string
  definitionJson: string
  createdAt: string
  updatedAt: string
}

interface UpsertStrategyDefinitionRequest {
  name: string
  description?: string
  tradingStyle: string
  definitionJson: string
}

// ── Scenario DTO shape (mirrors StrategyDefinitionScenarioDtos.cs) ────────────
interface DefinitionScenarioDto {
  id: string
  strategyDefinitionId: string
  name: string
  description?: string
  capital: number
  brokerAccount: string
  backtestFrom: string   // "YYYY-MM-DD"
  backtestTo: string     // "YYYY-MM-DD"
  parameterOverridesJson: string
  status: string
  lastRunAt?: string
  lastMetricsJson?: string
  createdAt: string
  updatedAt: string
}

interface UpsertDefinitionScenarioRequest {
  name: string
  description?: string
  capital: number
  brokerAccount: string
  backtestFrom: string
  backtestTo: string
  parameterOverridesJson: string
  status?: string
}

interface PatchDefinitionScenarioRequest {
  status?: string
  lastMetricsJson?: string
  lastRunAt?: string
}

// ── Mapping helpers ────────────────────────────────────────────────────────────

function dtoToStrategy(dto: StrategyDefinitionDto): Strategy {
  let parsed: Partial<Strategy> = {}
  try { parsed = JSON.parse(dto.definitionJson) as Partial<Strategy> } catch { /* ignore */ }

  // Normalise array fields up-front — guards against null/missing values in stored JSON
  // that would crash list-rendering code accessing .[0] or .map().
  const instruments = Array.isArray(parsed.instruments) ? parsed.instruments : []
  const indicators  = Array.isArray(parsed.indicators)  ? parsed.indicators  : []
  // Normalise stopStateMachine.states — may be absent or non-array in old DB rows
  const stopStateMachine = parsed.stopStateMachine != null
    ? { ...parsed.stopStateMachine, states: Array.isArray(parsed.stopStateMachine.states) ? parsed.stopStateMachine.states : [] }
    : parsed.stopStateMachine

  return {
    // defaults for missing fields so form always has valid state
    primaryTimeframe: Timeframe.D1,
    status:           StrategyStatus.Draft,
    longEntry:        { enabled: true,  groupOperator: ExitCombineLogic.AND, groups: [] },
    shortEntry:       { enabled: false, groupOperator: ExitCombineLogic.AND, groups: [] },
    longExit:         { enabled: true,  groupOperator: ExitCombineLogic.OR,  groups: [] },
    shortExit:        { enabled: false, groupOperator: ExitCombineLogic.OR,  groups: [] },
    exitBehaviour: {
      exitEndOfSession: true, exitAfterNBars: null, exitAtStopOrTargetOnly: false,
      combineLogic: ExitCombineLogic.OR,
      tradableDays: [DayOfWeek.Mon, DayOfWeek.Tue, DayOfWeek.Wed, DayOfWeek.Thu, DayOfWeek.Fri],
      sessionStart: '09:15', sessionEnd: '15:20',
    },
    stopLoss:     { type: StopType.ATRMultiple, baseValue: 2, allowedRange: { min: 1, max: 4 } },
    profitTarget: { type: StopType.ATRMultiple, baseValue: 4, allowedRange: { min: 2, max: 8 } },
    rrConfig:     { rrMin: 2, rrMax: 4, deriveTargetFromSL: true },
    trailing: {
      enabled: false, trailingType: TrailingType.ATRMultiple,
      trailingParams: { multiplier: 1.5 }, startCondition: StartCondition.Immediately, partialExits: [],
    },
    riskControls: { maxRiskPerTradePercent: 1, maxTradesPerDay: 5 },
    // overlay parsed fields (these override the defaults above)
    ...parsed,
    // Re-assert normalised values — must come AFTER ...parsed so they win
    instruments,
    indicators,
    ...(stopStateMachine !== undefined ? { stopStateMachine } : {}),
    // top-level fields always come from the DB row
    id:           dto.id,
    name:         dto.name,
    description:  dto.description,
    tradingStyle: (dto.tradingStyle as TradingStyle) ?? parsed.tradingStyle ?? TradingStyle.Swing,
    createdAt:    dto.createdAt,
    updatedAt:    dto.updatedAt,
  }
}

function strategyToUpsertRequest(s: Omit<Strategy, 'id' | 'createdAt' | 'updatedAt'> | Strategy): UpsertStrategyDefinitionRequest {
  return {
    name:           s.name,
    description:    s.description,
    tradingStyle:   s.tradingStyle,
    definitionJson: JSON.stringify(s),
  }
}

function scenarioDtoToScenario(dto: DefinitionScenarioDto): Scenario {
  let overrides: import('../types/strategy').ParameterOverride[] = []
  let metrics: import('../types/strategy').RunMetrics | undefined
  try { overrides = JSON.parse(dto.parameterOverridesJson) } catch { /* ignore */ }
  try { if (dto.lastMetricsJson) metrics = JSON.parse(dto.lastMetricsJson) } catch { /* ignore */ }
  return {
    id:                 dto.id,
    strategyId:         dto.strategyDefinitionId,
    name:               dto.name,
    description:        dto.description,
    capital:            dto.capital,
    brokerAccount:      dto.brokerAccount as Broker,
    backtestRange:      { from: dto.backtestFrom, to: dto.backtestTo },
    parameterOverrides: overrides,
    status:             dto.status as ScenarioStatus,
    lastRunAt:          dto.lastRunAt,
    lastMetrics:        metrics,
  }
}

function scenarioToUpsertRequest(s: Omit<Scenario, 'id' | 'strategyId'>): UpsertDefinitionScenarioRequest {
  return {
    name:                   s.name,
    description:            s.description,
    capital:                s.capital,
    brokerAccount:          s.brokerAccount,
    backtestFrom:           s.backtestRange.from,
    backtestTo:             s.backtestRange.to,
    parameterOverridesJson: JSON.stringify(s.parameterOverrides ?? []),
    status:                 s.status,
  }
}

// ── In-memory stores for runs/trades (no backend yet) ────────

// ── Helper: map a real StrategyInstance to the Deployment UI shape ──────────
function instanceToDeployment(inst: StrategyInstance, strategyId: string): Deployment {
  const modeMap: Record<string, DeploymentMode> = {
    ForwardTest: DeploymentMode.ForwardTest,
    Live:        DeploymentMode.Live,
    Backtest:    DeploymentMode.Backtest,
  }
  const statusMap: Record<string, DeploymentStatus> = {
    Running:   DeploymentStatus.Running,
    Active:    DeploymentStatus.Running,
    Paused:    DeploymentStatus.FwdTesting,
    Scheduled: DeploymentStatus.FwdTesting,
    Draft:     DeploymentStatus.Draft,
    Stopped:   DeploymentStatus.Archived,
  }
  let schedule: Deployment['schedule'] = { days: [] }
  try {
    const s = JSON.parse(inst.scheduleJson ?? '{}')
    schedule = { days: s.days ?? [], startTime: s.startTime, endTime: s.endTime }
  } catch { /* use empty schedule */ }
  return {
    id:               inst.id,
    strategyId,
    scenarioId:       '',
    name:             inst.name,
    symbol:           inst.internalSymbol,
    timeframe:        inst.timeframe as Timeframe,
    mode:             modeMap[inst.mode] ?? DeploymentMode.ForwardTest,
    broker:           (inst.brokerName ?? 'MStock') as Broker,
    allocatedCapital: inst.allocatedCapital ?? 0,
    schedule,
    status:           statusMap[inst.status] ?? DeploymentStatus.Draft,
  }
}
// Maps a BacktestResult (API shape) to the RunResult UI shape used by ResultsTab / CompareTab.
// Backend stores rates as decimals (0.25 = 25%); RunMetrics expects percentage values.
function backtestResultToRunResult(dto: BacktestResult, strategyId: string): RunResult {
  return {
    id:            dto.id ?? '',
    scenarioId:    dto.scenarioId ?? '',
    strategyId,
    mode:          RunMode.Backtest,
    dateRange:     {
      from: dto.fromDate?.slice(0, 10) ?? '',
      to:   dto.toDate?.slice(0, 10)   ?? '',
    },
    engineVersion: '1',
    dataVersion:   dto.dataHash ?? '',
    completedAt:   dto.startedAt ?? new Date().toISOString(),
    metrics: {
      returnPct:      (dto.totalReturn  ?? 0) * 100,
      maxDrawdownPct: (dto.maxDrawdown  ?? 0) * 100,
      sharpe:          dto.sharpeRatio  ?? 0,
      winRate:        (dto.winRate      ?? 0) * 100,
      profitFactor:    dto.profitFactor ?? 0,
      tradeCount:      dto.totalTrades  ?? 0,
      avgRPerTrade:    0,
      expectancy:      dto.expectancyPerTrade ?? 0,
      avgRealisedRR:   0,
    },
  }
}

export const strategyDomainApi = {
  // ── Strategy CRUD — real /api/strategy-definitions backend ────────────────
  listStrategies: async (): Promise<Strategy[]> => {
    const resp = await apiClient.get<ApiResponse<StrategyDefinitionDto[]>>('/strategy-definitions')
    if (!resp.data?.success) {
      throw new Error(resp.data?.error ?? 'Failed to load strategies')
    }
    return (resp.data.data ?? []).map(dtoToStrategy)
  },

  getStrategy: async (id: string): Promise<Strategy | null> => {
    const resp = await apiClient.get<ApiResponse<StrategyDefinitionDto>>(`/strategy-definitions/${id}`)
    if (!resp.data?.success) {
      throw new Error(resp.data?.error ?? `Strategy ${id} not found`)
    }
    return resp.data.data ? dtoToStrategy(resp.data.data) : null
  },

  createStrategy: async (s: Omit<Strategy, 'id' | 'createdAt' | 'updatedAt'>): Promise<Strategy> => {
    const body: UpsertStrategyDefinitionRequest = strategyToUpsertRequest(s as Strategy)
    const resp = await apiClient.post<ApiResponse<StrategyDefinitionDto>>('/strategy-definitions', body)
    if (!resp.data?.success || !resp.data.data) {
      throw new Error(resp.data?.error ?? 'Failed to create strategy')
    }
    return dtoToStrategy(resp.data.data)
  },

  updateStrategy: async (id: string, s: Partial<Strategy>): Promise<Strategy> => {
    // Fetch existing definition to merge with partial update
    const existing = await strategyDomainApi.getStrategy(id)
    const merged = { ...(existing ?? {}), ...s } as Strategy
    const body: UpsertStrategyDefinitionRequest = strategyToUpsertRequest(merged)
    const resp = await apiClient.put<ApiResponse<StrategyDefinitionDto>>(`/strategy-definitions/${id}`, body)
    if (!resp.data?.success || !resp.data.data) {
      throw new Error(resp.data?.error ?? 'Failed to update strategy')
    }
    return dtoToStrategy(resp.data.data)
  },

  deleteStrategy: async (id: string): Promise<void> => {
    await apiClient.delete(`/strategy-definitions/${id}`)
  },

  listScenarios: async (strategyId: string): Promise<Scenario[]> => {
    const resp = await apiClient.get<ApiResponse<DefinitionScenarioDto[]>>(
      `/strategy-definitions/${strategyId}/scenarios`)
    if (!resp.data?.success) {
      throw new Error(resp.data?.error ?? 'Failed to load scenarios')
    }
    return (resp.data.data ?? []).map(scenarioDtoToScenario)
  },

  createScenario: async (strategyId: string, s: Omit<Scenario, 'id' | 'strategyId'>): Promise<Scenario> => {
    const body = scenarioToUpsertRequest(s)
    const resp = await apiClient.post<ApiResponse<DefinitionScenarioDto>>(
      `/strategy-definitions/${strategyId}/scenarios`, body)
    if (!resp.data?.success || !resp.data.data) {
      throw new Error(resp.data?.error ?? 'Failed to create scenario')
    }
    return scenarioDtoToScenario(resp.data.data)
  },

  updateScenario: async (strategyId: string, scenarioId: string, s: Partial<Scenario>): Promise<Scenario> => {
    // Fetch existing to merge with partial update
    const existingResp = await apiClient.get<ApiResponse<DefinitionScenarioDto>>(
      `/strategy-definitions/${strategyId}/scenarios/${scenarioId}`)
    if (!existingResp.data?.success || !existingResp.data.data) {
      throw new Error(existingResp.data?.error ?? 'Failed to load scenario for update')
    }
    const existing = scenarioDtoToScenario(existingResp.data.data)
    const merged = { ...existing, ...s }
    const body = scenarioToUpsertRequest(merged)
    const resp = await apiClient.put<ApiResponse<DefinitionScenarioDto>>(
      `/strategy-definitions/${strategyId}/scenarios/${scenarioId}`, body)
    if (!resp.data?.success || !resp.data.data) {
      throw new Error(resp.data?.error ?? 'Failed to update scenario')
    }
    return scenarioDtoToScenario(resp.data.data)
  },

  deleteScenario: async (strategyId: string, scenarioId: string): Promise<void> => {
    await apiClient.delete(`/strategy-definitions/${strategyId}/scenarios/${scenarioId}`)
  },

  runScenario: async (strategyId: string, scenarioId: string): Promise<{ jobId: string }> => {
    // Fetch scenario from real API
    const scenResp = await apiClient.get<ApiResponse<DefinitionScenarioDto>>(
      `/strategy-definitions/${strategyId}/scenarios/${scenarioId}`)
    const scenDto = scenResp.data?.data
    if (!scenDto) throw new Error(`Scenario ${scenarioId} not found`)
    const scenario = scenarioDtoToScenario(scenDto)

    // Fetch strategy definition from real API
    const resp = await apiClient.get<ApiResponse<StrategyDefinitionDto>>(`/strategy-definitions/${strategyId}`)
    const dto = resp.data?.data
    if (!dto) throw new Error(`Strategy definition ${strategyId} not found`)

    const parsed = (() => { try { return JSON.parse(dto.definitionJson) as Partial<Strategy> } catch { return {} } })()
    const instruments = (parsed.instruments ?? [])
    const symbol = instruments[0] ?? ''
    // Normalise timeframe to values the backend validator accepts.
    // Old DB rows may have stored '1D' or '1h' before the enum was corrected;
    // cast to string to allow the dead-branch comparisons that handle those rows.
    const rawTf = (parsed.primaryTimeframe ?? '1d') as string
    const timeframe = rawTf === '1D' ? '1d' : rawTf === '1h' ? '60m' : rawTf

    if (!symbol) throw new Error('Strategy has no instruments — add at least one symbol in Core & Indicators.')

    // Apply parameter overrides to the definition JSON
    let definitionJson = dto.definitionJson
    if (scenario.parameterOverrides.length > 0) {
      const def = JSON.parse(definitionJson) as Record<string, unknown>
      for (const ov of scenario.parameterOverrides) {
        if (ov.section === OverrideSection.Indicator && ov.indicatorId && Array.isArray(def.indicators)) {
          const inds = def.indicators as Array<{ id: string; baseParams?: Record<string, unknown> }>
          const ind = inds.find(i => i.id === ov.indicatorId)
          if (ind?.baseParams) ind.baseParams[ov.paramKey] = ov.overrideValue
        }
      }
      definitionJson = JSON.stringify(def)
    }

    // Mark scenario as Running via PATCH
    await apiClient.patch<ApiResponse<DefinitionScenarioDto>>(
      `/strategy-definitions/${strategyId}/scenarios/${scenarioId}`,
      { status: ScenarioStatus.Running } satisfies PatchDefinitionScenarioRequest)

    // Launch real async backtest — pass scenarioId so the backend auto-updates
    // scenario.Status → Backtested and saves lastMetrics on completion.
    const btResp = await backtestApi.start({
      strategyName:    'GenericRules',
      parametersJson:  definitionJson,
      internalSymbol:  symbol,
      timeframe,
      fromDate:        scenario.backtestRange.from,
      toDate:          scenario.backtestRange.to,
      initialCapital:  scenario.capital,
      scenarioId:      scenarioId,
    })
    const jobId: string = btResp.data?.data?.jobId ?? ''

    // Record lastRunAt via PATCH
    await apiClient.patch<ApiResponse<DefinitionScenarioDto>>(
      `/strategy-definitions/${strategyId}/scenarios/${scenarioId}`,
      { lastRunAt: new Date().toISOString() } satisfies PatchDefinitionScenarioRequest)

    return { jobId }
  },

  listDeployments: async (strategyId: string): Promise<Deployment[]> => {
    const resp = await strategiesApi.list()
    const instances = resp.data?.data ?? []
    return instances
      .filter(inst => {
        if (inst.strategyType !== 'GenericRules') return false
        try { return JSON.parse(inst.parametersJson ?? '{}').id === strategyId }
        catch { return false }
      })
      .map(inst => instanceToDeployment(inst, strategyId))
  },

  createDeployment: async (strategyId: string, d: Omit<Deployment, 'id' | 'strategyId'>): Promise<Deployment> => {
    // For Forward / Live modes: create a real StrategyInstance via the backend.
    if (d.mode === DeploymentMode.ForwardTest || d.mode === DeploymentMode.Live) {
      const resp = await apiClient.get<ApiResponse<StrategyDefinitionDto>>(`/strategy-definitions/${strategyId}`)
      const dto = resp.data?.data
      if (dto) {
        const execMode = d.mode === DeploymentMode.Live ? 'Live' : 'ForwardTest'
        const createResp = await strategiesApi.create({
          name:             d.name,
          strategyType:     'GenericRules',
          internalSymbol:   d.symbol,
          timeframe:        d.timeframe,
          brokerName:       d.broker,
          mode:             execMode,
          parametersJson:   dto.definitionJson,
          allocatedCapital: d.allocatedCapital,
          scheduleJson:     JSON.stringify({ days: d.schedule.days, startTime: d.schedule.startTime, endTime: d.schedule.endTime }),
        })
        const newId = createResp.data?.data
        if (newId) {
          const instResp = await strategiesApi.get(newId)
          const inst = instResp.data?.data
          if (inst) return instanceToDeployment(inst, strategyId)
        }
      }
    }
    // Fallback (e.g. Backtest mode or fetch failure): return a transient stub
    return { ...d, id: `dep-${Date.now()}`, strategyId }
  },

  deleteDeployment: async (_strategyId: string, deploymentId: string): Promise<void> => {
    await strategiesApi.delete(deploymentId)
  },

  listRuns: async (strategyId: string): Promise<RunResult[]> => {
    const resp = await backtestApi.byDefinition(strategyId)
    if (!resp.data?.success) {
      throw new Error(resp.data?.error ?? 'Failed to load backtest results')
    }
    return (resp.data.data ?? []).map(dto => backtestResultToRunResult(dto, strategyId))
  },

  // PROMPT-002: listTrades is only called when runId is not a real GUID (non-saved runs).
  // Real trades for saved runs are fetched directly via backtestApi.trades(runId) in ResultsTab.
  listTrades: (_strategyId: string, _scenarioId: string, _runId: string): Promise<TradeRecord[]> =>
    Promise.resolve([]),

  createParameterSweep: async (
    strategyId: string,
    sweep: Omit<ParameterSweep, 'id' | 'strategyId' | 'generatedScenarioIds'>
  ): Promise<ParameterSweep & { generatedCount: number }> => {
    const steps = Math.floor((sweep.to - sweep.from) / sweep.step) + 1
    const sweepGroupId = `sweep-${Date.now()}`
    const ids: string[] = []
    for (let i = 0; i < steps; i++) {
      const val = sweep.from + i * sweep.step
      const overrides = [...sweep.otherOverrides, {
        section: sweep.section,
        indicatorId: sweep.indicatorId,
        paramKey: sweep.paramKey,
        baseValue: sweep.from,
        overrideValue: val,
      }]
      const body: UpsertDefinitionScenarioRequest = {
        name:                   `${sweep.paramKey} ${val}`,
        capital:                100000,
        brokerAccount:          Broker.MStock,
        backtestFrom:           '2023-01-01',
        backtestTo:             '2025-12-31',
        parameterOverridesJson: JSON.stringify(overrides),
        status:                 'Draft',
      }
      const resp = await apiClient.post<ApiResponse<DefinitionScenarioDto>>(
        `/strategy-definitions/${strategyId}/scenarios`, body)
      ids.push(resp.data.data!.id)
    }
    const created: ParameterSweep = {
      ...sweep, id: `psweep-${Date.now()}`, strategyId, generatedScenarioIds: ids,
    }
    // sweepGroupId retained in-memory for grouping — sweep metadata not yet DB-persisted
    void sweepGroupId
    return { ...created, generatedCount: steps }
  },
}

// ── P9 Screener ───────────────────────────────────────────────────────────────

export interface ScreenerResult {
  symbol: string
  lastClose: number
  sma200: number
  sma50: number
  yearHigh: number
  yearLow: number
  rsScore: number
  signal: 'VCP_BREAKOUT' | 'NEAR_BREAKOUT' | 'UPTREND'
  volumeConfirmed: boolean
  scannedAt: string
}

export const screenerApi = {
  scan: (params?: { signal?: string; minRsScore?: number; maxResults?: number }) =>
    apiClient.get<ApiResponse<ScreenerResult[]>>('/screener', { params }),
}

// ── P9 News ───────────────────────────────────────────────────────────────────

export interface NewsItem {
  id: string
  symbol: string | null
  headline: string
  summary: string | null
  source: string
  url: string | null
  publishedAt: string
  sentiment: 'Positive' | 'Negative' | 'Neutral' | 'Unknown' | null
  tags: string | null
}

export const newsApi = {
  getRecent: (params?: { symbol?: string; days?: number }) =>
    apiClient.get<ApiResponse<NewsItem[]>>('/news', { params }),
  create: (body: Omit<NewsItem, 'id'>) =>
    apiClient.post<ApiResponse<string>>('/news', body),
  delete: (id: string) =>
    apiClient.delete<ApiResponse<boolean>>(`/news/${id}`),
}
