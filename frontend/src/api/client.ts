import axios from 'axios'

// All timestamps from API are UTC ISO strings — display in IST (UTC+5:30)
export const apiClient = axios.create({
  baseURL: '/api',
  headers: { 'Content-Type': 'application/json' },
})

// Attach JWT token from Zustand auth store
apiClient.interceptors.request.use(config => {
  const token = localStorage.getItem('jwt_token')
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

apiClient.interceptors.response.use(
  res => res,
  err => {
    if (err.response?.status === 401) {
      localStorage.removeItem('jwt_token')
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
  delete: (id: string) => apiClient.delete<ApiResponse<boolean>>(`/strategies/${id}`),
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

export const instrumentsApi = {
  list: (params?: InstrumentsListParams) =>
    apiClient.get<ApiResponse<PagedResult<Instrument>>>('/instruments', { params }),
  search: (query: string) =>
    apiClient.get<ApiResponse<PagedResult<Instrument>>>('/instruments', {
      params: { search: query, pageSize: 20 },
    }),
  /** Trigger a master-data refresh from the broker (brokerName = "MStock" | "Zerodha" | "Upstox" | "all") */
  refresh: (brokerName = 'all') =>
    apiClient.post<ApiResponse<boolean>>(`/instruments/refresh?brokerName=${brokerName}`),
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
  run: (req: BacktestRequest) =>
    apiClient.post<ApiResponse<BacktestResult>>('/backtest/run', req),
  list: (strategyName?: string) =>
    apiClient.get<ApiResponse<BacktestResult[]>>('/backtest', { params: { strategyName } }),
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
  dataHash?: string
  startedAt?: string   // UTC ISO — display in IST
  trades?: BacktestTradeResult[]
  error?: string
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
