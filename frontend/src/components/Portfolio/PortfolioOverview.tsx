/**
 * PortfolioOverview — replaces the plain 6-card Overview page.
 *
 * Shows:
 * - 4 summary metric cards (Today P&L, Unrealized P&L, Capital, Running strategies)
 * - Per-strategy P&L breakdown table
 * - Portfolio drawdown / health section
 */

import { useQuery } from '@tanstack/react-query'
import { portfolioApi, PortfolioSummary, StrategyPnlRow } from '../../api/client'
import { formatInr } from '../../utils/datetime'

// ── Metric card ───────────────────────────────────────────────────────────────

function MetricCard({
  label, value, sub, valueColor = '#e2e8f0',
}: { label: string; value: string; sub?: string; valueColor?: string }) {
  return (
    <div style={{
      backgroundColor: '#1e1e2e', border: '1px solid #2d2d3f',
      borderRadius: 8, padding: '16px 20px', flex: 1, minWidth: 160,
    }}>
      <div style={{ fontSize: 11, color: '#64748b', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.06em', marginBottom: 6 }}>
        {label}
      </div>
      <div style={{ fontSize: 26, fontWeight: 800, color: valueColor, lineHeight: 1.1 }}>{value}</div>
      {sub && <div style={{ fontSize: 12, color: '#475569', marginTop: 4 }}>{sub}</div>}
    </div>
  )
}

// ── Status dot ────────────────────────────────────────────────────────────────

function StatusDot({ status }: { status: string }) {
  const color = status === 'Running' ? '#10b981' : status === 'Paused' ? '#f59e0b' : '#6b7280'
  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 5 }}>
      <span style={{ width: 7, height: 7, borderRadius: '50%', background: color, display: 'inline-block' }} />
      <span style={{ fontSize: 12, color }}>{status}</span>
    </span>
  )
}

// ── Strategy breakdown table ──────────────────────────────────────────────────

