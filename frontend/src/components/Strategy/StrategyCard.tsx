import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { StrategyInstance, strategiesApi, scenariosApi, brokerApi, BrokerPosition, CreateStrategyCommand, approvalApi } from '../../api/client'
import { formatInr } from '../../utils/datetime'
import { C } from '../../styles/tokens'
import ScenariosPanel from './ScenariosPanel'
import { PayoffChart, buildPayoffData, SpreadType as PayoffSpreadType } from './PayoffChart'

const STATUS_COLORS: Record<string, { bg: string; text: string; dot: string }> = {
  Draft:     { bg: C.surface3, text: C.textSub,  dot: C.textSub  },
  Running:   { bg: C.greenBg,  text: C.green,    dot: C.green    },
  Paused:    { bg: C.amberBg,  text: C.amber,    dot: C.amber    },
  Stopped:   { bg: C.surface,  text: C.textMuted, dot: C.textMuted },
  Scheduled: { bg: C.blueBg,   text: C.textSub,  dot: C.blue     },
  Error:     { bg: C.redBg,    text: C.red,      dot: C.red      },
}

interface Props {
  instance: StrategyInstance
  /** Called when user clicks "Forward Test" on a Backtest-mode instance */
  onPromoteToForward?: (instance: StrategyInstance) => void
  /** Called when a scenario run is enqueued — parent navigates to backtest tab to show live result */
  onScenarioJobStarted?: (jobId: string) => void
  /** Called when user clicks "Chart" in compare view — parent navigates to backtest tab for that run */
  onOpenBacktestResult?: (runId: string) => void
}

