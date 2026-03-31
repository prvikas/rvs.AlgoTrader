/**
 * StrategyLabPage — a 3-column Kanban board visualizing the full strategy pipeline.
 *
 * Columns:
 *   [1] Backtested       — past backtest runs with "▶ Forward Test" CTA
 *   [2] Forward Testing  — forward test sessions with "🚀 Go Live" CTA
 *   [3] Live             — live strategy instances with status + controls
 *
 * This is the "big picture" view that guides analysts through the pipeline:
 * Research → Validate → Deploy
 */

import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  backtestApi, forwardTestApi, strategiesApi,
  BacktestResult, ForwardTestSession, StrategyInstance,
} from '../api/client'
import { formatInr, formatIst } from '../utils/datetime'
import { PromoteToForwardTestModal } from '../components/ForwardTest/PromoteToForwardTestModal'
import { PreFlightChecklistModal } from '../components/ForwardTest/PreFlightChecklistModal'

// ── Shared UI helpers ─────────────────────────────────────────────────────────

function PipelineColumn({ title, icon, count, color, children }: {
  title: string; icon: string; count: number; color: string; children: React.ReactNode
}) {
  return (
    <div style={{ flex: 1, minWidth: 280, display: 'flex', flexDirection: 'column', gap: 0 }}>
      <div style={{
        display: 'flex', alignItems: 'center', justifyContent: 'space-between',
        padding: '12px 16px', background: '#1e1e2e',
        borderTop: `3px solid ${color}`, borderRadius: '8px 8px 0 0',
        borderLeft: '1px solid #2d2d3f', borderRight: '1px solid #2d2d3f',
      }}>
        <span style={{ fontWeight: 700, fontSize: 13, color: '#e2e8f0' }}>
          {icon} {title}
        </span>
        <span style={{ fontSize: 11, background: '#2d2d3f', color: '#8b8b9f', padding: '2px 8px', borderRadius: 10 }}>
          {count}
        </span>
      </div>
      <div style={{
        flex: 1, background: '#13131f', border: '1px solid #2d2d3f', borderTop: 'none',
        borderRadius: '0 0 8px 8px', padding: 12, display: 'flex',
        flexDirection: 'column', gap: 10, minHeight: 200, overflowY: 'auto', maxHeight: '65vh',
      }}>
        {children}
      </div>
    </div>
  )
}

function EmptyColumn({ message, action }: { message: string; action?: string }) {
  return (
    <div style={{ textAlign: 'center', padding: '24px 12px', color: '#475569', fontSize: 12 }}>
      <div style={{ fontSize: 24, marginBottom: 6 }}>—</div>
      <div>{message}</div>
      {action && <div style={{ marginTop: 4, color: '#3b82f6', fontSize: 11 }}>{action}</div>}
    </div>
  )
}

// ── Column 1: Backtest cards ──────────────────────────────────────────────────

