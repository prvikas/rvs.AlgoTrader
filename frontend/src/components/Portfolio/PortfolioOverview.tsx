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
import { C, CARD_PAD, TABLE_CELL, TABLE_HEADER_CELL } from '../../styles/tokens'

// ── Metric card — per UI_DESIGN_SPEC Section 5 ───────────────────────────────

function MetricCard({
  label, value, sub, valueColor = C.text,
}: { label: string; value: string; sub?: string; valueColor?: string }) {
  return (
    <div style={{
      background: C.surface, border: `1px solid ${C.border}`,
      borderRadius: 6, padding: CARD_PAD, flex: 1, minWidth: 160,
    }}>
      <div style={{ fontSize: 10, color: C.textMuted, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.08em', marginBottom: 4 }}>
        {label}
      </div>
      <div style={{ fontSize: 22, fontWeight: 800, color: valueColor, lineHeight: 1, fontFamily: "'JetBrains Mono', monospace", fontVariantNumeric: 'tabular-nums' }}>
        {value}
      </div>
      {sub && <div style={{ fontSize: 10, color: C.textDim, marginTop: 3 }}>{sub}</div>}
    </div>
  )
}

// ── Status dot ────────────────────────────────────────────────────────────────

function StatusDot({ status }: { status: string }) {
  const color = status === 'Running' ? C.green : status === 'Paused' ? C.amber : C.textMuted
  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 5 }}>
      <span style={{ width: 6, height: 6, borderRadius: '50%', background: color, display: 'inline-block' }} />
      <span style={{ fontSize: 11, color, fontWeight: 600 }}>{status}</span>
    </span>
  )
}

// ── Strategy breakdown table — per UI_DESIGN_SPEC Section 6 ──────────────────

