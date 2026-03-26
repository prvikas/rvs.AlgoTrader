import { useState, useEffect, useCallback, Fragment } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  strategiesApi, brokerApi, ordersApi, backtestApi, killSwitchApi, settingsApi,
  CreateStrategyCommand, BrokerStatus, Order,
  BacktestResult, BacktestTradeResult
} from '../api/client'
import { EquityCurveChart } from '../components/Backtest/EquityCurveChart'
import { KillSwitchBanner } from '../components/Dashboard/KillSwitchBanner'
import { ColdRestartBanner } from '../components/Dashboard/ColdRestartBanner'
import { StrategyCard } from '../components/Strategy/StrategyCard'
import { BrokerStatusBar } from '../components/Broker/BrokerStatusBar'
import { SymbolSearchInput } from '../components/Strategy/SymbolSearchInput'
import { ScheduleEditor, ScheduleConfig, defaultScheduleJson } from '../components/Strategy/ScheduleEditor'
import { StrategyParamsEditor, paramsToJson, defaultParams } from '../components/Strategy/StrategyParamsEditor'
import { FailureBehaviorEditor, FailureBehaviorConfig, defaultFailureBehavior, failureBehaviorToJson } from '../components/Strategy/FailureBehaviorEditor'
import { InstrumentsPage } from './InstrumentsPage'
import { ForwardTestPage } from './ForwardTestPage'
import { StrategyLabPage } from './StrategyLabPage'
import { UniversePage } from './UniversePage'
import { PortfolioOverview } from '../components/Portfolio/PortfolioOverview'
import { PromoteToForwardTestModal } from '../components/ForwardTest/PromoteToForwardTestModal'
import { formatInr, formatIst, isMarketHours } from '../utils/datetime'
import { useStrategyStream } from '../hooks/useSignalR'

type Page = 'portfolio' | 'strategies' | 'orders' | 'lab' | 'backtest' | 'forwardtest' | 'instruments' | 'universe' | 'settings'

const ALL_STRATEGIES = [
  { name: 'PriceActionBreakout', desc: 'Consolidation range breakout with volume confirmation', comingSoon: false },
  { name: 'EmaVwapMomentum', desc: 'EMA golden/death cross + VWAP + Bollinger Bands + volume + optional option chain PCR filter', comingSoon: false },
  { name: 'AlertCandleShort', desc: 'BankNifty/Nifty short: Alert Candle (low > 5-EMA) breakout · 1:3 RRR · one trade/day', comingSoon: false },
  { name: 'VWAPStrategy', desc: 'VWAP-based intraday strategy', comingSoon: true },
  { name: 'ORBStrategy', desc: 'Opening Range Breakout', comingSoon: true },
]

export function Dashboard() {
  const [activePage, setActivePage] = useState<Page>('portfolio')
  const activeBrokerName = localStorage.getItem('active_broker') || 'MStock'

  // ── Data Queries ──────────────────────────────────────────────────────────
  const { data: brokerStatus } = useQuery({
    queryKey: ['broker-status'],
    queryFn: () => brokerApi.status().then(r => Array.isArray(r.data.data) ? r.data.data : []),
    refetchInterval: 15_000,
  })

  const { data: orders } = useQuery({
    queryKey: ['orders'],
    queryFn: () => ordersApi.list().then(r => Array.isArray(r.data.data) ? r.data.data : []),
    refetchInterval: 10_000,
    enabled: activePage === 'orders' || activePage === 'portfolio',
  })

  const { data: killSwitchStatus } = useQuery({
    queryKey: ['kill-switch'],
    queryFn: () => killSwitchApi.status().then(r => r.data.data),
    refetchInterval: 30_000, // KillSwitchBanner also polls at 30s — no need for fast polling here
  })

  const { data: backtestResults } = useQuery({
    queryKey: ['backtest-results'],
    queryFn: () => backtestApi.list().then(r => Array.isArray(r.data.data) ? r.data.data : []),
    enabled: activePage === 'backtest' || activePage === 'lab',
    refetchInterval: 30_000,
  })

  const { isConnected: signalRConnected, coldRestartPaused } = useStrategyStream()

  return (
    <div style={{ display: 'flex', height: '100vh', backgroundColor: '#0f0f1a', color: '#e2e8f0', fontFamily: 'system-ui, sans-serif' }}>
      {/* Sidebar Navigation */}
      <div style={{ width: '200px', backgroundColor: '#1e1e2e', borderRight: '1px solid #2d2d3f', padding: '20px 0', overflowY: 'auto' }}>
        <div style={{ padding: '0 16px', marginBottom: '20px' }}>
          <h2 style={{ fontSize: '18px', fontWeight: 'bold', margin: '0 0 8px 0' }}>AlgoTrader</h2>
          <p style={{ fontSize: '12px', color: '#8b8b9f', margin: 0 }}>Trading Platform</p>
        </div>

        <nav aria-label="Main navigation" style={{ display: 'flex', flexDirection: 'column', gap: 0, padding: '0 8px' }}>
          {/* ── Live Trading ── */}
          <NavSectionLabel label="LIVE TRADING" />
          <NavItem page="portfolio" label="Portfolio" icon="📊" active={activePage} onClick={setActivePage} />
          <NavItem page="strategies" label="Strategies" icon="⚡" active={activePage} onClick={setActivePage} />
          <NavItem page="orders" label="Orders" icon="📋" active={activePage} onClick={setActivePage} />

          {/* ── Research ── */}
          <NavSectionLabel label="RESEARCH" />
          <NavItem page="lab" label="Strategy Lab" icon="🔬" active={activePage} onClick={setActivePage} accent="#10b981" />
          <NavItem page="backtest" label="Backtest" icon="📈" active={activePage} onClick={setActivePage} />
          <NavItem page="forwardtest" label="Forward Test" icon="🧪" active={activePage} onClick={setActivePage} />

          {/* ── Setup ── */}
          <NavSectionLabel label="SETUP" />
          <NavItem page="instruments" label="Instruments" icon="🔍" active={activePage} onClick={setActivePage} />
          <NavItem page="universe" label="Universe" icon="🌐" active={activePage} onClick={setActivePage} />
          <NavItem page="settings" label="Settings" icon="⚙" active={activePage} onClick={setActivePage} />
        </nav>
      </div>

      {/* Main Content */}
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
        {/* Header */}
        <div style={{ backgroundColor: '#1e1e2e', borderBottom: '1px solid #2d2d3f', padding: '14px 20px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <div>
            <h1 style={{ margin: 0, fontSize: '18px', fontWeight: 700 }}>
              {{ portfolio: 'Portfolio', strategies: 'Strategies', orders: 'Orders', lab: 'Strategy Lab', backtest: 'Backtest', forwardtest: 'Forward Test', instruments: 'Instruments', settings: 'Settings' }[activePage]}
            </h1>
            <p style={{ margin: '3px 0 0 0', fontSize: '11px', color: signalRConnected ? '#10b981' : '#6b7280' }}>
              {signalRConnected ? '● Live' : '○ Disconnected'}
            </p>
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 16 }}>
            {/* Broker connection status inline */}
            <BrokerStatusBar />

            {/* Divider */}
            <div style={{ width: 1, height: 20, background: '#2d2d3f' }} aria-hidden="true" />

            {/* Market hours indicator — always visible, color-coded */}
            {isMarketHours() ? (
              <span style={{
                display: 'inline-flex', alignItems: 'center', gap: 5,
                background: '#14532d', color: '#86efac',
                borderRadius: 20, padding: '3px 10px',
                fontSize: 11, fontWeight: 700,
              }}>
                <span style={{ width: 6, height: 6, borderRadius: '50%', background: '#16a34a' }} aria-hidden="true" />
                Market Open
              </span>
            ) : (
              <span style={{
                display: 'inline-flex', alignItems: 'center', gap: 5,
                background: '#1c1c2e', color: '#6b7280',
                borderRadius: 20, padding: '3px 10px',
                fontSize: 11, fontWeight: 600,
              }}>
                <span style={{ width: 6, height: 6, borderRadius: '50%', background: '#4b5563' }} aria-hidden="true" />
                Market Closed
              </span>
            )}
          </div>
        </div>

        {/* Kill Switch Banner — full-width critical alert */}
        {killSwitchStatus === true && <KillSwitchBanner />}

        {/* Cold Restart Banner — informational notice */}
        <ColdRestartBanner coldRestartPaused={coldRestartPaused} />

        {/* Content Area */}
        <div style={{ flex: 1, overflowY: 'auto', padding: '20px' }}>
          {activePage === 'portfolio' && <PortfolioOverview />}
          {activePage === 'strategies' && <StrategiesPage activeBroker={activeBrokerName} brokerStatus={brokerStatus ?? []} />}
          {activePage === 'orders' && <OrdersPage orders={orders ?? []} />}
          {activePage === 'lab' && <StrategyLabPage />}
          {activePage === 'backtest' && <BacktestPage backtestResults={backtestResults ?? []} />}
          {activePage === 'forwardtest' && <ForwardTestPage />}
          {activePage === 'instruments' && <InstrumentsPage />}
          {activePage === 'universe' && <UniversePage />}
          {activePage === 'settings' && <SettingsPage />}
        </div>
      </div>

    </div>
  )
}