function BacktestCard({ result, onPromote }: { result: BacktestResult; onPromote: () => void }) {
  const pnlColor = result.totalPnl >= 0 ? '#10b981' : '#ef4444'
  const canPromote = !!result.id

  return (
    <div style={{ background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 7, padding: '12px 14px' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: 8 }}>
        <div>
          <div style={{ fontWeight: 700, fontSize: 13, color: '#e2e8f0' }}>{result.strategyName}</div>
          <div style={{ fontSize: 11, color: '#64748b', marginTop: 2 }}>
            {result.symbol} · {result.timeframe}
            {result.startedAt && ` · ${formatIst(result.startedAt)}`}
          </div>
        </div>
        <div style={{ textAlign: 'right', flexShrink: 0 }}>
          <div style={{ fontSize: 15, fontWeight: 800, color: pnlColor }}>
            {result.totalPnl >= 0 ? '+' : ''}{formatInr(result.totalPnl)}
          </div>
          <div style={{ fontSize: 10, color: pnlColor }}>{(result.totalReturn * 100).toFixed(1)}%</div>
        </div>
      </div>

      {/* Metrics strip */}
      <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap', marginBottom: 10 }}>
        <MiniMetric label="Win%" value={`${(result.winRate * 100).toFixed(0)}%`} />
        <MiniMetric label="Trades" value={String(result.totalTrades)} />
        <MiniMetric label="Sharpe" value={result.sharpeRatio.toFixed(2)} />
        <MiniMetric label="Max DD" value={`${(result.maxDrawdown * 100).toFixed(1)}%`} color="#ef4444" />
      </div>

      {canPromote ? (
        <button
          onClick={onPromote}
          style={{
            width: '100%', padding: '7px 10px', background: '#1e3a5f',
            color: '#93c5fd', border: '1px solid #2563eb44', borderRadius: 5,
            cursor: 'pointer', fontSize: 12, fontWeight: 700,
          }}
        >
          🧪 Start Forward Test
        </button>
      ) : (
        <div style={{ fontSize: 11, color: '#475569', textAlign: 'center', paddingTop: 4 }}>
          Save result to enable promotion
        </div>
      )}
    </div>
  )
}

// ── Column 2: Forward test cards ──────────────────────────────────────────────

function ForwardTestCard({ session, onGoLive }: { session: ForwardTestSession; onGoLive: () => void }) {
  const pnlColor = session.totalPnl >= 0 ? '#10b981' : '#ef4444'
  const statusColor = session.status === 'Running' ? '#10b981' : session.status === 'Paused' ? '#f59e0b' : '#6b7280'
  const canPromote = session.status === 'Stopped' || session.status === 'Completed'

  return (
    <div style={{ background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 7, padding: '12px 14px' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: 8 }}>
        <div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginBottom: 2 }}>
            <span style={{ fontSize: 9, color: statusColor, fontWeight: 700 }}>● {session.status.toUpperCase()}</span>
          </div>
          <div style={{ fontWeight: 700, fontSize: 13, color: '#e2e8f0' }}>{session.instanceName}</div>
          <div style={{ fontSize: 11, color: '#64748b', marginTop: 2 }}>
            {session.internalSymbol} · {session.timeframe}
            {session.startedAt && ` · ${formatIst(session.startedAt)}`}
          </div>
        </div>
        <div style={{ textAlign: 'right', flexShrink: 0 }}>
          <div style={{ fontSize: 15, fontWeight: 800, color: pnlColor }}>
            {session.totalPnl >= 0 ? '+' : ''}{formatInr(session.totalPnl)}
          </div>
          <div style={{ fontSize: 10, color: pnlColor }}>{(session.totalReturn * 100).toFixed(1)}%</div>
        </div>
      </div>

      <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap', marginBottom: 10 }}>
        <MiniMetric label="Win%" value={`${(session.winRate * 100).toFixed(0)}%`} />
        <MiniMetric label="Trades" value={String(session.totalTrades)} />
        <MiniMetric label="Sharpe" value={session.sharpeRatio.toFixed(2)} />
        <MiniMetric label="Max DD" value={`${(session.maxDrawdown * 100).toFixed(1)}%`} color="#ef4444" />
      </div>

      {session.sourceBacktest && (
        <div style={{ fontSize: 11, color: '#475569', marginBottom: 8 }}>
          📊 Promoted from backtest · BT P&L: {formatInr(session.sourceBacktest.totalPnl)}
        </div>
      )}

      {canPromote ? (
        <button
          onClick={onGoLive}
          style={{
            width: '100%', padding: '7px 10px', background: '#14532d',
            color: '#86efac', border: '1px solid #16a34a44', borderRadius: 5,
            cursor: 'pointer', fontSize: 12, fontWeight: 700,
          }}
        >
          🚀 Go Live
        </button>
      ) : (
        <div style={{
          fontSize: 11, color: statusColor, textAlign: 'center',
          background: '#13131f', borderRadius: 5, padding: '5px 0',
        }}>
          {session.status === 'Running' ? '⚡ Running — stop before promoting to live' : `Session ${session.status}`}
        </div>
      )}
    </div>
  )
}

