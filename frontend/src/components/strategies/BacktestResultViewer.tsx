import React, { useState, useEffect, useMemo } from 'react'
import {
  LineChart, Line, XAxis, YAxis, Tooltip, ResponsiveContainer,
  ReferenceLine, CartesianGrid,
} from 'recharts'
import {
  backtestApi,
  BacktestResult,
  BacktestTradeResult,
  BacktestChartBar,
  BacktestJobStatus,
} from '../../api/client'
import { Scenario, ScenarioStatus, Strategy } from '../../types/strategy'
import { C, F, SP } from '../../styles/tokens'

// ── Props ─────────────────────────────────────────────────────────────────────

interface Props {
  strategy: Strategy
  scenario: Scenario
  /** Rolling 200-bar price window from SignalR during a live run */
  liveChartBars: BacktestChartBar[]
  /** Live progress / completed result from polling + SignalR */
  jobProgress: BacktestJobStatus | null
  /** jobId of the currently active backtest for this scenario (null if not running) */
  activeJobId: string | null
  signalRConnected: boolean
  onBack: () => void
}

type Tab = 'overview' | 'chart' | 'trades'

// ── Main component ────────────────────────────────────────────────────────────

export function BacktestResultViewer({
  strategy,
  scenario,
  liveChartBars,
  jobProgress,
  activeJobId,
  signalRConnected,
  onBack,
}: Props) {
  const [tab, setTab] = useState<Tab>('overview')
  const [loadedResult, setLoadedResult] = useState<BacktestResult | null>(null)
  const [loading, setLoading] = useState(false)
  const [stopping, setStopping] = useState(false)
  const [stopError, setStopError] = useState('')

  const isRunning = scenario.status === ScenarioStatus.Running

  // Prefer the result embedded in the final job status; fall back to API-loaded
  const result: BacktestResult | null = jobProgress?.result ?? loadedResult

  // Chart bars: use the completed result's downsampled sample when available,
  // otherwise use the live 200-bar window from SignalR
  const chartBars: BacktestChartBar[] =
    (result?.chartSample?.length ?? 0) > 0 ? result!.chartSample! : liveChartBars

  const trades: BacktestTradeResult[] = result?.trades ?? []

  // Switch to overview tab when run completes and result arrives
  useEffect(() => {
    if (!isRunning && result) setTab('overview')
  }, [isRunning, !!result]) // eslint-disable-line react-hooks/exhaustive-deps

  // Load result from API for already-Backtested scenarios that have no in-memory result
  useEffect(() => {
    if (isRunning || result || loading) return
    if (scenario.status !== ScenarioStatus.Backtested) return

    setLoading(true)
    backtestApi
      .byDefinition(strategy.id)
      .then(async resp => {
        const runs = resp.data?.data ?? []
        // Find the most recent run associated with this scenario (list is newest-first)
        const run = runs.find(r => r.scenarioId === scenario.id)
        if (!run?.id) return
        const full = await backtestApi.get(run.id)
        if (full.data?.data) setLoadedResult(full.data.data)
      })
      .catch(() => {})
      .finally(() => setLoading(false))
  }, [scenario.id, scenario.status]) // eslint-disable-line react-hooks/exhaustive-deps

  async function handleStop() {
    if (!activeJobId) return
    setStopping(true)
    setStopError('')
    try {
      await backtestApi.cancel(activeJobId)
    } catch {
      setStopError('Cancel request failed — try again.')
    } finally {
      setStopping(false)
    }
  }

  // Build equity curve from the trade list
  const equityCurve = useMemo(() => {
    if (!trades.length) return []
    let equity = scenario.capital
    const pts: { label: string; equity: number }[] = [
      { label: 'Start', equity },
    ]
    for (const t of trades) {
      equity += t.netPnl
      pts.push({
        label: new Date(t.exitTime).toLocaleDateString('en-IN', {
          month: 'short',
          day: 'numeric',
        }),
        equity: Math.round(equity),
      })
    }
    return pts
  }, [trades, scenario.capital])

  const hasResult = !!result
  const returnPct = result ? (result.totalReturn ?? 0) * 100 : null
  const ddPct = result ? (result.maxDrawdown ?? 0) * 100 : null

  return (
    <div style={{ display: 'flex', flexDirection: 'column', minHeight: 0 }}>

      {/* ── Header bar ── */}
      <div style={{
        display: 'flex', alignItems: 'center', gap: 10, padding: '8px 14px',
        borderBottom: `1px solid ${C.border}`, flexWrap: 'wrap', flexShrink: 0,
      }}>
        <button
          onClick={onBack}
          style={{ background: 'none', border: 'none', color: C.textMuted, cursor: 'pointer', fontSize: 13, padding: '2px 4px' }}
        >
          ←
        </button>
        <span style={{ fontWeight: 700, fontSize: 14, color: C.text }}>{scenario.name}</span>
        <span style={{ fontSize: 11, color: C.textMuted }}>
          {scenario.backtestRange.from.slice(0, 10)} – {scenario.backtestRange.to.slice(0, 10)}
        </span>
        <span style={{ fontSize: 11, color: C.textDim, fontFamily: F.mono }}>
          ₹{(scenario.capital / 1000).toFixed(0)}K
        </span>

        {isRunning && (
          <>
            <span style={{ display: 'flex', alignItems: 'center', gap: 5, fontSize: 11, color: C.amber }}>
              <span style={{
                display: 'inline-block', width: 7, height: 7, borderRadius: '50%',
                background: C.amber, animation: 'pulse 1.2s ease-in-out infinite',
              }} />
              {jobProgress?.progressPct
                ? `Running — ${jobProgress.progressPct.toFixed(0)}%`
                : 'Starting…'
              }
            </span>
            {!signalRConnected && (
              <span style={{ fontSize: 10, color: C.textDim }}>(polling)</span>
            )}
            <button
              onClick={handleStop}
              disabled={stopping}
              style={{
                marginLeft: 'auto', background: C.redBg, color: C.red,
                border: `1px solid ${C.red44}`, borderRadius: 4,
                padding: '4px 14px', cursor: stopping ? 'not-allowed' : 'pointer',
                fontSize: 11, fontWeight: 700, opacity: stopping ? 0.5 : 1,
              }}
            >
              {stopping ? 'Stopping…' : '⏹ Stop'}
            </button>
          </>
        )}
        {stopError && <span style={{ fontSize: 11, color: C.red }}>{stopError}</span>}
      </div>

      {/* ── Thin progress bar across the top (during run) ── */}
      {isRunning && (
        <div style={{ position: 'relative', height: 3, background: C.surface3, flexShrink: 0 }}>
          <div style={{
            position: 'absolute', inset: '0 auto 0 0',
            width: `${jobProgress?.progressPct ?? 0}%`,
            background: C.amber, transition: 'width 0.4s ease',
          }} />
        </div>
      )}

      {/* ── Loading state ── */}
      {loading && (
        <div style={{ padding: 40, textAlign: 'center', color: C.textMuted, fontSize: 13 }}>
          Loading backtest results…
        </div>
      )}

      {!loading && (
        <>
          {/* ── Metrics strip ── */}
          {(isRunning || hasResult) && (
            <div style={{
              display: 'flex', flexShrink: 0, overflowX: 'auto',
              borderBottom: `1px solid ${C.border}`,
            }}>
              <MetricBox
                label="Return"
                value={
                  isRunning && jobProgress
                    ? `${jobProgress.currentEquity >= scenario.capital ? '+' : ''}₹${Math.abs(Math.round(jobProgress.currentEquity - scenario.capital)).toLocaleString('en-IN')}`
                    : returnPct !== null
                      ? `${returnPct >= 0 ? '+' : ''}${returnPct.toFixed(1)}%`
                      : '—'
                }
                color={
                  isRunning && jobProgress
                    ? (jobProgress.currentEquity >= scenario.capital ? C.green : C.red)
                    : (returnPct ?? 0) >= 0 ? C.green : C.red
                }
              />
              <MetricBox
                label="Max DD"
                value={ddPct !== null ? `-${ddPct.toFixed(1)}%` : '—'}
                color={ddPct !== null && ddPct > 0 ? C.red : C.textMuted}
              />
              <MetricBox
                label="Sharpe"
                value={result?.sharpeRatio != null ? result.sharpeRatio.toFixed(2) : '—'}
              />
              <MetricBox
                label="Win Rate"
                value={result ? `${((result.winRate ?? 0) * 100).toFixed(0)}%` : '—'}
              />
              <MetricBox
                label="Profit Factor"
                value={result?.profitFactor != null ? result.profitFactor.toFixed(2) : '—'}
              />
              <MetricBox
                label="Trades"
                value={
                  isRunning && jobProgress
                    ? String(jobProgress.tradesSoFar)
                    : result ? String(result.totalTrades) : '—'
                }
              />
              {isRunning && jobProgress && (
                <MetricBox
                  label="Equity"
                  value={`₹${Math.round(jobProgress.currentEquity).toLocaleString('en-IN')}`}
                  color={jobProgress.currentEquity >= scenario.capital ? C.green : C.red}
                />
              )}
              {result?.expectancyPerTrade != null && (
                <MetricBox label="Expectancy" value={`₹${result.expectancyPerTrade.toFixed(0)}`} />
              )}
              {result?.calmarRatio != null && (
                <MetricBox label="Calmar" value={result.calmarRatio.toFixed(2)} />
              )}
            </div>
          )}

          {/* ── Tabs ── */}
          <div style={{ display: 'flex', borderBottom: `1px solid ${C.border}`, flexShrink: 0 }}>
            {(['overview', 'chart', 'trades'] as Tab[]).map(t => (
              <button
                key={t}
                onClick={() => setTab(t)}
                style={{
                  padding: '6px 14px', background: 'none', border: 'none',
                  borderBottom: `2px solid ${tab === t ? C.blue : 'transparent'}`,
                  cursor: 'pointer', fontSize: 12,
                  color: tab === t ? C.blue : C.textMuted, marginBottom: -1,
                }}
              >
                {t.charAt(0).toUpperCase() + t.slice(1)}
                {t === 'trades' && trades.length > 0 && (
                  <span style={{ marginLeft: 4, fontSize: 10, color: C.textDim }}>
                    ({trades.length})
                  </span>
                )}
              </button>
            ))}
          </div>

          {/* ── Tab content ── */}
          <div style={{ padding: '16px 14px' }}>
            {tab === 'overview' && (
              <OverviewTab
                result={result}
                scenario={scenario}
                equityCurve={equityCurve}
                isRunning={isRunning}
                jobProgress={jobProgress}
              />
            )}
            {tab === 'chart' && (
              <ChartTab chartBars={chartBars} isRunning={isRunning} />
            )}
            {tab === 'trades' && (
              <TradesTab trades={trades} initialCapital={scenario.capital} />
            )}
          </div>
        </>
      )}

      {/* ── No result and not running ── */}
      {!loading && !isRunning && !hasResult && (
        <div style={{
          padding: 40, display: 'flex', alignItems: 'center', justifyContent: 'center',
          flexDirection: 'column', gap: SP.sm,
        }}>
          <div style={{ fontSize: 13, color: C.textMuted }}>No results yet.</div>
          <div style={{ fontSize: 11, color: C.textDim }}>
            Run the backtest to see results here.
          </div>
        </div>
      )}
    </div>
  )
}

