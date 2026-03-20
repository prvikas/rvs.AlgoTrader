import { useState, useEffect, useRef } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { strategiesApi, brokerApi, ordersApi, instrumentsApi, backtestApi, CreateStrategyCommand, StrategyInstance, BrokerStatus, Order, Instrument } from '../api/client'
import { KillSwitchBanner } from '../components/Dashboard/KillSwitchBanner'
import { ColdRestartBanner } from '../components/Dashboard/ColdRestartBanner'
import { StrategyCard } from '../components/Strategy/StrategyCard'
import { BrokerStatusBar } from '../components/Broker/BrokerStatusBar'
import { formatInr, formatIst, isMarketHours } from '../utils/datetime'
import { useStrategyStream } from '../hooks/useSignalR'

type Page = 'overview' | 'strategies' | 'orders' | 'backtest' | 'settings'

export function Dashboard() {
  const qc = useQueryClient()
  const [activePage, setActivePage] = useState<Page>('overview')
  const [showCreateForm, setShowCreateForm] = useState(false)

  // ── Data Queries ──────────────────────────────────────────────────────────
  const { data: strategies } = useQuery({
    queryKey: ['strategies'],
    queryFn: () => strategiesApi.list().then(r => r.data.data ?? []),
    refetchInterval: 10_000,
  })

  const { data: brokerStatus } = useQuery({
    queryKey: ['broker-status'],
    queryFn: () => brokerApi.status().then(r => r.data.data ?? []),
    refetchInterval: 15_000,
  })

  const { data: orders } = useQuery({
    queryKey: ['orders'],
    queryFn: () => ordersApi.list().then(r => r.data.data ?? []),
    refetchInterval: 10_000,
    enabled: activePage === 'orders' || activePage === 'overview',
  })

  const { signals, isConnected: signalRConnected } = useStrategyStream()
  const marketOpen = isMarketHours()

  const running = strategies?.filter(s => s.status === 'Running') ?? []
  const paused = strategies?.filter(s => s.status === 'Paused') ?? []
  const stopped = strategies?.filter(s => s.status === 'Stopped') ?? []

  // Fall back to localStorage if the API hasn't returned yet (just logged in)
  const sessionBrokerName = localStorage.getItem('active_broker')
  const activeBroker = brokerStatus?.find(b => b.isConnected && b.isAuthenticated)
    ?? (sessionBrokerName ? { brokerName: sessionBrokerName, isConnected: true, isAuthenticated: true, lastCheckedAt: new Date().toISOString() } as BrokerStatus : undefined)
  const allConnected = brokerStatus?.filter(b => b.isConnected && b.isAuthenticated)
    ?? (sessionBrokerName ? [activeBroker!] : [])

  const handleLogout = () => {
    localStorage.removeItem('jwt_token')
    localStorage.removeItem('active_broker')
    window.location.href = '/login'
  }

  // ── Navigation ────────────────────────────────────────────────────────────
  const navItems: Array<{ id: Page; label: string }> = [
    { id: 'overview', label: 'Overview' },
    { id: 'strategies', label: 'Strategies' },
    { id: 'orders', label: 'Orders' },
    { id: 'backtest', label: 'Backtest' },
    { id: 'settings', label: 'Settings' },
  ]

  return (
    <div style={{ minHeight: '100vh', background: '#0f0f1a', color: '#e2e8f0', fontFamily: 'Inter, system-ui, sans-serif' }}>
      <KillSwitchBanner />
      <ColdRestartBanner />

      {/* ── Top Header ─────────────────────────────────────────────────────── */}
      <div style={{ padding: '12px 24px', borderBottom: '1px solid #1e1e2e', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
          <h1 style={{ margin: 0, fontSize: 20, fontWeight: 700 }}>AlgoTrader</h1>
          <span style={{
            background: marketOpen ? '#16a34a22' : '#6b728022',
            color: marketOpen ? '#16a34a' : '#6b7280',
            borderRadius: 12, padding: '2px 10px', fontSize: 11, fontWeight: 700
          }}>
            {marketOpen ? '● MARKET OPEN' : '○ MARKET CLOSED'}
          </span>
        </div>
        <div style={{ display: 'flex', gap: 16, alignItems: 'center', fontSize: 13, color: '#94a3b8' }}>
          <span style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
            <span style={{ width: 8, height: 8, borderRadius: '50%', background: signalRConnected ? '#16a34a' : '#dc2626', display: 'inline-block' }} />
            SignalR {signalRConnected ? 'Connected' : 'Disconnected'}
          </span>
          <BrokerStatusBar />
        </div>
      </div>

      {/* ── Connection & Mode Status Bar ────────────────────────────────────── */}
      <div style={{ padding: '10px 24px', borderBottom: '1px solid #1e1e2e', background: '#13131f', display: 'flex', gap: 24, alignItems: 'center', fontSize: 12 }}>
        {/* Active Broker */}
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <span style={{ color: '#64748b' }}>Active Broker:</span>
          {activeBroker ? (
            <span style={{ color: '#86efac', fontWeight: 700, display: 'flex', alignItems: 'center', gap: 4 }}>
              <span style={{ width: 6, height: 6, borderRadius: '50%', background: '#16a34a', display: 'inline-block' }} />
              {activeBroker.brokerName}
            </span>
          ) : (
            <span style={{ color: '#f87171', fontWeight: 600 }}>None Connected</span>
          )}
        </div>

        {/* All Connected Brokers */}
        {allConnected.length > 1 && (
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <span style={{ color: '#64748b' }}>Also Connected:</span>
            {allConnected.filter(b => b.brokerName !== activeBroker?.brokerName).map(b => (
              <span key={b.brokerName} style={{ color: '#94a3b8', fontWeight: 600 }}>{b.brokerName}</span>
            ))}
          </div>
        )}

        <div style={{ width: 1, height: 16, background: '#2d2d3f' }} />

        {/* Trading Mode Summary */}
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <span style={{ color: '#64748b' }}>Trading:</span>
          {running.length > 0 ? (
            <>
              {running.some(s => s.mode === 'Live') && (
                <span style={{ background: '#dc262622', color: '#f87171', padding: '1px 8px', borderRadius: 4, fontWeight: 700, fontSize: 11 }}>
                  LIVE
                </span>
              )}
              {running.some(s => s.mode === 'Forward') && (
                <span style={{ background: '#f59e0b22', color: '#fbbf24', padding: '1px 8px', borderRadius: 4, fontWeight: 700, fontSize: 11 }}>
                  PAPER
                </span>
              )}
              {running.some(s => s.mode === 'Backtest') && (
                <span style={{ background: '#3b82f622', color: '#60a5fa', padding: '1px 8px', borderRadius: 4, fontWeight: 700, fontSize: 11 }}>
                  BACKTEST
                </span>
              )}
            </>
          ) : (
            <span style={{ color: '#64748b' }}>No active strategies</span>
          )}
        </div>

        <div style={{ width: 1, height: 16, background: '#2d2d3f' }} />

        {/* Quick Stats */}
        <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
          <span style={{ color: '#64748b' }}>Strategies:</span>
          <span style={{ color: '#16a34a', fontWeight: 600 }}>{running.length} running</span>
          <span style={{ color: '#f59e0b', fontWeight: 600 }}>{paused.length} paused</span>
        </div>

        {/* Spacer + Logout */}
        <div style={{ marginLeft: 'auto', display: 'flex', gap: 8 }}>
          <button onClick={handleLogout} style={{
            fontSize: 11, padding: '3px 10px', borderRadius: 4,
            background: 'transparent', border: '1px solid #3d3d5c',
            color: '#94a3b8', cursor: 'pointer',
          }}>
            Logout
          </button>
        </div>
      </div>

      {/* ── Navigation Tabs ─────────────────────────────────────────────────── */}
      <div style={{ padding: '0 24px', borderBottom: '1px solid #1e1e2e', display: 'flex', gap: 0 }}>
        {navItems.map(item => (
          <button
            key={item.id}
            onClick={() => setActivePage(item.id)}
            style={{
              padding: '12px 20px',
              background: 'transparent',
              color: activePage === item.id ? '#e2e8f0' : '#64748b',
              border: 'none',
              borderBottom: activePage === item.id ? '2px solid #3b82f6' : '2px solid transparent',
              fontSize: 13,
              fontWeight: activePage === item.id ? 700 : 500,
              cursor: 'pointer',
              transition: 'all 0.2s'
            }}
          >
            {item.label}
          </button>
        ))}
      </div>

      {/* ── Content Area ────────────────────────────────────────────────────── */}
      <div style={{ padding: 24 }}>
        {activePage === 'overview' && (
          <OverviewPage
            strategies={strategies ?? []}
            orders={orders ?? []}
            signals={signals}
            running={running}
            paused={paused}
            stopped={stopped}
            brokerStatus={brokerStatus ?? []}
            onNavigate={setActivePage}
          />
        )}
        {activePage === 'strategies' && (
          <StrategiesPage
            strategies={strategies ?? []}
            showCreateForm={showCreateForm}
            setShowCreateForm={setShowCreateForm}
            activeBroker={activeBroker?.brokerName}
          />
        )}
        {activePage === 'orders' && <OrdersPage orders={orders ?? []} />}
        {activePage === 'backtest' && <BacktestPage />}
        {activePage === 'settings' && (
          <SettingsPage
            brokerStatus={brokerStatus ?? []}
            signalRConnected={signalRConnected}
            onLogout={handleLogout}
          />
        )}
      </div>
    </div>
  )
}

// ═══════════════════════════════════════════════════════════════════════════════
// ── Overview Page ─────────────────────────────────────────────────────────────
// ═══════════════════════════════════════════════════════════════════════════════

function OverviewPage({ strategies, orders, signals, running, paused, stopped, brokerStatus, onNavigate }: {
  strategies: StrategyInstance[]
  orders: Order[]
  signals: Array<{ symbol: string; signal: string; price?: number; timestamp: string }>
  running: StrategyInstance[]
  paused: StrategyInstance[]
  stopped: StrategyInstance[]
  brokerStatus: BrokerStatus[]
  onNavigate: (page: Page) => void
}) {
  return (
    <div>
      {/* Stats row */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 16, marginBottom: 24 }}>
        {[
          { label: 'Running', value: running.length, color: '#16a34a' },
          { label: 'Paused', value: paused.length, color: '#f59e0b' },
          { label: 'Stopped', value: stopped.length, color: '#6b7280' },
          { label: 'Recent Signals', value: signals.length, color: '#3b82f6' },
        ].map(stat => (
          <div key={stat.label} style={{ background: '#1e1e2e', borderRadius: 8, padding: 16, border: '1px solid #2d2d3f' }}>
            <div style={{ fontSize: 28, fontWeight: 700, color: stat.color }}>{stat.value}</div>
            <div style={{ fontSize: 12, color: '#94a3b8', marginTop: 4 }}>{stat.label}</div>
          </div>
        ))}
      </div>

      {/* Broker Connections */}
      <SectionHeader title="Broker Connections" />
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 16, marginBottom: 32 }}>
        {brokerStatus.map(b => (
          <div key={b.brokerName} style={{
            background: '#1e1e2e', borderRadius: 8, padding: 16, border: '1px solid #2d2d3f',
            borderLeft: b.isConnected && b.isAuthenticated ? '3px solid #16a34a' : '3px solid #dc2626'
          }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <span style={{ fontWeight: 700, fontSize: 14 }}>{b.brokerName}</span>
              <span style={{
                fontSize: 11, fontWeight: 700, padding: '2px 8px', borderRadius: 4,
                background: b.isConnected && b.isAuthenticated ? '#16a34a22' : '#dc262622',
                color: b.isConnected && b.isAuthenticated ? '#86efac' : '#fca5a5',
              }}>
                {b.isConnected && b.isAuthenticated ? 'CONNECTED' : 'DISCONNECTED'}
              </span>
            </div>
            <div style={{ fontSize: 12, color: '#64748b', marginTop: 8 }}>
              Session: {b.isAuthenticated ? 'Valid' : 'Invalid'} · Last check: {formatIst(b.lastCheckedAt)}
            </div>
          </div>
        ))}
        {brokerStatus.length === 0 && (
          <div style={{ color: '#64748b', fontSize: 13, gridColumn: '1 / -1' }}>
            No broker status available. Login to a broker first.
          </div>
        )}
      </div>

      {/* Active Strategies */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
        <SectionHeader title="Strategy Instances" />
        <button onClick={() => onNavigate('strategies')} style={{
          fontSize: 12, padding: '4px 12px', borderRadius: 4,
          background: '#3b82f6', color: '#fff', border: 'none', cursor: 'pointer', fontWeight: 600,
        }}>
          + New Strategy
        </button>
      </div>
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 16, marginBottom: 32 }}>
        {strategies.length === 0 && (
          <div style={{ color: '#64748b', fontSize: 13, padding: 20, background: '#1e1e2e', borderRadius: 8, border: '1px solid #2d2d3f', width: '100%' }}>
            No strategy instances configured. Click <strong>"+ New Strategy"</strong> to create one.
          </div>
        )}
        {strategies.map(s => <StrategyCard key={s.id} instance={s} />)}
      </div>

      {/* Recent Orders */}
      <SectionHeader title="Recent Orders" />
      <div style={{ background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 8, overflow: 'hidden', marginBottom: 32 }}>
        {orders.length === 0 ? (
          <div style={{ padding: 20, color: '#64748b', fontSize: 13, textAlign: 'center' }}>No orders yet.</div>
        ) : (
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
            <thead>
              <tr style={{ background: '#2d2d3f', color: '#94a3b8' }}>
                {['Symbol', 'Direction', 'Type', 'Qty', 'Price', 'Status', 'Time'].map(h => (
                  <th key={h} style={{ padding: '10px 16px', textAlign: 'left', fontWeight: 600 }}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {orders.slice(0, 10).map(o => (
                <tr key={o.id} style={{ borderBottom: '1px solid #2d2d3f' }}>
                  <td style={{ padding: '10px 16px', fontWeight: 600 }}>{o.internalSymbol}</td>
                  <td style={{ padding: '10px 16px' }}>
                    <span style={{ color: o.direction === 'Buy' ? '#16a34a' : '#dc2626', fontWeight: 700 }}>{o.direction}</span>
                  </td>
                  <td style={{ padding: '10px 16px', color: '#94a3b8' }}>{o.orderType}</td>
                  <td style={{ padding: '10px 16px' }}>{o.quantity}</td>
                  <td style={{ padding: '10px 16px' }}>{o.price ? formatInr(o.price) : '--'}</td>
                  <td style={{ padding: '10px 16px' }}>
                    <span style={{
                      fontSize: 11, fontWeight: 700, padding: '1px 6px', borderRadius: 4,
                      background: o.status === 'Filled' ? '#16a34a22' : o.status === 'Rejected' ? '#dc262622' : '#3b82f622',
                      color: o.status === 'Filled' ? '#86efac' : o.status === 'Rejected' ? '#fca5a5' : '#93c5fd',
                    }}>{o.status}</span>
                  </td>
                  <td style={{ padding: '10px 16px', color: '#94a3b8' }}>{formatIst(o.placedAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {/* Live Signals */}
      {signals.length > 0 && (
        <>
          <SectionHeader title="Live Signals" />
          <div style={{ background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 8, overflow: 'hidden' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
              <thead>
                <tr style={{ background: '#2d2d3f', color: '#94a3b8' }}>
                  {['Symbol', 'Signal', 'Price', 'Time'].map(h => (
                    <th key={h} style={{ padding: '10px 16px', textAlign: 'left', fontWeight: 600 }}>{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {signals.slice(0, 20).map((s, i) => (
                  <tr key={i} style={{ borderBottom: '1px solid #2d2d3f' }}>
                    <td style={{ padding: '10px 16px', fontWeight: 600 }}>{s.symbol}</td>
                    <td style={{ padding: '10px 16px' }}>
                      <span style={{
                        color: s.signal === 'BUY' ? '#16a34a' : s.signal === 'SELL' ? '#dc2626' : '#6b7280',
                        fontWeight: 700
                      }}>{s.signal}</span>
                    </td>
                    <td style={{ padding: '10px 16px' }}>{s.price ? formatInr(s.price) : '--'}</td>
                    <td style={{ padding: '10px 16px', color: '#94a3b8' }}>{formatIst(s.timestamp)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  )
}

// ═══════════════════════════════════════════════════════════════════════════════
// ── Strategies Page ───────────────────────────────────────────────────────────
// ═══════════════════════════════════════════════════════════════════════════════

const ALL_STRATEGIES = [
  { name: 'PriceActionBreakout', desc: 'Breakout on price action patterns with support/resistance levels' },
  { name: 'MovingAverageCrossover', desc: 'Dual MA crossover with configurable fast and slow periods' },
  { name: 'RSIMeanReversion', desc: 'Mean reversion using RSI overbought/oversold signals' },
  { name: 'VWAPStrategy', desc: 'Volume-weighted average price based intraday strategy' },
  { name: 'ORBStrategy', desc: 'Opening Range Breakout — trades first 15/30 min range breakout' },
  { name: 'SupertrendFollower', desc: 'Trend following using Supertrend indicator with ATR bands' },
  { name: 'BollingerBandSqueeze', desc: 'Volatility squeeze breakout using Bollinger Bands' },
]

function StrategiesPage({ strategies, showCreateForm, setShowCreateForm, activeBroker }: {
  strategies: StrategyInstance[]
  showCreateForm: boolean
  setShowCreateForm: (v: boolean) => void
  activeBroker?: string
}) {
  const qc = useQueryClient()
  const [selectedStrategies, setSelectedStrategies] = useState<string[]>(['PriceActionBreakout'])
  const [formData, setFormData] = useState({
    name: '',
    internalSymbol: '',
    timeframe: '5m',
    brokerName: activeBroker ?? 'MStock',
    mode: 'Forward',
    allocatedCapital: 10000,
  })
  const [createError, setCreateError] = useState('')
  const [createSuccess, setCreateSuccess] = useState('')
  const [symbolSearch, setSymbolSearch] = useState('')
  const [symbolResults, setSymbolResults] = useState<Instrument[]>([])
  const [showSymbolDropdown, setShowSymbolDropdown] = useState(false)
  const symbolSearchTimer = useRef<ReturnType<typeof setTimeout> | null>(null)

  // Debounced symbol search
  useEffect(() => {
    if (symbolSearch.length < 2) { setSymbolResults([]); return }
    if (symbolSearchTimer.current) clearTimeout(symbolSearchTimer.current)
    symbolSearchTimer.current = setTimeout(async () => {
      try {
        const res = await instrumentsApi.list({ active: true })
        const all = res.data.data ?? []
        const filtered = all.filter(i =>
          i.internalSymbol.toLowerCase().includes(symbolSearch.toLowerCase()) ||
          (i.tradingSymbol?.toLowerCase().includes(symbolSearch.toLowerCase())) ||
          (i.name?.toLowerCase().includes(symbolSearch.toLowerCase()))
        ).slice(0, 10)
        setSymbolResults(filtered)
        setShowSymbolDropdown(true)
      } catch { setSymbolResults([]) }
    }, 300)
  }, [symbolSearch])

  const toggleStrategy = (name: string) => {
    setSelectedStrategies(prev =>
      prev.includes(name) ? prev.filter(s => s !== name) : [...prev, name]
    )
  }

  const createMutation = useMutation({
    mutationFn: (cmd: CreateStrategyCommand) => strategiesApi.create(cmd),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['strategies'] })
      setShowCreateForm(false)
      setCreateSuccess('Strategy instance created successfully!')
      setCreateError('')
      setTimeout(() => setCreateSuccess(''), 3000)
    },
    onError: (err: any) => {
      const msg = err?.response?.data?.error || err?.response?.data?.message || err?.message || 'Failed to create strategy'
      setCreateError(msg)
    },
  })

  const handleCreate = (e: React.FormEvent) => {
    e.preventDefault()
    setCreateError('')
    if (!formData.name.trim()) { setCreateError('Instance name is required'); return }
    if (!formData.internalSymbol.trim()) { setCreateError('Symbol is required'); return }
    if (selectedStrategies.length === 0) { setCreateError('Select at least one strategy'); return }

    // Combine multiple strategies into parametersJson
    const cmd: CreateStrategyCommand = {
      ...formData,
      strategyType: selectedStrategies[0], // primary strategy (maps to backend StrategyType)
      parametersJson: JSON.stringify({
        strategies: selectedStrategies,     // all selected strategies
        combination: selectedStrategies.length > 1 ? 'AND' : 'SINGLE', // AND = all must agree
      }),
    }
    createMutation.mutate(cmd)
  }

  const modeColor = { Live: '#f87171', Forward: '#fbbf24', Backtest: '#60a5fa' }[formData.mode] ?? '#94a3b8'
  const modeBg = { Live: '#dc262622', Forward: '#f59e0b22', Backtest: '#3b82f622' }[formData.mode] ?? '#ffffff11'
  const modeLabel = formData.mode === 'Forward' ? 'PAPER' : formData.mode.toUpperCase()

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 24 }}>
        <h2 style={{ fontSize: 20, fontWeight: 700, margin: 0 }}>Strategy Management</h2>
        <button onClick={() => { setShowCreateForm(!showCreateForm); setCreateError('') }} style={{
          padding: '8px 16px', background: showCreateForm ? '#dc2626' : '#3b82f6', color: '#fff',
          border: 'none', borderRadius: 6, fontSize: 13, fontWeight: 600, cursor: 'pointer',
        }}>
          {showCreateForm ? '✕ Cancel' : '+ Create Strategy Instance'}
        </button>
      </div>

      {createSuccess && (
        <div style={{ background: '#14532d', border: '1px solid #16a34a', color: '#86efac', borderRadius: 6, padding: 12, marginBottom: 16, fontSize: 13 }}>
          ✓ {createSuccess}
        </div>
      )}

      {/* ── Create Form ─────────────────────────────────────────────────────── */}
      {showCreateForm && (
        <div style={{ background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 8, padding: 24, marginBottom: 32 }}>
          <h3 style={{ fontSize: 15, fontWeight: 700, marginBottom: 20 }}>Create New Strategy Instance</h3>

          {createError && (
            <div style={{ background: '#7f1d1d', border: '1px solid #991b1b', color: '#fca5a5', borderRadius: 6, padding: 10, marginBottom: 16, fontSize: 13 }}>
              ✕ {createError}
            </div>
          )}

          <form onSubmit={handleCreate}>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16, marginBottom: 20 }}>
              <FormField label="Instance Name *" value={formData.name} onChange={v => setFormData({ ...formData, name: v })} placeholder="e.g. NIFTY Paper Trade" required />

              {/* Symbol search with autocomplete */}
              <div style={{ position: 'relative' }}>
                <label style={{ fontSize: 12, fontWeight: 600, color: '#94a3b8', display: 'block', marginBottom: 6 }}>Symbol *</label>
                <input
                  type="text"
                  value={symbolSearch || formData.internalSymbol}
                  onChange={e => {
                    setSymbolSearch(e.target.value)
                    setFormData({ ...formData, internalSymbol: e.target.value })
                  }}
                  onFocus={() => symbolSearch.length >= 2 && setShowSymbolDropdown(true)}
                  onBlur={() => setTimeout(() => setShowSymbolDropdown(false), 200)}
                  placeholder="Type to search (e.g. NIFTY, RELIANCE)"
                  style={{ width: '100%', padding: '8px 10px', background: '#0f0f1a', border: '1px solid #2d2d3f', borderRadius: 6, color: '#e2e8f0', fontSize: 13, boxSizing: 'border-box' }}
                  required
                />
                {showSymbolDropdown && symbolResults.length > 0 && (
                  <div style={{ position: 'absolute', top: '100%', left: 0, right: 0, background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 6, zIndex: 100, maxHeight: 200, overflowY: 'auto' }}>
                    {symbolResults.map(inst => (
                      <div
                        key={inst.internalSymbol}
                        onClick={() => {
                          setFormData({ ...formData, internalSymbol: inst.internalSymbol })
                          setSymbolSearch(inst.internalSymbol)
                          setShowSymbolDropdown(false)
                        }}
                        style={{ padding: '8px 12px', cursor: 'pointer', fontSize: 13, borderBottom: '1px solid #2d2d3f' }}
                        onMouseEnter={e => (e.currentTarget.style.background = '#2d2d3f')}
                        onMouseLeave={e => (e.currentTarget.style.background = 'transparent')}
                      >
                        <strong>{inst.internalSymbol}</strong>
                        {inst.name && <span style={{ color: '#64748b', marginLeft: 8, fontSize: 11 }}>{inst.name}</span>}
                        <span style={{ float: 'right', fontSize: 10, color: '#64748b' }}>{inst.exchange}</span>
                      </div>
                    ))}
                  </div>
                )}
                {symbolSearch.length >= 2 && symbolResults.length === 0 && (
                  <div style={{ fontSize: 11, color: '#64748b', marginTop: 4 }}>
                    No instruments found. Type the exact symbol (e.g. NSE:NIFTY50) manually.
                  </div>
                )}
              </div>

              <div>
                <label style={{ fontSize: 12, fontWeight: 600, color: '#94a3b8', display: 'block', marginBottom: 6 }}>Timeframe</label>
                <select value={formData.timeframe} onChange={e => setFormData({ ...formData, timeframe: e.target.value })}
                  style={{ width: '100%', padding: '8px 10px', background: '#0f0f1a', border: '1px solid #2d2d3f', borderRadius: 6, color: '#e2e8f0', fontSize: 13 }}>
                  {['1m', '3m', '5m', '15m', '30m', '1h', '4h', '1D'].map(tf => <option key={tf} value={tf}>{tf}</option>)}
                </select>
              </div>

              <div>
                <label style={{ fontSize: 12, fontWeight: 600, color: '#94a3b8', display: 'block', marginBottom: 6 }}>
                  Trading Mode
                  <span style={{ marginLeft: 8, fontSize: 10, padding: '1px 6px', borderRadius: 3, background: modeBg, color: modeColor, fontWeight: 700 }}>
                    {modeLabel}
                  </span>
                </label>
                <select value={formData.mode} onChange={e => setFormData({ ...formData, mode: e.target.value })}
                  style={{ width: '100%', padding: '8px 10px', background: '#0f0f1a', border: '1px solid #2d2d3f', borderRadius: 6, color: '#e2e8f0', fontSize: 13 }}>
                  <option value="Forward">Paper Trading (Forward Test — no real money)</option>
                  <option value="Live">Live Trading (Real Money ⚠)</option>
                  <option value="Backtest">Backtest Only</option>
                </select>
              </div>

              <div>
                <label style={{ fontSize: 12, fontWeight: 600, color: '#94a3b8', display: 'block', marginBottom: 6 }}>Broker</label>
                <select value={formData.brokerName} onChange={e => setFormData({ ...formData, brokerName: e.target.value })}
                  style={{ width: '100%', padding: '8px 10px', background: '#0f0f1a', border: '1px solid #2d2d3f', borderRadius: 6, color: '#e2e8f0', fontSize: 13 }}>
                  <option value="MStock">MStock</option>
                  <option value="Zerodha">Zerodha</option>
                  <option value="Upstox">Upstox</option>
                </select>
              </div>

              <FormField label="Allocated Capital (₹)" value={String(formData.allocatedCapital)} onChange={v => setFormData({ ...formData, allocatedCapital: Number(v) || 0 })} placeholder="10000" />
            </div>

            {/* Multi-strategy selector */}
            <div style={{ marginBottom: 20 }}>
              <label style={{ fontSize: 12, fontWeight: 600, color: '#94a3b8', display: 'block', marginBottom: 8 }}>
                Strategy Logic *
                <span style={{ marginLeft: 8, fontWeight: 400, color: '#64748b' }}>
                  Select one or more — when multiple selected, ALL must agree before a trade is placed
                </span>
              </label>
              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(260px, 1fr))', gap: 8 }}>
                {ALL_STRATEGIES.map(s => {
                  const selected = selectedStrategies.includes(s.name)
                  return (
                    <div
                      key={s.name}
                      onClick={() => toggleStrategy(s.name)}
                      style={{
                        padding: '10px 12px', borderRadius: 6, cursor: 'pointer',
                        background: selected ? '#1e3a5f' : '#0f0f1a',
                        border: selected ? '1px solid #3b82f6' : '1px solid #2d2d3f',
                        transition: 'all 0.15s',
                      }}
                    >
                      <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 2 }}>
                        <span style={{
                          width: 14, height: 14, borderRadius: 3, flexShrink: 0,
                          background: selected ? '#3b82f6' : 'transparent',
                          border: selected ? 'none' : '1px solid #3d3d5c',
                          display: 'flex', alignItems: 'center', justifyContent: 'center',
                          fontSize: 10, color: '#fff',
                        }}>
                          {selected ? '✓' : ''}
                        </span>
                        <span style={{ fontSize: 13, fontWeight: 600, color: selected ? '#93c5fd' : '#e2e8f0' }}>{s.name}</span>
                      </div>
                      <div style={{ fontSize: 11, color: '#64748b', marginLeft: 22 }}>{s.desc}</div>
                    </div>
                  )
                })}
              </div>
              {selectedStrategies.length > 1 && (
                <div style={{ marginTop: 10, padding: '8px 12px', background: '#1e3a5f22', border: '1px solid #3b82f644', borderRadius: 6, fontSize: 12, color: '#93c5fd' }}>
                  ℹ {selectedStrategies.length} strategies selected — a trade will only be placed when <strong>all {selectedStrategies.length}</strong> agree simultaneously (AND logic)
                </div>
              )}
            </div>

            {formData.mode === 'Live' && (
              <div style={{ background: '#dc262622', border: '1px solid #dc262644', borderRadius: 6, padding: 12, marginBottom: 16, fontSize: 12, color: '#fca5a5' }}>
                ⚠ <strong>LIVE TRADING MODE</strong> — Real money will be used. Ensure your broker session is active and you have sufficient funds before starting.
              </div>
            )}

            <div style={{ display: 'flex', gap: 10, alignItems: 'center' }}>
              <button type="submit" disabled={createMutation.isPending} style={{
                padding: '10px 24px', background: createMutation.isPending ? '#4b5563' : '#3b82f6', color: '#fff',
                border: 'none', borderRadius: 6, fontSize: 13, fontWeight: 600,
                cursor: createMutation.isPending ? 'not-allowed' : 'pointer',
              }}>
                {createMutation.isPending ? 'Creating...' : 'Create Strategy Instance'}
              </button>
              {createMutation.isPending && <span style={{ fontSize: 12, color: '#94a3b8' }}>Saving to database...</span>}
            </div>
          </form>
        </div>
      )}

      {/* ── Active Strategy Instances ──────────────────────────────────────── */}
      <SectionHeader title={`Strategy Instances (${strategies.length})`} />
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 16, marginBottom: 32 }}>
        {strategies.length === 0 && (
          <div style={{ color: '#64748b', fontSize: 13, padding: 20, background: '#1e1e2e', borderRadius: 8, border: '1px solid #2d2d3f', width: '100%' }}>
            No strategy instances yet. Click <strong>"+ Create Strategy Instance"</strong> above to get started.
          </div>
        )}
        {strategies.map(s => <StrategyCard key={s.id} instance={s} />)}
      </div>
    </div>
  )
}

// ═══════════════════════════════════════════════════════════════════════════════
// ── Orders Page ───────────────────────────────────────────────────────────────
// ═══════════════════════════════════════════════════════════════════════════════

function OrdersPage({ orders }: { orders: Order[] }) {
  const [filterStatus, setFilterStatus] = useState<string>('all')

  const filtered = filterStatus === 'all' ? orders : orders.filter(o => o.status === filterStatus)

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 24 }}>
        <h2 style={{ fontSize: 20, fontWeight: 700, margin: 0 }}>Order History</h2>
        <div style={{ display: 'flex', gap: 8 }}>
          {['all', 'Pending', 'Filled', 'Cancelled', 'Rejected'].map(status => (
            <button key={status} onClick={() => setFilterStatus(status)} style={{
              padding: '4px 12px', borderRadius: 4, fontSize: 12, fontWeight: 600, cursor: 'pointer',
              background: filterStatus === status ? '#3b82f6' : 'transparent',
              color: filterStatus === status ? '#fff' : '#94a3b8',
              border: filterStatus === status ? 'none' : '1px solid #2d2d3f',
            }}>
              {status === 'all' ? 'All' : status}
            </button>
          ))}
        </div>
      </div>

      <div style={{ background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 8, overflow: 'hidden' }}>
        {filtered.length === 0 ? (
          <div style={{ padding: 20, color: '#64748b', fontSize: 13, textAlign: 'center' }}>No orders found.</div>
        ) : (
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
            <thead>
              <tr style={{ background: '#2d2d3f', color: '#94a3b8' }}>
                {['Symbol', 'Broker', 'Direction', 'Type', 'Qty', 'Price', 'Fill Price', 'Status', 'Placed At'].map(h => (
                  <th key={h} style={{ padding: '10px 16px', textAlign: 'left', fontWeight: 600 }}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {filtered.map(o => (
                <tr key={o.id} style={{ borderBottom: '1px solid #2d2d3f' }}>
                  <td style={{ padding: '10px 16px', fontWeight: 600 }}>{o.internalSymbol}</td>
                  <td style={{ padding: '10px 16px', color: '#94a3b8' }}>{o.brokerName}</td>
                  <td style={{ padding: '10px 16px' }}>
                    <span style={{ color: o.direction === 'Buy' ? '#16a34a' : '#dc2626', fontWeight: 700 }}>{o.direction}</span>
                  </td>
                  <td style={{ padding: '10px 16px', color: '#94a3b8' }}>{o.orderType}</td>
                  <td style={{ padding: '10px 16px' }}>{o.filledQuantity}/{o.quantity}</td>
                  <td style={{ padding: '10px 16px' }}>{o.price ? formatInr(o.price) : '--'}</td>
                  <td style={{ padding: '10px 16px' }}>{o.fillPrice ? formatInr(o.fillPrice) : '--'}</td>
                  <td style={{ padding: '10px 16px' }}>
                    <span style={{
                      fontSize: 11, fontWeight: 700, padding: '1px 6px', borderRadius: 4,
                      background: o.status === 'Filled' ? '#16a34a22' : o.status === 'Rejected' ? '#dc262622' : '#3b82f622',
                      color: o.status === 'Filled' ? '#86efac' : o.status === 'Rejected' ? '#fca5a5' : '#93c5fd',
                    }}>{o.status}</span>
                  </td>
                  <td style={{ padding: '10px 16px', color: '#94a3b8' }}>{formatIst(o.placedAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  )
}

// ═══════════════════════════════════════════════════════════════════════════════
// ── Backtest Page ─────────────────────────────────────────────────────────────
// ═══════════════════════════════════════════════════════════════════════════════

function BacktestPage() {
  const [selectedStrategies, setSelectedStrategies] = useState<string[]>(['PriceActionBreakout'])
  const [formData, setFormData] = useState({
    internalSymbol: '',
    timeframe: '5m',
    fromDate: '',
    toDate: '',
    initialCapital: 100000,
    riskPerTradePercent: 1,
  })
  const [result, setResult] = useState<any>(null)
  const [error, setError] = useState('')

  const runMutation = useMutation({
    mutationFn: () => backtestApi.run({
      strategyName: selectedStrategies[0],
      parametersJson: JSON.stringify({ strategies: selectedStrategies }),
      internalSymbol: formData.internalSymbol,
      timeframe: formData.timeframe,
      fromDate: formData.fromDate,
      toDate: formData.toDate,
      initialCapital: formData.initialCapital,
      riskPerTradePercent: formData.riskPerTradePercent,
    }),
    onSuccess: (res) => { setResult(res.data.data); setError('') },
    onError: (err: any) => {
      setError(err?.response?.data?.error || err?.response?.data?.message || err?.message || 'Backtest failed')
    },
  })

  const toggleStrategy = (name: string) =>
    setSelectedStrategies(prev => prev.includes(name) ? prev.filter(s => s !== name) : [...prev, name])

  return (
    <div>
      <h2 style={{ fontSize: 20, fontWeight: 700, marginBottom: 24 }}>Backtesting</h2>
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 24 }}>
        {/* Form */}
        <div style={{ background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 8, padding: 24 }}>
          <h3 style={{ fontSize: 14, fontWeight: 700, marginBottom: 16 }}>Configuration</h3>

          {error && (
            <div style={{ background: '#7f1d1d', border: '1px solid #991b1b', color: '#fca5a5', borderRadius: 6, padding: 10, marginBottom: 16, fontSize: 12 }}>
              ✕ {error}
            </div>
          )}

          <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
            {/* Strategy selection */}
            <div>
              <label style={{ fontSize: 12, fontWeight: 600, color: '#94a3b8', display: 'block', marginBottom: 6 }}>
                Strategies (select one or more)
              </label>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                {ALL_STRATEGIES.map(s => {
                  const sel = selectedStrategies.includes(s.name)
                  return (
                    <label key={s.name} style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer', fontSize: 13 }}>
                      <input type="checkbox" checked={sel} onChange={() => toggleStrategy(s.name)}
                        style={{ accentColor: '#3b82f6' }} />
                      <span style={{ color: sel ? '#93c5fd' : '#e2e8f0', fontWeight: sel ? 600 : 400 }}>{s.name}</span>
                    </label>
                  )
                })}
              </div>
            </div>

            <FormField label="Symbol" value={formData.internalSymbol} onChange={v => setFormData({ ...formData, internalSymbol: v })} placeholder="e.g. NSE:NIFTY50" />

            <div>
              <label style={{ fontSize: 12, fontWeight: 600, color: '#94a3b8', display: 'block', marginBottom: 6 }}>Timeframe</label>
              <select value={formData.timeframe} onChange={e => setFormData({ ...formData, timeframe: e.target.value })}
                style={{ width: '100%', padding: '8px 10px', background: '#0f0f1a', border: '1px solid #2d2d3f', borderRadius: 6, color: '#e2e8f0', fontSize: 13 }}>
                {['1m', '5m', '15m', '30m', '1h', '1D'].map(tf => <option key={tf} value={tf}>{tf}</option>)}
              </select>
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
              <div>
                <label style={{ fontSize: 12, fontWeight: 600, color: '#94a3b8', display: 'block', marginBottom: 6 }}>From Date</label>
                <input type="date" value={formData.fromDate} onChange={e => setFormData({ ...formData, fromDate: e.target.value })}
                  style={{ width: '100%', padding: '8px 10px', background: '#0f0f1a', border: '1px solid #2d2d3f', borderRadius: 6, color: '#e2e8f0', fontSize: 13, boxSizing: 'border-box' }} />
              </div>
              <div>
                <label style={{ fontSize: 12, fontWeight: 600, color: '#94a3b8', display: 'block', marginBottom: 6 }}>To Date</label>
                <input type="date" value={formData.toDate} onChange={e => setFormData({ ...formData, toDate: e.target.value })}
                  style={{ width: '100%', padding: '8px 10px', background: '#0f0f1a', border: '1px solid #2d2d3f', borderRadius: 6, color: '#e2e8f0', fontSize: 13, boxSizing: 'border-box' }} />
              </div>
            </div>

            <FormField label="Initial Capital (₹)" value={String(formData.initialCapital)} onChange={v => setFormData({ ...formData, initialCapital: Number(v) || 0 })} placeholder="100000" />
            <FormField label="Risk per Trade (%)" value={String(formData.riskPerTradePercent)} onChange={v => setFormData({ ...formData, riskPerTradePercent: Number(v) || 1 })} placeholder="1" />

            <button
              onClick={() => runMutation.mutate()}
              disabled={runMutation.isPending || !formData.internalSymbol || !formData.fromDate || !formData.toDate || selectedStrategies.length === 0}
              style={{
                padding: '10px 24px', color: '#fff', border: 'none', borderRadius: 6, fontSize: 13, fontWeight: 600,
                background: runMutation.isPending ? '#4b5563' : '#3b82f6',
                cursor: runMutation.isPending ? 'not-allowed' : 'pointer',
                marginTop: 8,
              }}
            >
              {runMutation.isPending ? '⏳ Running Backtest...' : '▶ Run Backtest'}
            </button>
          </div>
        </div>

        {/* Results */}
        <div style={{ background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 8, padding: 24 }}>
          <h3 style={{ fontSize: 14, fontWeight: 700, marginBottom: 16 }}>Results</h3>
          {!result && !runMutation.isPending && (
            <div style={{ color: '#64748b', fontSize: 13, textAlign: 'center', marginTop: 40 }}>
              Configure and run a backtest to see results here.
            </div>
          )}
          {runMutation.isPending && (
            <div style={{ color: '#94a3b8', fontSize: 13, textAlign: 'center', marginTop: 40 }}>
              ⏳ Running backtest, please wait...
            </div>
          )}
          {result && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              {[
                { label: 'Total P&L', value: formatInr(result.totalPnl), color: result.totalPnl >= 0 ? '#86efac' : '#fca5a5' },
                { label: 'Total Return', value: `${result.totalReturn?.toFixed(2)}%`, color: result.totalReturn >= 0 ? '#86efac' : '#fca5a5' },
                { label: 'Win Rate', value: `${result.winRate?.toFixed(1)}%`, color: result.winRate >= 50 ? '#86efac' : '#fbbf24' },
                { label: 'Total Trades', value: result.totalTrades, color: '#e2e8f0' },
                { label: 'Max Drawdown', value: `${result.maxDrawdown?.toFixed(2)}%`, color: '#fca5a5' },
                { label: 'Sharpe Ratio', value: result.sharpeRatio?.toFixed(2), color: result.sharpeRatio >= 1 ? '#86efac' : '#fbbf24' },
              ].map(item => (
                <div key={item.label} style={{ display: 'flex', justifyContent: 'space-between', padding: '8px 0', borderBottom: '1px solid #2d2d3f' }}>
                  <span style={{ fontSize: 13, color: '#94a3b8' }}>{item.label}</span>
                  <span style={{ fontSize: 14, fontWeight: 700, color: item.color }}>{item.value}</span>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

// ═══════════════════════════════════════════════════════════════════════════════
// ── Settings Page ─────────────────────────────────────────────────────────────
// ═══════════════════════════════════════════════════════════════════════════════

function SettingsPage({ brokerStatus, signalRConnected, onLogout }: {
  brokerStatus: BrokerStatus[]
  signalRConnected: boolean
  onLogout: () => void
}) {
  const qc = useQueryClient()
  const [refreshStatus, setRefreshStatus] = useState<'idle' | 'loading' | 'success' | 'error'>('idle')
  const [refreshMsg, setRefreshMsg] = useState('')

  // Instrument count query
  const { data: instrumentData } = useQuery({
    queryKey: ['instruments-count'],
    queryFn: () => instrumentsApi.list({ active: true }),
    refetchInterval: 30000,
  })
  const instrumentCount = instrumentData?.data?.data?.length ?? 0

  const handleRefresh = async (broker = 'all') => {
    setRefreshStatus('loading')
    setRefreshMsg('')
    try {
      await instrumentsApi.refresh(broker)
      setRefreshStatus('success')
      setRefreshMsg(`Master data refresh started for ${broker === 'all' ? 'all brokers' : broker}. Symbols will be updated within 30 seconds.`)
      qc.invalidateQueries({ queryKey: ['instruments-count'] })
    } catch (err: any) {
      setRefreshStatus('error')
      setRefreshMsg(err?.response?.data?.error || 'Refresh failed. Check broker session.')
    }
  }

  return (
    <div>
      <h2 style={{ fontSize: 20, fontWeight: 700, marginBottom: 24 }}>Settings</h2>
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16 }}>
        {/* Broker Connections */}
        <div style={{ background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 8, padding: 24 }}>
          <h3 style={{ fontSize: 14, fontWeight: 600, marginBottom: 16 }}>Broker Connections</h3>
          {brokerStatus.map(b => (
            <div key={b.brokerName} style={{ display: 'flex', justifyContent: 'space-between', padding: '8px 0', borderBottom: '1px solid #2d2d3f' }}>
              <span style={{ fontSize: 13 }}>{b.brokerName}</span>
              <span style={{
                fontSize: 12, fontWeight: 700,
                color: b.isConnected && b.isAuthenticated ? '#86efac' : '#fca5a5',
              }}>
                {b.isConnected && b.isAuthenticated ? 'Connected' : 'Disconnected'}
              </span>
            </div>
          ))}
        </div>

        {/* System Status */}
        <div style={{ background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 8, padding: 24 }}>
          <h3 style={{ fontSize: 14, fontWeight: 600, marginBottom: 16 }}>System Status</h3>
          <div style={{ display: 'flex', justifyContent: 'space-between', padding: '8px 0', borderBottom: '1px solid #2d2d3f' }}>
            <span style={{ fontSize: 13 }}>API Server</span>
            <span style={{ fontSize: 12, fontWeight: 700, color: '#86efac' }}>Connected</span>
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between', padding: '8px 0', borderBottom: '1px solid #2d2d3f' }}>
            <span style={{ fontSize: 13 }}>WebSocket (SignalR)</span>
            <span style={{ fontSize: 12, fontWeight: 700, color: signalRConnected ? '#86efac' : '#fca5a5' }}>
              {signalRConnected ? 'Connected' : 'Disconnected'}
            </span>
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between', padding: '8px 0', borderBottom: '1px solid #2d2d3f' }}>
            <span style={{ fontSize: 13 }}>Default Broker</span>
            <span style={{ fontSize: 12, fontWeight: 700, color: '#86efac' }}>MStock</span>
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between', padding: '8px 0' }}>
            <span style={{ fontSize: 13 }}>Instruments Loaded</span>
            <span style={{ fontSize: 12, fontWeight: 700, color: instrumentCount > 0 ? '#86efac' : '#fca5a5' }}>
              {instrumentCount > 0 ? instrumentCount.toLocaleString() : 'Not loaded'}
            </span>
          </div>
        </div>

        {/* Master Data / Scrip Master */}
        <div style={{ background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 8, padding: 24, gridColumn: '1 / -1' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
            <div>
              <h3 style={{ fontSize: 14, fontWeight: 600, margin: 0 }}>Master Data (Scrip Master)</h3>
              <p style={{ fontSize: 12, color: '#94a3b8', margin: '4px 0 0 0' }}>
                Downloads all tradeable symbols and tokens from the broker — same as what OpenAlgo does on every login.
                Happens automatically after login and daily at 08:00 IST. Refresh manually if symbols are missing.
              </p>
            </div>
            <div style={{ display: 'flex', gap: 8, flexShrink: 0 }}>
              {brokerStatus.filter(b => b.isConnected && b.isAuthenticated).map(b => (
                <button
                  key={b.brokerName}
                  onClick={() => handleRefresh(b.brokerName)}
                  disabled={refreshStatus === 'loading'}
                  style={{
                    padding: '7px 14px', background: '#3b82f6', color: '#fff',
                    border: 'none', borderRadius: 6, fontSize: 12, fontWeight: 600,
                    cursor: refreshStatus === 'loading' ? 'not-allowed' : 'pointer',
                    opacity: refreshStatus === 'loading' ? 0.6 : 1,
                  }}
                >
                  {refreshStatus === 'loading' ? '⟳ Refreshing…' : `↓ Refresh ${b.brokerName}`}
                </button>
              ))}
              <button
                onClick={() => handleRefresh('all')}
                disabled={refreshStatus === 'loading'}
                style={{
                  padding: '7px 14px', background: '#6366f1', color: '#fff',
                  border: 'none', borderRadius: 6, fontSize: 12, fontWeight: 600,
                  cursor: refreshStatus === 'loading' ? 'not-allowed' : 'pointer',
                  opacity: refreshStatus === 'loading' ? 0.6 : 1,
                }}
              >
                {refreshStatus === 'loading' ? '⟳ Refreshing…' : '↓ Refresh All'}
              </button>
            </div>
          </div>

          {/* Status message */}
          {refreshMsg && (
            <div style={{
              padding: '8px 12px', borderRadius: 6, fontSize: 12, marginTop: 8,
              background: refreshStatus === 'success' ? '#14532d' : '#7f1d1d',
              border: `1px solid ${refreshStatus === 'success' ? '#16a34a' : '#dc2626'}`,
              color: refreshStatus === 'success' ? '#86efac' : '#fca5a5',
            }}>
              {refreshStatus === 'success' ? '✓' : '✕'} {refreshMsg}
            </div>
          )}

          {/* Info grid */}
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 12, marginTop: 16 }}>
            <div style={{ background: '#0f172a', borderRadius: 6, padding: 12 }}>
              <div style={{ fontSize: 11, color: '#64748b', marginBottom: 4 }}>INSTRUMENTS LOADED</div>
              <div style={{ fontSize: 22, fontWeight: 700, color: instrumentCount > 0 ? '#60a5fa' : '#64748b' }}>
                {instrumentCount > 0 ? instrumentCount.toLocaleString() : '—'}
              </div>
            </div>
            <div style={{ background: '#0f172a', borderRadius: 6, padding: 12 }}>
              <div style={{ fontSize: 11, color: '#64748b', marginBottom: 4 }}>AUTO REFRESH</div>
              <div style={{ fontSize: 13, fontWeight: 600, color: '#86efac' }}>Daily 08:00 IST</div>
              <div style={{ fontSize: 11, color: '#64748b' }}>+ on every login</div>
            </div>
            <div style={{ background: '#0f172a', borderRadius: 6, padding: 12 }}>
              <div style={{ fontSize: 11, color: '#64748b', marginBottom: 4 }}>EXCHANGES</div>
              <div style={{ fontSize: 13, fontWeight: 600, color: '#e2e8f0' }}>NSE · BSE · NFO</div>
              <div style={{ fontSize: 11, color: '#64748b' }}>BFO · MCX · CDS</div>
            </div>
          </div>
        </div>

        {/* Account */}
        <div style={{ background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 8, padding: 24 }}>
          <h3 style={{ fontSize: 14, fontWeight: 600, marginBottom: 16 }}>Account</h3>
          <button onClick={onLogout} style={{
            padding: '8px 16px', background: '#dc2626', color: '#fff',
            border: 'none', borderRadius: 6, fontSize: 13, fontWeight: 600, cursor: 'pointer',
          }}>
            Logout
          </button>
        </div>
      </div>
    </div>
  )
}

// ═══════════════════════════════════════════════════════════════════════════════
// ── Shared Components ─────────────────────────────────────────────────────────
// ═══════════════════════════════════════════════════════════════════════════════

function SectionHeader({ title }: { title: string }) {
  return (
    <h2 style={{ fontSize: 13, fontWeight: 700, marginBottom: 12, color: '#94a3b8', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
      {title}
    </h2>
  )
}

function FormField({ label, value, onChange, placeholder, required }: {
  label: string; value: string; onChange: (v: string) => void; placeholder?: string; required?: boolean
}) {
  return (
    <div>
      <label style={{ fontSize: 12, fontWeight: 600, color: '#94a3b8', display: 'block', marginBottom: 6 }}>{label}</label>
      <input
        type="text"
        value={value}
        onChange={e => onChange(e.target.value)}
        placeholder={placeholder}
        required={required}
        style={{ width: '100%', padding: '8px 10px', background: '#0f0f1a', border: '1px solid #2d2d3f', borderRadius: 6, color: '#e2e8f0', fontSize: 13, boxSizing: 'border-box' }}
      />
    </div>
  )
}
