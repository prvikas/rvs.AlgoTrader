import { useQuery } from '@tanstack/react-query'
import { portfolioApi, strategiesApi, StrategyPnlRow } from '../api/client'
import { formatInr } from '../utils/datetime'
import { C, F, TABLE_CELL, TABLE_HEADER_CELL } from '../styles/tokens'

// ── Helpers ───────────────────────────────────────────────────────────────────

function pnlColor(val: number) {
  return val >= 0 ? C.green : C.red
}

function statusDot(status: string) {
  const s = status?.toLowerCase()
  let color: string = C.textMuted
  if (s === 'running') color = C.green
  else if (s === 'paused') color = C.amber
  else if (s === 'error') color = C.red
  else if (s === 'stopped' || s === 'completed') color = C.textMuted

  return (
    <span style={{
      display: 'inline-block',
      width: 8,
      height: 8,
      borderRadius: '50%',
      background: color,
      marginRight: 6,
      flexShrink: 0,
    }} />
  )
}

function modeBadge(mode: string) {
  const colors: Record<string, { bg: string; text: string }> = {
    Live: { bg: C.greenBg, text: C.green },
    Forward: { bg: C.blueBg, text: C.blue },
    Backtest: { bg: C.surface2, text: C.textSub },
  }
  const c = colors[mode] ?? { bg: C.surface2, text: C.textSub }
  return (
    <span style={{
      display: 'inline-block',
      padding: '2px 7px',
      borderRadius: 4,
      fontSize: 10,
      fontWeight: 700,
      letterSpacing: '0.04em',
      background: c.bg,
      color: c.text,
    }}>
      {mode}
    </span>
  )
}

// ── Metric card ───────────────────────────────────────────────────────────────

