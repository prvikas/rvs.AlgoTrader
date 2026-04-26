import { useState, useEffect, useCallback, useRef, Fragment } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  strategiesApi, ordersApi, backtestApi, killSwitchApi, settingsApi,
  Order, StrategyInstance,
  BacktestResult, BacktestTradeResult, BacktestJobStatus, BacktestChartBar,
} from '../api/client'
import { EquityCurveChart } from '../components/Backtest/EquityCurveChart'
import { KillSwitchBanner } from '../components/Dashboard/KillSwitchBanner'
import { ColdRestartBanner } from '../components/Dashboard/ColdRestartBanner'
import { BrokerStatusBar } from '../components/Broker/BrokerStatusBar'
import { BrokerLoginModal } from '../components/Broker/BrokerLoginModal'
import { ThemeSelector } from '../components/ui/ThemeSelector'
import { SymbolSearchInput } from '../components/Strategy/SymbolSearchInput'
import { StrategyParamsEditor, paramsToJson } from '../components/Strategy/StrategyParamsEditor'
import { InstrumentsPage } from './InstrumentsPage'
import { ForwardTestPage } from './ForwardTestPage'
import { StrategyLabPage } from './StrategyLabPage'
import { UniversePage } from './UniversePage'
import { InstrumentTypesPage } from './InstrumentTypesPage'
import { MasterDataRefreshPage } from './MasterDataRefreshPage'
import { TradeJournalPage } from './TradeJournalPage'
import { PortfolioAnalysisPage } from './PortfolioAnalysisPage'
import { RiskDashboardPage } from './RiskDashboardPage'
import { CorrelationPage } from './CorrelationPage'
import { NewsPage } from './NewsPage'
import { ScreenerPage } from './ScreenerPage'
import { OptionsIntelligencePage } from './OptionsIntelligencePage'
import { PortfolioOverview } from '../components/Portfolio/PortfolioOverview'
import { StrategiesPage as NewStrategiesPage } from './StrategiesPage'
import { PromoteToForwardTestModal } from '../components/ForwardTest/PromoteToForwardTestModal'
import { formatInr, formatIst, isMarketHours } from '../utils/datetime'
import { useStrategyStream } from '../hooks/useSignalR'
import { useBacktestSignalR } from '../hooks/useBacktestSignalR'
import { BacktestReplayChart } from '../components/Backtest/BacktestReplayChart'
import { C, NAV_HEIGHT, CONTENT_PAD, TABLE_CELL, TABLE_HEADER_CELL } from '../styles/tokens'
import { useUserMode } from '../context/UserModeContext'
import { GuidedDashboard } from '../components/Dashboard/GuidedDashboard'

type Page = 'portfolio' | 'strategies' | 'orders' | 'lab' | 'backtest' | 'forwardtest' | 'instruments' | 'master-data' | 'universe' | 'instrument-types' | 'settings' | 'journal' | 'portfolio-analysis' | 'risk' | 'correlation' | 'news' | 'screener' | 'options-intel'

// Descriptions for known strategies; unknown ones registered on backend show name only.
const STRATEGY_DESCS: Record<string, string> = {
  PriceActionBreakout:   'Consolidation range breakout with ATR/volume confirmation',
  EmaVwapMomentum:       'EMA golden/death cross + VWAP + Bollinger Bands + volume',
  AlertCandleShort:      'BankNifty/Nifty short: Alert Candle (low > 5-EMA) · 1:3 RRR',
  VcpSwing:              'STRAT-001: VCP swing — SMA200 + contractions + support/breakout entry',
  FibOptionSpread:       'STRAT-002: Fibonacci 0.618 hedged option credit spread',
  IntradayPcrOptions:    'STRAT-003: Intraday PCR/OI/VWAP options — delta-targeted strike',
  IronCondor:            'Short OTM call spread + short OTM put spread (4 legs, range-bound)',
  ShortStraddleStrangle: 'Short ATM straddle or OTM strangle — theta decay play',
  CalendarSpread:        'Sell near-expiry + buy far-expiry ATM — theta + vega play',
  VerticalSpread:        'Bull/Bear call or put spread — directional defined-risk (4 types)',
}