// ── Column 3: Live strategy cards ─────────────────────────────────────────────

function LiveStrategyCard({ instance }: { instance: StrategyInstance }) {
  const qc = useQueryClient()
  const statusColor = instance.status === 'Running' ? '#10b981' : instance.status === 'Paused' ? '#f59e0b' : '#6b7280'
  const pnl = (instance.todayRealizedPnl ?? 0) + (instance.todayUnrealizedPnl ?? 0)

  const startMutation = useMutation({
    mutationFn: () => strategiesApi.start(instance.id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['strategies'] }),
  })
  const pauseMutation = useMutation({
    mutationFn: () => strategiesApi.pause(instance.id, 'USER_REQUESTED'),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['strategies'] }),
  })
  const stopMutation = useMutation({
    mutationFn: () => strategiesApi.stop(instance.id, 'USER_REQUESTED'),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['strategies'] }),
  })

  return (
    <div style={{ background: '#1e1e2e', border: `1px solid ${statusColor}33`, borderRadius: 7, padding: '12px 14px' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: 8 }}>
        <div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginBottom: 2 }}>
            <span style={{ fontSize: 9, color: statusColor, fontWeight: 700 }}>● {instance.status.toUpperCase()}</span>
            <span style={{ fontSize: 9, background: '#14532d', color: '#86efac', padding: '1px 5px', borderRadius: 3, fontWeight: 700 }}>LIVE</span>
          </div>
          <div style={{ fontWeight: 700, fontSize: 13, color: '#e2e8f0' }}>{instance.name}</div>
          <div style={{ fontSize: 11, color: '#64748b', marginTop: 2 }}>
            {instance.internalSymbol} · {instance.timeframe} · {instance.brokerName}
          </div>
        </div>
        <div style={{ textAlign: 'right', flexShrink: 0 }}>
          <div style={{ fontSize: 14, fontWeight: 800, color: pnl >= 0 ? '#10b981' : '#ef4444' }}>
            {pnl >= 0 ? '+' : ''}{formatInr(pnl)}
          </div>
          <div style={{ fontSize: 10, color: '#64748b' }}>Today's P&L</div>
        </div>
      </div>

      <div style={{ display: 'flex', gap: 6, marginTop: 4 }}>
        {instance.status !== 'Running' && (
          <CtaButton
            label="▶ Start" color="#16a34a"
            onClick={() => startMutation.mutate()} loading={startMutation.isPending}
          />
        )}
        {instance.status === 'Running' && (
          <CtaButton
            label="⏸ Pause" color="#d97706"
            onClick={() => pauseMutation.mutate()} loading={pauseMutation.isPending}
          />
        )}
        {instance.status !== 'Stopped' && (
          <CtaButton
            label="■ Stop" color="#dc2626"
            onClick={() => stopMutation.mutate()} loading={stopMutation.isPending}
          />
        )}
      </div>
    </div>
  )
}

function CtaButton({ label, color, onClick, loading }: { label: string; color: string; onClick: () => void; loading?: boolean }) {
  return (
    <button
      onClick={onClick}
      disabled={loading}
      style={{
        flex: 1, padding: '5px 8px', background: color + '22',
        color, border: `1px solid ${color}44`, borderRadius: 5,
        cursor: loading ? 'not-allowed' : 'pointer', fontSize: 11, fontWeight: 700,
        opacity: loading ? 0.6 : 1,
      }}
    >
      {loading ? '…' : label}
    </button>
  )
}

function MiniMetric({ label, value, color = '#94a3b8' }: { label: string; value: string; color?: string }) {
  return (
    <div>
      <div style={{ fontSize: 9, color: '#475569', textTransform: 'uppercase', letterSpacing: '0.04em' }}>{label}</div>
      <div style={{ fontSize: 12, fontWeight: 700, color }}>{value}</div>
    </div>
  )
}

