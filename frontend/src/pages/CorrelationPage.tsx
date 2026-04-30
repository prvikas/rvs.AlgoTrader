import { useState, useMemo } from 'react'
import { useQuery, useMutation } from '@tanstack/react-query'
import {
  ScatterChart, Scatter, XAxis, YAxis, Tooltip,
  ResponsiveContainer, CartesianGrid,
} from 'recharts'
import {
  backtestApi,
  correlationApi,
  BacktestResult,
  StrategyReturnSeries,
  CorrelationMatrix,
  PortfolioConstructionResult,
} from '../api/client'
import { C, F, CONTENT_PAD, TABLE_CELL } from '../styles/tokens'

// ── Derive daily returns from backtest trade list ────────────────────────────
// Groups net PnL by exit date, fills zero for gap days, returns fractions.

function deriveDailyReturns(bt: BacktestResult): number[] {
  if (!bt.trades || bt.trades.length === 0 || !bt.initialCapital) return []

  // Map date string → cumulative net PnL
  const byDate = new Map<string, number>()
  for (const t of bt.trades) {
    const date = t.exitTime.slice(0, 10)
    byDate.set(date, (byDate.get(date) ?? 0) + t.netPnl)
  }

  // Sort dates and fill gaps with 0
  const dates = Array.from(byDate.keys()).sort()
  if (dates.length === 0) return []

  const start = new Date(dates[0])
  const end   = new Date(dates[dates.length - 1])
  const result: number[] = []
  for (let d = new Date(start); d <= end; d.setDate(d.getDate() + 1)) {
    const key = d.toISOString().slice(0, 10)
    result.push((byDate.get(key) ?? 0) / bt.initialCapital!)
  }
  return result
}

// ── Correlation heatmap ──────────────────────────────────────────────────────

function corrColor(r: number): string {
  // −1 → red, 0 → neutral surface, +1 → green
  if (r >= 0.7)  return C.greenBg    // high positive — muted green tint
  if (r >= 0.4)  return C.greenBg
  if (r <= -0.7) return C.redBg      // high negative — muted red tint
  if (r <= -0.4) return C.redBg
  return C.surface2
}

function corrTextColor(r: number, isDiag: boolean): string {
  if (isDiag) return C.textMuted
  if (r >= 0.7)  return C.green
  if (r <= -0.7) return C.red
  return C.text
}

function HeatmapCell({ value, isDiag }: { value: number; isDiag: boolean }) {
  return (
    <td style={{
      padding: '5px 8px',
      textAlign: 'center',
      fontFamily: F.mono,
      fontSize: 12,
      background: corrColor(value),
      color: corrTextColor(value, isDiag),
      border: `1px solid ${C.border}`,
      minWidth: 64,
    }}>
      {isDiag ? '—' : value.toFixed(2)}
    </td>
  )
}