// ── MetricBox ─────────────────────────────────────────────────────────────────

function MetricBox({ label, value, color }: { label: string; value: string; color?: string }) {
  return (
    <div style={{
      padding: '8px 16px', borderRight: `1px solid ${C.border}`,
      display: 'flex', flexDirection: 'column', gap: 2, minWidth: 100, flexShrink: 0,
    }}>
      <span style={{
        fontSize: 9, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.05em',
      }}>
        {label}
      </span>
      <span style={{ fontSize: 16, fontFamily: F.mono, fontWeight: 700, color: color ?? C.text }}>
        {value}
      </span>
    </div>
  )
}

// ── Overview tab ──────────────────────────────────────────────────────────────

function OverviewTab({ result, scenario, equityCurve, isRunning, jobProgress }: {
  result: BacktestResult | null
  scenario: Scenario
  equityCurve: { label: string; equity: number }[]
  isRunning: boolean
  jobProgress: BacktestJobStatus | null
}) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: SP.xl }}>

      {/* Equity curve */}
      <div>
        <SectionLabel>Equity Curve</SectionLabel>
        {equityCurve.length >= 2 ? (
          <ResponsiveContainer width="100%" height={200}>
            <LineChart data={equityCurve} margin={{ top: 4, right: 20, bottom: 0, left: 0 }}>
              <CartesianGrid strokeDasharray="3 3" stroke={C.border2} />
              <XAxis
                dataKey="label"
                tick={{ fill: C.textMuted, fontSize: 9 }}
                tickLine={false}
                interval="preserveStartEnd"
              />
              <YAxis
                tick={{ fill: C.textMuted, fontSize: 9 }}
                tickLine={false}
                axisLine={false}
                tickFormatter={(v: number) => `₹${(v / 1000).toFixed(0)}K`}
                width={52}
              />
              <Tooltip
                contentStyle={{ background: C.surface2, border: `1px solid ${C.border}`, fontSize: 11 }}
                formatter={(v: number) => [`₹${v.toLocaleString('en-IN')}`, 'Equity']}
              />
              <ReferenceLine
                y={scenario.capital}
                stroke={C.textDim}
                strokeDasharray="4 2"
                strokeWidth={1}
              />
              <Line
                type="monotone"
                dataKey="equity"
                stroke={C.green}
                dot={false}
                strokeWidth={2}
                isAnimationActive={false}
              />
            </LineChart>
          </ResponsiveContainer>
        ) : (
          <Placeholder>
            {isRunning
              ? 'Equity curve appears after the first trade closes'
              : 'No completed trades to plot'}
          </Placeholder>
        )}
      </div>

      {/* Run-in-progress indicator */}
      {isRunning && jobProgress && (
        <div style={{
          display: 'flex', alignItems: 'center', gap: 12, padding: SP.md,
          background: C.amber11, border: `1px solid ${C.amber44}`, borderRadius: 6,
          fontSize: 12, color: C.amber,
        }}>
          <span style={{ animation: 'pulse 1.2s ease-in-out infinite' }}>●</span>
          Backtest running
          {jobProgress.totalBars > 0 && (
            <span style={{ color: C.textMuted }}>
              — Bar {jobProgress.currentBar.toLocaleString()} / {jobProgress.totalBars.toLocaleString()}
            </span>
          )}
          {jobProgress.tradesSoFar > 0 && (
            <span style={{ color: C.textMuted }}>— {jobProgress.tradesSoFar} trades</span>
          )}
        </div>
      )}

      {/* Extended stats grid */}
      {result && (
        <div>
          <SectionLabel>Statistics</SectionLabel>
          <div style={{
            display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)',
            gap: 1, background: C.border, border: `1px solid ${C.border}`,
            borderRadius: 6, overflow: 'hidden',
          }}>
            {([
              ['Total Return', `${((result.totalReturn ?? 0) * 100).toFixed(2)}%`],
              ['Final Equity', `₹${Math.round(result.finalEquity ?? 0).toLocaleString('en-IN')}`],
              ['Net P&L',      `₹${Math.round(result.totalPnl ?? 0).toLocaleString('en-IN')}`],
              ['Sharpe',       (result.sharpeRatio ?? 0).toFixed(2)],
              ['Sortino',      result.sortinoRatio != null ? result.sortinoRatio.toFixed(2) : '—'],
              ['Calmar',       result.calmarRatio  != null ? result.calmarRatio.toFixed(2)  : '—'],
              ['Win Rate',     `${((result.winRate ?? 0) * 100).toFixed(1)}%`],
              ['Profit Factor',result.profitFactor != null ? result.profitFactor.toFixed(2) : '—'],
              ['Max DD',       `-${((result.maxDrawdown ?? 0) * 100).toFixed(1)}%`],
              ['Total Trades', String(result.totalTrades)],
              ['Wins',         result.winCount != null ? String(result.winCount) : '—'],
              ['Losses',       result.lossCount != null ? String(result.lossCount) : '—'],
              ['Avg Win',      result.avgWin  != null ? `₹${Math.round(result.avgWin).toLocaleString()}` : '—'],
              ['Avg Loss',     result.avgLoss != null ? `₹${Math.round(result.avgLoss).toLocaleString()}` : '—'],
              ['Expectancy',   result.expectancyPerTrade != null ? `₹${result.expectancyPerTrade.toFixed(0)}` : '—'],
            ] as [string, string][]).map(([lbl, val]) => (
              <div key={lbl} style={{ padding: '8px 12px', background: C.surface2 }}>
                <div style={{ fontSize: 9, color: C.textMuted, textTransform: 'uppercase', marginBottom: 2 }}>
                  {lbl}
                </div>
                <div style={{ fontSize: 13, fontFamily: F.mono, fontWeight: 600, color: C.text }}>
                  {val}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Monthly breakdown */}
      {(result?.monthlyBreakdown?.length ?? 0) > 0 && (
        <div>
          <SectionLabel>Monthly P&L</SectionLabel>
          <div style={{ overflowX: 'auto' }}>
            <table style={{ borderCollapse: 'collapse', fontSize: 11, width: '100%' }}>
              <thead>
                <tr style={{ background: C.surface2 }}>
                  {['Month', 'P&L', 'Trades', 'Win Rate'].map(h => (
                    <th key={h} style={{
                      padding: '5px 10px', textAlign: 'left', color: C.textMuted,
                      fontWeight: 600, borderBottom: `1px solid ${C.border}`,
                    }}>
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {result!.monthlyBreakdown!.map(m => (
                  <tr
                    key={`${m.year}-${m.month}`}
                    style={{ borderBottom: `1px solid ${C.border2}` }}
                  >
                    <td style={{ padding: '5px 10px', color: C.text }}>
                      {new Date(m.year, m.month - 1).toLocaleString('en-IN', { month: 'short' })} {m.year}
                    </td>
                    <td style={{
                      padding: '5px 10px', fontFamily: F.mono, textAlign: 'right',
                      color: m.pnl >= 0 ? C.green : C.red,
                    }}>
                      {m.pnl >= 0 ? '+' : ''}₹{Math.round(m.pnl).toLocaleString('en-IN')}
                    </td>
                    <td style={{ padding: '5px 10px', textAlign: 'right', color: C.text }}>
                      {m.trades}
                    </td>
                    <td style={{ padding: '5px 10px', textAlign: 'right', color: C.text }}>
                      {(m.winRate * 100).toFixed(0)}%
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  )
}

// ── Chart tab — SVG candlestick with indicator overlays ───────────────────────

const IND_COLORS = ['#60a5fa', '#f59e0b', '#a78bfa', '#34d399', '#fb923c', '#e879f9']

function CandlestickChart({ bars }: { bars: BacktestChartBar[] }) {
  // Aggregate to at most 400 visible candles so the SVG stays performant
  const MAX_BARS = 400
  const stride = Math.max(1, Math.ceil(bars.length / MAX_BARS))
  const visible: BacktestChartBar[] = []
  for (let i = 0; i < bars.length; i += stride) {
    const g = bars.slice(i, i + stride)
    if (g.length === 1) { visible.push(g[0]); continue }
    // Aggregate: OHLCV + carry last bar's indicators + first signal in group
    const sig = g.find(b => b.signal)
    visible.push({
      timeMs:     g[0].timeMs,
      open:       g[0].open,
      high:       Math.max(...g.map(b => b.high)),
      low:        Math.min(...g.map(b => b.low)),
      close:      g[g.length - 1].close,
      volume:     g.reduce((s, b) => s + b.volume, 0),
      signal:     sig?.signal ?? null,
      signalPrice:sig?.signalPrice,
      stopLoss:   g[g.length - 1].stopLoss,
      takeProfit: g[g.length - 1].takeProfit,
      indicators: g[g.length - 1].indicators,
    })
  }

  const W = 900, H = 300
  const PL = 60, PR = 12, PT = 10, PB = 28
  const chartW = W - PL - PR
  const chartH = H - PT - PB

  const prices = visible.flatMap(b => [b.high, b.low])
  const rawMin = Math.min(...prices), rawMax = Math.max(...prices)
  const pad    = (rawMax - rawMin) * 0.05 || 1
  const minP = rawMin - pad, maxP = rawMax + pad
  const pRange = maxP - minP

  const toY  = (p: number) => PT + chartH * (1 - (p - minP) / pRange)
  const barW = chartW / visible.length
  const toX  = (i: number) => PL + (i + 0.5) * barW
  const bodyW = Math.max(1, barW * 0.6)

  // Collect all unique indicator names present in any bar
  const indNames = [...new Set(visible.flatMap(b => Object.keys(b.indicators ?? {})))]

  // Y-axis ticks (5 evenly spaced)
  const yTicks = Array.from({ length: 5 }, (_, k) => minP + pRange * k / 4)

  const fmtIST = (ms: number) =>
    new Date(ms).toLocaleDateString('en-IN', {
      timeZone: 'Asia/Kolkata', day: '2-digit', month: 'short',
    })

  return (
    <div style={{ overflowX: 'auto' }}>
      <svg width={W} height={H} style={{ display: 'block', userSelect: 'none' }}>
        {/* Y-axis grid + labels */}
        {yTicks.map((t, k) => (
          <g key={k}>
            <line x1={PL} y1={toY(t)} x2={W - PR} y2={toY(t)}
              stroke={C.border2} strokeWidth={0.5} strokeDasharray="3 3" />
            <text x={PL - 4} y={toY(t) + 3} fill={C.textMuted} fontSize={8} textAnchor="end">
              {t >= 1000 ? `${(t / 1000).toFixed(1)}K` : t.toFixed(0)}
            </text>
          </g>
        ))}

        {/* Candle bodies + wicks */}
        {visible.map((b, i) => {
          const x        = toX(i)
          const isGreen  = b.close >= b.open
          const color    = isGreen ? '#00d07a' : '#ff4757'
          const yOpen    = toY(b.open)
          const yClose   = toY(b.close)
          const bodyTop  = Math.min(yOpen, yClose)
          const bodyH    = Math.max(1, Math.abs(yOpen - yClose))

          return (
            <g key={i}>
              {/* High–Low wick */}
              <line x1={x} y1={toY(b.high)} x2={x} y2={toY(b.low)}
                stroke={color} strokeWidth={Math.max(0.5, Math.min(1, barW * 0.15))} />
              {/* OHLC body */}
              <rect x={x - bodyW / 2} y={bodyTop} width={bodyW} height={bodyH}
                fill={color} opacity={0.82} />
              {/* SL level — thin red dashed line on bar when position is open */}
              {b.stopLoss != null && b.stopLoss > 0 && (
                <line x1={x - bodyW} y1={toY(b.stopLoss)} x2={x + bodyW} y2={toY(b.stopLoss)}
                  stroke="#ff4757" strokeWidth={0.8} strokeDasharray="2 2" opacity={0.6} />
              )}
              {/* TP level — thin green dashed line */}
              {b.takeProfit != null && b.takeProfit > 0 && (
                <line x1={x - bodyW} y1={toY(b.takeProfit)} x2={x + bodyW} y2={toY(b.takeProfit)}
                  stroke="#00d07a" strokeWidth={0.8} strokeDasharray="2 2" opacity={0.6} />
              )}
              {/* Entry signal ▲ — green triangle below candle low */}
              {b.signal === 'BUY' && (() => {
                const ty = toY(b.low) + 14
                return (
                  <polygon
                    points={`${x},${ty - 8} ${x - 5},${ty + 2} ${x + 5},${ty + 2}`}
                    fill="#00d07a" stroke="#003820" strokeWidth={0.5}
                  >
                    <title>Entry ▲ ₹{(b.signalPrice ?? b.close).toFixed(2)}</title>
                  </polygon>
                )
              })()}
              {/* Exit / SELL signal ▼ — red triangle above candle high */}
              {(b.signal === 'SELL' || b.signal === 'EXIT') && (() => {
                const ty = toY(b.high) - 14
                return (
                  <polygon
                    points={`${x},${ty + 8} ${x - 5},${ty - 2} ${x + 5},${ty - 2}`}
                    fill="#ff4757" stroke="#380000" strokeWidth={0.5}
                  >
                    <title>Exit ▼ ₹{(b.signalPrice ?? b.close).toFixed(2)}</title>
                  </polygon>
                )
              })()}
            </g>
          )
        })}

        {/* Indicator polylines — one per unique indicator name */}
        {indNames.map((name, ci) => {
          const pts = visible
            .map((b, i) => {
              const v = b.indicators?.[name]
              return v != null ? `${toX(i).toFixed(1)},${toY(v).toFixed(1)}` : null
            })
            .filter((p): p is string => p !== null)
          if (pts.length < 2) return null
          return (
            <polyline key={name} points={pts.join(' ')}
              fill="none" stroke={IND_COLORS[ci % IND_COLORS.length]}
              strokeWidth={1} opacity={0.75} strokeLinejoin="round" />
          )
        })}

        {/* X-axis date labels — sample ~8 evenly spaced */}
        {visible.map((b, i) => {
          const step = Math.max(1, Math.floor(visible.length / 8))
          if (i % step !== 0) return null
          return (
            <text key={i} x={toX(i)} y={H - 6} fill={C.textMuted} fontSize={8} textAnchor="middle">
              {fmtIST(b.timeMs)}
            </text>
          )
        })}
      </svg>

      {/* Indicator legend */}
      {indNames.length > 0 && (
        <div style={{ display: 'flex', gap: 14, flexWrap: 'wrap', marginTop: 6, fontSize: 10, color: C.textMuted }}>
          {indNames.map((name, ci) => (
            <span key={name} style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
              <span style={{
                width: 18, height: 2, display: 'inline-block', verticalAlign: 'middle',
                background: IND_COLORS[ci % IND_COLORS.length],
              }} />
              {name}
            </span>
          ))}
        </div>
      )}

      <div style={{ display: 'flex', gap: 16, marginTop: 6, fontSize: 10, color: C.textDim }}>
        <span style={{ color: '#00d07a' }}>▲ Entry</span>
        <span style={{ color: '#ff4757' }}>▼ Exit</span>
        <span style={{ color: '#ff475788' }}>— — SL</span>
        <span style={{ color: '#00d07a88' }}>— — TP</span>
      </div>
    </div>
  )
}

function ChartTab({ chartBars, isRunning }: {
  chartBars: BacktestChartBar[]
  isRunning: boolean
}) {
  if (!chartBars.length) {
    return (
      <Placeholder>
        {isRunning ? 'Waiting for chart data from the engine…' : 'No chart data available.'}
      </Placeholder>
    )
  }

  const entryCount = chartBars.filter(b => b.signal === 'BUY').length
  const exitCount  = chartBars.filter(b => b.signal === 'SELL' || b.signal === 'EXIT').length

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: SP.lg }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <SectionLabel style={{ margin: 0 }}>
          {isRunning ? 'Live — last 200 bars' : `${chartBars.length.toLocaleString()} bars (downsampled to ≤400)`}
        </SectionLabel>
        <div style={{ display: 'flex', gap: SP.md, fontSize: 10 }}>
          {entryCount > 0 && <span style={{ color: C.green }}>▲ {entryCount} entries</span>}
          {exitCount  > 0 && <span style={{ color: C.red   }}>▼ {exitCount} exits</span>}
        </div>
      </div>
      <CandlestickChart bars={chartBars} />
    </div>
  )
}

// ── Trades tab ────────────────────────────────────────────────────────────────

function TradesTab({ trades, initialCapital }: {
  trades: BacktestTradeResult[]
  initialCapital: number
}) {
  if (!trades.length) {
    return <Placeholder>No trades recorded.</Placeholder>
  }

  let runningEquity = initialCapital
  const rows = trades.map((t, i) => {
    runningEquity += t.netPnl
    return { ...t, equity: runningEquity, idx: i + 1 }
  })

  return (
    <div style={{ overflowX: 'auto' }}>
      <table style={{ borderCollapse: 'collapse', fontSize: 11, width: '100%', minWidth: 820 }}>
        <thead>
          <tr style={{ background: C.surface2 }}>
            {['#', 'Dir', 'Entry Date', 'Exit Date', 'Qty', 'Entry ₹', 'Exit ₹', 'Net P&L', 'Exit Reason', 'Equity', 'R'].map(h => (
              <th
                key={h}
                style={{
                  padding: '5px 8px',
                  textAlign: ['#', 'Dir'].includes(h) ? 'center' : 'right',
                  color: C.textMuted, fontWeight: 600,
                  borderBottom: `1px solid ${C.border}`, fontSize: 10,
                  whiteSpace: 'nowrap',
                }}
              >
                {h}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map(t => {
            const isWin = t.netPnl > 0
            return (
              <tr key={t.idx} style={{ borderBottom: `1px solid ${C.border2}` }}>
                <td style={{ padding: '4px 8px', textAlign: 'center', color: C.textDim, fontSize: 10 }}>
                  {t.idx}
                </td>
                <td style={{ padding: '4px 8px', textAlign: 'center' }}>
                  <span style={{
                    fontSize: 9, fontWeight: 700, padding: '1px 5px', borderRadius: 2,
                    background: t.direction === 'BUY' ? C.green18  : C.redBg,
                    color:      t.direction === 'BUY' ? C.green    : C.red,
                    border: `1px solid ${t.direction === 'BUY' ? C.green44 : C.red44}`,
                  }}>
                    {t.direction}
                  </span>
                </td>
                <td style={{ padding: '4px 8px', textAlign: 'right', color: C.textMuted, fontSize: 10 }}>
                  {new Date(t.entryTime).toLocaleDateString('en-IN', { month: 'short', day: 'numeric', year: '2-digit' })}
                </td>
                <td style={{ padding: '4px 8px', textAlign: 'right', color: C.textMuted, fontSize: 10 }}>
                  {new Date(t.exitTime).toLocaleDateString('en-IN', { month: 'short', day: 'numeric', year: '2-digit' })}
                </td>
                <td style={{ padding: '4px 8px', textAlign: 'right', fontFamily: F.mono, color: C.text }}>
                  {t.quantity}
                </td>
                <td style={{ padding: '4px 8px', textAlign: 'right', fontFamily: F.mono, color: C.text }}>
                  ₹{t.entryPrice.toFixed(2)}
                </td>
                <td style={{ padding: '4px 8px', textAlign: 'right', fontFamily: F.mono, color: C.text }}>
                  ₹{t.exitPrice.toFixed(2)}
                </td>
                <td style={{
                  padding: '4px 8px', textAlign: 'right', fontFamily: F.mono,
                  fontWeight: 700, color: isWin ? C.green : C.red,
                }}>
                  {t.netPnl >= 0 ? '+' : ''}₹{Math.round(t.netPnl).toLocaleString('en-IN')}
                </td>
                <td style={{ padding: '4px 8px', textAlign: 'right', color: C.textMuted, fontSize: 10 }}>
                  {t.exitReason}
                </td>
                <td style={{
                  padding: '4px 8px', textAlign: 'right', fontFamily: F.mono, fontSize: 10,
                  color: t.equity >= initialCapital ? C.green : C.red,
                }}>
                  ₹{Math.round(t.equity).toLocaleString('en-IN')}
                </td>
                <td style={{
                  padding: '4px 8px', textAlign: 'right', fontFamily: F.mono, fontSize: 10,
                  color: t.rMultiple != null
                    ? (t.rMultiple >= 0 ? C.green : C.red)
                    : C.textMuted,
                }}>
                  {t.rMultiple != null ? `${t.rMultiple.toFixed(1)}R` : '—'}
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function SectionLabel({ children, style }: { children: React.ReactNode; style?: React.CSSProperties }) {
  return (
    <div style={{
      fontSize: 11, fontWeight: 700, color: C.textMuted,
      textTransform: 'uppercase', letterSpacing: '0.05em',
      marginBottom: SP.sm, ...style,
    }}>
      {children}
    </div>
  )
}

function Placeholder({ children }: { children: React.ReactNode }) {
  return (
    <div style={{
      height: 160, display: 'flex', alignItems: 'center', justifyContent: 'center',
      background: C.surface2, borderRadius: 6, color: C.textDim, fontSize: 12,
    }}>
      {children}
    </div>
  )
}