export function Dashboard() {
  const { mode: userMode, toggle: toggleUserMode, isGuided } = useUserMode()
  const [activePage, setActivePage] = useState<Page>('portfolio')
  const [brokerLoginTarget, setBrokerLoginTarget] = useState<'MStock' | 'Zerodha' | 'Upstox' | null>(null)
  const [brokerDropOpen, setBrokerDropOpen] = useState(false)
  const [backtestPreset, setBacktestPreset] = useState<StrategyInstance | null>(null)
  const [scenarioJobId, setScenarioJobId] = useState<string | null>(null)
  const [openResultId, setOpenResultId] = useState<string | null>(null)

  // ── Data Queries ──────────────────────────────────────────────────────────
  // Prefetch registered strategies so they're cached for child pages
  useQuery({
    queryKey: ['registered-strategies'],
    queryFn: () => strategiesApi.getRegisteredNames().then(r => r.data.data ?? []),
    staleTime: 5 * 60 * 1000, // rarely changes — cache for 5 min
  })


  const { data: orders } = useQuery({
    queryKey: ['orders'],
    queryFn: () => ordersApi.list().then(r => Array.isArray(r.data.data) ? r.data.data : []),
    refetchInterval: 10_000,
    enabled: activePage === 'orders' || activePage === 'portfolio',
  })

  const qc = useQueryClient()

  const { data: killSwitchStatus } = useQuery({
    queryKey: ['kill-switch'],
    queryFn: () => killSwitchApi.status().then(r => r.data.data),
    refetchInterval: 30_000,
  })

  const killSwitchMut = useMutation({
    mutationFn: (active: boolean) => active
      ? killSwitchApi.activate('Manual — UI kill switch')
      : killSwitchApi.deactivate('Manual — UI kill switch'),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['kill-switch'] }),
  })

  const { data: backtestResults } = useQuery({
    queryKey: ['backtest-results'],
    queryFn: () => backtestApi.list().then(r => r.data.data?.items ?? []),
    enabled: activePage === 'backtest' || activePage === 'lab',
    refetchInterval: 30_000,
  })

  const { isConnected: signalRConnected, coldRestartPaused } = useStrategyStream()

  // ── Grouped nav definition ────────────────────────────────────────────────
  type NavGroup = { id: Page; label: string } | {
    label: string
    items: Array<{ id: Page; label: string }>
  }
  const NAV_GROUPS: NavGroup[] = [
    { id: 'portfolio',  label: 'Portfolio' },
    { id: 'strategies', label: 'Strategies' },
    { id: 'orders',     label: 'Orders' },
    {
      label: 'Research',
      items: [
        { id: 'lab',         label: 'Strategy Lab' },
        { id: 'backtest',    label: 'Backtest' },
        { id: 'forwardtest', label: 'Fwd Test' },
        { id: 'screener',       label: 'Screener' },
        { id: 'news',           label: 'News' },
        { id: 'options-intel',  label: 'Options' },
      ],
    },
    {
      label: 'Data',
      items: [
        { id: 'instruments',       label: 'Instruments' },
        { id: 'universe',          label: 'Universe' },
        { id: 'instrument-types',  label: 'Inst. Types' },
        { id: 'master-data',       label: 'Master Data' },
      ],
    },
    {
      label: 'Analytics',
      items: [
        { id: 'journal',            label: 'Trade Journal' },
        { id: 'portfolio-analysis', label: 'P&L Analysis' },
        { id: 'risk',               label: 'Risk' },
        { id: 'correlation',        label: 'Correlation' },
      ],
    },
    { id: 'settings', label: 'Settings' },
  ]

  const [openNavGroup, setOpenNavGroup] = useState<string | null>(null)

  // Which group label is currently active (for highlight)
  function activeGroupLabel(): string | null {
    for (const g of NAV_GROUPS) {
      if ('items' in g && g.items.some(i => i.id === activePage)) return g.label
    }
    return null
  }

  return (
    <div
      style={{ display: 'flex', flexDirection: 'column', height: '100vh', minWidth: 1280, backgroundColor: C.bg, color: C.text, fontFamily: "'Inter', system-ui, sans-serif" }}
      onClick={() => { setOpenNavGroup(null); setBrokerDropOpen(false) }}   // close dropdowns on page click
    >
      {/* Top Navigation Bar */}
      <header style={{
        height: NAV_HEIGHT, flexShrink: 0, background: C.navBg,
        borderBottom: `1px solid ${C.navBorder}`,
        display: 'flex', alignItems: 'center',
        paddingLeft: 16, paddingRight: 16, gap: 0,
        position: 'relative', zIndex: 100,   // keeps dropdowns above content
        overflow: 'visible',                 // allow dropdown menus to escape nav bounds
      }}>
        {/* Brand */}
        <span style={{ fontSize: 13, fontWeight: 800, color: C.text, letterSpacing: '0.05em', marginRight: 16, flexShrink: 0 }}>
          RVS
        </span>

        {/* Grouped Nav */}
        <div style={{ flex: 1, display: 'flex', alignItems: 'center', height: '100%' }}>
          {NAV_GROUPS.map(group => {
            if ('id' in group) {
              // Direct tab
              const isActive = activePage === group.id
              return (
                <button
                  key={group.id}
                  onClick={e => { e.stopPropagation(); setActivePage(group.id); setOpenNavGroup(null) }}
                  style={{
                    height: NAV_HEIGHT, padding: '0 11px',
                    background: 'transparent', border: 'none', borderRadius: 0,
                    borderBottom: isActive ? `2px solid ${C.navActive}` : '2px solid transparent',
                    color: isActive ? C.text : C.navMuted,
                    cursor: 'pointer', fontSize: 11, fontWeight: isActive ? 700 : 500,
                    letterSpacing: '0.04em', textTransform: 'uppercase', whiteSpace: 'nowrap',
                    flexShrink: 0,
                  }}
                >
                  {group.label}
                </button>
              )
            }
            // Dropdown group
            const isGroupActive = activeGroupLabel() === group.label
            const isOpen = openNavGroup === group.label
            return (
              <div key={group.label} style={{ position: 'relative', height: '100%', display: 'flex', alignItems: 'center', flexShrink: 0 }}>
                <button
                  onClick={e => { e.stopPropagation(); setOpenNavGroup(isOpen ? null : group.label) }}
                  style={{
                    height: NAV_HEIGHT, padding: '0 11px',
                    background: 'transparent', border: 'none', borderRadius: 0,
                    borderBottom: (isGroupActive || isOpen) ? `2px solid ${C.navActive}` : '2px solid transparent',
                    color: (isGroupActive || isOpen) ? C.text : C.navMuted,
                    cursor: 'pointer', fontSize: 11, fontWeight: isGroupActive ? 700 : 500,
                    letterSpacing: '0.04em', textTransform: 'uppercase', whiteSpace: 'nowrap',
                    display: 'flex', alignItems: 'center', gap: 4,
                  }}
                >
                  {group.label}
                  <span style={{ fontSize: 8, opacity: 0.6 }}>{isOpen ? '▲' : '▼'}</span>
                </button>
                {isOpen && (
                  <div
                    onClick={e => e.stopPropagation()}
                    style={{
                      position: 'absolute', top: NAV_HEIGHT, left: 0,
                      background: C.surface, border: `1px solid ${C.border3}`,
                      borderRadius: '0 0 6px 6px', minWidth: 160,
                      boxShadow: '0 8px 24px rgba(0,0,0,0.5)',
                      zIndex: 200,
                    }}
                  >
                    {group.items.map(item => {
                      const isActive = activePage === item.id
                      return (
                        <button
                          key={item.id}
                          onClick={() => { setActivePage(item.id); setOpenNavGroup(null) }}
                          style={{
                            width: '100%', padding: '9px 14px',
                            background: isActive ? C.surface2 : 'transparent',
                            border: 'none', borderLeft: isActive ? `2px solid ${C.navActive}` : '2px solid transparent',
                            color: isActive ? C.text : C.textSub,
                            cursor: 'pointer', fontSize: 12, textAlign: 'left',
                            fontWeight: isActive ? 600 : 400,
                            display: 'block', whiteSpace: 'nowrap',
                          }}
                        >
                          {item.label}
                        </button>
                      )
                    })}
                  </div>
                )}
              </div>
            )
          })}
        </div>

        {/* Right cluster — fixed, never shrinks */}
        <div style={{ flexShrink: 0, display: 'flex', alignItems: 'center', gap: 10, marginLeft: 8 }}>
          <MarketClockComponent />
          <MarketStatusComponent />
          <BrokerStatusBar />
          <SignalRIndicator connected={signalRConnected} />

          {/* Broker Login — always visible regardless of brokerRequired flag */}
          <div style={{ position: 'relative' }}>
            <button
              onClick={e => { e.stopPropagation(); setBrokerDropOpen(o => !o) }}
              title="Login to broker"
              style={{
                fontSize: 11, fontWeight: 600, padding: '3px 10px', borderRadius: 4,
                cursor: 'pointer', background: '#1e3a5f',
                color: '#93c5fd', border: '1px solid #1d4ed8',
              }}
            >
              🔑 Broker
            </button>
            {brokerDropOpen && (
              <div
                style={{
                  position: 'absolute', top: '110%', right: 0, zIndex: 200,
                  background: '#1a1a2e', border: `1px solid #2a2a40`,
                  borderRadius: 6, padding: '4px 0', minWidth: 130,
                  boxShadow: '0 4px 16px rgba(0,0,0,0.5)',
                }}
                onClick={e => e.stopPropagation()}
              >
                {(['MStock', 'Zerodha', 'Upstox'] as const).map(b => (
                  <button
                    key={b}
                    onClick={() => { setBrokerLoginTarget(b); setBrokerDropOpen(false) }}
                    style={{
                      display: 'block', width: '100%', textAlign: 'left',
                      padding: '7px 14px', background: 'none', border: 'none',
                      color: '#93c5fd', cursor: 'pointer', fontSize: 12,
                    }}
                    onMouseEnter={e => (e.currentTarget.style.background = '#1e3a5f')}
                    onMouseLeave={e => (e.currentTarget.style.background = 'none')}
                  >
                    {b}
                  </button>
                ))}
              </div>
            )}
          </div>

          {/* Kill Switch toggle — always accessible */}
          <button
            onClick={e => { e.stopPropagation(); killSwitchMut.mutate(!killSwitchStatus) }}
            disabled={killSwitchMut.isPending}
            title={killSwitchStatus ? 'Kill switch ON — click to resume live trading' : 'Click to block all live orders'}
            style={{
              fontSize: 11, fontWeight: 700, padding: '3px 10px', borderRadius: 4,
              cursor: killSwitchMut.isPending ? 'not-allowed' : 'pointer',
              background: killSwitchStatus ? '#7f1d1d' : 'transparent',
              color: killSwitchStatus ? '#fca5a5' : '#6b7280',
              border: `1px solid ${killSwitchStatus ? '#991b1b' : '#3f3f46'}`,
              opacity: killSwitchMut.isPending ? 0.6 : 1,
              transition: 'background 0.15s, color 0.15s, border-color 0.15s',
            }}
          >
            {killSwitchStatus ? '⛔ Live OFF' : '● Live ON'}
          </button>

          <ThemeSelector />
          <UserModeToggle mode={userMode} onToggle={toggleUserMode} />
          <LogoutButton />
        </div>
      </header>

      {/* Broker Login Modal — opened via the always-visible 🔑 Broker nav button */}
      {brokerLoginTarget && (
        <BrokerLoginModal
          broker={brokerLoginTarget}
          apiKey=""
          onSuccess={() => { setBrokerLoginTarget(null) }}
          onClose={() => setBrokerLoginTarget(null)}
        />
      )}

      {/* Kill Switch Banner — full-width critical alert */}
      {killSwitchStatus === true && <KillSwitchBanner />}

      {/* Cold Restart Banner — informational notice */}
      <ColdRestartBanner coldRestartPaused={coldRestartPaused} />

      {/* Main Content Area */}
      <main style={{
        flex: 1, overflowY: activePage === 'strategies' ? 'hidden' : 'auto',
        padding: activePage === 'strategies' ? 0 : CONTENT_PAD,
        background: C.bg,
        display: 'flex', flexDirection: 'column',
      }}>
        {activePage === 'portfolio' && (
          isGuided
            ? <GuidedDashboard onNavigate={(page) => setActivePage(page as Page)} />
            : <PortfolioOverview />
        )}
        {activePage === 'strategies' && <NewStrategiesPage />}
        {activePage === 'orders' && <OrdersPage orders={orders ?? []} />}
        {activePage === 'lab' && <StrategyLabPage />}
        {activePage === 'backtest' && (
          <BacktestPage
            backtestResults={backtestResults ?? []}
            preset={backtestPreset}
            onPresetConsumed={() => setBacktestPreset(null)}
            initialJobId={scenarioJobId}
            onInitialJobConsumed={() => setScenarioJobId(null)}
            openResultId={openResultId}
            onOpenResultConsumed={() => setOpenResultId(null)}
          />
        )}
        {activePage === 'forwardtest' && <ForwardTestPage />}
        {activePage === 'master-data' && <MasterDataRefreshPage />}
        {activePage === 'instruments' && (
          <InstrumentsPage onGoToRefresh={() => setActivePage('master-data')} />
        )}
        {activePage === 'universe' && <UniversePage />}
        {activePage === 'instrument-types' && <InstrumentTypesPage />}
        {activePage === 'settings' && <SettingsPage />}
        {activePage === 'journal' && <TradeJournalPage />}
        {activePage === 'portfolio-analysis' && <PortfolioAnalysisPage />}
        {activePage === 'risk' && <RiskDashboardPage />}
        {activePage === 'correlation' && <CorrelationPage />}
        {activePage === 'news' && <NewsPage />}
        {activePage === 'screener' && <ScreenerPage />}
        {activePage === 'options-intel' && <OptionsIntelligencePage />}
      </main>

    </div>
  )
}

// ──────────────────────────────────────────────────────────────────────────────
// OVERVIEW PAGE
// ──────────────────────────────────────────────────────────────────────────────

// OverviewPage replaced by PortfolioOverview component (imported from components/Portfolio)

// ORDERS PAGE
// ──────────────────────────────────────────────────────────────────────────────

