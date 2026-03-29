import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { StrategyInstance, strategiesApi, brokerApi, BrokerPosition, CreateStrategyCommand } from '../../api/client'
import { formatInr } from '../../utils/datetime'
import ScenariosPanel from './ScenariosPanel'

const STATUS_COLORS: Record<string, { bg: string; text: string; dot: string }> = {
  Draft:     { bg: '#1e293b', text: '#94a3b8', dot: '#94a3b8' },
  Running:   { bg: '#14532d', text: '#86efac', dot: '#16a34a' },
  Paused:    { bg: '#422006', text: '#fde68a', dot: '#f59e0b' },
  Stopped:   { bg: '#1c1c1c', text: '#6b7280', dot: '#6b7280' },
  Scheduled: { bg: '#1e3a5f', text: '#93c5fd', dot: '#3b82f6' },
  Error:     { bg: '#450a0a', text: '#fca5a5', dot: '#dc2626' },
}

interface Props {
  instance: StrategyInstance
  /** Called when user clicks "Backtest" — parent navigates to backtest tab with pre-filled form */
  onRunBacktest?: (instance: StrategyInstance) => void
  /** Called when user clicks "Forward Test" on a Backtest-mode instance */
  onPromoteToForward?: (instance: StrategyInstance) => void
}

function PnlBadge({ value, label }: { value: number; label: string }) {
  const isPositive = value >= 0
  const color = isPositive ? '#16a34a' : '#dc2626'
  const bg   = isPositive ? '#16a34a18' : '#dc262618'
  return (
    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 2 }}>
      <span style={{ fontSize: 11, color: '#64748b', fontWeight: 600, letterSpacing: '0.02em' }}>
        {label}
      </span>
      <span style={{
        background: bg, color, borderRadius: 5, padding: '2px 8px',
        fontSize: 13, fontWeight: 700, fontVariantNumeric: 'tabular-nums',
      }}>
        {isPositive && value > 0 ? '+' : ''}{formatInr(value)}
      </span>
    </div>
  )
}

function ActionButton({
  onClick, disabled, color, children, ariaLabel,
}: {
  onClick: () => void; disabled: boolean; color: string
  children: React.ReactNode; ariaLabel: string
}) {
  const [hovered, setHovered] = useState(false)
  return (
    <button
      onClick={onClick}
      disabled={disabled}
      aria-label={ariaLabel}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      style={{
        background: disabled ? '#374151' : hovered ? `${color}cc` : color,
        color: 'white', border: 'none', borderRadius: 5,
        padding: '6px 14px', cursor: disabled ? 'not-allowed' : 'pointer',
        fontSize: 12, fontWeight: 700, transition: 'background 0.15s, transform 0.1s',
        transform: hovered && !disabled ? 'translateY(-1px)' : 'none', outline: 'none',
      }}
      onFocus={e => (e.currentTarget.style.boxShadow = `0 0 0 2px ${color}60`)}
      onBlur={e => (e.currentTarget.style.boxShadow = '')}
    >
      {children}
    </button>
  )
}

function SmallButton({
  onClick, color, bg, children, title,
}: { onClick: () => void; color: string; bg: string; children: React.ReactNode; title?: string }) {
  return (
    <button
      onClick={onClick}
      title={title}
      style={{
        padding: '4px 10px', background: bg, color, border: `1px solid ${color}44`,
        borderRadius: 4, cursor: 'pointer', fontSize: 11, fontWeight: 700,
      }}
    >
      {children}
    </button>
  )
}

// ─────────────────────────────────────────────────────────────────────────────
// Open Positions Panel
// ─────────────────────────────────────────────────────────────────────────────