function PnlBadge({ value, label }: { value: number; label: string }) {
  const isPositive = value >= 0
  const color = isPositive ? C.green : C.red
  const bg   = isPositive ? C.green18 : C.red18
  return (
    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 2 }}>
      <span style={{ fontSize: 11, color: C.textSub, fontWeight: 600, letterSpacing: '0.02em' }}>
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
        background: disabled ? C.surface3 : hovered ? `${color}cc` : color,
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
  onClick, color, bg, children, title, borderColor,
}: { onClick: () => void; color: string; bg: string; children: React.ReactNode; title?: string; borderColor?: string }) {
  return (
    <button
      onClick={onClick}
      title={title}
      style={{
        padding: '4px 10px', background: bg, color,
        border: `1px solid ${borderColor ?? `${color}44`}`,
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
  if (isLoading) return <p style={{ color: C.textSub, fontSize: 12, margin: 0 }}>Loading positions…</p>
  if (!positions || positions.length === 0)
    return <p style={{ color: C.textSub, fontSize: 12, margin: 0 }}>No open positions.</p>
  return (
    <div style={{ overflowX: 'auto' }}>
      <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
        <thead>
          <tr style={{ borderBottom: `1px solid ${C.border3}` }}>
            {['Symbol', 'Qty', 'Avg Price', 'LTP', 'P&L', 'Product'].map(col => (
              <th key={col} style={{ padding: '5px 8px', textAlign: 'left', color: C.textSub, fontWeight: 600 }}>{col}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {positions.map((pos: BrokerPosition, i: number) => (
            <tr key={i} style={{ borderBottom: `1px solid ${C.border}` }}>
              <td style={{ padding: '5px 8px', color: C.text, fontWeight: 600 }}>{pos.internalSymbol}</td>
              <td style={{ padding: '5px 8px', color: pos.quantity > 0 ? C.green : C.red }}>{pos.quantity}</td>
              <td style={{ padding: '5px 8px', color: C.textSub }}>{formatInr(pos.averagePrice)}</td>
              <td style={{ padding: '5px 8px', color: C.text }}>{formatInr(pos.lastPrice)}</td>
              <td style={{ padding: '5px 8px', color: pos.pnl >= 0 ? C.green : C.red, fontWeight: 700 }}>
                {pos.pnl >= 0 ? '+' : ''}{formatInr(pos.pnl)}
              </td>
              <td style={{ padding: '5px 8px', color: C.textSub }}>{pos.productType}</td>
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
        position: 'fixed', top: 36, right: 0, bottom: 0, width: 520,
        background: C.surface, borderLeft: `1px solid ${C.border3}`,
        zIndex: 300, overflowY: 'auto', padding: '16px 20px',
        boxShadow: '-8px 0 32px rgba(0,0,0,0.6)',
        display: 'flex', flexDirection: 'column', gap: 12,
      }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', paddingBottom: 8, borderBottom: `1px solid ${C.border3}` }}>
          <span style={{ fontSize: 12, fontWeight: 700, color: C.text, textTransform: 'uppercase', letterSpacing: '0.06em' }}>
            Edit Strategy
          </span>
          <button onClick={onClose} style={{ background: 'none', border: 'none', cursor: 'pointer', fontSize: 16, color: C.textSub }}>✕</button>
        </div>

        {instance.status === 'Running' && (
          <div style={{ background: C.amberBg, border: `1px solid ${C.amber44}`, borderRadius: 4, padding: '8px 12px', fontSize: 11, color: C.amber }}>
            Stop or pause the strategy before editing.
          </div>
        )}

        {errorMsg && (
          <div style={{ background: C.redBg, border: `1px solid ${C.red44}`, borderRadius: 4, padding: '8px 12px', fontSize: 12, color: C.red }}>
            {errorMsg}
          </div>
        )}

        <label style={{ display: 'flex', flexDirection: 'column', gap: 4, fontSize: 11, color: C.textSub, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
          Instance Name
          <input
            value={name}
            onChange={e => setName(e.target.value)}
            style={{ background: C.surface2, border: `1px solid ${C.border3}`, borderRadius: 4, color: C.text, padding: '7px 10px', fontSize: 13 }}
          />
        </label>

        <label style={{ display: 'flex', flexDirection: 'column', gap: 4, fontSize: 11, color: C.textSub, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
          Symbol
          <input
            value={internalSymbol}
            onChange={e => setInternalSymbol(e.target.value)}
            style={{ background: C.surface2, border: `1px solid ${C.border3}`, borderRadius: 4, color: C.text, padding: '7px 10px', fontSize: 13 }}
          />
        </label>

        <label style={{ display: 'flex', flexDirection: 'column', gap: 4, fontSize: 11, color: C.textSub, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
          Timeframe
          <select
            value={timeframe}
            onChange={e => setTimeframe(e.target.value)}
            style={{ background: C.surface2, border: `1px solid ${C.border3}`, borderRadius: 4, color: C.text, padding: '7px 10px', fontSize: 13 }}
          >
            {['1m', '5m', '15m', '30m', '60m', '1d'].map(tf => (
              <option key={tf} value={tf}>{tf}</option>
            ))}
          </select>
        </label>

        <label style={{ display: 'flex', flexDirection: 'column', gap: 4, fontSize: 11, color: C.textSub, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
          Broker
          <input
            value={brokerName}
            onChange={e => setBrokerName(e.target.value)}
            style={{ background: C.surface2, border: `1px solid ${C.border3}`, borderRadius: 4, color: C.text, padding: '7px 10px', fontSize: 13 }}
          />
        </label>

        <label style={{ display: 'flex', flexDirection: 'column', gap: 4, fontSize: 11, color: C.textSub, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
          Allocated Capital (₹)
          <input
            type="number"
            value={allocatedCapital}
            onChange={e => setAllocatedCapital(e.target.value)}
            style={{ background: C.surface2, border: `1px solid ${C.border3}`, borderRadius: 4, color: C.text, padding: '7px 10px', fontSize: 13 }}
          />
        </label>

        <label style={{ display: 'flex', flexDirection: 'column', gap: 4, fontSize: 11, color: C.textSub, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
          Parameters JSON
          <textarea
            value={parametersJson}
            onChange={e => setParametersJson(e.target.value)}
            rows={6}
            style={{ background: C.surface2, border: `1px solid ${C.border3}`, borderRadius: 4, color: C.text, padding: '7px 10px', fontSize: 12, fontFamily: 'monospace', resize: 'vertical' }}
          />
        </label>

        <div style={{ display: 'flex', gap: 8, marginTop: 'auto', paddingTop: 12, borderTop: `1px solid ${C.border3}` }}>
          <button
            onClick={onClose}
            style={{ flex: 1, padding: '8px', background: C.surface2, color: C.textSub, border: `1px solid ${C.border3}`, borderRadius: 4, cursor: 'pointer', fontSize: 12 }}
          >
            Cancel
          </button>
          <button
            onClick={() => updateMutation.mutate()}
            disabled={updateMutation.isPending || instance.status === 'Running'}
            style={{
              flex: 1, padding: '8px', background: C.blue, color: 'white',
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
        backgroundColor: C.surface, border: `2px solid ${C.red}`, borderRadius: 10,
        padding: 28, maxWidth: 440, width: '90%',
      }}>
        <h3 style={{ color: C.red, fontSize: 18, fontWeight: 700, marginTop: 0, marginBottom: 8 }}>
          Start Live Trading
        </h3>
        <p style={{ color: C.text, fontSize: 14, lineHeight: 1.6, marginBottom: 8 }}>
          You are about to start <strong>{instance.name}</strong> in <strong>Live mode</strong>.
        </p>
        <p style={{ color: C.red, fontSize: 13, lineHeight: 1.6, marginBottom: 20 }}>
          This will place <strong>real orders</strong> on <strong>{instance.brokerName}</strong> using up to{' '}
          <strong>{formatInr(instance.allocatedCapital ?? 0)}</strong> of capital. Losses are real.
        </p>
        <div style={{ display: 'flex', gap: 12, justifyContent: 'flex-end' }}>
          <button
            onClick={onCancel}
            style={{ padding: '8px 16px', background: C.border3, color: C.text, border: `1px solid ${C.border3}`, borderRadius: 6, cursor: 'pointer', fontSize: 14 }}
          >
            Cancel
          </button>
          <button
            onClick={onConfirm}
            disabled={isPending}
            style={{ padding: '8px 18px', background: C.red, color: 'white', border: 'none', borderRadius: 6, cursor: isPending ? 'not-allowed' : 'pointer', fontSize: 14, fontWeight: 700 }}
          >
            {isPending ? 'Starting…' : 'Yes, Start Live'}
          </button>
        </div>
      </div>
    </div>
  )
}

// ─────────────────────────────────────────────────────────────────────────────
// Approval Drawer
// ─────────────────────────────────────────────────────────────────────────────

function ApprovalDrawer({ instance, onClose }: { instance: StrategyInstance; onClose: () => void }) {
  const qc = useQueryClient()
  const [notes, setNotes] = useState('')
  const [revokeReason, setRevokeReason] = useState('')

  const { data: checksData, isLoading: checksLoading } = useQuery({
    queryKey: ['approval-checks', instance.id],
    queryFn: () => approvalApi.getChecks(instance.id).then(r => r.data.data!),
  })

  const { data: activeApproval } = useQuery({
    queryKey: ['approval-status', instance.id],
    queryFn: () => approvalApi.getStatus(instance.id).then(r => r.data.data ?? null),
  })

  const approveMutation = useMutation({
    mutationFn: () => approvalApi.approve(instance.id, notes || undefined),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['approval-status', instance.id] })
      qc.invalidateQueries({ queryKey: ['approval-checks', instance.id] })
      onClose()
    },
  })

  const revokeMutation = useMutation({
    mutationFn: () => approvalApi.revoke(instance.id, activeApproval!.id, revokeReason),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['approval-status', instance.id] })
      onClose()
    },
  })

  const pct = (v?: number) => v != null ? `${(v * 100).toFixed(1)}%` : '—'

  return (
    <>
      <div onClick={onClose} style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.4)', zIndex: 299 }} />
      <div style={{
        position: 'fixed', top: 36, right: 0, bottom: 0, width: 520,
        background: C.surface, borderLeft: `1px solid ${C.border3}`,
        zIndex: 300, overflowY: 'auto', padding: '16px 20px',
        boxShadow: '-8px 0 32px rgba(0,0,0,0.6)',
        display: 'flex', flexDirection: 'column', gap: 12,
      }}>
        {/* Header */}
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', paddingBottom: 8, borderBottom: `1px solid ${C.border3}` }}>
          <span style={{ fontSize: 12, fontWeight: 700, color: C.text, textTransform: 'uppercase', letterSpacing: '0.06em' }}>
            Approval Gate — {instance.name}
          </span>
          <button onClick={onClose} style={{ background: 'none', border: 'none', cursor: 'pointer', fontSize: 16, color: C.textSub }}>✕</button>
        </div>

        {/* Current status */}
        {activeApproval ? (
          <div style={{ background: C.greenBg, border: `1px solid ${C.green44}`, borderRadius: 6, padding: '10px 14px' }}>
            <div style={{ fontSize: 11, fontWeight: 700, color: C.green, textTransform: 'uppercase', letterSpacing: '0.05em', marginBottom: 4 }}>
              Active Approval
            </div>
            <div style={{ fontSize: 12, color: C.text }}>
              Approved by <strong>{activeApproval.approvedBy}</strong> on{' '}
              {new Date(activeApproval.createdAt).toLocaleString()}
            </div>
            {activeApproval.approvalNotes && (
              <div style={{ fontSize: 12, color: C.textSub, marginTop: 4 }}>Notes: {activeApproval.approvalNotes}</div>
            )}
          </div>
        ) : (
          <div style={{ background: C.redBg, border: `1px solid ${C.red44}`, borderRadius: 6, padding: '10px 14px', fontSize: 12, color: C.red }}>
            No active approval. This strategy will NOT place real orders until approved.
          </div>
        )}

        {/* Automated check results */}
        <div style={{ fontSize: 11, fontWeight: 700, color: C.textSub, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
          Automated Checks
        </div>
        {checksLoading ? (
          <p style={{ color: C.textSub, fontSize: 12, margin: 0 }}>Running checks…</p>
        ) : checksData ? (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            {[
              { label: 'CAGR', value: pct(checksData.cagr), pass: (checksData.cagr ?? 0) >= 0.20 },
              { label: 'Max Drawdown', value: pct(checksData.drawdown), pass: (checksData.drawdown ?? 1) <= 0.20 },
              { label: 'Fwd Test Days', value: checksData.forwardTestDays != null ? `${checksData.forwardTestDays}d` : '—', pass: (checksData.forwardTestDays ?? 0) >= 15 },
              { label: 'Fwd Win Rate', value: pct(checksData.forwardWinRate), pass: (checksData.forwardWinRate ?? 0) >= 0.40 },
            ].map(({ label, value, pass }) => (
              <div key={label} style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', background: C.surface2, borderRadius: 4, padding: '6px 10px' }}>
                <span style={{ fontSize: 12, color: C.textSub }}>{label}</span>
                <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
                  <span style={{ fontSize: 12, fontWeight: 700, color: C.text, fontVariantNumeric: 'tabular-nums' }}>{value}</span>
                  <span style={{ fontSize: 10, fontWeight: 700, color: pass ? C.green : C.red }}>{pass ? 'PASS' : 'FAIL'}</span>
                </div>
              </div>
            ))}
            <div style={{ fontSize: 11, color: checksData.automatedChecksPassed ? C.green : C.red, fontWeight: 700, textAlign: 'right' }}>
              {checksData.automatedChecksPassed ? 'All automated checks passed' : `${checksData.failedChecks.length} check(s) failed`}
            </div>
          </div>
        ) : null}

        {/* Manual approval always required disclaimer */}
        <div style={{ background: C.surface2, borderRadius: 4, padding: '8px 12px', fontSize: 11, color: C.textSub, borderLeft: `3px solid ${C.amber}` }}>
          Manual approval is always required, even when all automated checks pass.
        </div>

        {/* Actions */}
        {activeApproval ? (
          <>
            <label style={{ display: 'flex', flexDirection: 'column', gap: 4, fontSize: 11, color: C.textSub, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
              Revocation Reason
              <input
                value={revokeReason}
                onChange={e => setRevokeReason(e.target.value)}
                placeholder="Why are you revoking this approval?"
                style={{ background: C.surface2, border: `1px solid ${C.border3}`, borderRadius: 4, color: C.text, padding: '7px 10px', fontSize: 13 }}
              />
            </label>
            <button
              onClick={() => revokeMutation.mutate()}
              disabled={revokeMutation.isPending || !revokeReason.trim()}
              style={{
                padding: '9px', background: C.red, color: 'white',
                border: 'none', borderRadius: 4, cursor: revokeMutation.isPending || !revokeReason.trim() ? 'not-allowed' : 'pointer',
                fontSize: 12, fontWeight: 700, opacity: !revokeReason.trim() ? 0.5 : 1,
              }}
            >
              {revokeMutation.isPending ? 'Revoking…' : 'Revoke Approval'}
            </button>
          </>
        ) : (
          <>
            <label style={{ display: 'flex', flexDirection: 'column', gap: 4, fontSize: 11, color: C.textSub, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
              Approval Notes (optional)
              <textarea
                value={notes}
                onChange={e => setNotes(e.target.value)}
                placeholder="Add notes about this approval decision…"
                rows={3}
                style={{ background: C.surface2, border: `1px solid ${C.border3}`, borderRadius: 4, color: C.text, padding: '7px 10px', fontSize: 13, resize: 'vertical' }}
              />
            </label>
            <button
              onClick={() => approveMutation.mutate()}
              disabled={approveMutation.isPending}
              style={{
                padding: '9px', background: C.green, color: 'white',
                border: 'none', borderRadius: 4, cursor: approveMutation.isPending ? 'not-allowed' : 'pointer',
                fontSize: 12, fontWeight: 700,
              }}
            >
              {approveMutation.isPending ? 'Approving…' : 'Approve for Live Trading'}
            </button>
          </>
        )}
      </div>
    </>
  )
}

// ─────────────────────────────────────────────────────────────────────────────
// StrategyCard
// ─────────────────────────────────────────────────────────────────────────────

export function StrategyCard({ instance, onPromoteToForward, onScenarioJobStarted, onOpenBacktestResult }: Props) {
  const qc = useQueryClient()
  const [liveConfirmOpen, setLiveConfirmOpen] = useState(false)
  const [positionsExpanded, setPositionsExpanded] = useState(false)
  const [payoffOpen, setPayoffOpen] = useState(false)
  const [editOpen, setEditOpen] = useState(false)
  const [approvalOpen, setApprovalOpen] = useState(false)

  const { data: approvalStatus } = useQuery({
    queryKey: ['approval-status', instance.id],
    queryFn: () => approvalApi.getStatus(instance.id).then(r => r.data.data ?? null),
    enabled: instance.mode === 'Live',
    staleTime: 30_000,
  })

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

  // Shared cache with ScenariosPanel — no extra network request when panel is visible
  const { data: scenariosResp } = useQuery({
    queryKey: ['scenarios', instance.id],
    queryFn: () => scenariosApi.list(instance.id),
    staleTime: 30_000,
  })
  const hasScenarios = (scenariosResp?.data?.data?.length ?? 0) > 0

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

  // Derive options payoff data from parametersJson (GenericRules config)
  const optionsPayoff = (() => {
    try {
      const cfg = JSON.parse(instance.parametersJson ?? '{}')
      const oc = cfg.optionsConfig
      if (!oc?.enabled) return null
      const typeMap: Record<string, PayoffSpreadType> = {
        BullCallSpread: 'bull_call', BearPutSpread: 'bear_put',
        BullPutSpread: 'bull_put', BearCallSpread: 'bear_call',
        IronCondor: 'iron_condor', ShortStraddle: 'straddle',
        ShortStrangle: 'strangle', LongCall: 'bull_call', LongPut: 'bear_put',
      }
      const payoffType = typeMap[oc.spreadType]
      if (!payoffType) return null
      // Derive a representative spot from symbol name
      const symUpper = (instance.internalSymbol ?? '').toUpperCase()
      const spot = symUpper.includes('BANK') ? 48000 : symUpper.includes('NIFTY') ? 22500 : 22500
      const interval = 100
      const atm = Math.round(spot / interval) * interval
      const isShort = oc.spreadType === 'ShortStraddle' || oc.spreadType === 'ShortStrangle'
      return buildPayoffData({
        type: payoffType, spot: atm,
        lowerStrike: atm - interval * 2,
        upperStrike: atm + interval * 2,
        netPremium: isShort ? -(atm * 0.012) : atm * 0.008,
        innerLowerStrike: payoffType === 'iron_condor' ? atm - interval : undefined,
        innerUpperStrike: payoffType === 'iron_condor' ? atm + interval : undefined,
      })
    } catch { return null }
  })()

  return (
    <>
      <article
        aria-label={`Strategy: ${instance.name}, Status: ${instance.status}`}
        style={{
          background: C.surface,
          border: isLive ? `1px solid ${C.amberBg}` : `1px solid ${C.border3}`,
          borderRadius: 10, padding: 16,
          display: 'flex', flexDirection: 'column', gap: 12,
        }}
      >
        {/* Mode stripe — Live / Forward / Backtest */}
        {isLive && (
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', background: C.amberBg, borderRadius: 4, padding: '4px 10px' }}>
            <span style={{ fontSize: 11, color: C.amber, fontWeight: 600 }}>LIVE — Real money orders</span>
            <span style={{
              fontSize: 10, fontWeight: 700, borderRadius: 3, padding: '2px 7px',
              background: approvalStatus ? C.greenBg : C.redBg,
              color: approvalStatus ? C.green : C.red,
              border: `1px solid ${approvalStatus ? C.green44 : C.red44}`,
            }}>
              {approvalStatus ? 'APPROVED' : 'NO APPROVAL'}
            </span>
          </div>
        )}
        {isForward && (
          <div style={{ background: C.blueBg, borderRadius: 4, padding: '4px 10px', fontSize: 11, color: C.textSub, fontWeight: 600 }}>
            FORWARD TEST — Paper trading
          </div>
        )}
        {isBacktest && (
          <div style={{ background: C.amberBg, borderRadius: 4, padding: '4px 10px', fontSize: 11, color: C.amber, fontWeight: 600, display: 'flex', alignItems: 'center', gap: 6 }}>
            <span>STEP 1 — Run backtest</span>
            <span style={{ color: C.textSub, fontWeight: 400 }}>→</span>
            <span style={{ color: C.textSub, fontWeight: 400 }}>STEP 2 — Promote to Forward Test</span>
            <span style={{ color: C.textSub, fontWeight: 400 }}>→</span>
            <span style={{ color: C.textSub, fontWeight: 400 }}>STEP 3 — Live</span>
          </div>
        )}

        {/* Header: name + status badge */}
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
          <div style={{ minWidth: 0, flex: 1, marginRight: 10 }}>
            <div style={{ fontWeight: 700, fontSize: 15, color: C.text, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
              title={instance.name}>
              {instance.name}
            </div>
            <div style={{ fontSize: 12, color: C.textSub, marginTop: 3 }}>
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
        <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap', fontSize: 12, color: C.textSub }}>
          <span>Broker: <strong style={{ color: C.textSub }}>{instance.brokerName}</strong></span>
          <span>Mode: <strong style={{ color: isLive ? C.amber : isForward ? C.blue : C.textSub }}>{instance.mode}</strong></span>
          {instance.allocatedCapital != null && instance.allocatedCapital > 0 && (
            <span>Capital: <strong style={{ color: C.textSub }}>{formatInr(instance.allocatedCapital)}</strong></span>
          )}
        </div>

        {/* Real-time P&L strip */}
        {showPnl && (
          <div style={{
            display: 'flex', gap: 0, padding: '8px 12px',
            background: C.bg, borderRadius: 7, border: `1px solid ${C.border3}`,
            alignItems: 'center', justifyContent: 'space-around',
          }}>
            <PnlBadge value={todayRealized} label="Realized" />
            <div style={{ width: 1, height: 32, background: C.border3, flexShrink: 0 }} />
            <PnlBadge value={todayUnrealized} label="Unrealized" />
            <div style={{ width: 1, height: 32, background: C.border3, flexShrink: 0 }} />
            <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 2 }}>
              <span style={{ fontSize: 11, color: C.textSub, fontWeight: 600 }}>Open</span>
              <span style={{ fontSize: 15, fontWeight: 700, color: openPositions > 0 ? C.blue : C.textSub, fontVariantNumeric: 'tabular-nums' }}>
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
              style={{ background: 'none', border: 'none', cursor: 'pointer', color: C.blue, fontSize: 12, fontWeight: 600, padding: '0 0 6px 0', display: 'flex', alignItems: 'center', gap: 4 }}
            >
              {positionsExpanded ? '▲' : '▶'} View open positions ({openPositions})
            </button>
            {positionsExpanded && (
              <div style={{ background: C.bg, borderRadius: 6, padding: '10px 12px', border: `1px solid ${C.border3}` }}>
                <PositionsPanel brokerName={instance.brokerName} />
              </div>
            )}
          </div>
        )}

        {/* Options payoff chart (collapsible, only for options-based strategies) */}
        {optionsPayoff && (
          <div>
            <button
              onClick={() => setPayoffOpen(o => !o)}
              style={{
                background: 'none', border: 'none', cursor: 'pointer',
                color: C.blue, fontSize: 11, fontWeight: 600, padding: '0 0 6px 0',
                display: 'flex', alignItems: 'center', gap: 4,
              }}
            >
              {payoffOpen ? '▲' : '▶'} {payoffOpen ? 'Hide' : 'View'} payoff diagram
            </button>
            {payoffOpen && (
              <div style={{ background: C.bg, borderRadius: 6, padding: '12px 14px', border: `1px solid ${C.border3}` }}>
                <PayoffChart
                  data={optionsPayoff.data}
                  breakeven={optionsPayoff.breakeven}
                  height={200}
                />
              </div>
            )}
          </div>
        )}

        {/* Primary action buttons */}
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
          {/* Forward / Live mode: normal Start/Pause/Stop */}
          {!isBacktest && !isRunning && (
            <div title={!hasScenarios ? 'Create at least one scenario before starting' : undefined}>
              <ActionButton
                onClick={() => isLive ? setLiveConfirmOpen(true) : startMutation.mutate()}
                disabled={startMutation.isPending || !hasScenarios}
                color={isLive ? C.red : C.green}
                ariaLabel={`Start strategy ${instance.name}`}
              >
                {startMutation.isPending ? 'Starting…' : '▶ Start'}
              </ActionButton>
            </div>
          )}
          {!isBacktest && isRunning && (
            <ActionButton
              onClick={() => pauseMutation.mutate()}
              disabled={pauseMutation.isPending}
              color={C.amber}
              ariaLabel={`Pause strategy ${instance.name}`}
            >
              {pauseMutation.isPending ? 'Pausing…' : '⏸ Pause'}
            </ActionButton>
          )}
          {!isBacktest && (isRunning || isPaused) && (
            <ActionButton
              onClick={() => stopMutation.mutate()}
              disabled={stopMutation.isPending}
              color={C.red}
              ariaLabel={`Stop strategy ${instance.name}`}
            >
              {stopMutation.isPending ? 'Stopping…' : '■ Stop'}
            </ActionButton>
          )}
        </div>

        {/* Secondary actions (Edit / Backtest / Promote) */}
        <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', borderTop: `1px solid ${C.border3}`, paddingTop: 10 }}>
          <SmallButton
            onClick={() => setEditOpen(true)}
            color={C.textSub}
            bg={C.surface2}
            title="Edit instance settings"
          >
            Edit
          </SmallButton>

          {/* Backtest → Forward promotion: only shown on Backtest-mode instances */}
          {onPromoteToForward && isBacktest && (
            <SmallButton
              onClick={() => onPromoteToForward(instance)}
              color={C.green}
              bg={C.greenBg88}
              title="Run a backtest first, then use 'Promote to Forward Test' on the result"
            >
              Promote to Forward Test →
            </SmallButton>
          )}

          {/* Approval Gate — only for Live mode instances */}
          {isLive && (
            <SmallButton
              onClick={() => setApprovalOpen(true)}
              color={approvalStatus ? C.green : C.red}
              bg={approvalStatus ? C.greenBg : C.redBg}
              borderColor={approvalStatus ? C.green44 : C.red44}
              title={approvalStatus ? 'View or revoke live-trading approval' : 'Approve this strategy for live trading'}
            >
              {approvalStatus ? 'Approved' : 'Approve'}
            </SmallButton>
          )}

          <SmallButton
            onClick={() => {
              if (window.confirm(`Delete strategy "${instance.name}"? This cannot be undone.`)) {
                deleteMutation.mutate()
              }
            }}
            color={C.red}
            bg={C.redBg88}
            title="Permanently delete this strategy instance"
          >
            {deleteMutation.isPending ? 'Deleting…' : 'Delete'}
          </SmallButton>
        </div>
        {/* Scenarios */}
        <div style={{ borderTop: `1px solid ${C.border3}`, margin: '4px -16px -16px', padding: 0 }}>
          <ScenariosPanel
            instanceId={instance.id}
            strategyType={instance.strategyType}
            instanceName={instance.name}
            baseParametersJson={instance.parametersJson ?? '{}'}
            defaultSymbol={instance.internalSymbol}
            defaultTimeframe={instance.timeframe || '1d'}
            defaultFromDate={new Date(Date.now() - 365 * 24 * 60 * 60 * 1000).toISOString().slice(0, 10)}
            defaultToDate={new Date().toISOString().slice(0, 10)}
            onJobStarted={onScenarioJobStarted}
            onOpenBacktestResult={onOpenBacktestResult}
          />
        </div>
      </article>

      {/* Edit Drawer */}
      {editOpen && <EditDrawer instance={instance} onClose={() => setEditOpen(false)} />}

      {/* Approval Drawer */}
      {approvalOpen && <ApprovalDrawer instance={instance} onClose={() => setApprovalOpen(false)} />}

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