function StrategyTable({ rows }: { rows: StrategyPnlRow[] }) {
  if (rows.length === 0) {
    return (
      <p style={{ color: '#475569', fontSize: 13, padding: '16px 0' }}>
        No strategies yet. Create one in the Strategies page.
      </p>
    )
  }

  return (
    <div style={{ overflowX: 'auto' }}>
      <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13 }}>
        <thead>
          <tr style={{ borderBottom: '1px solid #2d2d3f' }}>
            {['Strategy', 'Symbol', 'Mode', 'Status', 'Capital', 'Realized P&L', 'Unrealized P&L', 'Net P&L', '%'].map(h => (
              <th key={h} style={{ padding: '8px 10px', textAlign: 'left', color: '#64748b', fontWeight: 600, whiteSpace: 'nowrap' }}>
                {h}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map(row => (
            <tr key={row.instanceId} style={{ borderBottom: '1px solid #1e2030' }}>
              <td style={{ padding: '9px 10px', fontWeight: 600, color: '#e2e8f0' }}>{row.name}</td>
              <td style={{ padding: '9px 10px', color: '#94a3b8', fontSize: 12 }}>{row.internalSymbol}</td>
              <td style={{ padding: '9px 10px' }}>
                <span style={{
                  fontSize: 10, fontWeight: 700, padding: '2px 7px', borderRadius: 10,
                  background: row.mode === 'Live' ? '#14532d' : row.mode === 'Forward' ? '#1e3a5f' : '#292524',
                  color: row.mode === 'Live' ? '#86efac' : row.mode === 'Forward' ? '#93c5fd' : '#9ca3af',
                }}>
                  {row.mode}
                </span>
              </td>
              <td style={{ padding: '9px 10px' }}><StatusDot status={row.status} /></td>
              <td style={{ padding: '9px 10px', color: '#94a3b8' }}>{formatInr(row.allocatedCapital)}</td>
              <td style={{ padding: '9px 10px', color: row.todayRealizedPnl >= 0 ? '#10b981' : '#ef4444', fontWeight: 600 }}>
                {row.todayRealizedPnl >= 0 ? '+' : ''}{formatInr(row.todayRealizedPnl)}
              </td>
              <td style={{ padding: '9px 10px', color: row.todayUnrealizedPnl >= 0 ? '#10b981' : '#ef4444' }}>
                {row.todayUnrealizedPnl >= 0 ? '+' : ''}{formatInr(row.todayUnrealizedPnl)}
              </td>
              <td style={{ padding: '9px 10px', color: row.todayTotalPnl >= 0 ? '#10b981' : '#ef4444', fontWeight: 700 }}>
                {row.todayTotalPnl >= 0 ? '+' : ''}{formatInr(row.todayTotalPnl)}
              </td>
              <td style={{ padding: '9px 10px', color: row.pnlPercent >= 0 ? '#10b981' : '#ef4444', fontWeight: 600 }}>
                {row.pnlPercent >= 0 ? '+' : ''}{row.pnlPercent.toFixed(2)}%
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

// ── Fallback when no portfolio data yet ───────────────────────────────────────

function EmptyPortfolio() {
  return (
    <div style={{
      background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 8,
      padding: '40px 24px', textAlign: 'center',
    }}>
      <div style={{ fontSize: 32, marginBottom: 12 }}>📊</div>
      <p style={{ color: '#64748b', fontSize: 14, margin: '0 0 8px 0' }}>No active strategies</p>
      <p style={{ color: '#475569', fontSize: 12, margin: 0 }}>
        Start by running a <strong>Backtest</strong>, then promote it to a Forward Test, and finally to Live.
      </p>
    </div>
  )
}

// ── Page root ─────────────────────────────────────────────────────────────────

export function PortfolioOverview() {
  const { data: summary, isLoading } = useQuery<PortfolioSummary>({
    queryKey: ['portfolio-summary'],
    queryFn: () => portfolioApi.summary().then(r => r.data.data!),
    refetchInterval: 10_000,
  })

  if (isLoading || !summary) {
    return (
      <div>
        <div style={{ display: 'flex', gap: 12, marginBottom: 20, flexWrap: 'wrap' }}>
          {['Today P&L', 'Unrealized', 'Capital', 'Running'].map(l => (
            <div key={l} style={{ flex: 1, minWidth: 160, height: 82, background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 8, animation: 'pulse 1.5s infinite' }} />
          ))}
        </div>
      </div>
    )
  }

  const pnlColor = summary.todayTotalPnl >= 0 ? '#10b981' : '#ef4444'
  const realizedColor = summary.todayTotalRealizedPnl >= 0 ? '#10b981' : '#ef4444'
  const unrealizedColor = summary.todayTotalUnrealizedPnl >= 0 ? '#10b981' : '#ef4444'

  return (
    <div>
      {/* ── Summary metric cards ── */}
      <div style={{ display: 'flex', gap: 12, marginBottom: 24, flexWrap: 'wrap' }}>
        <MetricCard
          label="Today's P&L (Realized)"
          value={(summary.todayTotalRealizedPnl >= 0 ? '+' : '') + formatInr(summary.todayTotalRealizedPnl)}
          sub="Net of brokerage &amp; taxes"
          valueColor={realizedColor}
        />
        <MetricCard
          label="Unrealized P&L"
          value={(summary.todayTotalUnrealizedPnl >= 0 ? '+' : '') + formatInr(summary.todayTotalUnrealizedPnl)}
          sub="Mark-to-market on open positions"
          valueColor={unrealizedColor}
        />
        <MetricCard
          label="Net P&L Today"
          value={(summary.todayTotalPnl >= 0 ? '+' : '') + formatInr(summary.todayTotalPnl)}
          sub="Realized + Unrealized"
          valueColor={pnlColor}
        />
        <MetricCard
          label="Capital Deployed"
          value={formatInr(summary.totalAllocatedCapital)}
          sub={`${summary.runningCount} live running · ${summary.forwardTestCount} forward testing`}
          valueColor="#3b82f6"
        />
      </div>

      {/* ── Strategy status summary ── */}
      <div style={{ display: 'flex', gap: 10, marginBottom: 20, flexWrap: 'wrap' }}>
        {[
          { label: 'Live Running', count: summary.runningCount, color: '#10b981', bg: '#14532d' },
          { label: 'Forward Testing', count: summary.forwardTestCount, color: '#3b82f6', bg: '#1e3a5f' },
          { label: 'Paused', count: summary.pausedCount, color: '#f59e0b', bg: '#292524' },
          { label: 'Stopped', count: summary.stoppedCount, color: '#6b7280', bg: '#1c1c2e' },
        ].map(s => (
          <div key={s.label} style={{
            display: 'flex', alignItems: 'center', gap: 8,
            background: s.bg, border: `1px solid ${s.color}30`,
            borderRadius: 6, padding: '6px 12px',
          }}>
            <span style={{ fontSize: 18, fontWeight: 800, color: s.color }}>{s.count}</span>
            <span style={{ fontSize: 12, color: s.color, fontWeight: 600 }}>{s.label}</span>
          </div>
        ))}
      </div>

      {/* ── Per-strategy breakdown ── */}
      <div style={{ background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 8, padding: '16px 18px' }}>
        <div style={{ fontSize: 13, fontWeight: 700, color: '#8b8b9f', marginBottom: 12, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
          Strategy Breakdown
        </div>
        {summary.byStrategy.length === 0
          ? <EmptyPortfolio />
          : <StrategyTable rows={summary.byStrategy} />
        }
      </div>

      {/* ── Pipeline guidance (when no live strategies) ── */}
      {summary.runningCount === 0 && summary.byStrategy.length === 0 && (
        <div style={{
          marginTop: 20, background: '#13131f', border: '1px solid #1e3a5f',
          borderRadius: 8, padding: '14px 18px', fontSize: 13, color: '#93c5fd',
          display: 'flex', alignItems: 'flex-start', gap: 10,
        }}>
          <span style={{ fontSize: 18 }}>ℹ️</span>
          <div>
            <strong>Start your trading pipeline:</strong>
            <ol style={{ margin: '6px 0 0 0', paddingLeft: 18, lineHeight: 1.8, color: '#7dd3fc' }}>
              <li>Run a <strong>Backtest</strong> to validate your strategy on historical data</li>
              <li>Promote to <strong>Forward Test</strong> for paper trading with live market data</li>
              <li>Review results and <strong>Promote to Live</strong> when satisfied</li>
            </ol>
          </div>
        </div>
      )}
    </div>
  )
}