function OrdersPage({ orders }: { orders: Order[] }) {
  return (
    <div>
      <SectionLabel title="Orders" />
      <div style={{ border: `1px solid ${C.border}`, borderRadius: 6, overflowX: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
          <thead>
            <tr style={{ background: C.surface2, borderBottom: `1px solid ${C.border}` }}>
              {['ID', 'Symbol', 'Side', 'Qty', 'Price', 'Status', 'Placed', 'Broker'].map(col => (
                <th key={col} style={{ padding: TABLE_HEADER_CELL, textAlign: 'left', color: C.textMuted, fontWeight: 600, fontSize: 10, textTransform: 'uppercase', letterSpacing: '0.07em', whiteSpace: 'nowrap' }}>{col}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {orders.length > 0 ? (
              orders.map(order => (
                <tr
                  key={order.id}
                  style={{ borderBottom: `1px solid ${C.border2}` }}
                  onMouseEnter={e => (e.currentTarget.style.background = C.surface)}
                  onMouseLeave={e => (e.currentTarget.style.background = 'transparent')}
                >
                  <td style={{ padding: TABLE_CELL, color: C.text, fontFamily: "'JetBrains Mono', monospace", fontSize: 11, fontVariantNumeric: 'tabular-nums' }}>{order.id.slice(0, 8)}</td>
                  <td style={{ padding: TABLE_CELL, color: C.text, fontWeight: 600 }}>{order.internalSymbol}</td>
                  <td style={{ padding: TABLE_CELL, color: order.direction === 'BUY' ? C.green : C.red, fontWeight: 600 }}>{order.direction}</td>
                  <td style={{ padding: TABLE_CELL, color: C.text }}>{order.quantity}</td>
                  <td style={{ padding: TABLE_CELL, color: C.text, fontFamily: "'JetBrains Mono', monospace", fontVariantNumeric: 'tabular-nums' }}>{formatInr(order.price)}</td>
                  <td style={{ padding: TABLE_CELL, color: statusColor(order.status) }}>{order.status}</td>
                  <td style={{ padding: TABLE_CELL, color: C.textSub, fontSize: 11 }}>{formatIst(order.placedAt)}</td>
                  <td style={{ padding: TABLE_CELL, color: C.textSub, fontSize: 11 }}>{order.brokerName}</td>
                </tr>
              ))
            ) : (
              <tr>
                <td colSpan={8} style={{ padding: '16px 12px', textAlign: 'center', color: C.textMuted, fontSize: 12 }}>No orders</td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  )
}

function statusColor(status: string): string {
  switch (status) {
    case 'FILLED': return C.green
    case 'CANCELLED': return C.textMuted
    case 'REJECTED': return C.red
    default: return C.amber
  }
}

// ──────────────────────────────────────────────────────────────────────────────
// BACKTEST PAGE
// ──────────────────────────────────────────────────────────────────────────────

function BacktestPage({ backtestResults, preset, onPresetConsumed, initialJobId, onInitialJobConsumed, openResultId, onOpenResultConsumed }: {
  backtestResults: BacktestResult[]
  preset?: StrategyInstance | null
  onPresetConsumed?: () => void
  /** Job ID started from ScenariosPanel — immediately track it and show live progress */
  initialJobId?: string | null
  onInitialJobConsumed?: () => void
  /** Backtest run ID to fetch and display (from compare-view Chart button) */
  openResultId?: string | null
  onOpenResultConsumed?: () => void
}) {
  const qc = useQueryClient()
  const { data: registeredStrategies } = useQuery({
    queryKey: ['registered-strategies'],
    queryFn: () => strategiesApi.getRegisteredNames().then(r => r.data.data ?? []),
    staleTime: 5 * 60 * 1000,
  })
  const [selectedStrategy, setSelectedStrategy] = useState('AlertCandleShort')
  const [formData, setFormData] = useState({
    internalSymbol: '',
    timeframe: '5m',
    fromDate: '2024-01-01',
    toDate: new Date().toISOString().slice(0, 10),
    initialCapital: 100000,
    riskPerTradePct: 1,
    fillModel: 0 as 0 | 1 | 2,
    slippageBasisPoints: 5,
    brokerageFlatPerSide: 20,
    // Trailing stop
    trailActivationR: 0,
    trailOffsetR: 0.5,
    breakEvenAt1R: false,
    // Circuit breaker
    circuitBreakerPct: 0.5,
  })
  const [strategyParams, setStrategyParams] = useState<Record<string, unknown>>({})
  const [errorMsg, setErrorMsg] = useState('')
  const [successMsg, setSuccessMsg] = useState('')
  const [runResult, setRunResult] = useState<BacktestResult | null>(null)
  const [expandedRow, setExpandedRow] = useState<string | null>(null)
  const [promoteBacktest, setPromoteBacktest] = useState<BacktestResult | null>(null)
  const [showForm, setShowForm] = useState(false)

  // Async job state
  const [activeJobId, setActiveJobId] = useState<string | null>(null)
  const [jobStatus, setJobStatus] = useState<BacktestJobStatus | null>(null)
  const pollIntervalRef = useRef<ReturnType<typeof setInterval> | null>(null)

  // Chart state — live rolling window during run; full sample after completion
  const [fullChartBars, setFullChartBars] = useState<BacktestChartBar[]>([])

  // Fullscreen chart modal — used by both the live/replay charts and "Chart" button in past runs
  const [fullscreenChart, setFullscreenChart] = useState<{ bars: BacktestChartBar[]; title: string } | null>(null)
  const [loadingChartId, setLoadingChartId] = useState<string | null>(null)

  const { liveChartBars } = useBacktestSignalR({
    jobId: activeJobId,
    onProgress: (s) => setJobStatus(s),
    onCompleted: (s) => {
      setJobStatus(s)
      if (s.status === 'Completed' && s.result) {
        setRunResult(s.result)
        setFullChartBars(s.result.chartSample ?? [])
        setSuccessMsg(`Backtest complete — ${s.result.totalTrades} trades, P&L: ${s.result.totalPnl >= 0 ? '+' : ''}${Math.abs(s.result.totalPnl).toLocaleString('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 0 })}`)
        setShowForm(false)
        qc.invalidateQueries({ queryKey: ['backtest-results'] })
        setTimeout(() => setSuccessMsg(''), 8000)
      } else if (s.status === 'Failed') {
        setErrorMsg(s.error ?? 'Backtest failed')
      }
      setActiveJobId(null)
    },
  })

  // Pre-fill form when navigated from a StrategyCard
  useEffect(() => {
    if (preset) {
      setSelectedStrategy(preset.strategyType)
      setFormData(prev => ({
        ...prev,
        internalSymbol: preset.internalSymbol,
        timeframe: preset.timeframe,
      }))
      if (preset.parametersJson) {
        try {
          const parsed = JSON.parse(preset.parametersJson)
          setStrategyParams(parsed)
        } catch { /* ignore invalid JSON */ }
      }
      setShowForm(true)
      onPresetConsumed?.()
    }
  }, [preset]) // eslint-disable-line react-hooks/exhaustive-deps

  // Scenario job started from Strategies page — adopt the job and show live progress + chart
  useEffect(() => {
    if (!initialJobId || activeJobId) return
    setActiveJobId(initialJobId)
    setJobStatus({ jobId: initialJobId, status: 'Queued', progressPct: 0, currentBar: 0, totalBars: 0, tradesSoFar: 0, currentEquity: 0 })
    setFullChartBars([])
    setRunResult(null)
    setErrorMsg('')
    setShowForm(false)
    onInitialJobConsumed?.()
  }, [initialJobId]) // eslint-disable-line react-hooks/exhaustive-deps

  // "Chart" clicked on a scenario compare row — fetch the run and display it
  useEffect(() => {
    if (!openResultId) return
    onOpenResultConsumed?.()
    setRunResult(null)
    setFullChartBars([])
    setErrorMsg('')
    setShowForm(false)
    backtestApi.get(openResultId).then(resp => {
      const result = resp.data.data
      if (result) {
        setRunResult(result)
        setFullChartBars(result.chartSample ?? [])
      }
    }).catch(() => setErrorMsg('Failed to load backtest result'))
  }, [openResultId]) // eslint-disable-line react-hooks/exhaustive-deps

  // Poll job status every 2 seconds while running
  useEffect(() => {
    if (!activeJobId) return
    const poll = async () => {
      try {
        const resp = await backtestApi.status(activeJobId)
        const s = resp.data.data
        if (!s) return
        setJobStatus(s)
        if (s.status === 'Completed' || s.status === 'Failed' || s.status === 'Cancelled') {
          if (pollIntervalRef.current) clearInterval(pollIntervalRef.current)
          pollIntervalRef.current = null
          if (s.status === 'Completed' && s.result) {
            setRunResult(s.result)
            setSuccessMsg(`Backtest complete — ${s.result.totalTrades} trades, P&L: ${s.result.totalPnl >= 0 ? '+' : ''}${Math.abs(s.result.totalPnl).toLocaleString('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 0 })}`)
            setShowForm(false)
            qc.invalidateQueries({ queryKey: ['backtest-results'] })
            setTimeout(() => setSuccessMsg(''), 8000)
          } else if (s.status === 'Failed') {
            setErrorMsg(s.error ?? 'Backtest failed')
          }
          setActiveJobId(null)
        }
      } catch { /* ignore poll errors */ }
    }
    poll() // immediate first poll
    pollIntervalRef.current = setInterval(poll, 2000)
    return () => { if (pollIntervalRef.current) clearInterval(pollIntervalRef.current) }
  }, [activeJobId, qc]) // eslint-disable-line react-hooks/exhaustive-deps

  const toggleRow = useCallback((id: string) => {
    setExpandedRow(prev => prev === id ? null : id)
  }, [])

  const startMutation = useMutation({
    mutationFn: async () => {
      const cmd = {
        strategyName: selectedStrategy,
        internalSymbol: formData.internalSymbol,
        timeframe: formData.timeframe,
        fromDate: formData.fromDate,
        toDate: formData.toDate,
        initialCapital: formData.initialCapital,
        riskPerTradePercent: formData.riskPerTradePct,
        parametersJson: paramsToJson(strategyParams),
        fillModel: formData.fillModel,
        slippageBasisPoints: formData.slippageBasisPoints,
        brokerageFlatPerSide: formData.brokerageFlatPerSide,
        trailActivationR: formData.trailActivationR,
        trailOffsetR: formData.trailOffsetR,
        breakEvenAt1R: formData.breakEvenAt1R,
        circuitBreakerPct: formData.circuitBreakerPct,
      }
      return backtestApi.start(cmd)
    },
    onSuccess: (response) => {
      const jobId = response.data.data?.jobId
      if (jobId) {
        setActiveJobId(jobId)
        setJobStatus({ jobId, status: 'Queued', progressPct: 0, currentBar: 0, totalBars: 0, tradesSoFar: 0, currentEquity: 0 })
        setFullChartBars([])
        setShowForm(false)
      }
    },
    onError: (err: any) => {
      setErrorMsg(err.response?.data?.error || 'Failed to start backtest')
    }
  })

  const handleRunBacktest = () => {
    setErrorMsg('')
    setRunResult(null)
    setJobStatus(null)
    if (!formData.internalSymbol) {
      setErrorMsg('Symbol is required')
      return
    }
    if (Object.keys(strategyParams).length === 0) {
      setErrorMsg('Strategy parameters not loaded — wait for the schema to finish loading')
      return
    }
    startMutation.mutate()
  }

  const handleCancelJob = () => {
    if (activeJobId) {
      backtestApi.cancel(activeJobId)
      setActiveJobId(null)
      setJobStatus(null)
    }
  }

  /** Open the fullscreen chart for a past run, fetching chartSample on demand if needed. */
  const handleOpenChart = useCallback(async (result: BacktestResult) => {
    const makeTitle = (r: BacktestResult) =>
      `${r.strategyName} · ${r.symbol} · ${r.timeframe} — ${
        r.startedAt ? formatIst(r.startedAt) : ''
      }`
    if (result.chartSample?.length) {
      setFullscreenChart({ bars: result.chartSample, title: makeTitle(result) })
      return
    }
    if (!result.id) return
    setLoadingChartId(result.id)
    try {
      const resp = await backtestApi.get(result.id)
      const full = resp.data.data
      if (full?.chartSample?.length) {
        setFullscreenChart({ bars: full.chartSample, title: makeTitle(full) })
      }
    } catch { /* ignore */ } finally {
      setLoadingChartId(null)
    }
  }, [])

  /** Download the PDF report for a past run. */
  const handleDownloadPdf = useCallback(async (id: string) => {
    try {
      const resp = await backtestApi.report(id)
      const url = URL.createObjectURL(resp.data as Blob)
      const a = document.createElement('a')
      a.href = url
      a.download = `backtest-${id.slice(0, 8)}.pdf`
      document.body.appendChild(a)
      a.click()
      document.body.removeChild(a)
      URL.revokeObjectURL(url)
    } catch { /* ignore */ }
  }, [])

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8, paddingBottom: 6, borderBottom: `1px solid ${C.border}` }}>
        <span style={{ fontSize: 11, fontWeight: 700, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.08em' }}>
          Backtest Lab
        </span>
        <button
          onClick={() => setShowForm(!showForm)}
          style={{
            padding: '6px 12px',
            backgroundColor: C.blue,
            color: '#fff',
            border: 'none',
            borderRadius: 4,
            cursor: 'pointer',
            fontSize: 12,
            fontWeight: 600,
            textTransform: 'uppercase',
            letterSpacing: '0.05em',
          }}
        >
          {showForm ? '✕ Close' : '▶ Run'}
        </button>
      </div>

      {/* Right-side Drawer Overlay */}
      {showForm && (
        <div onClick={() => setShowForm(false)} style={{
          position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.4)', zIndex: 199,
        }} />
      )}

      {/* Right-side Drawer */}
      {showForm && (
        <div style={{
          position: 'fixed', top: NAV_HEIGHT, right: 0, bottom: 0,
          width: 520, background: C.surface,
          borderLeft: `1px solid ${C.border}`,
          zIndex: 200, overflowY: 'auto',
          padding: '16px 20px',
          boxShadow: '-8px 0 32px rgba(0,0,0,0.6)',
          display: 'flex', flexDirection: 'column', gap: 12,
        }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', paddingBottom: 8, borderBottom: `1px solid ${C.border}` }}>
            <h3 style={{ margin: 0, fontSize: 13, fontWeight: 700, color: C.text, textTransform: 'uppercase', letterSpacing: '0.05em' }}>Run Backtest</h3>
            <button onClick={() => setShowForm(false)} style={{ background: 'none', border: 'none', cursor: 'pointer', fontSize: 18, color: C.textMuted }}>✕</button>
          </div>

          {errorMsg && <div style={{ backgroundColor: C.redBg, color: C.red, padding: '10px 12px', borderRadius: 4, marginBottom: '8px', fontSize: 12, border: `1px solid ${C.red30}` }}>{errorMsg}</div>}
          {successMsg && <div style={{ backgroundColor: C.greenBg, color: C.green, padding: '10px 12px', borderRadius: 4, marginBottom: '8px', fontSize: 12, border: `1px solid ${C.green30}` }}>{successMsg}</div>}

          {/* Form fields — scrollable within drawer */}
          <div style={{ display: 'flex', flexDirection: 'column', gap: 12, flex: 1, overflowY: 'auto' }}>
            <div>
              <label style={{ display: 'block', fontSize: 12, fontWeight: 600, marginBottom: 8, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.05em' }}>Strategy</label>
              <div style={{ display: 'grid', gap: 6, maxHeight: '140px', overflowY: 'auto' }}>
                {(registeredStrategies ?? []).map(name => (
                  <button
                    key={name}
                    onClick={() => { setSelectedStrategy(name); setStrategyParams({}) }}
                    style={{
                      padding: '10px 12px',
                      backgroundColor: selectedStrategy === name ? C.blue : C.surface2,
                      color: C.text,
                      border: selectedStrategy === name ? `1px solid ${C.blue}` : `1px solid ${C.border}`,
                      borderRadius: 4,
                      cursor: 'pointer',
                      textAlign: 'left',
                      fontSize: 12,
                    }}
                  >
                    <div style={{ fontWeight: 600 }}>{name}</div>
                    {STRATEGY_DESCS[name] && (
                      <div style={{ fontSize: 10, color: C.textSub, marginTop: 2 }}>{STRATEGY_DESCS[name]}</div>
                    )}
                  </button>
                ))}
                {!registeredStrategies && (
                  <div style={{ fontSize: 12, color: C.textMuted, padding: '8px 0' }}>Loading…</div>
                )}
              </div>
            </div>

            <SymbolSearchInput
              value={formData.internalSymbol}
              onChange={(sym) => setFormData({ ...formData, internalSymbol: sym })}
            />

            <FormField
              label="Timeframe"
              type="select"
              value={formData.timeframe}
              onChange={(v) => setFormData({ ...formData, timeframe: v })}
              options={[
                { value: '1m', label: '1 min' },
                { value: '5m', label: '5 min' },
                { value: '15m', label: '15 min' },
                { value: '30m', label: '30 min' },
                { value: '60m', label: '60 min' },
              ]}
            />

            <FormField
              label="From Date"
              type="date"
              value={formData.fromDate}
              onChange={(v) => setFormData({ ...formData, fromDate: v })}
            />

            <FormField
              label="To Date"
              type="date"
              value={formData.toDate}
              onChange={(v) => setFormData({ ...formData, toDate: v })}
            />

            <FormField
              label="Initial Capital (₹)"
              type="number"
              value={formData.initialCapital.toString()}
              onChange={(v) => setFormData({ ...formData, initialCapital: parseInt(v) || 100000 })}
            />

            <FormField
              label="Risk per Trade (%)"
              type="number"
              value={formData.riskPerTradePct.toString()}
              onChange={(v) => setFormData({ ...formData, riskPerTradePct: parseFloat(v) || 1 })}
            />

            <div style={{ paddingTop: 4, borderTop: `1px solid ${C.border}` }}>
              <div>
                <label style={{ display: 'block', fontSize: 12, fontWeight: 600, marginBottom: 8, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                  Fill Model
                </label>
                <select
                  value={formData.fillModel}
                  onChange={e => setFormData({ ...formData, fillModel: parseInt(e.target.value) as 0|1|2 })}
                  style={{ background: C.surface2, border: `1px solid ${C.border}`, borderRadius: 4, color: C.text, padding: '6px 10px', fontSize: 12, width: '100%', boxSizing: 'border-box' }}
                >
                  <option value={0}>Next Bar Open (default)</option>
                  <option value={1}>Next Bar Open + Slippage</option>
                  <option value={2}>Signal Bar Close ⚠️</option>
                </select>
                <p style={{ color: C.textSub, fontSize: 10, marginTop: 4, marginBottom: 0 }}>
                  NextBarOpen avoids lookahead bias.
                </p>
              </div>

              <div style={{ marginTop: 10 }}>
                <label style={{ display: 'block', fontSize: 12, fontWeight: 600, marginBottom: 6, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                  Slippage (basis points)
                </label>
                <input
                  type="number" min={0} max={100} step={1}
                  value={formData.slippageBasisPoints}
                  onChange={e => setFormData({ ...formData, slippageBasisPoints: parseInt(e.target.value) || 0 })}
                  style={{ background: C.surface2, border: `1px solid ${C.border}`, borderRadius: 4, color: C.text, padding: '6px 10px', fontSize: 12, width: '100%', boxSizing: 'border-box' }}
                />
                <p style={{ color: C.textSub, fontSize: 10, marginTop: 4, marginBottom: 0 }}>
                  1 bp = 0.01%.
                </p>
              </div>

              <div style={{ marginTop: 10 }}>
                <label style={{ display: 'block', fontSize: 12, fontWeight: 600, marginBottom: 6, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                  Brokerage per Order Leg (₹)
                </label>
                <input
                  type="number" min={0} step={1}
                  value={formData.brokerageFlatPerSide}
                  onChange={e => setFormData({ ...formData, brokerageFlatPerSide: parseInt(e.target.value) || 20 })}
                  style={{ background: C.surface2, border: `1px solid ${C.border}`, borderRadius: 4, color: C.text, padding: '6px 10px', fontSize: 12, width: '100%', boxSizing: 'border-box' }}
                />
                <p style={{ color: C.textSub, fontSize: 10, marginTop: 4, marginBottom: 0 }}>
                  ₹20/order typical. Set to 0 for %.
                </p>
              </div>
            </div>

            {/* ── Trailing Stop ──────────────────────────────────────────── */}
            <div style={{ paddingTop: 4, borderTop: `1px solid ${C.border}` }}>
              <div style={{ fontSize: 11, fontWeight: 700, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.06em', marginBottom: 8, paddingBottom: 4, borderBottom: `1px solid ${C.border}` }}>
                Trailing Stop
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8 }}>
                <div>
                  <label style={{ display: 'block', fontSize: 11, fontWeight: 600, marginBottom: 4, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                    Activate at (R)
                  </label>
                  <input
                    type="number" min={0} max={10} step={0.5}
                    value={formData.trailActivationR}
                    onChange={e => setFormData({ ...formData, trailActivationR: parseFloat(e.target.value) || 0 })}
                    style={{ background: C.surface2, border: `1px solid ${C.border}`, borderRadius: 4, color: C.text, padding: '6px 10px', fontSize: 12, width: '100%', boxSizing: 'border-box' }}
                  />
                  <p style={{ color: C.textSub, fontSize: 10, marginTop: 3, marginBottom: 0 }}>0 = disabled</p>
                </div>
                <div>
                  <label style={{ display: 'block', fontSize: 11, fontWeight: 600, marginBottom: 4, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                    Trail Offset (R)
                  </label>
                  <input
                    type="number" min={0.1} max={5} step={0.1}
                    value={formData.trailOffsetR}
                    onChange={e => setFormData({ ...formData, trailOffsetR: parseFloat(e.target.value) || 0.5 })}
                    style={{ background: C.surface2, border: `1px solid ${C.border}`, borderRadius: 4, color: C.text, padding: '6px 10px', fontSize: 12, width: '100%', boxSizing: 'border-box' }}
                  />
                  <p style={{ color: C.textSub, fontSize: 10, marginTop: 3, marginBottom: 0 }}>Behind best price</p>
                </div>
              </div>
              <div style={{ marginTop: 8 }}>
                <label style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer', fontSize: 12, color: C.text }}>
                  <input
                    type="checkbox"
                    checked={formData.breakEvenAt1R}
                    onChange={e => setFormData({ ...formData, breakEvenAt1R: e.target.checked })}
                    style={{ accentColor: C.green }}
                  />
                  Break-even at 1R (slide SL to entry once 1R gained)
                </label>
              </div>
            </div>

            {/* ── Circuit Breaker ────────────────────────────────────────── */}
            <div style={{ paddingTop: 4, borderTop: `1px solid ${C.border}` }}>
              <div style={{ fontSize: 11, fontWeight: 700, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.06em', marginBottom: 8, paddingBottom: 4, borderBottom: `1px solid ${C.border}` }}>
                Circuit Breaker
              </div>
              <div>
                <label style={{ display: 'block', fontSize: 11, fontWeight: 600, marginBottom: 4, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                  Stop at Capital Loss (%)
                </label>
                <input
                  type="number" min={0} max={100} step={5}
                  value={Math.round(formData.circuitBreakerPct * 100)}
                  onChange={e => setFormData({ ...formData, circuitBreakerPct: (parseInt(e.target.value) || 0) / 100 })}
                  style={{ background: C.surface2, border: `1px solid ${C.border}`, borderRadius: 4, color: C.text, padding: '6px 10px', fontSize: 12, width: '100%', boxSizing: 'border-box' }}
                />
                <p style={{ color: C.textSub, fontSize: 10, marginTop: 3, marginBottom: 0 }}>
                  Stop if equity drops below this % of initial capital. Default 50%. Set to 0 to disable.
                </p>
              </div>
            </div>

            <div>
              <label style={{ display: 'block', fontSize: 12, fontWeight: 600, marginBottom: 8, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.05em' }}>Strategy Parameters</label>
              <StrategyParamsEditor
                strategyName={selectedStrategy}
                value={strategyParams}
                onChange={setStrategyParams}
              />
            </div>
          </div>

          {/* Action buttons — sticky at bottom */}
          <div style={{ display: 'flex', gap: 8, paddingTop: 12, borderTop: `1px solid ${C.border}`, flexShrink: 0 }}>
            <button
              onClick={() => setShowForm(false)}
              style={{
                flex: 1,
                padding: '8px 12px',
                backgroundColor: C.surface2,
                color: C.text,
                border: `1px solid ${C.border}`,
                borderRadius: 4,
                cursor: 'pointer',
                fontSize: 12,
                fontWeight: 600,
                textTransform: 'uppercase',
                letterSpacing: '0.05em',
              }}
            >
              Cancel
            </button>
            <button
              onClick={handleRunBacktest}
              disabled={startMutation.isPending || !!activeJobId}
              style={{
                flex: 1,
                padding: '8px 12px',
                backgroundColor: C.blue,
                color: '#fff',
                border: 'none',
                borderRadius: 4,
                cursor: (startMutation.isPending || !!activeJobId) ? 'not-allowed' : 'pointer',
                fontSize: 12,
                fontWeight: 700,
                textTransform: 'uppercase',
                letterSpacing: '0.05em',
                opacity: (startMutation.isPending || !!activeJobId) ? 0.7 : 1,
              }}
            >
              {startMutation.isPending ? 'Starting...' : activeJobId ? 'Running...' : 'Run'}
            </button>
          </div>
        </div>
      )}

      {/* Async progress bar */}
      {jobStatus && (jobStatus.status === 'Queued' || jobStatus.status === 'Running' || jobStatus.status === 'Downloading') && (
        <div style={{ marginTop: 12, padding: '14px 16px', background: '#0d1b2e', border: '1px solid #2563eb44', borderRadius: 6 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8 }}>
            <span style={{ fontSize: 12, color: '#93c5fd', fontWeight: 600 }}>
              {jobStatus.status === 'Downloading' ? '⬇ Downloading history…' : jobStatus.status === 'Queued' ? '⏳ Queued…' : '⟳ Running backtest…'}
            </span>
            <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
              <span style={{ fontSize: 11, color: '#64748b', fontFamily: 'monospace' }}>
                {jobStatus.status === 'Running' && jobStatus.totalBars > 0
                  ? `Bar ${jobStatus.currentBar.toLocaleString()} / ${jobStatus.totalBars.toLocaleString()} · ${jobStatus.tradesSoFar} trades`
                  : ''}
              </span>
              <button
                onClick={handleCancelJob}
                style={{ padding: '2px 8px', background: '#450a0a', color: '#fca5a5', border: '1px solid #7f1d1d', borderRadius: 3, cursor: 'pointer', fontSize: 10, fontWeight: 600 }}
              >
                Cancel
              </button>
            </div>
          </div>
          {/* Progress bar */}
          <div style={{ height: 6, background: C.blueBg, borderRadius: 3, overflow: 'hidden' }}>
            <div style={{
              height: '100%',
              width: `${jobStatus.progressPct}%`,
              background: `linear-gradient(90deg, ${C.blue99}, ${C.blue})`,
              borderRadius: 3,
              transition: 'width 0.4s ease',
            }} />
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: 5 }}>
            <span style={{ fontSize: 10, color: '#64748b' }}>{jobStatus.progressPct.toFixed(0)}% complete</span>
            {jobStatus.currentEquity > 0 && (
              <span style={{ fontSize: 10, color: '#64748b', fontFamily: 'monospace' }}>
                Equity: {formatInr(jobStatus.currentEquity)}
              </span>
            )}
          </div>
        </div>
      )}

      {/* Inline last-run result panel */}
      {runResult && !activeJobId && (
        <div style={{
          marginTop: 12, padding: '14px 16px',
          background: runResult.circuitBreakerHit ? '#2d1500' : runResult.error ? '#450a0a' : '#0a2a1a',
          border: `1px solid ${runResult.circuitBreakerHit ? '#f97316' : runResult.error ? C.red : C.green}44`,
          borderRadius: 6,
        }}>
          {runResult.error && !runResult.circuitBreakerHit ? (
            <div style={{ color: C.red, fontSize: 13, fontWeight: 600 }}>{runResult.error}</div>
          ) : (
            <>
              {/* Circuit breaker warning banner */}
              {runResult.circuitBreakerHit && (
                <div style={{
                  marginBottom: 10, padding: '8px 12px',
                  background: '#431407', border: '1px solid #f9731644',
                  borderRadius: 4, display: 'flex', alignItems: 'flex-start', gap: 8,
                }}>
                  <span style={{ fontSize: 16, flexShrink: 0 }}>⚡</span>
                  <div>
                    <div style={{ fontSize: 12, fontWeight: 700, color: '#fb923c', marginBottom: 2 }}>
                      CIRCUIT BREAKER — Loss-making strategy
                    </div>
                    <div style={{ fontSize: 11, color: '#fdba74', lineHeight: 1.5 }}>
                      {runResult.circuitBreakerReason ?? 'Backtest stopped early: equity fell below the circuit breaker threshold.'}
                    </div>
                  </div>
                </div>
              )}
              <div style={{ fontSize: 11, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.06em', marginBottom: 8 }}>Last Run — {runResult.strategyName} · {runResult.symbol} · {runResult.timeframe}</div>
              <div style={{ display: 'flex', gap: 20, flexWrap: 'wrap' }}>
                {[
                  { label: 'Net P&L', value: formatInr(runResult.totalPnl), color: runResult.totalPnl >= 0 ? C.green : C.red },
                  { label: 'Return', value: `${(runResult.totalReturn * 100).toFixed(1)}%`, color: runResult.totalReturn >= 0 ? C.green : C.red },
                  { label: 'Win%', value: `${(runResult.winRate * 100).toFixed(0)}%`, color: C.text },
                  { label: 'Trades', value: String(runResult.totalTrades), color: C.text },
                  { label: 'Sharpe', value: runResult.sharpeRatio.toFixed(2), color: runResult.sharpeRatio >= 1 ? C.green : C.amber },
                  { label: 'MaxDD', value: `${(runResult.maxDrawdown * 100).toFixed(1)}%`, color: C.red },
                ].map(m => (
                  <div key={m.label}>
                    <div style={{ fontSize: 10, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.05em' }}>{m.label}</div>
                    <div style={{ fontSize: 16, fontWeight: 800, color: m.color, fontFamily: 'monospace' }}>{m.value}</div>
                  </div>
                ))}
              </div>
              {runResult.id && (
                <button
                  onClick={() => setPromoteBacktest(runResult)}
                  style={{ marginTop: 10, padding: '5px 12px', background: '#1e3a5f', color: '#93c5fd', border: '1px solid #2563eb44', borderRadius: 4, cursor: 'pointer', fontSize: 11, fontWeight: 700 }}
                >
                  Start Forward Test with this result
                </button>
              )}
            </>
          )}
        </div>
      )}

      {/* ── Live rolling chart during run ──────────────────────────────────── */}
      {liveChartBars.length > 0 && activeJobId && (
        <div style={{ marginTop: 12 }}>
          <BacktestReplayChart
            bars={liveChartBars}
            isLive
            height={380}
            title={`Live — Bar ${jobStatus?.currentBar?.toLocaleString() ?? 0} / ${jobStatus?.totalBars?.toLocaleString() ?? 0}`}
            onToggleFullscreen={() => setFullscreenChart({
              bars: liveChartBars,
              title: `Live — ${jobStatus?.currentBar?.toLocaleString() ?? 0} bars`,
            })}
          />
        </div>
      )}

      {/* ── Full replay chart after completion ─────────────────────────────── */}
      {fullChartBars.length > 0 && !activeJobId && runResult && (
        <div style={{ marginTop: 12 }}>
          <BacktestReplayChart
            bars={fullChartBars}
            isLive={false}
            height={420}
            title={`${runResult.strategyName} · ${runResult.symbol} · ${runResult.timeframe} — ${fullChartBars.filter(b => b.signal).length} signals`}
            onToggleFullscreen={() => setFullscreenChart({
              bars: fullChartBars,
              title: `${runResult.strategyName} · ${runResult.symbol} · ${runResult.timeframe}`,
            })}
          />
        </div>
      )}

      {/* Past Results */}
      <div style={{ marginTop: 20 }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8, paddingBottom: 6, borderBottom: `1px solid ${C.border}` }}>
          <span style={{ fontSize: 11, fontWeight: 700, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.08em' }}>
            Past Runs
          </span>
        </div>
      <div style={{ border: `1px solid ${C.border}`, borderRadius: 6, overflowX: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
          <thead>
            <tr style={{ background: C.surface2, borderBottom: `1px solid ${C.border}` }}>
              {['', 'Date (IST)', 'Strategy', 'Symbol', 'TF', 'Net P&L', 'Return', 'Win%', 'Mo.Win%', 'Trades', 'Sharpe', 'Sortino', 'Calmar', 'MaxDD', 'PF', 'Expectancy', ''].map((col, i) => (
                <th key={i} style={{ padding: TABLE_HEADER_CELL, textAlign: 'left', color: C.textMuted, fontWeight: 600, fontSize: 10, textTransform: 'uppercase', letterSpacing: '0.07em', whiteSpace: 'nowrap' }}>{col}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {backtestResults.length > 0 ? (
              backtestResults.map(result => {
                const rowId = result.id ?? result.strategyName + result.symbol
                const isExpanded = expandedRow === rowId
                return (
                  <Fragment key={rowId}>
                    <tr
                      style={{ borderBottom: isExpanded ? 'none' : `1px solid ${C.border2}`, cursor: 'pointer', background: isExpanded ? C.surface : 'transparent' }}
                      onClick={() => toggleRow(rowId)}
                      onMouseEnter={e => !isExpanded && (e.currentTarget.style.background = C.surface)}
                      onMouseLeave={e => !isExpanded && (e.currentTarget.style.background = 'transparent')}
                    >
                      <td style={{ padding: TABLE_CELL, color: C.textMuted, fontSize: 11 }}>
                        {isExpanded ? '▼' : '▶'}
                      </td>
                      <td style={{ padding: TABLE_CELL, color: C.textSub, fontSize: 11, whiteSpace: 'nowrap' }}>
                        {result.startedAt ? formatIst(result.startedAt) : '--'}
                      </td>
                      <td style={{ padding: TABLE_CELL, color: C.text, fontWeight: 600 }}>{result.strategyName}</td>
                      <td style={{ padding: TABLE_CELL, color: C.text }}>{result.symbol}</td>
                      <td style={{ padding: TABLE_CELL, color: C.textMuted, fontSize: 11 }}>{result.timeframe}</td>
                      <td style={{ padding: TABLE_CELL, color: result.totalPnl >= 0 ? C.green : C.red, fontWeight: 600, fontFamily: "'JetBrains Mono', monospace", fontVariantNumeric: 'tabular-nums' }}>
                        {formatInr(result.totalPnl)}
                      </td>
                      <td style={{ padding: TABLE_CELL, color: result.totalReturn >= 0 ? C.green : C.red, fontFamily: "'JetBrains Mono', monospace", fontVariantNumeric: 'tabular-nums' }}>
                        {(result.totalReturn * 100).toFixed(2)}%
                      </td>
                      <td style={{ padding: TABLE_CELL, color: C.text, fontFamily: "'JetBrains Mono', monospace", fontVariantNumeric: 'tabular-nums' }}>{(result.winRate * 100).toFixed(1)}%</td>
                      <td style={{ padding: TABLE_CELL, color: result.monthlyWinRate != null && result.monthlyWinRate >= 0.6 ? C.green : C.amber, fontFamily: "'JetBrains Mono', monospace", fontVariantNumeric: 'tabular-nums' }}>
                        {result.monthlyWinRate != null ? `${(result.monthlyWinRate * 100).toFixed(0)}%` : '--'}
                      </td>
                      <td style={{ padding: TABLE_CELL, color: C.text, fontFamily: "'JetBrains Mono', monospace", fontVariantNumeric: 'tabular-nums' }}>{result.totalTrades}</td>
                      <td style={{ padding: TABLE_CELL, color: result.sharpeRatio >= 1 ? C.green : result.sharpeRatio >= 0 ? C.amber : C.red, fontFamily: "'JetBrains Mono', monospace", fontVariantNumeric: 'tabular-nums' }}>
                        {result.sharpeRatio.toFixed(2)}
                      </td>
                      <td style={{ padding: TABLE_CELL, color: result.sortinoRatio != null && result.sortinoRatio >= 1 ? C.green : C.amber, fontFamily: "'JetBrains Mono', monospace", fontVariantNumeric: 'tabular-nums' }}>
                        {result.sortinoRatio != null ? result.sortinoRatio.toFixed(2) : '--'}
                      </td>
                      <td style={{ padding: TABLE_CELL, color: C.text, fontFamily: "'JetBrains Mono', monospace", fontVariantNumeric: 'tabular-nums' }}>
                        {result.calmarRatio != null ? result.calmarRatio.toFixed(2) : '--'}
                      </td>
                      <td style={{ padding: TABLE_CELL, color: C.red, fontFamily: "'JetBrains Mono', monospace", fontVariantNumeric: 'tabular-nums' }}>
                        {(result.maxDrawdown * 100).toFixed(1)}%
                      </td>
                      <td style={{ padding: TABLE_CELL, color: result.profitFactor != null && result.profitFactor >= 1.5 ? C.green : C.amber, fontFamily: "'JetBrains Mono', monospace", fontVariantNumeric: 'tabular-nums' }}>
                        {result.profitFactor != null ? result.profitFactor.toFixed(2) : '--'}
                      </td>
                      <td style={{ padding: TABLE_CELL, color: result.expectancyPerTrade != null && result.expectancyPerTrade >= 0 ? C.green : C.red, fontFamily: "'JetBrains Mono', monospace", fontVariantNumeric: 'tabular-nums' }}>
                        {result.expectancyPerTrade != null ? formatInr(result.expectancyPerTrade) : '--'}
                      </td>
                      <td style={{ padding: TABLE_CELL }} onClick={e => e.stopPropagation()}>
                        <div style={{ display: 'flex', gap: 4 }}>
                          {/* Chart — opens fullscreen chart modal */}
                          <button
                            onClick={() => handleOpenChart(result)}
                            disabled={loadingChartId === result.id}
                            title="View replay chart"
                            style={{ padding: '3px 7px', backgroundColor: '#1e3a5f', color: '#93c5fd', border: '1px solid #2563eb44', borderRadius: 3, cursor: 'pointer', fontSize: 10, fontWeight: 600, opacity: loadingChartId === result.id ? 0.6 : 1 }}
                          >
                            {loadingChartId === result.id ? '…' : 'Chart'}
                          </button>
                          {/* PDF View */}
                          {result.id && (
                            <button
                              onClick={() => window.open(`/api/backtest/${result.id}/report`, '_blank')}
                              title="View PDF report in new tab"
                              style={{ padding: '3px 7px', backgroundColor: C.surface2, color: C.textSub, border: `1px solid ${C.border}`, borderRadius: 3, cursor: 'pointer', fontSize: 10, fontWeight: 600 }}
                            >
                              View PDF
                            </button>
                          )}
                          {/* PDF Download */}
                          {result.id && (
                            <button
                              onClick={() => handleDownloadPdf(result.id!)}
                              title="Download PDF report"
                              style={{ padding: '3px 7px', backgroundColor: C.blue, color: '#fff', border: 'none', borderRadius: 3, cursor: 'pointer', fontSize: 10, fontWeight: 600 }}
                            >
                              ↓ PDF
                            </button>
                          )}
                          {result.id && (
                            <button
                              onClick={() => setPromoteBacktest(result)}
                              title="Start a Forward Test from this backtest"
                              style={{ padding: '3px 7px', backgroundColor: C.blueBg, color: C.blue, border: `1px solid ${C.blue30}`, borderRadius: 3, cursor: 'pointer', fontSize: 10, fontWeight: 700 }}
                            >
                              Fwd
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                    {isExpanded && (
                      <tr key={`${rowId}-detail`} style={{ borderBottom: `1px solid ${C.border2}`, background: C.surface2 }}>
                        <td colSpan={17} style={{ padding: '12px 14px' }}>
                          <BacktestDetailPanel result={result} />
                        </td>
                      </tr>
                    )}
                  </Fragment>
                )
              })
            ) : (
              <tr>
                <td colSpan={17} style={{ padding: '16px 12px', textAlign: 'center', color: C.textMuted, fontSize: 12 }}>No backtest results yet</td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      </div>

      {/* Promote to Forward Test modal */}
      {promoteBacktest && (
        <PromoteToForwardTestModal
          backtest={promoteBacktest}
          onClose={() => setPromoteBacktest(null)}
        />
      )}

      {/* ── Fullscreen chart overlay ─────────────────────────────────────── */}
      {fullscreenChart && (
        <BacktestReplayChart
          bars={fullscreenChart.bars}
          isLive={false}
          height={window.innerHeight - 44}
          title={fullscreenChart.title}
          fullscreen
          onToggleFullscreen={() => setFullscreenChart(null)}
        />
      )}
    </div>
  )
}

// ──────────────────────────────────────────────────────────────────────────────
// BACKTEST DETAIL PANEL (expandable row)
// ──────────────────────────────────────────────────────────────────────────────

const MONTH_NAMES = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec']

function StatCell({ label, value, color = C.text }: { label: string; value: string; color?: string }) {
  return (
    <div>
      <div style={{ fontSize: 10, color: C.textMuted, marginBottom: 3, fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.05em' }}>{label}</div>
      <div style={{ fontSize: 13, fontWeight: 700, color, fontFamily: "'JetBrains Mono', monospace" }}>{value}</div>
    </div>
  )
}

function BacktestDetailPanel({ result }: { result: BacktestResult }) {
  const trades = result.trades ?? []
  const monthly = result.monthlyBreakdown ?? []
  const yearly = result.yearlyBreakdown ?? []
  const zeroLossYears = yearly.filter(y => y.pnl > 0).map(y => y.year)
  const bestYear  = yearly.length > 0 ? yearly.reduce((a, b) => a.pnl > b.pnl ? a : b) : null
  const worstYear = yearly.length > 0 ? yearly.reduce((a, b) => a.pnl < b.pnl ? a : b) : null

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>

      {/* ── Equity Curve ─────────────────────────────────────────────── */}
      {trades.length > 0 && result.initialCapital != null && (
        <div>
          <div style={{ fontSize: 10, color: C.textMuted, marginBottom: 6, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.06em', borderBottom: `1px solid ${C.border}`, paddingBottom: 4 }}>Equity Curve</div>
          <EquityCurveChart trades={trades} initialCapital={result.initialCapital} />
        </div>
      )}

      {/* ── Core Stats ────────────────────────────────────────────────── */}
      <div>
        <div style={{ fontSize: 10, color: C.textMuted, marginBottom: 8, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.06em', borderBottom: `1px solid ${C.border}`, paddingBottom: 4 }}>Performance Stats</div>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(110px, 1fr))', gap: 10 }}>
          <StatCell label="Avg Win"            value={result.avgWin != null ? formatInr(result.avgWin) : '--'}                         color={C.green} />
          <StatCell label="Avg Loss"           value={result.avgLoss != null ? formatInr(result.avgLoss) : '--'}                       color={C.red} />
          <StatCell label="Max Consec Losses"  value={result.maxConsecutiveLosses?.toString() ?? '--'}                                 color={C.amber} />
          <StatCell label="Win Count"          value={result.winCount?.toString() ?? '--'}                                             color={C.green} />
          <StatCell label="Loss Count"         value={result.lossCount?.toString() ?? '--'}                                            color={C.red} />
          <StatCell label="Final Equity"       value={result.finalEquity != null ? formatInr(result.finalEquity) : '--'}               color={C.blue} />
          <StatCell label="Profit Factor"      value={result.profitFactor != null ? result.profitFactor.toFixed(2) : '--'}             color={result.profitFactor != null && result.profitFactor >= 1.5 ? C.green : C.amber} />
          <StatCell label="Expectancy"         value={result.expectancyPerTrade != null ? formatInr(result.expectancyPerTrade) : '--'} color={result.expectancyPerTrade != null && result.expectancyPerTrade >= 0 ? C.green : C.red} />
          <StatCell label="Max Lots"           value={result.maxLots != null ? String(result.maxLots) : '--'}                          color={C.text} />
          <StatCell label="DD Recovery (bars)" value={result.drawdownRecoveryBars != null ? (result.drawdownRecoveryBars < 0 ? 'Not recovered' : String(result.drawdownRecoveryBars)) : '--'} color={C.amber} />
        </div>
      </div>

      {/* ── Risk-Adjusted Returns ─────────────────────────────────────── */}
      <div>
        <div style={{ fontSize: 10, color: C.textMuted, marginBottom: 8, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.06em', borderBottom: `1px solid ${C.border}`, paddingBottom: 4 }}>Risk-Adjusted Returns</div>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(110px, 1fr))', gap: 10 }}>
          <StatCell label="Sharpe (trade)"  value={result.sharpeRatio != null ? result.sharpeRatio.toFixed(2) : '--'}       color={result.sharpeRatio != null && result.sharpeRatio >= 1 ? C.green : C.amber} />
          <StatCell label="Sharpe (daily)"  value={result.dailySharpe != null ? result.dailySharpe.toFixed(2) : '--'}       color={result.dailySharpe != null && result.dailySharpe >= 1 ? C.green : C.amber} />
          <StatCell label="Sharpe (monthly)"value={result.monthlySharpe != null ? result.monthlySharpe.toFixed(2) : '--'}   color={result.monthlySharpe != null && result.monthlySharpe >= 1 ? C.green : C.amber} />
          <StatCell label="Sortino"         value={result.sortinoRatio != null ? result.sortinoRatio.toFixed(2) : '--'}     color={result.sortinoRatio != null && result.sortinoRatio >= 1 ? C.green : C.amber} />
          <StatCell label="Calmar"          value={result.calmarRatio != null ? result.calmarRatio.toFixed(2) : '--'}       color={C.text} />
          <StatCell label="Monthly Win%"    value={result.monthlyWinRate != null ? `${(result.monthlyWinRate * 100).toFixed(0)}%` : '--'} color={result.monthlyWinRate != null && result.monthlyWinRate >= 0.6 ? C.green : C.amber} />
        </div>
      </div>

      {/* ── Yearly Breakdown ─────────────────────────────────────────── */}
      {yearly.length > 0 && (
        <div>
          <div style={{ fontSize: 10, color: C.textMuted, marginBottom: 8, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.06em', borderBottom: `1px solid ${C.border}`, paddingBottom: 4 }}>
            Year-by-Year  ·  Best: <span style={{ color: C.green }}>{bestYear?.year ?? '--'} ({bestYear ? `+${formatInr(bestYear.pnl)}` : '--'})</span>  ·  Worst: <span style={{ color: C.red }}>{worstYear?.year ?? '--'} ({worstYear ? formatInr(worstYear.pnl) : '--'})</span>  ·  Positive years: <span style={{ color: C.green }}>{zeroLossYears.length}</span>
          </div>
          <div style={{ overflowX: 'auto' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 11 }}>
              <thead>
                <tr style={{ borderBottom: `1px solid ${C.border}` }}>
                  {['Year', 'Net P&L', 'Return', 'Trades', 'Win%'].map(col => (
                    <th key={col} style={{ padding: '3px 8px', textAlign: 'left', color: C.textMuted, fontWeight: 600, fontSize: 9, textTransform: 'uppercase', letterSpacing: '0.06em' }}>{col}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {yearly.map(y => (
                  <tr key={y.year} style={{ borderBottom: `1px solid ${C.border2}`, background: y.pnl > 0 ? '#00200f' : y.pnl < 0 ? '#1a0000' : 'transparent' }}>
                    <td style={{ padding: '3px 8px', color: C.text, fontWeight: 700, fontFamily: 'monospace' }}>{y.year}</td>
                    <td style={{ padding: '3px 8px', color: y.pnl >= 0 ? C.green : C.red, fontWeight: 600, fontFamily: 'monospace' }}>{y.pnl >= 0 ? '+' : ''}{formatInr(y.pnl)}</td>
                    <td style={{ padding: '3px 8px', color: y.return >= 0 ? C.green : C.red, fontFamily: 'monospace' }}>{(y.return * 100).toFixed(1)}%</td>
                    <td style={{ padding: '3px 8px', color: C.text, fontFamily: 'monospace' }}>{y.trades}</td>
                    <td style={{ padding: '3px 8px', color: C.text, fontFamily: 'monospace' }}>{(y.winRate * 100).toFixed(0)}%</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* ── Monthly Breakdown (heat-map style) ───────────────────────── */}
      {monthly.length > 0 && (
        <div>
          <div style={{ fontSize: 10, color: C.textMuted, marginBottom: 8, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.06em', borderBottom: `1px solid ${C.border}`, paddingBottom: 4 }}>Monthly P&L</div>
          <div style={{ overflowX: 'auto' }}>
            <table style={{ borderCollapse: 'collapse', fontSize: 10, minWidth: 600 }}>
              <thead>
                <tr>
                  <th style={{ padding: '3px 8px', textAlign: 'left', color: C.textMuted, fontSize: 9, fontWeight: 600, minWidth: 44 }}>Year</th>
                  {MONTH_NAMES.map(m => (
                    <th key={m} style={{ padding: '3px 6px', textAlign: 'center', color: C.textMuted, fontSize: 9, fontWeight: 600, minWidth: 50 }}>{m}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {Array.from(new Set(monthly.map(m => m.year))).sort().map(year => (
                  <tr key={year}>
                    <td style={{ padding: '2px 8px', color: C.text, fontWeight: 700, fontFamily: 'monospace', fontSize: 10 }}>{year}</td>
                    {Array.from({ length: 12 }, (_, idx) => {
                      const m = monthly.find(x => x.year === year && x.month === idx + 1)
                      const bg = m == null ? 'transparent' : m.pnl > 0 ? `rgba(0,208,122,${Math.min(0.6, Math.abs(m.pnl) / 20000)})` : `rgba(255,71,87,${Math.min(0.6, Math.abs(m.pnl) / 20000)})`
                      return (
                        <td key={idx} title={m ? `${MONTH_NAMES[idx]} ${year}: ${formatInr(m.pnl)} · ${m.trades} trades · ${(m.winRate*100).toFixed(0)}% win` : ''} style={{ padding: '2px 4px', textAlign: 'center', background: bg, borderRadius: 2, fontFamily: 'monospace', color: m == null ? C.textMuted : m.pnl >= 0 ? C.green : C.red, fontSize: 10, cursor: m ? 'default' : 'default' }}>
                          {m ? (m.pnl >= 0 ? '+' : '') + (m.pnl / 1000).toFixed(1) + 'k' : '--'}
                        </td>
                      )
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* ── Trade Log ────────────────────────────────────────────────── */}
      {trades.length > 0 && (
        <div>
          <div style={{ fontSize: 10, color: C.textMuted, marginBottom: 6, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.06em', borderBottom: `1px solid ${C.border}`, paddingBottom: 4 }}>Trade Log ({trades.length} trades)</div>
          <div style={{ overflowX: 'auto', maxHeight: 240, overflowY: 'auto' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 11 }}>
              <thead style={{ position: 'sticky', top: 0, background: C.surface3 }}>
                <tr style={{ borderBottom: `1px solid ${C.border}` }}>
                  {['#', 'Side', 'Entry (IST)', 'Exit (IST)', 'Entry ₹', 'Exit ₹', 'Qty', 'Gross', 'Net', 'Reason'].map(col => (
                    <th key={col} style={{ padding: '4px 6px', textAlign: 'left', color: C.textMuted, fontWeight: 600, fontSize: 9, whiteSpace: 'nowrap' }}>{col}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {trades.map((t: BacktestTradeResult, i: number) => (
                  <tr key={i} style={{ borderBottom: `1px solid ${C.border2}` }}>
                    <td style={{ padding: '3px 6px', color: C.textMuted, fontSize: 10 }}>{i + 1}</td>
                    <td style={{ padding: '3px 6px', color: t.direction === 'Long' || t.direction === 'BUY' ? C.green : C.red, fontWeight: 600, fontSize: 10 }}>{t.direction}</td>
                    <td style={{ padding: '3px 6px', color: C.textSub, fontSize: 10 }}>{formatIst(t.entryTime)}</td>
                    <td style={{ padding: '3px 6px', color: C.textSub, fontSize: 10 }}>{formatIst(t.exitTime)}</td>
                    <td style={{ padding: '3px 6px', color: C.text, fontFamily: "'JetBrains Mono', monospace", fontSize: 10 }}>{formatInr(t.entryPrice)}</td>
                    <td style={{ padding: '3px 6px', color: C.text, fontFamily: "'JetBrains Mono', monospace", fontSize: 10 }}>{formatInr(t.exitPrice)}</td>
                    <td style={{ padding: '3px 6px', color: C.text, fontSize: 10 }}>{t.quantity}</td>
                    <td style={{ padding: '3px 6px', color: t.grossPnl >= 0 ? C.green : C.red, fontFamily: "'JetBrains Mono', monospace", fontSize: 10 }}>{formatInr(t.grossPnl)}</td>
                    <td style={{ padding: '3px 6px', color: t.netPnl >= 0 ? C.green : C.red, fontWeight: 600, fontFamily: "'JetBrains Mono', monospace", fontSize: 10 }}>{formatInr(t.netPnl)}</td>
                    <td style={{ padding: '3px 6px', color: C.textMuted, fontSize: 9 }}>{t.exitReason}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {trades.length === 0 && (
        <p style={{ color: C.textMuted, fontSize: 11 }}>No trade data available for this run.</p>
      )}
    </div>
  )
}

// ──────────────────────────────────────────────────────────────────────────────
// SETTINGS PAGE
// ──────────────────────────────────────────────────────────────────────────────

function SettingsPage() {
  const qc = useQueryClient()
  const [saved, setSaved] = useState(false)
  const [botToken, setBotToken] = useState('')
  const [chatId, setChatId] = useState('')
  const [drawdownPct, setDrawdownPct] = useState('')
  const [alertsEnabled, setAlertsEnabled] = useState(true)

  const { data: settingsResp, isLoading } = useQuery({
    queryKey: ['notification-settings'],
    queryFn: () => settingsApi.getNotifications(),
  })

  // Populate form fields once when settings data first loads.
  // useEffect with [loadedSettings] dependency — runs whenever the query result arrives.
  const loadedSettings = settingsResp?.data?.data
  useEffect(() => {
    if (loadedSettings) {
      setChatId(loadedSettings.telegramChatId ?? '')
      setDrawdownPct(String(loadedSettings.maxDailyDrawdownPct))
      setAlertsEnabled(loadedSettings.alertsEnabled ?? true)
    }
  }, [loadedSettings])

  const saveMutation = useMutation({
    mutationFn: () => settingsApi.updateNotifications({
      telegramBotToken: botToken || undefined,
      telegramChatId: chatId || undefined,
      maxDailyDrawdownPct: drawdownPct ? parseFloat(drawdownPct) : undefined,
      alertsEnabled,
    }),
    onSuccess: () => {
      setSaved(true)
      setBotToken('')
      qc.invalidateQueries({ queryKey: ['notification-settings'] })
      setTimeout(() => setSaved(false), 3000)
    }
  })

  const settings = loadedSettings

  const inputStyle: React.CSSProperties = {
    background: '#13131f',
    border: '1px solid #2d2d3f',
    borderRadius: 4,
    color: '#e2e8f0',
    padding: '8px 12px',
    fontSize: 13,
    width: '100%',
    boxSizing: 'border-box',
  }

  const labelStyle: React.CSSProperties = {
    display: 'block',
    color: '#94a3b8',
    fontSize: 12,
    marginBottom: 6,
    fontWeight: 600,
  }

  return (
    <div>
      <SectionLabel title="Settings" />

      {/* ── Telegram Notifications ── */}
      <div style={{
        backgroundColor: C.surface, border: `1px solid ${C.border}`,
        borderRadius: 6, padding: 16, marginBottom: 12
      }}>
        <h3 style={{ color: C.text, fontSize: 12, fontWeight: 700, marginBottom: 4, marginTop: 0, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
          Telegram Alerts
        </h3>
        <p style={{ color: C.textMuted, fontSize: 11, marginBottom: 16, marginTop: 0 }}>
          Receive real-time alerts for drawdown breaches and broker re-auth failures via Telegram Bot.
        </p>

        {isLoading ? (
          <p style={{ color: C.textMuted, fontSize: 12 }}>Loading…</p>
        ) : (
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
            <div>
              <label style={{ ...labelStyle, color: C.textMuted }}>
                Bot Token
                {settings?.telegramBotTokenMasked && (
                  <span style={{ color: C.blue, marginLeft: 8, fontWeight: 400, fontSize: 11 }}>
                    (current: {settings.telegramBotTokenMasked})
                  </span>
                )}
              </label>
              <input
                type="password"
                placeholder="Leave blank to keep existing token"
                value={botToken}
                onChange={e => setBotToken(e.target.value)}
                style={{ ...inputStyle, background: C.surface2, border: `1px solid ${C.border}`, color: C.text }}
              />
              <p style={{ color: C.textSub, fontSize: 10, marginTop: 4, marginBottom: 0 }}>
                Get your token from @BotFather on Telegram.
              </p>
            </div>

            <div>
              <label style={{ ...labelStyle, color: C.textMuted }}>Chat ID</label>
              <input
                type="text"
                placeholder="e.g. -1001234567890"
                value={chatId}
                onChange={e => setChatId(e.target.value)}
                style={{ ...inputStyle, background: C.surface2, border: `1px solid ${C.border}`, color: C.text }}
              />
              <p style={{ color: C.textSub, fontSize: 10, marginTop: 4, marginBottom: 0 }}>
                Your Telegram chat or channel ID.
              </p>
            </div>
          </div>
        )}
      </div>

      {/* ── Monitoring Thresholds ── */}
      <div style={{
        backgroundColor: C.surface, border: `1px solid ${C.border}`,
        borderRadius: 6, padding: 16, marginBottom: 12
      }}>
        <h3 style={{ color: C.text, fontSize: 12, fontWeight: 700, marginBottom: 4, marginTop: 0, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
          Drawdown Alert Threshold
        </h3>
        <p style={{ color: C.textMuted, fontSize: 11, marginBottom: 16, marginTop: 0 }}>
          Alert fires when a running strategy's unrealized P&L exceeds this % of allocated capital.
        </p>

        <div style={{ display: 'grid', gridTemplateColumns: '160px 1fr', gap: 12, alignItems: 'start' }}>
          <div>
            <label style={{ ...labelStyle, color: C.textMuted }}>Max Daily Drawdown %</label>
            <input
              type="number"
              min={0.1} max={50} step={0.5}
              value={drawdownPct}
              onChange={e => setDrawdownPct(e.target.value)}
              style={{ ...inputStyle, background: C.surface2, border: `1px solid ${C.border}`, color: C.text }}
            />
          </div>
          <div style={{ paddingTop: 26 }}>
            <span style={{ color: C.textSub, fontSize: 11 }}>
              Current: <strong style={{ color: C.text }}>{settings?.maxDailyDrawdownPct ?? 3}%</strong> (default 3%)
            </span>
          </div>
        </div>

        <div style={{ marginTop: 12 }}>
          <label style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer' }}>
            <input
              type="checkbox"
              checked={alertsEnabled}
              onChange={e => setAlertsEnabled(e.target.checked)}
              style={{ width: 14, height: 14, cursor: 'pointer', accentColor: C.blue }}
            />
            <span style={{ color: C.textMuted, fontSize: 12, fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.05em' }}>Enable monitoring alerts</span>
          </label>
        </div>
      </div>

      {/* ── Save Button ── */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
        <button
          onClick={() => saveMutation.mutate()}
          disabled={saveMutation.isPending}
          style={{
            background: C.blue,
            color: 'white',
            border: 'none',
            borderRadius: 4,
            padding: '8px 16px',
            fontSize: 12,
            fontWeight: 700,
            cursor: 'pointer',
            textTransform: 'uppercase',
            letterSpacing: '0.05em',
            opacity: saveMutation.isPending ? 0.7 : 1,
          }}
        >
          {saveMutation.isPending ? 'Saving…' : 'Save Settings'}
        </button>
        {saved && (
          <span style={{ color: C.green, fontSize: 11, fontWeight: 600 }}>
            ✓ Saved
          </span>
        )}
        {saveMutation.isError && (
          <span style={{ color: C.red, fontSize: 11 }}>
            Failed to save
          </span>
        )}
      </div>
    </div>
  )
}

// ──────────────────────────────────────────────────────────────────────────────
// NAVIGATION HELPERS
// ──────────────────────────────────────────────────────────────────────────────

// ── Market Clock — updates every second ────────────────────────────────────────
function MarketClockComponent() {
  const [time, setTime] = useState(new Date())

  useEffect(() => {
    const interval = setInterval(() => setTime(new Date()), 1000)
    return () => clearInterval(interval)
  }, [])

  const istTime = time.toLocaleString('en-IN', {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
    timeZone: 'Asia/Kolkata'
  })

  return (
    <span style={{ fontSize: 11, color: C.textMuted, fontFamily: "'JetBrains Mono', monospace", fontVariantNumeric: 'tabular-nums' }}>
      {istTime} IST
    </span>
  )
}

// ── Market Status Indicator ────────────────────────────────────────────────────
function MarketStatusComponent() {
  return isMarketHours() ? (
    <span style={{
      display: 'inline-flex', alignItems: 'center', gap: 5,
      background: C.greenBg, color: C.green,
      borderRadius: 20, padding: '3px 10px',
      fontSize: 11, fontWeight: 700,
    }}>
      <span style={{ width: 6, height: 6, borderRadius: '50%', background: C.green }} aria-hidden="true" />
      OPEN
    </span>
  ) : (
    <span style={{
      display: 'inline-flex', alignItems: 'center', gap: 5,
      background: C.surface2, color: C.textMuted,
      borderRadius: 20, padding: '3px 10px',
      fontSize: 11, fontWeight: 600,
    }}>
      <span style={{ width: 6, height: 6, borderRadius: '50%', background: C.textMuted }} aria-hidden="true" />
      CLOSED
    </span>
  )
}

// ── SignalR Connection Indicator ────────────────────────────────────────────────
function SignalRIndicator({ connected }: { connected: boolean }) {
  return (
    <span style={{
      display: 'inline-flex', alignItems: 'center', gap: 5,
      fontSize: 11, color: connected ? C.green : C.textMuted
    }}>
      <span style={{ width: 6, height: 6, borderRadius: '50%', background: connected ? C.green : C.textMuted }} aria-hidden="true" />
      {connected ? 'LIVE' : 'offline'}
    </span>
  )
}

// ── Logout Button ───────────────────────────────────────────────────────────────
function LogoutButton() {
  const handleLogout = () => {
    localStorage.removeItem('auth_token')
    window.location.href = '/login'
  }
  return (
    <button
      onClick={handleLogout}
      style={{
        fontSize: 11, color: C.textMuted, background: 'transparent', border: 'none',
        cursor: 'pointer', padding: '4px 8px', textTransform: 'uppercase', fontWeight: 600
      }}
      title="Logout"
    >
      Logout
    </button>
  )
}

// ──────────────────────────────────────────────────────────────────────────────
// SHARED COMPONENTS
// ──────────────────────────────────────────────────────────────────────────────

// ── SectionLabel — per UI_DESIGN_SPEC Section 8 ────────────────────────────────
function SectionLabel({ title, action }: { title: string; action?: React.ReactNode }) {
  return (
    <div style={{
      display: 'flex', justifyContent: 'space-between', alignItems: 'center',
      marginBottom: 8, paddingBottom: 6,
      borderBottom: `1px solid ${C.border}`,
    }}>
      <span style={{ fontSize: 11, fontWeight: 700, color: C.textMuted,
        textTransform: 'uppercase', letterSpacing: '0.08em' }}>
        {title}
      </span>
      {action}
    </div>
  )
}

function FormField({
  label,
  value,
  onChange,
  placeholder = '',
  type = 'text',
  options = [],
}: {
  label: string
  value: string
  onChange: (val: string) => void
  placeholder?: string
  type?: 'text' | 'number' | 'date' | 'select'
  options?: Array<{ value: string; label: string }>
}) {
  return (
    <div style={{ marginBottom: '16px' }}>
      <label style={{ display: 'block', fontSize: '13px', fontWeight: '600', marginBottom: '4px', color: '#8b8b9f' }}>
        {label}
      </label>
      {type === 'select' ? (
        <select
          value={value}
          onChange={(e) => onChange(e.target.value)}
          style={{
            width: '100%',
            padding: '8px 12px',
            backgroundColor: '#2d2d3f',
            color: '#e2e8f0',
            border: '1px solid #3b3b4f',
            borderRadius: '6px',
            fontSize: '13px',
          }}
        >
          {options.map(opt => (
            <option key={opt.value} value={opt.value}>{opt.label}</option>
          ))}
        </select>
      ) : (
        <input
          type={type}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder={placeholder}
          style={{
            width: '100%',
            padding: '8px 12px',
            backgroundColor: '#2d2d3f',
            color: '#e2e8f0',
            border: '1px solid #3b3b4f',
            borderRadius: '6px',
            fontSize: '13px',
            boxSizing: 'border-box',
          }}
        />
      )}
    </div>
  )
}

// ── UserModeToggle ───────────────────────────────────────────────────────────

function UserModeToggle({ mode, onToggle }: { mode: 'guided' | 'pro'; onToggle: () => void }) {
  const isGuided = mode === 'guided'
  return (
    <button
      onClick={onToggle}
      title={isGuided ? 'Switch to Pro mode' : 'Switch to Guided mode'}
      style={{
        display: 'flex', alignItems: 'center', gap: 5,
        padding: '3px 9px', borderRadius: 12,
        background: isGuided ? C.blue22 : C.surface2,
        border: `1px solid ${isGuided ? C.blue44 : C.border3}`,
        color: isGuided ? C.blue : C.textMuted,
        cursor: 'pointer', fontSize: 10, fontWeight: 700,
        letterSpacing: '0.06em', textTransform: 'uppercase',
        transition: 'all 0.15s',
      }}
    >
      {isGuided ? '✦ Guided' : '⚙ Pro'}
    </button>
  )
}