function MetricCard({ label, value, color = C.text, sub }: {
  label: string; value: string; color?: string; sub?: string
}) {
  return (
    <div style={{
      background: C.surface,
      border: `1px solid ${C.border}`,
      borderRadius: 6,
      padding: '12px 16px',
      flex: 1,
      minWidth: 160,
    }}>
      <div style={{ fontSize: 10, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.05em', fontWeight: 600, marginBottom: 6 }}>
        {label}
      </div>
      <div style={{ fontSize: 22, fontWeight: 800, color, fontFamily: F.mono, lineHeight: 1 }}>
        {value}
      </div>
      {sub && <div style={{ fontSize: 11, color: C.textMuted, marginTop: 4 }}>{sub}</div>}
    </div>
  )
}

// ── Progress bar control ──────────────────────────────────────────────────────

function ProgressControl({ label, current, limit, colorFill = C.red }: {
  label: string; current: number; limit: number; colorFill?: string
}) {
  const pct = limit > 0 ? Math.min(100, Math.abs(current / limit) * 100) : 0
  const dangerZone = pct >= 80

  return (
    <div style={{ marginBottom: 16 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 6 }}>
        <span style={{ fontSize: 12, fontWeight: 600, color: C.text }}>{label}</span>
        <span style={{ fontSize: 12, fontFamily: F.mono, color: dangerZone ? C.red : C.textSub }}>
          {formatInr(Math.abs(current))} / {formatInr(limit)} ({pct.toFixed(1)}%)
        </span>
      </div>
      <div style={{ background: C.surface2, borderRadius: 4, height: 8, overflow: 'hidden' }}>
        <div style={{
          width: `${pct}%`,
          height: '100%',
          background: dangerZone ? C.red : colorFill,
          borderRadius: 4,
          transition: 'width 0.3s',
        }} />
      </div>
    </div>
  )
}

// ── Strategy risk table ───────────────────────────────────────────────────────

function StrategyRiskTable({ rows }: { rows: StrategyPnlRow[] }) {
  const thStyle: React.CSSProperties = {
    padding: TABLE_HEADER_CELL,
    fontSize: 10,
    fontWeight: 700,
    color: C.textMuted,
    textTransform: 'uppercase',
    letterSpacing: '0.05em',
    textAlign: 'left',
    background: C.surface2,
    whiteSpace: 'nowrap',
  }

  const tdStyle: React.CSSProperties = {
    padding: TABLE_CELL,
    fontSize: 12,
    verticalAlign: 'middle',
  }

  return (
    <div style={{ background: C.surface, border: `1px solid ${C.border}`, borderRadius: 6, overflow: 'hidden' }}>
      <div style={{ padding: '10px 12px', borderBottom: `1px solid ${C.border}`, fontSize: 12, fontWeight: 700, color: C.textSub, textTransform: 'uppercase', letterSpacing: '0.04em' }}>
        Strategy Risk Table
      </div>
      <table style={{ width: '100%', borderCollapse: 'collapse' }}>
        <thead>
          <tr>
            <th style={thStyle}>Name</th>
            <th style={thStyle}>Mode</th>
            <th style={thStyle}>Status</th>
            <th style={{ ...thStyle, textAlign: 'right' }}>Allocated</th>
            <th style={{ ...thStyle, textAlign: 'right' }}>Today P&L</th>
            <th style={{ ...thStyle, textAlign: 'right' }}>P&L %</th>
          </tr>
        </thead>
        <tbody>
          {rows.length === 0 && (
            <tr>
              <td colSpan={6} style={{ padding: '24px 12px', textAlign: 'center', color: C.textMuted, fontSize: 13 }}>
                No active strategies.
              </td>
            </tr>
          )}
          {rows.map((row, idx) => (
            <tr
              key={row.instanceId}
              style={{
                background: idx % 2 === 0 ? C.surface : C.surface3,
                borderBottom: `1px solid ${C.border2}`,
              }}
            >
              <td style={{ ...tdStyle, fontWeight: 600, color: C.text }}>
                <div>{row.name}</div>
                <div style={{ fontSize: 10, color: C.textMuted, marginTop: 1 }}>{row.internalSymbol}</div>
              </td>
              <td style={tdStyle}>{modeBadge(row.mode)}</td>
              <td style={tdStyle}>
                <div style={{ display: 'flex', alignItems: 'center' }}>
                  {statusDot(row.status)}
                  <span style={{ fontSize: 11, color: C.textSub }}>{row.status}</span>
                </div>
              </td>
              <td style={{ ...tdStyle, textAlign: 'right', fontFamily: F.mono, color: C.text }}>
                {formatInr(row.allocatedCapital)}
              </td>
              <td style={{ ...tdStyle, textAlign: 'right', fontFamily: F.mono, fontWeight: 700, color: pnlColor(row.todayTotalPnl) }}>
                {row.todayTotalPnl >= 0 ? '+' : ''}{formatInr(row.todayTotalPnl)}
              </td>
              <td style={{ ...tdStyle, textAlign: 'right', fontFamily: F.mono, color: pnlColor(row.pnlPercent) }}>
                {row.pnlPercent >= 0 ? '+' : ''}{row.pnlPercent.toFixed(2)}%
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

// ── Page root ─────────────────────────────────────────────────────────────────

export function RiskDashboardPage() {
  const { data: summary, isLoading: summaryLoading, isError: summaryError } = useQuery({
    queryKey: ['portfolio-summary'],
    queryFn: () => portfolioApi.summary().then(r => r.data.data),
    refetchInterval: 15_000,
  })

  const { data: strategies, isLoading: stratLoading } = useQuery({
    queryKey: ['strategies-list'],
    queryFn: () => strategiesApi.list().then(r => r.data.data ?? []),
    refetchInterval: 15_000,
  })

  const isLoading = summaryLoading || stratLoading
  const isError = summaryError

  // Derive open positions count from strategies data
  const openPositions = (strategies ?? []).reduce(
    (s, st) => s + (st.openPositionCount ?? 0), 0,
  )

  // Daily loss limit: read from settings (not available here, so use a sensible default placeholder)
  // We use the running strategies' allocated capital to approximate a limit
  const totalAllocated = summary?.totalAllocatedCapital ?? 0
  const dailyLossLimit = totalAllocated * 0.02 // 2% of deployed capital as daily limit placeholder
  const todayRealizedPnl = summary?.todayTotalRealizedPnl ?? 0
  const todayUnrealizedPnl = summary?.todayTotalUnrealizedPnl ?? 0

  // Kill switch: we don't have a direct field in portfolio summary; check strategy statuses
  // A kill switch trip would stop all running strategies
  const runningCount = summary?.runningCount ?? 0
  const killSwitchTripped = runningCount === 0 && (strategies ?? []).length > 0

  const maxPositionsLimit = 20 // standard limit — would come from settings in a full integration

  return (
    <div>
      {/* Page header */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 }}>
        <h2 style={{ margin: 0, fontSize: 18, fontWeight: 700 }}>Risk Dashboard</h2>
        <span style={{ fontSize: 11, color: C.textMuted }}>Auto-refreshes every 15s</span>
      </div>

      {isLoading && <p style={{ color: C.textMuted }}>Loading risk data...</p>}
      {isError && <p style={{ color: C.red }}>Failed to load portfolio summary.</p>}

      {!isLoading && (
        <>
          {/* Top metric cards */}
          <div style={{ display: 'flex', gap: 12, marginBottom: 20, flexWrap: 'wrap' }}>
            <MetricCard
              label="Total Allocated Capital"
              value={formatInr(totalAllocated)}
              color={C.text}
            />
            <MetricCard
              label="Today Realized P&L"
              value={(todayRealizedPnl >= 0 ? '+' : '') + formatInr(todayRealizedPnl)}
              color={pnlColor(todayRealizedPnl)}
              sub="Closed trades today"
            />
            <MetricCard
              label="Today Unrealized P&L"
              value={(todayUnrealizedPnl >= 0 ? '+' : '') + formatInr(todayUnrealizedPnl)}
              color={pnlColor(todayUnrealizedPnl)}
              sub="Open positions MTM"
            />
            <MetricCard
              label="Open Positions"
              value={String(openPositions)}
              color={openPositions > 0 ? C.blue : C.textMuted}
              sub={`${runningCount} running ${summary?.pausedCount ?? 0 > 0 ? `· ${summary?.pausedCount} paused` : ''}`.trim()}
            />
          </div>

          {/* Risk Controls */}
          <div style={{
            background: C.surface,
            border: `1px solid ${C.border}`,
            borderRadius: 6,
            padding: '14px 16px',
            marginBottom: 16,
          }}>
            <div style={{ fontSize: 12, fontWeight: 700, color: C.textSub, textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: 14 }}>
              Risk Controls
            </div>

            {totalAllocated > 0 && (
              <ProgressControl
                label="Daily Loss Limit (2% of deployed)"
                current={Math.min(0, todayRealizedPnl)}
                limit={dailyLossLimit}
                colorFill={C.red}
              />
            )}

            <ProgressControl
              label="Max Positions"
              current={openPositions}
              limit={maxPositionsLimit}
              colorFill={C.blue}
            />

            {/* Kill switch status */}
            <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginTop: 4 }}>
              <span style={{ fontSize: 12, fontWeight: 600, color: C.text }}>Kill Switch</span>
              <span style={{
                display: 'inline-flex',
                alignItems: 'center',
                gap: 5,
                padding: '3px 10px',
                borderRadius: 4,
                fontSize: 11,
                fontWeight: 700,
                background: killSwitchTripped ? C.redBg : C.greenBg,
                color: killSwitchTripped ? C.red : C.green,
                border: `1px solid ${killSwitchTripped ? C.red : C.green}`,
              }}>
                <span style={{
                  width: 7, height: 7, borderRadius: '50%',
                  background: killSwitchTripped ? C.red : C.green,
                  display: 'inline-block',
                }} />
                {killSwitchTripped ? 'TRIPPED' : 'ACTIVE'}
              </span>
              {killSwitchTripped && (
                <span style={{ fontSize: 11, color: C.red }}>
                  All strategies are stopped — kill switch may be engaged.
                </span>
              )}
            </div>
          </div>

          {/* Strategy risk table */}
          <StrategyRiskTable rows={summary?.byStrategy ?? []} />
        </>
      )}
    </div>
  )
}