// ──────────────────────────────────────────────────────────────────────────────
// OVERVIEW PAGE
// ──────────────────────────────────────────────────────────────────────────────

// OverviewPage replaced by PortfolioOverview component (imported from components/Portfolio)

// ──────────────────────────────────────────────────────────────────────────────
// STRATEGIES PAGE
// ──────────────────────────────────────────────────────────────────────────────

function StrategiesPage({ activeBroker, brokerStatus }: { activeBroker: string; brokerStatus: BrokerStatus[] }) {
  const qc = useQueryClient()
  const [showForm, setShowForm] = useState(false)
  const [selectedStrategy, setSelectedStrategy] = useState('AlertCandleShort')
  const [formData, setFormData] = useState({
    name: '',
    internalSymbol: '',
    timeframe: '5m',
    mode: 'Forward' as 'Live' | 'Forward' | 'Backtest',
    brokerName: activeBroker,
    allocatedCapital: 50000,
  })
  const [strategyParams, setStrategyParams] = useState<Record<string, unknown>>(defaultParams('AlertCandleShort'))
  const [scheduleConfig, setScheduleConfig] = useState<ScheduleConfig | undefined>(undefined)
  const [failureBehavior, setFailureBehavior] = useState<FailureBehaviorConfig>(defaultFailureBehavior())
  const [errorMsg, setErrorMsg] = useState('')
  const [successMsg, setSuccessMsg] = useState('')
  const [liveConfirmOpen, setLiveConfirmOpen] = useState(false)
  const [pendingCmd, setPendingCmd] = useState<CreateStrategyCommand | null>(null)

  const { data: strategies } = useQuery({
    queryKey: ['strategies'],
    queryFn: () => strategiesApi.list().then(r => Array.isArray(r.data.data) ? r.data.data : []),
    refetchInterval: 10_000,
  })

  const createMutation = useMutation({
    mutationFn: (cmd: CreateStrategyCommand) => strategiesApi.create(cmd),
    onSuccess: () => {
      setSuccessMsg('Strategy created successfully!')
      setShowForm(false)
      qc.invalidateQueries({ queryKey: ['strategies'] })
      setTimeout(() => setSuccessMsg(''), 3000)
    },
    onError: (err: any) => {
      setErrorMsg(err.response?.data?.error || 'Failed to create strategy')
    }
  })

  const handleSelectStrategy = (name: string) => {
    setSelectedStrategy(name)
    setStrategyParams(defaultParams(name))
  }

  const handleCreateStrategy = () => {
    if (!formData.name.trim()) {
      setErrorMsg('Instance name is required')
      return
    }
    if (!formData.internalSymbol) {
      setErrorMsg('Symbol is required')
      return
    }
    if (!selectedStrategy) {
      setErrorMsg('Strategy type is required')
      return
    }

    // Broker-auth guard: warn if Live mode selected but no broker is authenticated
    if (formData.mode === 'Live') {
      const selectedBroker = brokerStatus.find(b => b.brokerName === formData.brokerName)
      if (!selectedBroker?.isAuthenticated) {
        setErrorMsg(`Broker "${formData.brokerName}" is not authenticated. Please connect it in the broker settings before creating a Live instance.`)
        return
      }
    }

    const cmd: CreateStrategyCommand = {
      name: formData.name,
      strategyType: selectedStrategy,
      internalSymbol: formData.internalSymbol,
      timeframe: formData.timeframe,
      mode: formData.mode,
      brokerName: formData.brokerName,
      parametersJson: paramsToJson(strategyParams),
      scheduleJson: scheduleConfig ? JSON.stringify(scheduleConfig) : defaultScheduleJson(),
      failureBehaviorJson: failureBehaviorToJson(failureBehavior),
      allocatedCapital: formData.allocatedCapital,
    }

    // Live mode: require explicit confirmation before creating
    if (formData.mode === 'Live') {
      setPendingCmd(cmd)
      setLiveConfirmOpen(true)
      return
    }

    createMutation.mutate(cmd)
  }

  const handleLiveConfirm = () => {
    if (pendingCmd) {
      createMutation.mutate(pendingCmd)
    }
    setLiveConfirmOpen(false)
    setPendingCmd(null)
  }

  return (
    <div>
      <SectionHeader
        title="Strategy Instances"
        action={
          <button
            onClick={() => setShowForm(!showForm)}
            style={{
              padding: '8px 16px',
              backgroundColor: '#3b82f6',
              color: '#fff',
              border: 'none',
              borderRadius: '6px',
              cursor: 'pointer',
              fontSize: '14px',
            }}
          >
            {showForm ? 'Close' : '+ New Instance'}
          </button>
        }
      />

      {showForm && (
        <div style={{ backgroundColor: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: '8px', padding: '20px', marginBottom: '20px' }}>
          <h3 style={{ margin: '0 0 16px 0', fontSize: '16px', fontWeight: 'bold' }}>Create New Instance</h3>

          {errorMsg && <div style={{ backgroundColor: '#7f1d1d', color: '#fecaca', padding: '12px', borderRadius: '6px', marginBottom: '12px', fontSize: '14px' }}>{errorMsg}</div>}
          {successMsg && <div style={{ backgroundColor: '#065f46', color: '#a7f3d0', padding: '12px', borderRadius: '6px', marginBottom: '12px', fontSize: '14px' }}>{successMsg}</div>}

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '20px' }}>
            {/* Left Column: Configuration */}
            <div>
              <FormField
                label="Instance Name"
                value={formData.name}
                onChange={(v) => setFormData({ ...formData, name: v })}
                placeholder="e.g., RELIANCE Breakout #1"
              />

              <div style={{ marginBottom: '16px' }}>
                <label style={{ display: 'block', fontSize: '13px', fontWeight: '600', marginBottom: '8px', color: '#8b8b9f' }}>Strategy Type</label>
                <div style={{ display: 'grid', gap: '8px', maxHeight: '200px', overflowY: 'auto' }}>
                  {ALL_STRATEGIES.map(strat => (
                    <button
                      key={strat.name}
                      onClick={() => !strat.comingSoon && handleSelectStrategy(strat.name)}
                      disabled={strat.comingSoon}
                      title={strat.comingSoon ? 'Coming soon — not yet available' : undefined}
                      style={{
                        padding: '12px',
                        backgroundColor: selectedStrategy === strat.name ? '#3b82f6' : '#2d2d3f',
                        color: strat.comingSoon ? '#4b5563' : '#e2e8f0',
                        border: selectedStrategy === strat.name ? '2px solid #60a5fa' : '1px solid #2d2d3f',
                        borderRadius: '6px',
                        cursor: strat.comingSoon ? 'not-allowed' : 'pointer',
                        textAlign: 'left',
                        fontSize: '13px',
                        transition: 'all 0.2s',
                        opacity: strat.comingSoon ? 0.45 : 1,
                      }}
                    >
                      <div style={{ fontWeight: '600', display: 'flex', alignItems: 'center', gap: 6 }}>
                        {strat.name}
                        {strat.comingSoon && (
                          <span style={{ fontSize: '10px', background: '#374151', color: '#6b7280', borderRadius: 4, padding: '1px 5px', fontWeight: 500 }}>
                            Soon
                          </span>
                        )}
                      </div>
                      <div style={{ fontSize: '12px', color: '#b0b0c0', marginTop: '4px' }}>{strat.desc}</div>
                    </button>
                  ))}
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
                label="Mode"
                type="select"
                value={formData.mode}
                onChange={(v) => setFormData({ ...formData, mode: v as 'Live' | 'Forward' | 'Backtest' })}
                options={[
                  { value: 'Live', label: 'Live Trading' },
                  { value: 'Forward', label: 'Forward Test' },
                  { value: 'Backtest', label: 'Backtest' },
                ]}
              />

              <FormField
                label="Broker"
                type="select"
                value={formData.brokerName}
                onChange={(v) => setFormData({ ...formData, brokerName: v })}
                options={
                  brokerStatus.length > 0
                    ? brokerStatus.map(b => ({ value: b.brokerName, label: b.brokerName }))
                    : [
                        { value: 'MStock', label: 'MStock' },
                        { value: 'Zerodha', label: 'Zerodha' },
                        { value: 'Upstox', label: 'Upstox' },
                      ]
                }
              />

              <FormField
                label="Allocated Capital (₹)"
                type="number"
                value={formData.allocatedCapital.toString()}
                onChange={(v) => setFormData({ ...formData, allocatedCapital: parseInt(v) || 50000 })}
              />
            </div>

            {/* Right Column: Schedule & Parameters */}
            <div>
              <div style={{ marginBottom: '16px' }}>
                <label style={{ display: 'block', fontSize: '13px', fontWeight: '600', marginBottom: '8px', color: '#8b8b9f' }}>Schedule</label>
                <ScheduleEditor
                  value={scheduleConfig}
                  onChange={setScheduleConfig}
                />
              </div>

              <div>
                <label style={{ display: 'block', fontSize: '13px', fontWeight: '600', marginBottom: '8px', color: '#8b8b9f' }}>Strategy Parameters</label>
                <StrategyParamsEditor
                  strategyName={selectedStrategy}
                  value={strategyParams}
                  onChange={setStrategyParams}
                />
              </div>

              <div style={{ marginTop: 16 }}>
                <label style={{ display: 'block', fontSize: '13px', fontWeight: '600', marginBottom: '8px', color: '#8b8b9f' }}>Failure Behavior</label>
                <FailureBehaviorEditor
                  value={failureBehavior}
                  onChange={setFailureBehavior}
                />
              </div>
            </div>
          </div>

          {/* Live mode broker-auth inline warning */}
          {formData.mode === 'Live' && brokerStatus.length > 0 && !brokerStatus.find(b => b.brokerName === formData.brokerName)?.isAuthenticated && (
            <div style={{ background: '#451a03', border: '1px solid #92400e', borderRadius: 6, padding: '10px 14px', marginTop: 12, fontSize: 13, color: '#fbbf24', display: 'flex', gap: 8, alignItems: 'flex-start' }}>
              <span>⚠️</span>
              <span>
                <strong>{formData.brokerName}</strong> is not authenticated.
                Live trading requires an active broker session. Go to <strong>Settings → Broker</strong> to connect first.
              </span>
            </div>
          )}

          <div style={{ display: 'flex', gap: '12px', marginTop: '20px', justifyContent: 'flex-end' }}>
            <button
              onClick={() => setShowForm(false)}
              style={{
                padding: '8px 16px',
                backgroundColor: '#2d2d3f',
                color: '#e2e8f0',
                border: '1px solid #3b3b4f',
                borderRadius: '6px',
                cursor: 'pointer',
                fontSize: '14px',
              }}
            >
              Cancel
            </button>
            <button
              onClick={handleCreateStrategy}
              disabled={createMutation.isPending}
              style={{
                padding: '8px 16px',
                backgroundColor: formData.mode === 'Live' ? '#dc2626' : '#3b82f6',
                color: '#fff',
                border: 'none',
                borderRadius: '6px',
                cursor: createMutation.isPending ? 'not-allowed' : 'pointer',
                fontSize: '14px',
                opacity: createMutation.isPending ? 0.7 : 1,
              }}
            >
              {createMutation.isPending ? 'Creating...' : formData.mode === 'Live' ? '⚠ Create Live Instance' : 'Create Instance'}
            </button>
          </div>
        </div>
      )}

      {/* Live Start Confirmation Modal */}
      {liveConfirmOpen && (
        <div style={{
          position: 'fixed', inset: 0, backgroundColor: 'rgba(0,0,0,0.7)',
          display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000,
        }}>
          <div style={{
            backgroundColor: '#1e1e2e', border: '2px solid #dc2626', borderRadius: 10,
            padding: 28, maxWidth: 440, width: '90%',
          }}>
            <h3 style={{ color: '#fca5a5', fontSize: 18, fontWeight: 700, marginTop: 0, marginBottom: 8 }}>
              ⚠ Live Trading — Real Money
            </h3>
            <p style={{ color: '#e2e8f0', fontSize: 14, lineHeight: 1.6, marginBottom: 8 }}>
              You are about to create a <strong>Live trading instance</strong> for{' '}
              <strong>{pendingCmd?.name}</strong>.
            </p>
            <p style={{ color: '#fca5a5', fontSize: 13, lineHeight: 1.6, marginBottom: 20 }}>
              This will place <strong>real orders</strong> with {pendingCmd?.brokerName} using up to{' '}
              <strong>₹{pendingCmd?.allocatedCapital?.toLocaleString()}</strong> of allocated capital.
              Losses are real. Please confirm you intend to trade live.
            </p>
            <div style={{ display: 'flex', gap: 12, justifyContent: 'flex-end' }}>
              <button
                onClick={() => { setLiveConfirmOpen(false); setPendingCmd(null) }}
                style={{ padding: '8px 16px', background: '#2d2d3f', color: '#e2e8f0', border: '1px solid #3b3b4f', borderRadius: 6, cursor: 'pointer', fontSize: 14 }}
              >
                Cancel
              </button>
              <button
                onClick={handleLiveConfirm}
                style={{ padding: '8px 18px', background: '#dc2626', color: '#fff', border: 'none', borderRadius: 6, cursor: 'pointer', fontSize: 14, fontWeight: 700 }}
              >
                Yes, Trade Live
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Strategies Grid */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(350px, 1fr))', gap: '20px' }}>
        {strategies && strategies.length > 0 ? (
          strategies.map(strat => <StrategyCard key={strat.id} instance={strat} />)
        ) : (
          <p style={{ color: '#8b8b9f', fontSize: '14px' }}>No strategies yet. Create one to get started.</p>
        )}
      </div>
    </div>
  )
}