// ── Page root ─────────────────────────────────────────────────────────────────

export function StrategyLabPage() {
  const [promoteBacktest, setPromoteBacktest] = useState<BacktestResult | null>(null)
  const [promoteToLive, setPromoteToLive] = useState<ForwardTestSession | null>(null)

  const { data: backtests = [] } = useQuery({
    queryKey: ['backtest-results'],
    queryFn: () => backtestApi.list().then(r => r.data.data?.items ?? []),
    refetchInterval: 30_000,
  })

  const { data: forwardTests = [] } = useQuery({
    queryKey: ['forward-tests'],
    queryFn: () => forwardTestApi.list().then(r => r.data.data ?? []),
    refetchInterval: 15_000,
  })

  const { data: allStrategies = [] } = useQuery({
    queryKey: ['strategies'],
    queryFn: () => strategiesApi.list().then(r => r.data.data ?? []),
    refetchInterval: 10_000,
  })

  const liveStrategies = allStrategies.filter(s => s.mode === 'Live')

  return (
    <div>
      {/* Header */}
      <div style={{ marginBottom: 20 }}>
        <h2 style={{ margin: '0 0 4px 0', fontSize: 20, fontWeight: 800 }}>Strategy Lab</h2>
        <p style={{ margin: 0, fontSize: 12, color: '#64748b' }}>
          Full pipeline view — track strategies from research to live deployment
        </p>
      </div>

      {/* Pipeline arrows */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 16, fontSize: 12, color: '#475569' }}>
        <span style={{ color: '#3b82f6', fontWeight: 700 }}>🔬 Backtest</span>
        <span>→</span>
        <span style={{ color: '#10b981', fontWeight: 700 }}>🧪 Forward Test</span>
        <span>→</span>
        <span style={{ color: '#f59e0b', fontWeight: 700 }}>🚀 Live</span>
      </div>

      {/* 3-column Kanban */}
      <div style={{ display: 'flex', gap: 16, alignItems: 'flex-start' }}>
        {/* Column 1: Backtest */}
        <PipelineColumn title="Backtested" icon="🔬" count={backtests.length} color="#3b82f6">
          {backtests.length === 0
            ? <EmptyColumn message="No backtests yet" action="Run one in the Backtest page →" />
            : backtests.map((bt, i) => (
              <BacktestCard
                key={bt.id ?? i}
                result={bt}
                onPromote={() => setPromoteBacktest(bt)}
              />
            ))
          }
        </PipelineColumn>

        {/* Column 2: Forward Testing */}
        <PipelineColumn title="Forward Testing" icon="🧪" count={forwardTests.length} color="#10b981">
          {forwardTests.length === 0
            ? <EmptyColumn message="No forward tests yet" action="Promote a backtest to start paper trading →" />
            : forwardTests.map(ft => (
              <ForwardTestCard
                key={ft.instanceId}
                session={ft}
                onGoLive={() => setPromoteToLive(ft)}
              />
            ))
          }
        </PipelineColumn>

        {/* Column 3: Live */}
        <PipelineColumn title="Live Trading" icon="🚀" count={liveStrategies.length} color="#f59e0b">
          {liveStrategies.length === 0
            ? <EmptyColumn message="No live strategies yet" action="Promote a successful forward test to go live →" />
            : liveStrategies.map(s => (
              <LiveStrategyCard key={s.id} instance={s} />
            ))
          }
        </PipelineColumn>
      </div>

      {/* Modals */}
      {promoteBacktest && (
        <PromoteToForwardTestModal
          backtest={promoteBacktest}
          onClose={() => setPromoteBacktest(null)}
        />
      )}
      {promoteToLive && (
        <PreFlightChecklistModal
          session={promoteToLive}
          onClose={() => setPromoteToLive(null)}
        />
      )}
    </div>
  )
}
