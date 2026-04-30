/**
 * ForwardTestPage — enhanced forward test session management.
 *
 * Improvements over v1:
 * - Source backtest comparison panel (when promoted from a backtest)
 * - PreFlightChecklistModal instead of a blind single-click "Promote to Live"
 * - Richer status indicators and metric badges
 */

import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { forwardTestApi, ForwardTestSession } from '../api/client'
import { C, CHART } from '../styles/tokens'
import { formatInr, formatIst } from '../utils/datetime'
import { BacktestComparisonPanel } from '../components/ForwardTest/BacktestComparisonPanel'
import { PreFlightChecklistModal } from '../components/ForwardTest/PreFlightChecklistModal'
import {
  AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip,
  ResponsiveContainer, ReferenceLine,
} from 'recharts'
import { formatInTimeZone } from 'date-fns-tz'

function statusColor(status: string): string {
  switch (status?.toLowerCase()) {
    case 'running':   return C.green
    case 'paused':    return C.amber
    case 'stopped':   return C.textMuted
    case 'completed': return C.blue
    case 'pending':   return CHART.violet
    default:          return C.textSub
  }
}

function MetricBadge({ label, value, color = C.text }: { label: string; value: string; color?: string }) {
  return (
    <div style={{ minWidth: 90 }}>
      <div style={{ fontSize: 10, color: C.textMuted, marginBottom: 2, fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.04em' }}>{label}</div>
      <div style={{ fontSize: 15, fontWeight: 700, color }}>{value}</div>
    </div>
  )
}

function ForwardEquityCurve({ points, initialCapital }: { points: NonNullable<ForwardTestSession['equityCurvePoints']>; initialCapital: number }) {
  if (points.length === 0) {
    return <p style={{ color: C.textMuted, fontSize: 12 }}>No equity curve data yet — trades will appear here as they complete.</p>
  }

  const data = points.map(p => ({
    label: formatInTimeZone(new Date(p.time), 'Asia/Kolkata', 'dd MMM'),
    equity: p.equity,
    pnl: p.pnl,
  }))

  const equities = data.map(d => d.equity)
  const min = Math.min(...equities)
  const max = Math.max(...equities)
  const pad = (max - min) * 0.1 || initialCapital * 0.05

  return (
    <div style={{ width: '100%', height: 180 }}>
      <ResponsiveContainer>
        <AreaChart data={data} margin={{ top: 4, right: 8, bottom: 0, left: 8 }}>
          <defs>
            <linearGradient id="fwdEquityGrad" x1="0" y1="0" x2="0" y2="1">
              <stop offset="5%"  stopColor={C.green} stopOpacity={0.25} />
              <stop offset="95%" stopColor={C.green} stopOpacity={0.02} />
            </linearGradient>
          </defs>
          <CartesianGrid strokeDasharray="3 3" stroke={C.border2} vertical={false} />
          <XAxis dataKey="label" tick={{ fill: C.textMuted, fontSize: 10 } as any} tickLine={false} axisLine={false} interval="preserveStartEnd" />
          <YAxis
            domain={[min - pad, max + pad]}
            tick={{ fill: C.textMuted, fontSize: 10 } as any}
            tickLine={false}
            axisLine={false}
            tickFormatter={v => `₹${(v / 1000).toFixed(0)}k`}
            width={52}
          />
          <Tooltip
            content={({ active, payload }) => {
              if (!active || !payload?.length) return null
              const d = payload[0].payload
              return (
                <div style={{ background: CHART.tooltip.bg, border: `1px solid ${CHART.tooltip.border}`, borderRadius: 6, padding: '8px 12px', fontSize: 12 }}>
                  <div style={{ color: C.textSub, marginBottom: 4 }}>{d.label}</div>
                  <div style={{ color: C.text, fontWeight: 700 }}>Equity: {formatInr(d.equity)}</div>
                  <div style={{ color: d.pnl >= 0 ? C.green : C.red, fontWeight: 600 }}>
                    P&amp;L: {d.pnl >= 0 ? '+' : ''}{formatInr(d.pnl)}
                  </div>
                </div>
              )
            }}
          />
          <ReferenceLine y={initialCapital} stroke={C.textMuted} strokeDasharray="4 2" />
          <Area type="monotone" dataKey="equity" stroke={C.green} strokeWidth={2} fill="url(#fwdEquityGrad)" dot={false} activeDot={{ r: 4, fill: C.green }} />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  )
}

// ── Session card ──────────────────────────────────────────────────────────────

function ForwardTestCard({ session }: { session: ForwardTestSession }) {
  const [expanded, setExpanded] = useState(false)
  const [showGoLive, setShowGoLive] = useState(false)

  const pnlColor = session.totalPnl >= 0 ? C.green : C.red
  const returnPct = (session.totalReturn * 100).toFixed(2)
  const canPromote = session.status === 'Completed' || session.status === 'Stopped'

  return (
    <>
      <div style={{ backgroundColor: C.surface, border: `1px solid ${C.border2}`, borderRadius: 8, marginBottom: 16, overflow: 'hidden' }}>
        {/* Header bar */}
        <div
          style={{ padding: '14px 18px', cursor: 'pointer', display: 'flex', alignItems: 'center', gap: 12, justifyContent: 'space-between' }}
          onClick={() => setExpanded(e => !e)}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: 12, minWidth: 0 }}>
            <span style={{ fontSize: 11, color: statusColor(session.status), fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.05em', flexShrink: 0 }}>
              ● {session.status}
            </span>
            {session.sourceBacktestId && (
              <span style={{ fontSize: 10, background: C.blueBg, color: C.textSub, padding: '2px 7px', borderRadius: 4, fontWeight: 600, flexShrink: 0 }}>
                📊 From Backtest
              </span>
            )}
            <div style={{ minWidth: 0 }}>
              <div style={{ fontWeight: 700, fontSize: 15, color: C.text, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                {session.instanceName}
              </div>
              <div style={{ fontSize: 11, color: C.textMuted, marginTop: 2 }}>
                {session.strategyType} · {session.internalSymbol} · {session.timeframe}
                {session.startedAt && ` · Started ${formatIst(session.startedAt)}`}
              </div>
            </div>
          </div>

          <div style={{ display: 'flex', alignItems: 'center', gap: 24, flexShrink: 0 }}>
            <div style={{ textAlign: 'right' }}>
              <div style={{ fontSize: 18, fontWeight: 800, color: pnlColor }}>
                {session.totalPnl >= 0 ? '+' : ''}{formatInr(session.totalPnl)}
              </div>
              <div style={{ fontSize: 12, color: pnlColor }}>{returnPct}% return</div>
            </div>
            <span style={{ color: C.textMuted, fontSize: 12 }}>{expanded ? '▲' : '▼'}</span>
          </div>
        </div>

        {/* Metrics strip */}
        <div style={{ padding: '0 18px 14px', display: 'flex', gap: 24, flexWrap: 'wrap', borderBottom: expanded ? `1px solid ${C.border2}` : 'none' }}>
          <MetricBadge label="Win Rate" value={`${(session.winRate * 100).toFixed(1)}%`} color={session.winRate >= 0.5 ? C.green : C.amber} />
          <MetricBadge label="Trades" value={String(session.totalTrades)} />
          <MetricBadge label="Max Drawdown" value={`${(session.maxDrawdown * 100).toFixed(1)}%`} color={C.red} />
          <MetricBadge label="Sharpe" value={session.sharpeRatio.toFixed(2)} color={session.sharpeRatio >= 1 ? C.green : C.amber} />
          <MetricBadge label="Capital" value={formatInr(session.initialCapital)} />
          <MetricBadge label="Current Equity" value={formatInr(session.currentEquity)} color={session.currentEquity >= session.initialCapital ? C.green : C.red} />
          {session.openPositionCount > 0 && (
            <MetricBadge label="Open Positions" value={String(session.openPositionCount)} color={C.blue} />
          )}
        </div>

        {/* Expanded detail */}
        {expanded && (
          <div style={{ padding: '16px 18px' }}>
            {/* Equity curve */}
            <div style={{ marginBottom: 16 }}>
              <div style={{ fontSize: 12, color: C.textMuted, marginBottom: 6 }}>Equity Curve</div>
              <ForwardEquityCurve
                points={session.equityCurvePoints ?? []}
                initialCapital={session.initialCapital}
              />
            </div>

            {/* Backtest comparison panel (only if promoted from backtest) */}
            {session.sourceBacktest && (
              <div style={{ background: C.bg, border: `1px solid ${C.blueBg}`, borderRadius: 6, padding: '12px 14px', marginBottom: 16 }}>
                <BacktestComparisonPanel session={session} source={session.sourceBacktest} />
              </div>
            )}

            {/* Actions */}
            <div style={{ display: 'flex', gap: 10, justifyContent: 'flex-end', marginTop: 8 }}>
              {canPromote ? (
                <button
                  onClick={() => setShowGoLive(true)}
                  style={{ padding: '8px 16px', background: C.green, color: 'black', border: 'none', borderRadius: 6, cursor: 'pointer', fontSize: 13, fontWeight: 700 }}
                >
                  🚀 Promote to Live
                </button>
              ) : (
                <span style={{ fontSize: 12, color: C.textMuted, alignSelf: 'center' }}>
                  Stop the forward test session to enable live promotion.
                </span>
              )}
            </div>
          </div>
        )}
      </div>

      {showGoLive && (
        <PreFlightChecklistModal
          session={session}
          onClose={() => setShowGoLive(false)}
        />
      )}
    </>
  )
}

// ── Page root ─────────────────────────────────────────────────────────────────

export function ForwardTestPage() {
  const { data: sessions, isLoading, isError } = useQuery({
    queryKey: ['forward-tests'],
    queryFn: () => forwardTestApi.list().then(r => r.data.data ?? []),
    refetchInterval: 15_000,
  })

  const running = sessions?.filter(s => s.status?.toLowerCase() === 'running') ?? []
  const other   = sessions?.filter(s => s.status?.toLowerCase() !== 'running') ?? []
  const sorted  = [...running, ...other]

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 20 }}>
        <h2 style={{ margin: 0, fontSize: 20, fontWeight: 'bold' }}>Forward Tests</h2>
        <span style={{ fontSize: 12, color: C.textMuted }}>
          {running.length} running · {sessions?.length ?? 0} total
        </span>
      </div>

      <div style={{
        background: C.blueBg, border: `1px solid ${C.blue44}`, borderRadius: 6,
        padding: '10px 14px', marginBottom: 20, fontSize: 13, color: C.textSub,
      }}>
        ℹ️ <strong>Forward tests</strong> paper-trade the strategy logic in real-time — no real capital at risk.
        Use <em>Promote to Live</em> after reviewing metrics. A pre-flight checklist will verify readiness before going live.
      </div>

      {isLoading && <p style={{ color: C.textMuted }}>Loading sessions…</p>}
      {isError   && <p style={{ color: C.red }}>Failed to load forward test sessions.</p>}

      {!isLoading && !isError && sorted.length === 0 && (
        <div style={{
          background: C.surface, border: `1px solid ${C.border2}`, borderRadius: 8,
          padding: 32, textAlign: 'center',
        }}>
          <div style={{ fontSize: 28, marginBottom: 10 }}>🧪</div>
          <p style={{ color: C.textMuted, fontSize: 14, margin: '0 0 6px 0' }}>
            No forward test sessions yet.
          </p>
          <p style={{ color: C.textSub, fontSize: 12, margin: 0 }}>
            Run a <strong>Backtest</strong>, then click <strong>"🧪 Start Forward Test"</strong> on a result,
            or use <strong>Strategy Lab</strong> to see the full pipeline.
          </p>
        </div>
      )}

      {sorted.map(session => (
        <ForwardTestCard key={session.instanceId} session={session} />
      ))}
    </div>
  )
}