// ──────────────────────────────────────────────────────────────────────────────
// ORDERS PAGE
// ──────────────────────────────────────────────────────────────────────────────

function OrdersPage({ orders }: { orders: Order[] }) {
  return (
    <div>
      <SectionHeader title="Orders" />
      <div style={{ backgroundColor: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: '8px', overflowX: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '13px' }}>
          <thead>
            <tr style={{ backgroundColor: '#2d2d3f', borderBottom: '1px solid #3b3b4f' }}>
              {['ID', 'Symbol', 'Side', 'Qty', 'Price', 'Status', 'Placed', 'Broker'].map(col => (
                <th key={col} style={{ padding: '12px', textAlign: 'left', color: '#8b8b9f', fontWeight: '600' }}>{col}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {orders.length > 0 ? (
              orders.map(order => (
                <tr key={order.id} style={{ borderBottom: '1px solid #2d2d3f' }}>
                  <td style={{ padding: '12px', color: '#e2e8f0', fontFamily: 'monospace', fontSize: '11px' }}>{order.id.slice(0, 8)}</td>
                  <td style={{ padding: '12px', color: '#e2e8f0', fontWeight: '600' }}>{order.internalSymbol}</td>
                  <td style={{ padding: '12px', color: order.direction === 'BUY' ? '#10b981' : '#ef4444' }}>{order.direction}</td>
                  <td style={{ padding: '12px', color: '#e2e8f0' }}>{order.quantity}</td>
                  <td style={{ padding: '12px', color: '#e2e8f0' }}>{formatInr(order.price)}</td>
                  <td style={{ padding: '12px', color: statusColor(order.status) }}>{order.status}</td>
                  <td style={{ padding: '12px', color: '#8b8b9f', fontSize: '12px' }}>{formatIst(order.placedAt)}</td>
                  <td style={{ padding: '12px', color: '#8b8b9f' }}>{order.brokerName}</td>
                </tr>
              ))
            ) : (
              <tr>
                <td colSpan={8} style={{ padding: '24px', textAlign: 'center', color: '#8b8b9f' }}>No orders</td>
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
    case 'FILLED': return '#10b981'
    case 'CANCELLED': return '#6b7280'
    case 'REJECTED': return '#ef4444'
    default: return '#f59e0b'
  }
}

// ──────────────────────────────────────────────────────────────────────────────
// BACKTEST PAGE
// ──────────────────────────────────────────────────────────────────────────────

function BacktestPage({ backtestResults }: {
  backtestResults: BacktestResult[];
}) {
  const qc = useQueryClient()
  const [selectedStrategy, setSelectedStrategy] = useState('AlertCandleShort')
  const [formData, setFormData] = useState({
    internalSymbol: '',
    timeframe: '5m',
    fromDate: '2024-01-01',
    toDate: '2024-03-21',
    initialCapital: 100000,
    riskPerTradePct: 1,
    fillModel: 0 as 0 | 1 | 2,
    slippageBasisPoints: 5,
    brokerageFlatPerSide: 20,
  })
  const [strategyParams, setStrategyParams] = useState<Record<string, unknown>>(defaultParams('AlertCandleShort'))
  const [errorMsg, setErrorMsg] = useState('')
  const [successMsg, setSuccessMsg] = useState('')
  const [expandedRow, setExpandedRow] = useState<string | null>(null)
  const [promoteBacktest, setPromoteBacktest] = useState<BacktestResult | null>(null)

  const toggleRow = useCallback((id: string) => {
    setExpandedRow(prev => prev === id ? null : id)
  }, [])

  const runMutation = useMutation({
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
      }
      return backtestApi.run(cmd)
    },
    onSuccess: () => {
      setSuccessMsg('Backtest started successfully!')
      qc.invalidateQueries({ queryKey: ['backtest-results'] })
      setTimeout(() => setSuccessMsg(''), 3000)
    },
    onError: (err: any) => {
      setErrorMsg(err.response?.data?.error || 'Failed to run backtest')
    }
  })

  const handleRunBacktest = () => {
    if (!formData.internalSymbol) {
      setErrorMsg('Symbol is required')
      return
    }
    runMutation.mutate()
  }

  return (
    <div>
      <SectionHeader title="Backtest Lab" />

      {/* Form */}
      <div style={{ backgroundColor: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: '8px', padding: '20px', marginBottom: '20px' }}>
        <h3 style={{ margin: '0 0 16px 0', fontSize: '16px', fontWeight: 'bold' }}>Run Backtest</h3>

        {errorMsg && <div style={{ backgroundColor: '#7f1d1d', color: '#fecaca', padding: '12px', borderRadius: '6px', marginBottom: '12px', fontSize: '14px' }}>{errorMsg}</div>}
        {successMsg && <div style={{ backgroundColor: '#065f46', color: '#a7f3d0', padding: '12px', borderRadius: '6px', marginBottom: '12px', fontSize: '14px' }}>{successMsg}</div>}

        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: '16px', marginBottom: '16px' }}>
          <div>
            <label style={{ display: 'block', fontSize: '13px', fontWeight: '600', marginBottom: '4px', color: '#8b8b9f' }}>Strategy</label>
            <div style={{ display: 'grid', gap: '8px', maxHeight: '120px', overflowY: 'auto' }}>
              {ALL_STRATEGIES.map(strat => (
                <button
                  key={strat.name}
                  disabled={strat.comingSoon}
                  onClick={() => {
                    if (!strat.comingSoon) {
                      setSelectedStrategy(strat.name)
                      setStrategyParams(defaultParams(strat.name))
                    }
                  }}
                  title={strat.comingSoon ? 'Coming soon — not yet available' : undefined}
                  style={{
                    padding: '8px 12px',
                    backgroundColor: selectedStrategy === strat.name ? '#3b82f6' : '#2d2d3f',
                    color: strat.comingSoon ? '#4b5563' : '#e2e8f0',
                    border: selectedStrategy === strat.name ? '2px solid #60a5fa' : '1px solid #2d2d3f',
                    borderRadius: '4px',
                    cursor: strat.comingSoon ? 'not-allowed' : 'pointer',
                    textAlign: 'left',
                    fontSize: '12px',
                    opacity: strat.comingSoon ? 0.4 : 1,
                    display: 'flex',
                    alignItems: 'center',
                    gap: 6,
                  }}
                >
                  {strat.name}
                  {strat.comingSoon && (
                    <span style={{ fontSize: '10px', background: '#374151', color: '#6b7280', borderRadius: 3, padding: '1px 4px' }}>Soon</span>
                  )}
                </button>
              ))}
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
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr 1fr', gap: '16px', marginBottom: '16px' }}>
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
        </div>

        {/* ── Fill Model + Cost Settings ── */}
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: '16px', marginBottom: '16px', padding: '14px', background: '#13131f', borderRadius: 6, border: '1px solid #2d2d3f' }}>
          <div>
            <label style={{ display: 'block', fontSize: '12px', fontWeight: '600', marginBottom: '6px', color: '#64748b' }}>
              Fill Model
            </label>
            <select
              value={formData.fillModel}
              onChange={e => setFormData({ ...formData, fillModel: parseInt(e.target.value) as 0|1|2 })}
              style={{ background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 4, color: '#e2e8f0', padding: '7px 10px', fontSize: 13, width: '100%' }}
            >
              <option value={0}>Next Bar Open (default)</option>
              <option value={1}>Next Bar Open + Slippage</option>
              <option value={2}>Signal Bar Close ⚠️</option>
            </select>
            <p style={{ color: '#475569', fontSize: 11, marginTop: 4 }}>
              NextBarOpen avoids lookahead bias. SignalBarClose may inflate results.
            </p>
          </div>
          <div>
            <label style={{ display: 'block', fontSize: '12px', fontWeight: '600', marginBottom: '6px', color: '#64748b' }}>
              Slippage (basis points)
            </label>
            <input
              type="number" min={0} max={100} step={1}
              value={formData.slippageBasisPoints}
              onChange={e => setFormData({ ...formData, slippageBasisPoints: parseInt(e.target.value) || 0 })}
              style={{ background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 4, color: '#e2e8f0', padding: '7px 10px', fontSize: 13, width: '100%', boxSizing: 'border-box' }}
            />
            <p style={{ color: '#475569', fontSize: 11, marginTop: 4 }}>
              Applied when Fill = Next Bar Open + Slippage. 1 bp = 0.01%.
            </p>
          </div>
          <div>
            <label style={{ display: 'block', fontSize: '12px', fontWeight: '600', marginBottom: '6px', color: '#64748b' }}>
              Brokerage per Order Leg (₹)
            </label>
            <input
              type="number" min={0} step={1}
              value={formData.brokerageFlatPerSide}
              onChange={e => setFormData({ ...formData, brokerageFlatPerSide: parseInt(e.target.value) || 20 })}
              style={{ background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 4, color: '#e2e8f0', padding: '7px 10px', fontSize: 13, width: '100%', boxSizing: 'border-box' }}
            />
            <p style={{ color: '#475569', fontSize: 11, marginTop: 4 }}>
              ₹20/order = Zerodha/Upstox model. Set to 0 for % brokerage.
            </p>
          </div>
        </div>

        <div style={{ marginBottom: '16px' }}>
          <label style={{ display: 'block', fontSize: '13px', fontWeight: '600', marginBottom: '8px', color: '#8b8b9f' }}>Strategy Parameters</label>
          <StrategyParamsEditor
            strategyName={selectedStrategy}
            value={strategyParams}
            onChange={setStrategyParams}
          />
        </div>

        <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
          <button
            onClick={handleRunBacktest}
            disabled={runMutation.isPending}
            style={{
              padding: '10px 20px',
              backgroundColor: '#3b82f6',
              color: '#fff',
              border: 'none',
              borderRadius: '6px',
              cursor: runMutation.isPending ? 'not-allowed' : 'pointer',
              fontSize: '14px',
              fontWeight: '600',
              opacity: runMutation.isPending ? 0.7 : 1,
            }}
          >
            {runMutation.isPending ? 'Running...' : '▶ Run Backtest'}
          </button>
        </div>
      </div>

      {/* Past Results */}
      <SectionHeader title="Past Runs" />
      <div style={{ backgroundColor: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: '8px', overflowX: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '13px' }}>
          <thead>
            <tr style={{ backgroundColor: '#2d2d3f', borderBottom: '1px solid #3b3b4f' }}>
              {['', 'Date (IST)', 'Strategy', 'Symbol', 'TF', 'Net P&L', 'Return', 'Win%', 'Trades', 'Sharpe', 'Calmar', 'MaxDD', 'PF', 'Expectancy', ''].map((col, i) => (
                <th key={i} style={{ padding: '10px 8px', textAlign: 'left', color: '#8b8b9f', fontWeight: '600', whiteSpace: 'nowrap' }}>{col}</th>
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
                      style={{ borderBottom: isExpanded ? 'none' : '1px solid #2d2d3f', cursor: 'pointer', background: isExpanded ? '#252538' : 'transparent' }}
                      onClick={() => toggleRow(rowId)}
                    >
                      <td style={{ padding: '10px 8px', color: '#64748b', fontSize: '11px' }}>
                        {isExpanded ? '▼' : '▶'}
                      </td>
                      <td style={{ padding: '10px 8px', color: '#94a3b8', fontSize: '12px', whiteSpace: 'nowrap' }}>
                        {result.startedAt ? formatIst(result.startedAt) : '--'}
                      </td>
                      <td style={{ padding: '10px 8px', color: '#e2e8f0', fontWeight: '600' }}>{result.strategyName}</td>
                      <td style={{ padding: '10px 8px', color: '#e2e8f0' }}>{result.symbol}</td>
                      <td style={{ padding: '10px 8px', color: '#8b8b9f' }}>{result.timeframe}</td>
                      <td style={{ padding: '10px 8px', color: result.totalPnl >= 0 ? '#10b981' : '#ef4444', fontWeight: '600' }}>
                        {formatInr(result.totalPnl)}
                      </td>
                      <td style={{ padding: '10px 8px', color: result.totalReturn >= 0 ? '#10b981' : '#ef4444' }}>
                        {(result.totalReturn * 100).toFixed(2)}%
                      </td>
                      <td style={{ padding: '10px 8px', color: '#e2e8f0' }}>{(result.winRate * 100).toFixed(1)}%</td>
                      <td style={{ padding: '10px 8px', color: '#e2e8f0' }}>{result.totalTrades}</td>
                      <td style={{ padding: '10px 8px', color: result.sharpeRatio >= 1 ? '#10b981' : result.sharpeRatio >= 0 ? '#f59e0b' : '#ef4444' }}>
                        {result.sharpeRatio.toFixed(2)}
                      </td>
                      <td style={{ padding: '10px 8px', color: '#e2e8f0' }}>
                        {result.calmarRatio != null ? result.calmarRatio.toFixed(2) : '--'}
                      </td>
                      <td style={{ padding: '10px 8px', color: '#ef4444' }}>
                        {(result.maxDrawdown * 100).toFixed(1)}%
                      </td>
                      <td style={{ padding: '10px 8px', color: result.profitFactor != null && result.profitFactor >= 1.5 ? '#10b981' : '#f59e0b' }}>
                        {result.profitFactor != null ? result.profitFactor.toFixed(2) : '--'}
                      </td>
                      <td style={{ padding: '10px 8px', color: result.expectancyPerTrade != null && result.expectancyPerTrade >= 0 ? '#10b981' : '#ef4444' }}>
                        {result.expectancyPerTrade != null ? formatInr(result.expectancyPerTrade) : '--'}
                      </td>
                      <td style={{ padding: '10px 8px' }} onClick={e => e.stopPropagation()}>
                        <div style={{ display: 'flex', gap: 5 }}>
                          <button
                            onClick={() => window.open(`/api/backtest/${result.id}/report`, '_blank')}
                            style={{ padding: '3px 7px', backgroundColor: '#3b82f6', color: '#fff', border: 'none', borderRadius: '4px', cursor: 'pointer', fontSize: '11px' }}
                          >
                            ↓ PDF
                          </button>
                          {result.id && (
                            <button
                              onClick={() => setPromoteBacktest(result)}
                              title="Start a Forward Test from this backtest"
                              style={{ padding: '3px 7px', backgroundColor: '#1e3a5f', color: '#93c5fd', border: '1px solid #2563eb44', borderRadius: '4px', cursor: 'pointer', fontSize: '11px', fontWeight: 700 }}
                            >
                              🧪 FwdTest
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                    {isExpanded && (
                      <tr key={`${rowId}-detail`} style={{ borderBottom: '1px solid #2d2d3f', background: '#13131f' }}>
                        <td colSpan={15} style={{ padding: '16px 20px' }}>
                          <BacktestDetailPanel result={result} />
                        </td>
                      </tr>
                    )}
                  </Fragment>
                )
              })
            ) : (
              <tr>
                <td colSpan={15} style={{ padding: '24px', textAlign: 'center', color: '#8b8b9f' }}>No backtest results yet</td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {/* Promote to Forward Test modal */}
      {promoteBacktest && (
        <PromoteToForwardTestModal
          backtest={promoteBacktest}
          onClose={() => setPromoteBacktest(null)}
        />
      )}
    </div>
  )
}

// ──────────────────────────────────────────────────────────────────────────────
// BACKTEST DETAIL PANEL (expandable row)
// ──────────────────────────────────────────────────────────────────────────────

function BacktestDetailPanel({ result }: { result: BacktestResult }) {
  const trades = result.trades ?? []
  return (
    <div>
      {/* Summary stat row */}
      <div style={{ display: 'flex', gap: 24, flexWrap: 'wrap', marginBottom: 16 }}>
        {[
          { label: 'Avg Win', value: result.avgWin != null ? formatInr(result.avgWin) : '--', color: '#10b981' },
          { label: 'Avg Loss', value: result.avgLoss != null ? formatInr(result.avgLoss) : '--', color: '#ef4444' },
          { label: 'Max Consec Losses', value: result.maxConsecutiveLosses?.toString() ?? '--', color: '#f59e0b' },
          { label: 'Win Count', value: result.winCount?.toString() ?? '--', color: '#10b981' },
          { label: 'Loss Count', value: result.lossCount?.toString() ?? '--', color: '#ef4444' },
          { label: 'Final Equity', value: result.finalEquity != null ? formatInr(result.finalEquity) : '--', color: '#3b82f6' },
        ].map(stat => (
          <div key={stat.label}>
            <div style={{ fontSize: 11, color: '#64748b', marginBottom: 2 }}>{stat.label}</div>
            <div style={{ fontSize: 15, fontWeight: 700, color: stat.color }}>{stat.value}</div>
          </div>
        ))}
      </div>

      {/* Equity Curve */}
      {trades.length > 0 && result.initialCapital != null && (
        <div style={{ marginBottom: 16 }}>
          <div style={{ fontSize: 12, color: '#64748b', marginBottom: 6 }}>Equity Curve</div>
          <EquityCurveChart trades={trades} initialCapital={result.initialCapital} />
        </div>
      )}

      {/* Trade Table */}
      {trades.length > 0 && (
        <div>
          <div style={{ fontSize: 12, color: '#64748b', marginBottom: 6 }}>Trade Log ({trades.length} trades)</div>
          <div style={{ overflowX: 'auto', maxHeight: 260, overflowY: 'auto' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '12px' }}>
              <thead style={{ position: 'sticky', top: 0, background: '#1e1e2e' }}>
                <tr style={{ borderBottom: '1px solid #2d2d3f' }}>
                  {['#', 'Side', 'Entry Time (IST)', 'Exit Time (IST)', 'Entry ₹', 'Exit ₹', 'Qty', 'Gross P&L', 'Net P&L', 'Exit Reason'].map(col => (
                    <th key={col} style={{ padding: '6px 8px', textAlign: 'left', color: '#64748b', fontWeight: 600, whiteSpace: 'nowrap' }}>{col}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {trades.map((t: BacktestTradeResult, i: number) => (
                  <tr key={i} style={{ borderBottom: '1px solid #1e1e2e' }}>
                    <td style={{ padding: '5px 8px', color: '#475569' }}>{i + 1}</td>
                    <td style={{ padding: '5px 8px', color: t.direction === 'Long' || t.direction === 'BUY' ? '#10b981' : '#ef4444', fontWeight: 600 }}>{t.direction}</td>
                    <td style={{ padding: '5px 8px', color: '#94a3b8' }}>{formatIst(t.entryTime)}</td>
                    <td style={{ padding: '5px 8px', color: '#94a3b8' }}>{formatIst(t.exitTime)}</td>
                    <td style={{ padding: '5px 8px', color: '#e2e8f0' }}>{formatInr(t.entryPrice)}</td>
                    <td style={{ padding: '5px 8px', color: '#e2e8f0' }}>{formatInr(t.exitPrice)}</td>
                    <td style={{ padding: '5px 8px', color: '#e2e8f0' }}>{t.quantity}</td>
                    <td style={{ padding: '5px 8px', color: t.grossPnl >= 0 ? '#10b981' : '#ef4444' }}>{formatInr(t.grossPnl)}</td>
                    <td style={{ padding: '5px 8px', color: t.netPnl >= 0 ? '#10b981' : '#ef4444', fontWeight: 600 }}>{formatInr(t.netPnl)}</td>
                    <td style={{ padding: '5px 8px', color: '#64748b' }}>{t.exitReason}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {trades.length === 0 && (
        <p style={{ color: '#475569', fontSize: 13 }}>No trade data available for this run.</p>
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
      <SectionHeader title="Settings" />

      {/* ── Telegram Notifications ── */}
      <div style={{
        backgroundColor: '#1e1e2e', border: '1px solid #2d2d3f',
        borderRadius: 8, padding: 24, marginBottom: 20
      }}>
        <h3 style={{ color: '#e2e8f0', fontSize: 16, fontWeight: 700, marginBottom: 4, marginTop: 0 }}>
          📱 Telegram Alerts
        </h3>
        <p style={{ color: '#64748b', fontSize: 12, marginBottom: 20, marginTop: 0 }}>
          Receive real-time alerts for drawdown breaches and broker re-auth failures via Telegram Bot.
        </p>

        {isLoading ? (
          <p style={{ color: '#64748b' }}>Loading…</p>
        ) : (
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16 }}>
            <div>
              <label style={labelStyle}>
                Bot Token
                {settings?.telegramBotTokenMasked && (
                  <span style={{ color: '#3b82f6', marginLeft: 8, fontWeight: 400 }}>
                    (current: {settings.telegramBotTokenMasked})
                  </span>
                )}
              </label>
              <input
                type="password"
                placeholder="Leave blank to keep existing token"
                value={botToken}
                onChange={e => setBotToken(e.target.value)}
                style={inputStyle}
              />
              <p style={{ color: '#475569', fontSize: 11, marginTop: 4 }}>
                Get your token from @BotFather on Telegram.
              </p>
            </div>

            <div>
              <label style={labelStyle}>Chat ID</label>
              <input
                type="text"
                placeholder="e.g. -1001234567890"
                value={chatId}
                onChange={e => setChatId(e.target.value)}
                style={inputStyle}
              />
              <p style={{ color: '#475569', fontSize: 11, marginTop: 4 }}>
                Your Telegram chat or channel ID. Send /start to your bot and check getUpdates.
              </p>
            </div>
          </div>
        )}
      </div>

      {/* ── Monitoring Thresholds ── */}
      <div style={{
        backgroundColor: '#1e1e2e', border: '1px solid #2d2d3f',
        borderRadius: 8, padding: 24, marginBottom: 20
      }}>
        <h3 style={{ color: '#e2e8f0', fontSize: 16, fontWeight: 700, marginBottom: 4, marginTop: 0 }}>
          📉 Drawdown Alert Threshold
        </h3>
        <p style={{ color: '#64748b', fontSize: 12, marginBottom: 20, marginTop: 0 }}>
          Alert fires when a running strategy's intraday unrealized P&L exceeds this % of allocated capital.
          One alert per strategy per trading day (duplicate suppression built in).
        </p>

        <div style={{ display: 'grid', gridTemplateColumns: '200px 1fr', gap: 16, alignItems: 'start' }}>
          <div>
            <label style={labelStyle}>Max Daily Drawdown %</label>
            <input
              type="number"
              min={0.1} max={50} step={0.5}
              value={drawdownPct}
              onChange={e => setDrawdownPct(e.target.value)}
              style={inputStyle}
            />
          </div>
          <div style={{ paddingTop: 22 }}>
            <span style={{ color: '#475569', fontSize: 12 }}>
              Current: <strong style={{ color: '#e2e8f0' }}>{settings?.maxDailyDrawdownPct ?? 3}%</strong>
              &nbsp;— Default is 3%. Evaluated every 30 seconds during market hours.
            </span>
          </div>
        </div>

        <div style={{ marginTop: 16 }}>
          <label style={{ display: 'flex', alignItems: 'center', gap: 10, cursor: 'pointer' }}>
            <input
              type="checkbox"
              checked={alertsEnabled}
              onChange={e => setAlertsEnabled(e.target.checked)}
              style={{ width: 16, height: 16, cursor: 'pointer' }}
            />
            <span style={{ color: '#94a3b8', fontSize: 13 }}>Enable monitoring alerts</span>
          </label>
        </div>
      </div>

      {/* ── Save Button ── */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 16 }}>
        <button
          onClick={() => saveMutation.mutate()}
          disabled={saveMutation.isPending}
          style={{
            background: '#3b82f6',
            color: 'white',
            border: 'none',
            borderRadius: 6,
            padding: '10px 24px',
            fontSize: 14,
            fontWeight: 600,
            cursor: 'pointer',
          }}
        >
          {saveMutation.isPending ? 'Saving…' : 'Save Settings'}
        </button>
        {saved && (
          <span style={{ color: '#16a34a', fontSize: 13, fontWeight: 600 }}>
            ✓ Settings saved
          </span>
        )}
        {saveMutation.isError && (
          <span style={{ color: '#dc2626', fontSize: 13 }}>
            Failed to save. Please try again.
          </span>
        )}
      </div>
    </div>
  )
}

// ──────────────────────────────────────────────────────────────────────────────
// NAVIGATION HELPERS
// ──────────────────────────────────────────────────────────────────────────────

function NavSectionLabel({ label }: { label: string }) {
  return (
    <div style={{ padding: '12px 14px 4px 14px', fontSize: 9, fontWeight: 800, color: '#4b5563', letterSpacing: '0.10em', textTransform: 'uppercase' }}>
      {label}
    </div>
  )
}

function NavItem({ page, label, icon, active, onClick, accent = '#3b82f6' }: {
  page: Page; label: string; icon: string
  active: Page; onClick: (p: Page) => void; accent?: string
}) {
  const isActive = active === page
  return (
    <button
      onClick={() => onClick(page)}
      aria-current={isActive ? 'page' : undefined}
      style={{
        padding: '9px 14px',
        backgroundColor: isActive ? '#2d2d4f' : 'transparent',
        color: isActive ? '#e2e8f0' : '#8b8b9f',
        border: 'none',
        borderLeft: isActive ? `3px solid ${accent}` : '3px solid transparent',
        borderRadius: '0 6px 6px 0',
        cursor: 'pointer',
        fontSize: '13px',
        fontWeight: isActive ? 700 : 400,
        textAlign: 'left',
        transition: 'all 0.15s',
        display: 'flex',
        alignItems: 'center',
        gap: 8,
        outline: 'none',
        width: '100%',
      }}
      onMouseEnter={(e) => { if (!isActive) { e.currentTarget.style.color = '#e2e8f0'; e.currentTarget.style.backgroundColor = '#252538' } }}
      onMouseLeave={(e) => { if (!isActive) { e.currentTarget.style.color = '#8b8b9f'; e.currentTarget.style.backgroundColor = 'transparent' } }}
    >
      <span aria-hidden="true" style={{ fontSize: 13 }}>{icon}</span>
      {label}
    </button>
  )
}

// ──────────────────────────────────────────────────────────────────────────────
// SHARED COMPONENTS
// ──────────────────────────────────────────────────────────────────────────────

function SectionHeader({ title, action }: { title: string; action?: React.ReactNode }) {
  return (
    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '20px' }}>
      <h2 style={{ margin: 0, fontSize: '20px', fontWeight: 'bold' }}>{title}</h2>
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