function StrategyTable({ rows }: { rows: StrategyPnlRow[] }) {
  if (rows.length === 0) {
    return (
      <p style={{ color: C.textMuted, fontSize: 12, padding: '12px 0' }}>
        No strategies yet. Create one in the Strategies page.
      </p>
    )
  }

  return (
    <div style={{ border: `1px solid ${C.border}`, borderRadius: 6, overflowX: 'auto' }}>
      <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
        <thead>
          <tr style={{ background: C.surface2, borderBottom: `1px solid ${C.border}` }}>
            {['Strategy', 'Symbol', 'Mode', 'Status', 'Capital', 'Realized P&L', 'Unrealized P&L', 'Net P&L', '%'].map(h => (
              <th key={h} style={{ padding: TABLE_HEADER_CELL, textAlign: 'left', color: C.textMuted, fontWeight: 600, fontSize: 10, textTransform: 'uppercase', letterSpacing: '0.07em', whiteSpace: 'nowrap' }}>
                {h}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map(row => (
            <tr
              key={row.instanceId}
              style={{ borderBottom: `1px solid ${C.border2}` }}
              onMouseEnter={e => (e.currentTarget.style.background = C.surface)}
              onMouseLeave={e => (e.currentTarget.style.background = 'transparent')}
            >
              <td style={{ padding: TABLE_CELL, fontWeight: 600, color: C.text }}>{row.name}</td>
              <td style={{ padding: TABLE_CELL, color: C.textSub, fontSize: 11 }}>{row.internalSymbol}</td>
              <td style={{ padding: TABLE_CELL }}>
                <span style={{
                  fontSize: 9, fontWeight: 800, padding: '2px 6px', borderRadius: 3,
                  background: row.mode === 'Live' ? C.greenBg : row.mode === 'Forward' ? C.blueBg : C.amberBg,
                  color: row.mode === 'Live' ? C.green : row.mode === 'Forward' ? C.blue : C.amber,
                  border: `1px solid ${row.mode === 'Live' ? C.green + '30' : row.mode === 'Forward' ? C.blue + '30' : C.amber + '30'}`,
                  textTransform: 'uppercase',
                  letterSpacing: '0.08em',
                }}>
                  {row.mode}
                </span>
              </td>
              <td style={{ padding: TABLE_CELL }}><StatusDot status={row.status} /></td>
              <td style={{ padding: TABLE_CELL, color: C.textSub, fontFamily: "'JetBrains Mono', monospace", fontVariantNumeric: 'tabular-nums' }}>{formatInr(row.allocatedCapital)}</td>
              <td style={{ padding: TABLE_CELL, color: row.todayRealizedPnl >= 0 ? C.green : C.red, fontWeight: 600, fontFamily: "'JetBrains Mono', monospace", fontVariantNumeric: 'tabular-nums' }}>
                {row.todayRealizedPnl >= 0 ? '+' : ''}{formatInr(row.todayRealizedPnl)}
              </td>
              <td style={{ padding: TABLE_CELL, color: row.todayUnrealizedPnl >= 0 ? C.green : C.red, fontFamily: "'JetBrains Mono', monospace", fontVariantNumeric: 'tabular-nums' }}>
                {row.todayUnrealizedPnl >= 0 ? '+' : ''}{formatInr(row.todayUnrealizedPnl)}
              </td>
              <td style={{ padding: TABLE_CELL, color: row.todayTotalPnl >= 0 ? C.green : C.red, fontWeight: 700, fontFamily: "'JetBrains Mono', monospace", fontVariantNumeric: 'tabular-nums' }}>
                {row.todayTotalPnl >= 0 ? '+' : ''}{formatInr(row.todayTotalPnl)}
              </td>
              <td style={{ padding: TABLE_CELL, color: row.pnlPercent >= 0 ? C.green : C.red, fontWeight: 600, fontFamily: "'JetBrains Mono', monospace", fontVariantNumeric: 'tabular-nums' }}>
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
      background: C.surface, border: `1px solid ${C.border}`, borderRadius: 6,
      padding: '28px 20px', textAlign: 'center',
    }}>
      <p style={{ color: C.textMuted, fontSize: 12, margin: 0 }}>
        No active strategies. Create one in the Strategies tab to get started.
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
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 8, marginBottom: 12 }}>
          {['Today P&L', 'Unrealized', 'Capital', 'Running'].map(l => (
            <div key={l} style={{ flex: 1, minWidth: 160, height: 68, background: C.surface, border: `1px solid ${C.border}`, borderRadius: 6, animation: 'pulse 1.5s infinite' }} />
          ))}
        </div>
      </div>
    )
  }

  const pnlColor = summary.todayTotalPnl >= 0 ? C.green : C.red
  const realizedColor = summary.todayTotalRealizedPnl >= 0 ? C.green : C.red
  const unrealizedColor = summary.todayTotalUnrealizedPnl >= 0 ? C.green : C.red

  return (
    <div>
      {/* ── Summary metric cards ── */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 8, marginBottom: 12 }}>
        <MetricCard
          label="Today's P&L (Realized)"
          value={(summary.todayTotalRealizedPnl >= 0 ? '+' : '') + formatInr(summary.todayTotalRealizedPnl)}
          sub="Net of brokerage & taxes"
          valueColor={realizedColor}
        />
        <MetricCard
          label="Unrealized P&L"
          value={(summary.todayTotalUnrealizedPnl >= 0 ? '+' : '') + formatInr(summary.todayTotalUnrealizedPnl)}
          sub="Mark-to-market positions"
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
          sub={`${summary.runningCount} running`}
          valueColor={C.blue}
        />
      </div>

      {/* ── Strategy status summary ── */}
      <div style={{ display: 'flex', gap: 8, marginBottom: 12, flexWrap: 'wrap' }}>
        {[
          { label: 'Live Running', count: summary.runningCount, color: C.green, bg: C.greenBg },
          { label: 'Forward Testing', count: summary.forwardTestCount, color: C.blue, bg: C.blueBg },
          { label: 'Paused', count: summary.pausedCount, color: C.amber, bg: C.amberBg },
          { label: 'Stopped', count: summary.stoppedCount, color: C.textMuted, bg: C.surface2 },
        ].map(s => (
          <div key={s.label} style={{
            display: 'flex', alignItems: 'center', gap: 6,
            background: s.bg, border: `1px solid ${s.color}30`,
            borderRadius: 4, padding: '4px 10px',
          }}>
            <span style={{ fontSize: 14, fontWeight: 800, color: s.color, fontFamily: "'JetBrains Mono', monospace" }}>{s.count}</span>
            <span style={{ fontSize: 11, color: s.color, fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.05em' }}>{s.label}</span>
          </div>
        ))}
      </div>

      {/* ── Per-strategy breakdown ── */}
      <div style={{ marginBottom: 12 }}>
        <div style={{
          display: 'flex', justifyContent: 'space-between', alignItems: 'center',
          marginBottom: 8, paddingBottom: 6,
          borderBottom: `1px solid ${C.border}`,
        }}>
          <span style={{ fontSize: 11, fontWeight: 700, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.08em' }}>
            Strategy Breakdown
          </span>
        </div>
        {summary.byStrategy.length === 0
          ? <EmptyPortfolio />
          : <StrategyTable rows={summary.byStrategy} />
        }
      </div>

      {/* ── Pipeline guidance (when no live strategies) ── */}
      {summary.runningCount === 0 && summary.byStrategy.length === 0 && (
        <div style={{
          marginTop: 12, background: C.surface2, border: `1px solid ${C.border}`,
          borderRadius: 6, padding: '12px 14px', fontSize: 12, color: C.blue,
          display: 'flex', alignItems: 'flex-start', gap: 8,
        }}>
          <span style={{ fontSize: 14, flexShrink: 0 }}>ℹ</span>
          <div>
            <strong style={{ color: C.text, fontSize: 11, textTransform: 'uppercase', letterSpacing: '0.05em', display: 'block', marginBottom: 4 }}>Start your trading pipeline:</strong>
            <ol style={{ margin: 0, paddingLeft: 16, lineHeight: 1.6, color: C.blue, fontSize: 11 }}>
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
