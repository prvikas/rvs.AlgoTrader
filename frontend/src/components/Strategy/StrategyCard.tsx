import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { StrategyInstance, strategiesApi, brokerApi, BrokerPosition } from '../../api/client'
import { formatInr } from '../../utils/datetime'

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
        background: bg,
        color,
        borderRadius: 5,
        padding: '2px 8px',
        fontSize: 13,
        fontWeight: 700,
        fontVariantNumeric: 'tabular-nums',
      }}>
        {isPositive && value > 0 ? '+' : ''}{formatInr(value)}
      </span>
    </div>
  )
}

// Minimal styled action button with hover state
function ActionButton({
  onClick, disabled, color, children, ariaLabel,
}: {
  onClick: () => void
  disabled: boolean
  color: string
  children: React.ReactNode
  ariaLabel: string
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
        color: 'white',
        border: 'none',
        borderRadius: 5,
        padding: '6px 14px',
        cursor: disabled ? 'not-allowed' : 'pointer',
        fontSize: 12,
        fontWeight: 700,
        transition: 'background 0.15s, transform 0.1s',
        transform: hovered && !disabled ? 'translateY(-1px)' : 'none',
        outline: 'none',
      }}
      onFocus={e => (e.currentTarget.style.boxShadow = `0 0 0 2px ${color}60`)}
      onBlur={e => (e.currentTarget.style.boxShadow = '')}
    >
      {children}
    </button>
  )
}

// ─────────────────────────────────────────────────────────────────────────────
// Open Positions Panel (lazy-loaded when expanded)
// ─────────────────────────────────────────────────────────────────────────────

