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
export const instrumentsApi = {
  list: (params?: { exchange?: string; active?: boolean }) =>
    apiClient.get<ApiResponse<Instrument[]>>('/instruments', { params }),
  search: (query: string) =>
    apiClient.get<ApiResponse<Instrument[]>>('/instruments', { params: { search: query } }),
  /** Trigger a master-data refresh from the broker (brokerName = "MStock" | "Zerodha" | "Upstox" | "all") */
  refresh: (brokerName = 'all') =>
    apiClient.post<ApiResponse<boolean>>(`/instruments/refresh?brokerName=${brokerName}`),
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
  isAuthenticated: boolean  // was isSessionValid — matches BrokerConnectionStatusDto
  lastCheckedAt?: string
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

export interface BacktestRequest {
  strategyName: string
  parametersJson: string
  internalSymbol: string
  timeframe: string
  fromDate: string
  toDate: string
  initialCapital: number
  riskPerTradePercent?: number
}

export interface BacktestResult {
  id?: string
  success: boolean
  strategyName: string
  symbol: string
  timeframe: string
  totalPnl: number
  totalReturn: number
  maxDrawdown: number
  sharpeRatio: number
  winRate: number
  totalTrades: number
  error?: string
}

export interface Instrument {
  internalSymbol: string
  exchange: string
  tradingSymbol: string
  name?: string
  instrumentType?: string
  isActive: boolean
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
  allocatedCapital?: number
}