function CorrelationHeatmap({ matrix }: { matrix: CorrelationMatrix }) {
  return (
    <div style={{ overflowX: 'auto' }}>
      <table style={{ borderCollapse: 'collapse', width: '100%' }}>
        <thead>
          <tr>
            <th style={{ padding: TABLE_CELL, background: C.surface2, fontSize: 11, color: C.textMuted }}></th>
            {matrix.strategyNames.map(name => (
              <th key={name} style={{
                padding: TABLE_CELL, background: C.surface2,
                fontSize: 11, color: C.textMuted,
                fontWeight: 700, textAlign: 'center',
                maxWidth: 100, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
              }} title={name}>
                {name.length > 14 ? name.slice(0, 14) + '…' : name}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {matrix.coefficients.map((row, i) => (
            <tr key={i}>
              <td style={{
                padding: TABLE_CELL, background: C.surface2,
                fontSize: 11, color: C.textMuted, fontWeight: 700,
                whiteSpace: 'nowrap',
              }} title={matrix.strategyNames[i]}>
                {matrix.strategyNames[i].length > 14
                  ? matrix.strategyNames[i].slice(0, 14) + '…'
                  : matrix.strategyNames[i]}
              </td>
              {row.map((val, j) => (
                <HeatmapCell key={j} value={val} isDiag={i === j} />
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

// ── Efficient frontier scatter ───────────────────────────────────────────────

function EfficientFrontier({ result }: { result: PortfolioConstructionResult }) {
  const data = useMemo(() => result.efficientFrontierSamples.map(p => ({
    vol: +(p.annualVolatility * 100).toFixed(2),
    ret: +(p.annualReturn * 100).toFixed(2),
    sharpe: +p.sharpeRatio.toFixed(2),
  })), [result.efficientFrontierSamples])

  const optimal = {
    vol: +(result.portfolioAnnualVolatility * 100).toFixed(2),
    ret: +(result.portfolioAnnualReturn * 100).toFixed(2),
    sharpe: +result.portfolioSharpe.toFixed(2),
  }

  return (
    <ResponsiveContainer width="100%" height={260}>
      <ScatterChart margin={{ top: 8, right: 16, bottom: 16, left: 8 }}>
        <CartesianGrid stroke={C.border} strokeDasharray="3 3" />
        <XAxis
          dataKey="vol" name="Volatility %" type="number"
          tick={{ fontSize: 11, fill: C.textMuted }}
          label={{ value: 'Annual Vol %', position: 'insideBottom', offset: -8, fontSize: 11, fill: C.textMuted }}
        />
        <YAxis
          dataKey="ret" name="Return %" type="number"
          tick={{ fontSize: 11, fill: C.textMuted }}
          label={{ value: 'Annual Ret %', angle: -90, position: 'insideLeft', fontSize: 11, fill: C.textMuted }}
        />
        <Tooltip
          cursor={{ strokeDasharray: '3 3' }}
          contentStyle={{ background: C.surface, border: `1px solid ${C.border}`, fontSize: 11 }}
          formatter={(val, name) => [`${val}%`, name === 'vol' ? 'Volatility' : 'Return']}
        />
        <Scatter name="Portfolios" data={data} fill={C.textMuted} opacity={0.4} r={2} />
        <Scatter name="Optimal" data={[optimal]} fill={C.green} r={6} />
      </ScatterChart>
    </ResponsiveContainer>
  )
}

// ── Main page ────────────────────────────────────────────────────────────────

export function CorrelationPage() {
  const [selectedIds, setSelectedIds] = useState<string[]>([])
  const [matrixResult, setMatrixResult] = useState<CorrelationMatrix | null>(null)
  const [portfolioResult, setPortfolioResult] = useState<PortfolioConstructionResult | null>(null)

  const { data: backtests, isLoading } = useQuery({
    queryKey: ['backtest-results-correlation'],
    queryFn: () => backtestApi.list().then(r => r.data.data?.items ?? []),
  })

  const completedWithTrades = useMemo(
    () => (backtests ?? []).filter(bt => bt.success && bt.trades && bt.trades.length > 0),
    [backtests],
  )

  const returnSeriesMap = useMemo(() => {
    const map = new Map<string, StrategyReturnSeries>()
    for (const bt of completedWithTrades) {
      if (!bt.id) continue
      map.set(bt.id, {
        strategyInstanceId: bt.id,
        strategyName: `${bt.strategyName} / ${bt.symbol}`,
        dailyReturns: deriveDailyReturns(bt),
      })
    }
    return map
  }, [completedWithTrades])

  const matrixMut = useMutation({
    mutationFn: (series: StrategyReturnSeries[]) => correlationApi.matrix(series),
    onSuccess: r => setMatrixResult(r.data.data ?? null),
  })

  const portfolioMut = useMutation({
    mutationFn: (series: StrategyReturnSeries[]) => correlationApi.portfolio(series),
    onSuccess: r => setPortfolioResult(r.data.data ?? null),
  })

  function toggleSelect(id: string) {
    setSelectedIds(prev =>
      prev.includes(id) ? prev.filter(x => x !== id) : [...prev, id]
    )
    setMatrixResult(null)
    setPortfolioResult(null)
  }

  function run() {
    const series = selectedIds
      .map(id => returnSeriesMap.get(id))
      .filter((s): s is StrategyReturnSeries => !!s && s.dailyReturns.length >= 2)
    if (series.length < 2) return
    matrixMut.mutate(series)
    portfolioMut.mutate(series)
  }

  const canRun = selectedIds.length >= 2
  const isBusy = matrixMut.isPending || portfolioMut.isPending

  return (
    <div style={{ padding: CONTENT_PAD, display: 'flex', flexDirection: 'column', gap: 16 }}>

      {/* Header row */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
        <span style={{ fontSize: 13, fontWeight: 700, color: C.text, textTransform: 'uppercase', letterSpacing: '0.06em' }}>
          Strategy Correlation
        </span>
        <span style={{ fontSize: 11, color: C.textMuted }}>
          Select 2+ completed backtests to analyse return correlations and build an optimal portfolio.
        </span>
        <button
          onClick={run}
          disabled={!canRun || isBusy}
          style={{
            marginLeft: 'auto', padding: '5px 16px', fontSize: 12, fontWeight: 700,
            background: canRun && !isBusy ? C.blue : C.surface2,
            color: canRun && !isBusy ? 'white' : C.textMuted,
            border: `1px solid ${canRun && !isBusy ? C.blue : C.border}`,
            borderRadius: 6, cursor: canRun && !isBusy ? 'pointer' : 'default',
            transition: 'background 0.1s',
          }}
        >
          {isBusy ? 'Analysing…' : 'Run Analysis'}
        </button>
      </div>

      {/* Backtest selection */}
      <div style={{ background: C.surface, border: `1px solid ${C.border}`, borderRadius: 8, overflow: 'hidden' }}>
        <div style={{
          padding: '8px 14px', fontSize: 11, fontWeight: 700,
          color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.06em',
          borderBottom: `1px solid ${C.border}`, background: C.surface2,
        }}>
          Backtest Runs — select to include ({selectedIds.length} selected)
        </div>
        {isLoading && (
          <div style={{ padding: '14px 16px', fontSize: 12, color: C.textMuted }}>Loading…</div>
        )}
        {!isLoading && completedWithTrades.length === 0 && (
          <div style={{ padding: '14px 16px', fontSize: 12, color: C.textMuted }}>
            No completed backtests with trade data found. Run backtests first.
          </div>
        )}
        {completedWithTrades.length > 0 && (
          <table style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead>
              <tr style={{ background: C.surface2 }}>
                {['', 'Strategy', 'Symbol', 'Timeframe', 'Trades', 'Sharpe', 'Total Return', 'Max DD'].map(h => (
                  <th key={h} style={{
                    padding: TABLE_CELL, textAlign: h === '' ? 'center' : 'left',
                    fontSize: 11, fontWeight: 700, color: C.textMuted,
                    textTransform: 'uppercase', letterSpacing: '0.05em',
                    borderBottom: `1px solid ${C.border}`,
                  }}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {completedWithTrades.map(bt => {
                const selected = selectedIds.includes(bt.id!)
                return (
                  <tr
                    key={bt.id}
                    onClick={() => bt.id && toggleSelect(bt.id)}
                    style={{
                      cursor: 'pointer',
                      background: selected ? C.blueBg : 'transparent',
                      borderBottom: `1px solid ${C.border2}`,
                      transition: 'background 0.1s',
                    }}
                    onMouseEnter={e => { if (!selected) (e.currentTarget as HTMLElement).style.background = C.surface3 }}
                    onMouseLeave={e => { (e.currentTarget as HTMLElement).style.background = selected ? C.blueBg : 'transparent' }}
                  >
                    <td style={{ padding: TABLE_CELL, textAlign: 'center' }}>
                      <span style={{
                        display: 'inline-block', width: 14, height: 14, borderRadius: 3,
                        border: `2px solid ${selected ? C.blue : C.border3}`,
                        background: selected ? C.blue : 'transparent',
                      }} />
                    </td>
                    <td style={{ padding: TABLE_CELL, fontSize: 12, color: C.text }}>{bt.strategyName}</td>
                    <td style={{ padding: TABLE_CELL, fontSize: 12, color: C.textSub, fontFamily: F.mono }}>{bt.symbol}</td>
                    <td style={{ padding: TABLE_CELL, fontSize: 12, color: C.textSub }}>{bt.timeframe}</td>
                    <td style={{ padding: TABLE_CELL, fontSize: 12, color: C.textSub, fontFamily: F.mono, textAlign: 'right' }}>{bt.totalTrades}</td>
                    <td style={{ padding: TABLE_CELL, fontSize: 12, fontFamily: F.mono, textAlign: 'right', color: bt.sharpeRatio >= 1 ? C.green : bt.sharpeRatio < 0 ? C.red : C.text }}>
                      {bt.sharpeRatio.toFixed(2)}
                    </td>
                    <td style={{ padding: TABLE_CELL, fontSize: 12, fontFamily: F.mono, textAlign: 'right', color: bt.totalReturn >= 0 ? C.green : C.red }}>
                      {(bt.totalReturn * 100).toFixed(1)}%
                    </td>
                    <td style={{ padding: TABLE_CELL, fontSize: 12, fontFamily: F.mono, textAlign: 'right', color: C.red }}>
                      {(bt.maxDrawdown * 100).toFixed(1)}%
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        )}
      </div>

      {/* Error states */}
      {(matrixMut.isError || portfolioMut.isError) && (
        <div style={{ padding: '10px 14px', background: C.redBg, border: `1px solid ${C.red44}`, borderRadius: 6, fontSize: 12, color: C.red }}>
          Analysis failed. Ensure selected backtests have sufficient trade history.
        </div>
      )}

      {/* Results — matrix + warnings */}
      {matrixResult && (
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 320px', gap: 16 }}>
          {/* Heatmap */}
          <div style={{ background: C.surface, border: `1px solid ${C.border}`, borderRadius: 8, overflow: 'hidden' }}>
            <div style={{
              padding: '8px 14px', fontSize: 11, fontWeight: 700,
              color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.06em',
              borderBottom: `1px solid ${C.border}`, background: C.surface2,
            }}>
              Pearson Correlation Matrix
            </div>
            <div style={{ padding: 12 }}>
              <CorrelationHeatmap matrix={matrixResult} />
            </div>
          </div>

          {/* High-correlation warnings */}
          <div style={{ background: C.surface, border: `1px solid ${C.border}`, borderRadius: 8, overflow: 'hidden' }}>
            <div style={{
              padding: '8px 14px', fontSize: 11, fontWeight: 700,
              color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.06em',
              borderBottom: `1px solid ${C.border}`, background: C.surface2,
            }}>
              Risk Warnings
            </div>
            <div style={{ padding: 12, display: 'flex', flexDirection: 'column', gap: 8 }}>
              {matrixResult.highCorrelationPairs.length === 0 ? (
                <div style={{ fontSize: 12, color: C.green }}>No high-correlation pairs detected (&lt;0.7 threshold).</div>
              ) : (
                matrixResult.highCorrelationPairs.map((pair, i) => (
                  <div key={i} style={{
                    padding: '8px 10px',
                    background: Math.abs(pair.correlation) >= 0.9 ? C.redBg : C.amberBg,
                    border: `1px solid ${Math.abs(pair.correlation) >= 0.9 ? C.red44 : C.amber44}`,
                    borderRadius: 6,
                  }}>
                    <div style={{ fontSize: 11, fontWeight: 700, color: Math.abs(pair.correlation) >= 0.9 ? C.red : C.amber, marginBottom: 4 }}>
                      {Math.abs(pair.correlation) >= 0.9 ? 'HIGH RISK' : 'WARNING'}
                    </div>
                    <div style={{ fontSize: 11, color: C.text, marginBottom: 2 }}>{pair.strategyA}</div>
                    <div style={{ fontSize: 11, color: C.textMuted }}>↕ {pair.strategyB}</div>
                    <div style={{ fontSize: 12, fontFamily: F.mono, fontWeight: 700, marginTop: 4, color: C.text }}>
                      r = {pair.correlation.toFixed(3)}
                    </div>
                  </div>
                ))
              )}
            </div>
          </div>
        </div>
      )}

      {/* Portfolio construction results */}
      {portfolioResult && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          {/* Metric strip */}
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(5, 1fr)', gap: 8 }}>
            {[
              { label: 'Portfolio Sharpe', value: portfolioResult.portfolioSharpe.toFixed(2), color: portfolioResult.portfolioSharpe >= 1 ? C.green : C.text },
              { label: 'Annual Return', value: (portfolioResult.portfolioAnnualReturn * 100).toFixed(1) + '%', color: portfolioResult.portfolioAnnualReturn >= 0 ? C.green : C.red },
              { label: 'Annual Volatility', value: (portfolioResult.portfolioAnnualVolatility * 100).toFixed(1) + '%', color: C.text },
              { label: 'Max Drawdown', value: (portfolioResult.maxDrawdown * 100).toFixed(1) + '%', color: C.red },
              { label: 'Diversification Ratio', value: portfolioResult.diversificationRatio.toFixed(2), color: portfolioResult.diversificationRatio >= 1.2 ? C.green : C.text },
            ].map(m => (
              <div key={m.label} style={{ background: C.surface, border: `1px solid ${C.border}`, borderRadius: 8, padding: '10px 14px' }}>
                <div style={{ fontSize: 11, fontWeight: 700, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.06em', marginBottom: 6 }}>{m.label}</div>
                <div style={{ fontSize: 20, fontWeight: 700, fontFamily: F.mono, color: m.color }}>{m.value}</div>
              </div>
            ))}
          </div>

          {/* Weights + Efficient frontier */}
          <div style={{ display: 'grid', gridTemplateColumns: '280px 1fr', gap: 12 }}>
            {/* Optimal weights */}
            <div style={{ background: C.surface, border: `1px solid ${C.border}`, borderRadius: 8, overflow: 'hidden' }}>
              <div style={{
                padding: '8px 14px', fontSize: 11, fontWeight: 700,
                color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.06em',
                borderBottom: `1px solid ${C.border}`, background: C.surface2,
              }}>
                Optimal Weights (Max Sharpe)
              </div>
              <div style={{ padding: 12 }}>
                {Object.entries(portfolioResult.optimalWeights)
                  .sort(([, a], [, b]) => b - a)
                  .map(([name, weight]) => (
                    <div key={name} style={{ marginBottom: 8 }}>
                      <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 11, color: C.textSub, marginBottom: 3 }}>
                        <span title={name}>{name.length > 22 ? name.slice(0, 22) + '…' : name}</span>
                        <span style={{ fontFamily: F.mono, fontWeight: 700, color: C.text }}>{(weight * 100).toFixed(1)}%</span>
                      </div>
                      <div style={{ height: 4, background: C.surface2, borderRadius: 2 }}>
                        <div style={{ height: '100%', width: `${weight * 100}%`, background: C.blue, borderRadius: 2 }} />
                      </div>
                    </div>
                  ))}
              </div>
            </div>

            {/* Efficient frontier chart */}
            <div style={{ background: C.surface, border: `1px solid ${C.border}`, borderRadius: 8, overflow: 'hidden' }}>
              <div style={{
                padding: '8px 14px', fontSize: 11, fontWeight: 700,
                color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.06em',
                borderBottom: `1px solid ${C.border}`, background: C.surface2,
              }}>
                Efficient Frontier — 10,000 Monte Carlo Portfolios
                <span style={{ marginLeft: 12, color: C.green, fontWeight: 400 }}>● Optimal (max Sharpe)</span>
              </div>
              <div style={{ padding: 12 }}>
                <EfficientFrontier result={portfolioResult} />
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