function PositionsPanel({ brokerName }: { brokerName: string }) {
  const { data: positions, isLoading } = useQuery({
    queryKey: ['positions', brokerName],
    queryFn: () => brokerApi.positions(brokerName).then(r => r.data.data ?? []),
    refetchInterval: 10_000,
  })

  if (isLoading) {
    return <p style={{ color: '#64748b', fontSize: 12, margin: 0 }}>Loading positions…</p>
  }

  if (!positions || positions.length === 0) {
    return <p style={{ color: '#475569', fontSize: 12, margin: 0 }}>No open positions.</p>
  }

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
// Live Start Confirmation Modal
// ─────────────────────────────────────────────────────────────────────────────

function LiveConfirmModal({
  instance,
  onConfirm,
  onCancel,
  isPending,
}: {
  instance: StrategyInstance
  onConfirm: () => void
  onCancel: () => void
  isPending: boolean
}) {
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
          ⚠ Start Live Trading
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

export function StrategyCard({ instance }: Props) {
  const qc = useQueryClient()
  const [liveConfirmOpen, setLiveConfirmOpen] = useState(false)
  const [positionsExpanded, setPositionsExpanded] = useState(false)

  const startMutation = useMutation({
    mutationFn: () => strategiesApi.start(instance.id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['strategies'] })
      setLiveConfirmOpen(false)
    }
  })

  const stopMutation = useMutation({
    mutationFn: () => strategiesApi.stop(instance.id, 'MANUAL'),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['strategies'] })
  })

  const pauseMutation = useMutation({
    mutationFn: () => strategiesApi.pause(instance.id, 'MANUAL'),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['strategies'] })
  })

  const isRunning  = instance.status === 'Running'
  const isPaused   = instance.status === 'Paused'
  const isLive     = instance.mode === 'Live'
  const statusCfg  = STATUS_COLORS[instance.status] ?? STATUS_COLORS.Stopped

  const todayRealized   = instance.todayRealizedPnl   ?? 0
  const todayUnrealized = instance.todayUnrealizedPnl ?? 0
  const openPositions   = instance.openPositionCount  ?? 0
  const showPnl = isRunning || isPaused || todayRealized !== 0 || todayUnrealized !== 0

  const handleStart = () => {
    if (isLive) {
      setLiveConfirmOpen(true)
    } else {
      startMutation.mutate()
    }
  }

  return (
    <>
      <article
        aria-label={`Strategy: ${instance.name}, Status: ${instance.status}`}
        style={{
          background: '#1e1e2e',
          border: isLive ? '1px solid #451a03' : '1px solid #2d2d3f',
          borderRadius: 10,
          padding: 16,
          display: 'flex',
          flexDirection: 'column',
          gap: 12,
        }}
      >
        {/* Live mode warning stripe */}
        {isLive && (
          <div style={{ background: '#451a03', borderRadius: 4, padding: '4px 10px', fontSize: 11, color: '#fbbf24', fontWeight: 600 }}>
            ⚠ LIVE — Real money orders
          </div>
        )}

        {/* Header: name + status badge */}
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
          <div style={{ minWidth: 0, flex: 1, marginRight: 10 }}>
            <div style={{
              fontWeight: 700,
              fontSize: 15,
              color: '#e2e8f0',
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
            }}
              title={instance.name}
            >
              {instance.name}
            </div>
            <div style={{ fontSize: 12, color: '#64748b', marginTop: 3 }}>
              {instance.strategyType} · {instance.internalSymbol} · {instance.timeframe}
            </div>
          </div>

          <span style={{
            display: 'flex',
            alignItems: 'center',
            gap: 5,
            background: statusCfg.bg,
            color: statusCfg.text,
            borderRadius: 20,
            padding: '3px 10px',
            fontSize: 11,
            fontWeight: 700,
            whiteSpace: 'nowrap',
            flexShrink: 0,
          }}>
            <span style={{
              width: 6, height: 6, borderRadius: '50%',
              background: statusCfg.dot, flexShrink: 0,
            }} aria-hidden="true" />
            {instance.status}
          </span>
        </div>

        {/* Meta row */}
        <div style={{
          display: 'flex',
          gap: 12,
          flexWrap: 'wrap',
          fontSize: 12,
          color: '#64748b',
        }}>
          <span>
            Broker: <strong style={{ color: '#94a3b8' }}>{instance.brokerName}</strong>
          </span>
          <span>
            Mode: <strong style={{ color: isLive ? '#fbbf24' : '#94a3b8' }}>{instance.mode}</strong>
          </span>
          {instance.allocatedCapital != null && instance.allocatedCapital > 0 && (
            <span>
              Capital: <strong style={{ color: '#94a3b8' }}>{formatInr(instance.allocatedCapital)}</strong>
            </span>
          )}
        </div>

        {/* Real-time P&L strip */}
        {showPnl && (
          <div style={{
            display: 'flex',
            gap: 0,
            padding: '8px 12px',
            background: '#13131f',
            borderRadius: 7,
            border: '1px solid #2d2d3f',
            alignItems: 'center',
            justifyContent: 'space-around',
          }}
            aria-label="Today's P&L summary"
          >
            <PnlBadge value={todayRealized} label="Realized" />
            <div style={{ width: 1, height: 32, background: '#2d2d3f', flexShrink: 0 }} aria-hidden="true" />
            <PnlBadge value={todayUnrealized} label="Unrealized" />
            <div style={{ width: 1, height: 32, background: '#2d2d3f', flexShrink: 0 }} aria-hidden="true" />
            <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 2 }}>
              <span style={{ fontSize: 11, color: '#64748b', fontWeight: 600 }}>Open</span>
              <span style={{
                fontSize: 15,
                fontWeight: 700,
                color: openPositions > 0 ? '#3b82f6' : '#475569',
                fontVariantNumeric: 'tabular-nums',
              }}>
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
              style={{
                background: 'none', border: 'none', cursor: 'pointer',
                color: '#3b82f6', fontSize: 12, fontWeight: 600, padding: '0 0 6px 0',
                display: 'flex', alignItems: 'center', gap: 4,
              }}
            >
              {positionsExpanded ? '▲' : '▶'} View open positions ({openPositions})
            </button>
            {positionsExpanded && (
              <div style={{ background: '#13131f', borderRadius: 6, padding: '10px 12px', border: '1px solid #2d2d3f' }}>
                <PositionsPanel brokerName={instance.brokerName} />
              </div>
            )}
          </div>
        )}

        {/* Action buttons */}
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
          {!isRunning && (
            <ActionButton
              onClick={handleStart}
              disabled={startMutation.isPending}
              color={isLive ? '#dc2626' : '#16a34a'}
              ariaLabel={`Start strategy ${instance.name}`}
            >
              {startMutation.isPending ? 'Starting…' : '▶ Start'}
            </ActionButton>
          )}
          {isRunning && (
            <ActionButton
              onClick={() => pauseMutation.mutate()}
              disabled={pauseMutation.isPending}
              color="#d97706"
              ariaLabel={`Pause strategy ${instance.name}`}
            >
              {pauseMutation.isPending ? 'Pausing…' : '⏸ Pause'}
            </ActionButton>
          )}
          {(isRunning || isPaused) && (
            <ActionButton
              onClick={() => stopMutation.mutate()}
              disabled={stopMutation.isPending}
              color="#dc2626"
              ariaLabel={`Stop strategy ${instance.name}`}
            >
              {stopMutation.isPending ? 'Stopping…' : '⏹ Stop'}
            </ActionButton>
          )}
        </div>
      </article>

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
