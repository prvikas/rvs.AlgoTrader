import { useQuery } from '@tanstack/react-query'
import { strategiesApi, backtestApi, StrategyInstance } from '../api/client'
import { C, CONTENT_PAD } from '../styles/tokens'
import { formatInr } from '../utils/datetime'

// ── Metric card ───────────────────────────────────────────────────────────────

function MetricCard({
  label, value, sub, color,
}: {
  label: string
  value: string
  sub?: string
  color?: string
}) {
  return (
    <div style={{
      background: C.surface, border: `1px solid ${C.border}`,
      borderRadius: 8, padding: '12px 16px',
    }}>
      <div style={{ fontSize: 11, fontWeight: 700, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.06em', marginBottom: 6 }}>
        {label}
      </div>
      <div style={{
        fontSize: 22, fontWeight: 700, fontVariantNumeric: 'tabular-nums',
        fontFamily: '"JetBrains Mono", monospace',
        color: color ?? C.text,
      }}>
        {value}
      </div>
      {sub && (
        <div style={{ fontSize: 11, color: C.textSub, marginTop: 4 }}>{sub}</div>
      )}
    </div>
  )
}

// ── Strategy risk table ───────────────────────────────────────────────────────

function StrategyRiskTable({ strategies }: { strategies: StrategyInstance[] }) {
  const running = strategies.filter(s => s.status === 'Running')
  if (running.length === 0) return null

  return (
    <div style={{
      background: C.surface, border: `1px solid ${C.border}`,
      borderRadius: 8, overflow: 'hidden',
    }}>
      <div style={{ padding: '10px 14px', fontSize: 12, fontWeight: 700, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.06em', borderBottom: `1px solid ${C.border}` }}>
        Running Strategies — Capital Exposure
      </div>
      <table style={{ width: '100%', borderCollapse: 'collapse' }}>
        <thead>
          <tr style={{ background: C.surface2 }}>
            {['Strategy', 'Symbol', 'Broker', 'Mode', 'Capital', 'Today P&L'].map(h => (
              <th key={h} style={{
                padding: '5px 10px', textAlign: 'left',
                fontSize: 10, fontWeight: 700, color: C.textMuted,
                textTransform: 'uppercase', letterSpacing: '0.05em',
                borderBottom: `1px solid ${C.border}`,
              }}>{h}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {running.map(s => {
            const pnl = s.todayRealizedPnl ?? 0
            return (
              <tr key={s.id}>
                <td style={{ padding: '5px 10px', fontSize: 13, color: C.text, borderBottom: `1px solid ${C.border2}` }}>
                  {s.strategyType}
                </td>
                <td style={{ padding: '5px 10px', fontSize: 13, color: C.textSub, borderBottom: `1px solid ${C.border2}` }}>
                  {s.internalSymbol}
                </td>
                <td style={{ padding: '5px 10px', fontSize: 12, color: C.textSub, borderBottom: `1px solid ${C.border2}` }}>
                  {s.brokerName}
                </td>
                <td style={{ padding: '5px 10px', fontSize: 11, borderBottom: `1px solid ${C.border2}` }}>
                  <span style={{
                    padding: '1px 6px', borderRadius: 3, fontWeight: 700,
                    background: s.mode === 'Live' ? C.redBg : s.mode === 'ForwardTest' ? C.blueBg : C.amberBg,
                    color: s.mode === 'Live' ? C.red : s.mode === 'ForwardTest' ? C.blue : C.amber,
                  }}>{s.mode}</span>
                </td>
                <td style={{ padding: '5px 10px', fontSize: 13, fontVariantNumeric: 'tabular-nums', color: C.text, borderBottom: `1px solid ${C.border2}` }}>
                  {s.allocatedCapital ? formatInr(s.allocatedCapital) : '—'}
                </td>
                <td style={{ padding: '5px 10px', fontSize: 13, fontVariantNumeric: 'tabular-nums', color: pnl >= 0 ? C.green : C.red, borderBottom: `1px solid ${C.border2}` }}>
                  {formatInr(pnl)}
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

// ── Best/worst backtest summary ───────────────────────────────────────────────

function BacktestRiskSummary() {
  const { data } = useQuery({
    queryKey: ['backtests-risk'],
    queryFn: () => backtestApi.list(undefined, 1, 100),
  })

  const backtests = data?.data?.data?.items ?? []
  if (backtests.length === 0) return null

  const sorted = [...backtests].sort((a, b) => b.totalReturn - a.totalReturn)
  const best = sorted[0]
  const worst = sorted[sorted.length - 1]
  const avgDD = backtests.reduce((s, b) => s + b.maxDrawdown, 0) / backtests.length
  const avgSharpe = backtests.reduce((s, b) => s + b.sharpeRatio, 0) / backtests.length

  return (
    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 12, marginBottom: 16 }}>
      <MetricCard
        label="Best Backtest Return"
        value={`${(best.totalReturn * 100).toFixed(2)}%`}
        sub={`${best.strategyName} / ${best.symbol}`}
        color={C.green}
      />
      <MetricCard
        label="Worst Backtest Return"
        value={`${(worst.totalReturn * 100).toFixed(2)}%`}
        sub={`${worst.strategyName} / ${worst.symbol}`}
        color={worst.totalReturn < 0 ? C.red : C.text}
      />
      <MetricCard
        label="Avg Max Drawdown"
        value={`${(avgDD * 100).toFixed(2)}%`}
        sub={`across ${backtests.length} runs`}
        color={avgDD > 0.2 ? C.red : avgDD > 0.1 ? C.amber : C.text}
      />
      <MetricCard
        label="Avg Sharpe Ratio"
        value={avgSharpe.toFixed(2)}
        sub="across all backtests"
        color={avgSharpe >= 1.5 ? C.green : avgSharpe >= 0.5 ? C.amber : C.red}
      />
    </div>
  )
}

// ── Main Page ─────────────────────────────────────────────────────────────────

export function RiskDashboardPage() {
  const { data: strategiesData, isLoading } = useQuery({
    queryKey: ['strategies-risk'],
    queryFn: () => strategiesApi.list(),
    refetchInterval: 30_000,
  })

  const strategies: StrategyInstance[] = strategiesData?.data?.data ?? []

  const running = strategies.filter(s => s.status === 'Running')
  const liveCount = running.filter(s => s.mode === 'Live').length
  const fwdCount = running.filter(s => s.mode === 'ForwardTest').length
  const totalCapital = strategies
    .filter(s => s.status === 'Running')
    .reduce((sum, s) => sum + (s.allocatedCapital ?? 0), 0)
  const todayPnl = strategies
    .filter(s => s.status === 'Running')
    .reduce((sum, s) => sum + (s.todayRealizedPnl ?? 0), 0)

  return (
    <div style={{ padding: CONTENT_PAD }}>
      <div style={{ fontSize: 18, fontWeight: 700, color: C.text, marginBottom: 16 }}>
        Risk Dashboard
      </div>

      {/* Portfolio-level summary */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 12, marginBottom: 16 }}>
        <MetricCard
          label="Running Strategies"
          value={String(running.length)}
          sub={`${liveCount} Live, ${fwdCount} Fwd Test`}
          color={liveCount > 0 ? C.amber : C.text}
        />
        <MetricCard
          label="Total Capital Deployed"
          value={formatInr(totalCapital)}
          sub="across all running strategies"
        />
        <MetricCard
          label="Today Realized P&L"
          value={formatInr(todayPnl)}
          color={todayPnl >= 0 ? C.green : C.red}
        />
        <MetricCard
          label="Total Strategies"
          value={String(strategies.length)}
          sub={`${strategies.filter(s => s.status === 'Stopped').length} stopped`}
        />
      </div>

      {/* Backtest risk summary */}
      <BacktestRiskSummary />

      {/* Per-strategy exposure */}
      {isLoading ? (
        <div style={{ color: C.textMuted, fontSize: 13, textAlign: 'center', padding: 32 }}>
          Loading strategies…
        </div>
      ) : (
        <StrategyRiskTable strategies={strategies} />
      )}

      {!isLoading && running.length === 0 && (
        <div style={{
          background: C.surface, border: `1px solid ${C.border}`,
          borderRadius: 8, padding: 32, textAlign: 'center',
          color: C.textMuted, fontSize: 13,
        }}>
          No strategies are currently running. Start a strategy to see live risk metrics.
        </div>
      )}
    </div>
  )
}