function PositionsPanel({ brokerName }: { brokerName: string }) {
  const { data: positions, isLoading } = useQuery({
    queryKey: ['positions', brokerName],
    queryFn: () => brokerApi.positions(brokerName).then(r => r.data.data ?? []),
    refetchInterval: 10_000,
  })
  if (isLoading) return <p style={{ color: '#64748b', fontSize: 12, margin: 0 }}>Loading positions…</p>
  if (!positions || positions.length === 0)
    return <p style={{ color: '#475569', fontSize: 12, margin: 0 }}>No open positions.</p>
  return (
    <div style={{ overflowX: 'auto' }}>
      <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
        <thead>
          <tr style={{ borderBottom: '1px solid #2d2d3f' }}>
            {['Symbol', 'Qty', 'Avg Price', 'LTP', 'P&L', 'Product'].map(col => (
              <th key={col} style={{ padding: '5px 8px', textAlign: 'left', color: '#64748b', fontWeight: 600 }}>{col}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {positions.map((pos: BrokerPosition, i: number) => (
            <tr key={i} style={{ borderBottom: '1px solid #1e1e2e' }}>
              <td style={{ padding: '5px 8px', color: '#e2e8f0', fontWeight: 600 }}>{pos.internalSymbol}</td>
              <td style={{ padding: '5px 8px', color: pos.quantity > 0 ? '#10b981' : '#ef4444' }}>{pos.quantity}</td>
              <td style={{ padding: '5px 8px', color: '#94a3b8' }}>{formatInr(pos.averagePrice)}</td>
              <td style={{ padding: '5px 8px', color: '#e2e8f0' }}>{formatInr(pos.lastPrice)}</td>
              <td style={{ padding: '5px 8px', color: pos.pnl >= 0 ? '#10b981' : '#ef4444', fontWeight: 700 }}>
                {pos.pnl >= 0 ? '+' : ''}{formatInr(pos.pnl)}
              </td>
              <td style={{ padding: '5px 8px', color: '#64748b' }}>{pos.productType}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

// ─────────────────────────────────────────────────────────────────────────────
// Edit Drawer
// ─────────────────────────────────────────────────────────────────────────────

function EditDrawer({ instance, onClose }: { instance: StrategyInstance; onClose: () => void }) {
  const qc = useQueryClient()
  const [name, setName] = useState(instance.name)
  const [internalSymbol, setInternalSymbol] = useState(instance.internalSymbol)
  const [timeframe, setTimeframe] = useState(instance.timeframe)
  const [brokerName, setBrokerName] = useState(instance.brokerName)
  const [allocatedCapital, setAllocatedCapital] = useState(String(instance.allocatedCapital ?? 50000))
  const [parametersJson, setParametersJson] = useState(instance.parametersJson ?? '{}')
  const [errorMsg, setErrorMsg] = useState('')

  const updateMutation = useMutation({
    mutationFn: () => strategiesApi.update(instance.id, {
      name, internalSymbol, timeframe, brokerName,
      allocatedCapital: parseFloat(allocatedCapital) || undefined,
      parametersJson,
    } as Partial<CreateStrategyCommand>),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['strategies'] })
      onClose()
    },
    onError: (err: any) => {
      setErrorMsg(err.response?.data?.error || 'Update failed')
    },
  })

  return (
    <>
      {/* Overlay */}
      <div
        onClick={onClose}
        style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.4)', zIndex: 299 }}
      />
      {/* Drawer */}
      <div style={{
        position: 'fixed', top: 36, right: 0, bottom: 0, width: 480,
        background: '#0d0d17', borderLeft: '1px solid #2d2d3f',
        zIndex: 300, overflowY: 'auto', padding: '16px 20px',
        boxShadow: '-8px 0 32px rgba(0,0,0,0.6)',
        display: 'flex', flexDirection: 'column', gap: 12,
      }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', paddingBottom: 8, borderBottom: '1px solid #2d2d3f' }}>
          <span style={{ fontSize: 12, fontWeight: 700, color: '#e2e8f0', textTransform: 'uppercase', letterSpacing: '0.06em' }}>
            Edit Strategy
          </span>
          <button onClick={onClose} style={{ background: 'none', border: 'none', cursor: 'pointer', fontSize: 16, color: '#64748b' }}>✕</button>
        </div>

        {instance.status === 'Running' && (
          <div style={{ background: '#422006', border: '1px solid #f59e0b44', borderRadius: 4, padding: '8px 12px', fontSize: 11, color: '#fde68a' }}>
            Stop or pause the strategy before editing.
          </div>
        )}

        {errorMsg && (
          <div style={{ background: '#450a0a', border: '1px solid #dc262644', borderRadius: 4, padding: '8px 12px', fontSize: 12, color: '#fca5a5' }}>
            {errorMsg}
          </div>
        )}

        <label style={{ display: 'flex', flexDirection: 'column', gap: 4, fontSize: 11, color: '#64748b', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
          Instance Name
          <input
            value={name}
            onChange={e => setName(e.target.value)}
            style={{ background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 4, color: '#e2e8f0', padding: '7px 10px', fontSize: 13 }}
          />
        </label>

        <label style={{ display: 'flex', flexDirection: 'column', gap: 4, fontSize: 11, color: '#64748b', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
          Symbol
          <input
            value={internalSymbol}
            onChange={e => setInternalSymbol(e.target.value)}
            style={{ background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 4, color: '#e2e8f0', padding: '7px 10px', fontSize: 13 }}
          />
        </label>

        <label style={{ display: 'flex', flexDirection: 'column', gap: 4, fontSize: 11, color: '#64748b', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
          Timeframe
          <select
            value={timeframe}
            onChange={e => setTimeframe(e.target.value)}
            style={{ background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 4, color: '#e2e8f0', padding: '7px 10px', fontSize: 13 }}
          >
            {['1m', '5m', '15m', '30m', '60m', '1d'].map(tf => (
              <option key={tf} value={tf}>{tf}</option>
            ))}
          </select>
        </label>

        <label style={{ display: 'flex', flexDirection: 'column', gap: 4, fontSize: 11, color: '#64748b', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
          Broker
          <input
            value={brokerName}
            onChange={e => setBrokerName(e.target.value)}
            style={{ background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 4, color: '#e2e8f0', padding: '7px 10px', fontSize: 13 }}
          />
        </label>

        <label style={{ display: 'flex', flexDirection: 'column', gap: 4, fontSize: 11, color: '#64748b', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
          Allocated Capital (₹)
          <input
            type="number"
            value={allocatedCapital}
            onChange={e => setAllocatedCapital(e.target.value)}
            style={{ background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 4, color: '#e2e8f0', padding: '7px 10px', fontSize: 13 }}
          />
        </label>

        <label style={{ display: 'flex', flexDirection: 'column', gap: 4, fontSize: 11, color: '#64748b', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
          Parameters JSON
          <textarea
            value={parametersJson}
            onChange={e => setParametersJson(e.target.value)}
            rows={6}
            style={{ background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 4, color: '#e2e8f0', padding: '7px 10px', fontSize: 12, fontFamily: 'monospace', resize: 'vertical' }}
          />
        </label>

        <div style={{ display: 'flex', gap: 8, marginTop: 'auto', paddingTop: 12, borderTop: '1px solid #2d2d3f' }}>
          <button
            onClick={onClose}
            style={{ flex: 1, padding: '8px', background: '#1e1e2e', color: '#94a3b8', border: '1px solid #2d2d3f', borderRadius: 4, cursor: 'pointer', fontSize: 12 }}
          >
            Cancel
          </button>
          <button
            onClick={() => updateMutation.mutate()}
            disabled={updateMutation.isPending || instance.status === 'Running'}
            style={{
              flex: 1, padding: '8px', background: '#2563eb', color: '#fff',
              border: 'none', borderRadius: 4, cursor: updateMutation.isPending || instance.status === 'Running' ? 'not-allowed' : 'pointer',
              fontSize: 12, fontWeight: 700, opacity: updateMutation.isPending || instance.status === 'Running' ? 0.6 : 1,
            }}
          >
            {updateMutation.isPending ? 'Saving…' : 'Save'}
          </button>
        </div>
      </div>
    </>
  )
}

// ─────────────────────────────────────────────────────────────────────────────
// Live Start Confirmation Modal
// ─────────────────────────────────────────────────────────────────────────────

function LiveConfirmModal({
  instance, onConfirm, onCancel, isPending,
}: { instance: StrategyInstance; onConfirm: () => void; onCancel: () => void; isPending: boolean }) {
  return (
    <div style={{
      position: 'fixed', inset: 0, backgroundColor: 'rgba(0,0,0,0.75)',
      display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000,
    }}>
      <div style={{
        backgroundColor: '#1e1e2e', border: '2px solid #dc2626', borderRadius: 10,
        padding: 28, maxWidth: 440, width: '90%',
      }}>
        <h3 style={{ color: '#fca5a5', fontSize: 18, fontWeight: 700, marginTop: 0, marginBottom: 8 }}>
          Start Live Trading
        </h3>
        <p style={{ color: '#e2e8f0', fontSize: 14, lineHeight: 1.6, marginBottom: 8 }}>
          You are about to start <strong>{instance.name}</strong> in <strong>Live mode</strong>.
        </p>
        <p style={{ color: '#fca5a5', fontSize: 13, lineHeight: 1.6, marginBottom: 20 }}>
          This will place <strong>real orders</strong> on <strong>{instance.brokerName}</strong> using up to{' '}
          <strong>{formatInr(instance.allocatedCapital ?? 0)}</strong> of capital. Losses are real.
        </p>
        <div style={{ display: 'flex', gap: 12, justifyContent: 'flex-end' }}>
          <button
            onClick={onCancel}
            style={{ padding: '8px 16px', background: '#2d2d3f', color: '#e2e8f0', border: '1px solid #3b3b4f', borderRadius: 6, cursor: 'pointer', fontSize: 14 }}
          >
            Cancel
          </button>
          <button
            onClick={onConfirm}
            disabled={isPending}
            style={{ padding: '8px 18px', background: '#dc2626', color: '#fff', border: 'none', borderRadius: 6, cursor: isPending ? 'not-allowed' : 'pointer', fontSize: 14, fontWeight: 700 }}
          >
            {isPending ? 'Starting…' : 'Yes, Start Live'}
          </button>
        </div>
      </div>
    </div>
  )
}

// ─────────────────────────────────────────────────────────────────────────────
// StrategyCard
// ─────────────────────────────────────────────────────────────────────────────

export function StrategyCard({ instance, onRunBacktest, onPromoteToForward }: Props) {
  const qc = useQueryClient()
  const [liveConfirmOpen, setLiveConfirmOpen] = useState(false)
  const [positionsExpanded, setPositionsExpanded] = useState(false)
  const [editOpen, setEditOpen] = useState(false)

  const deleteMutation = useMutation({
    mutationFn: () => strategiesApi.delete(instance.id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['strategies'] }),
  })
  const startMutation = useMutation({
    mutationFn: () => strategiesApi.start(instance.id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['strategies'] })
      setLiveConfirmOpen(false)
    },
  })
  const stopMutation = useMutation({
    mutationFn: () => strategiesApi.stop(instance.id, 'MANUAL'),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['strategies'] }),
  })
  const pauseMutation = useMutation({
    mutationFn: () => strategiesApi.pause(instance.id, 'MANUAL'),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['strategies'] }),
  })

  const isRunning  = instance.status === 'Running'
  const isPaused   = instance.status === 'Paused'
  const isLive     = instance.mode === 'Live'
  const isForward  = instance.mode === 'Forward'
  const isBacktest = instance.mode === 'Backtest'
  const statusCfg = STATUS_COLORS[instance.status] ?? STATUS_COLORS.Stopped

  const todayRealized   = instance.todayRealizedPnl   ?? 0
  const todayUnrealized = instance.todayUnrealizedPnl ?? 0
  const openPositions   = instance.openPositionCount  ?? 0
  const showPnl = isRunning || isPaused || todayRealized !== 0 || todayUnrealized !== 0

  return (
    <>
      <article
        aria-label={`Strategy: ${instance.name}, Status: ${instance.status}`}
        style={{
          background: '#0d0d17',
          border: isLive ? '1px solid #451a03' : '1px solid #2d2d3f',
          borderRadius: 10, padding: 16,
          display: 'flex', flexDirection: 'column', gap: 12,
        }}
      >
        {/* Mode stripe — Live / Forward / Backtest */}
        {isLive && (
          <div style={{ background: '#451a03', borderRadius: 4, padding: '4px 10px', fontSize: 11, color: '#fbbf24', fontWeight: 600 }}>
            LIVE — Real money orders
          </div>
        )}
        {isForward && (
          <div style={{ background: '#0c2233', borderRadius: 4, padding: '4px 10px', fontSize: 11, color: '#93c5fd', fontWeight: 600 }}>
            FORWARD TEST — Paper trading
          </div>
        )}
        {isBacktest && (
          <div style={{ background: '#1c1a03', borderRadius: 4, padding: '4px 10px', fontSize: 11, color: '#fde68a', fontWeight: 600, display: 'flex', alignItems: 'center', gap: 6 }}>
            <span>STEP 1 — Run backtest</span>
            <span style={{ color: '#78716c', fontWeight: 400 }}>→</span>
            <span style={{ color: '#78716c', fontWeight: 400 }}>STEP 2 — Promote to Forward Test</span>
            <span style={{ color: '#78716c', fontWeight: 400 }}>→</span>
            <span style={{ color: '#78716c', fontWeight: 400 }}>STEP 3 — Live</span>
          </div>
        )}

        {/* Header: name + status badge */}
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
          <div style={{ minWidth: 0, flex: 1, marginRight: 10 }}>
            <div style={{ fontWeight: 700, fontSize: 15, color: '#e2e8f0', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
              title={instance.name}>
              {instance.name}
            </div>
            <div style={{ fontSize: 12, color: '#64748b', marginTop: 3 }}>
              {instance.strategyType} · {instance.internalSymbol} · {instance.timeframe}
            </div>
          </div>
          <span style={{
            display: 'flex', alignItems: 'center', gap: 5,
            background: statusCfg.bg, color: statusCfg.text,
            borderRadius: 20, padding: '3px 10px', fontSize: 11, fontWeight: 700, whiteSpace: 'nowrap', flexShrink: 0,
          }}>
            <span style={{ width: 6, height: 6, borderRadius: '50%', background: statusCfg.dot, flexShrink: 0 }} />
            {instance.status}
          </span>
        </div>

        {/* Meta row */}
        <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap', fontSize: 12, color: '#64748b' }}>
          <span>Broker: <strong style={{ color: '#94a3b8' }}>{instance.brokerName}</strong></span>
          <span>Mode: <strong style={{ color: isLive ? '#fbbf24' : isForward ? '#93c5fd' : '#94a3b8' }}>{instance.mode}</strong></span>
          {instance.allocatedCapital != null && instance.allocatedCapital > 0 && (
            <span>Capital: <strong style={{ color: '#94a3b8' }}>{formatInr(instance.allocatedCapital)}</strong></span>
          )}
        </div>

        {/* Real-time P&L strip */}
        {showPnl && (
          <div style={{
            display: 'flex', gap: 0, padding: '8px 12px',
            background: '#090910', borderRadius: 7, border: '1px solid #2d2d3f',
            alignItems: 'center', justifyContent: 'space-around',
          }}>
            <PnlBadge value={todayRealized} label="Realized" />
            <div style={{ width: 1, height: 32, background: '#2d2d3f', flexShrink: 0 }} />
            <PnlBadge value={todayUnrealized} label="Unrealized" />
            <div style={{ width: 1, height: 32, background: '#2d2d3f', flexShrink: 0 }} />
            <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 2 }}>
              <span style={{ fontSize: 11, color: '#64748b', fontWeight: 600 }}>Open</span>
              <span style={{ fontSize: 15, fontWeight: 700, color: openPositions > 0 ? '#3b82f6' : '#475569', fontVariantNumeric: 'tabular-nums' }}>
                {openPositions}
              </span>
            </div>
          </div>
        )}

        {/* Open positions expandable */}
        {openPositions > 0 && (
          <div>
            <button
              onClick={() => setPositionsExpanded(e => !e)}
              style={{ background: 'none', border: 'none', cursor: 'pointer', color: '#3b82f6', fontSize: 12, fontWeight: 600, padding: '0 0 6px 0', display: 'flex', alignItems: 'center', gap: 4 }}
            >
              {positionsExpanded ? '▲' : '▶'} View open positions ({openPositions})
            </button>
            {positionsExpanded && (
              <div style={{ background: '#090910', borderRadius: 6, padding: '10px 12px', border: '1px solid #2d2d3f' }}>
                <PositionsPanel brokerName={instance.brokerName} />
              </div>
            )}
          </div>
        )}

        {/* Primary action buttons */}
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
          {/* Backtest mode: primary action is "Run Backtest", not Start */}
          {isBacktest && !isRunning && onRunBacktest && (
            <ActionButton
              onClick={() => onRunBacktest(instance)}
              disabled={false}
              color="#d97706"
              ariaLabel={`Run backtest for ${instance.name}`}
            >
              ▶ Run Backtest
            </ActionButton>
          )}

          {/* Forward / Live mode: normal Start/Pause/Stop */}
          {!isBacktest && !isRunning && (
            <ActionButton
              onClick={() => isLive ? setLiveConfirmOpen(true) : startMutation.mutate()}
              disabled={startMutation.isPending}
              color={isLive ? '#dc2626' : '#16a34a'}
              ariaLabel={`Start strategy ${instance.name}`}
            >
              {startMutation.isPending ? 'Starting…' : '▶ Start'}
            </ActionButton>
          )}
          {!isBacktest && isRunning && (
            <ActionButton
              onClick={() => pauseMutation.mutate()}
              disabled={pauseMutation.isPending}
              color="#d97706"
              ariaLabel={`Pause strategy ${instance.name}`}
            >
              {pauseMutation.isPending ? 'Pausing…' : '⏸ Pause'}
            </ActionButton>
          )}
          {!isBacktest && (isRunning || isPaused) && (
            <ActionButton
              onClick={() => stopMutation.mutate()}
              disabled={stopMutation.isPending}
              color="#dc2626"
              ariaLabel={`Stop strategy ${instance.name}`}
            >
              {stopMutation.isPending ? 'Stopping…' : '■ Stop'}
            </ActionButton>
          )}
        </div>

        {/* Secondary actions (Edit / Backtest / Promote) */}
        <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', borderTop: '1px solid #2d2d3f', paddingTop: 10 }}>
          <SmallButton
            onClick={() => setEditOpen(true)}
            color="#94a3b8"
            bg="#1e1e2e"
            title="Edit instance settings"
          >
            Edit
          </SmallButton>

          {/* For non-backtest modes, show the backtest link as a secondary option */}
          {onRunBacktest && !isBacktest && (
            <SmallButton
              onClick={() => onRunBacktest(instance)}
              color="#3b82f6"
              bg="#1e3a5f22"
              title="Run a historical backtest for this strategy"
            >
              Backtest
            </SmallButton>
          )}

          {/* Backtest → Forward promotion: only shown on Backtest-mode instances */}
          {onPromoteToForward && isBacktest && (
            <SmallButton
              onClick={() => onPromoteToForward(instance)}
              color="#10b981"
              bg="#14532d22"
              title="Run a backtest first, then use 'Promote to Forward Test' on the result"
            >
              Promote to Forward Test →
            </SmallButton>
          )}

          <SmallButton
            onClick={() => {
              if (window.confirm(`Delete strategy "${instance.name}"? This cannot be undone.`)) {
                deleteMutation.mutate()
              }
            }}
            color="#ef4444"
            bg="#450a0a22"
            title="Permanently delete this strategy instance"
          >
            {deleteMutation.isPending ? 'Deleting…' : 'Delete'}
          </SmallButton>
        </div>
      </article>

      {/* Scenarios Panel */}
      <ScenariosPanel
        instanceId={instance.id}
        strategyType={instance.strategyType}
        instanceName={instance.name}
        baseParametersJson={instance.parametersJson ?? '{}'}
      />

      {/* Edit Drawer */}
      {editOpen && <EditDrawer instance={instance} onClose={() => setEditOpen(false)} />}

      {/* Live Start Confirmation Modal */}
      {liveConfirmOpen && (
        <LiveConfirmModal
          instance={instance}
          onConfirm={() => startMutation.mutate()}
          onCancel={() => setLiveConfirmOpen(false)}
          isPending={startMutation.isPending}
        />
      )}
    </>
  )
}
