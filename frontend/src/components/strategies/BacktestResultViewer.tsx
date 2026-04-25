import React, { useState, useEffect, useRef, useMemo } from 'react'
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
  liveChartBars: BacktestChartBar[]
  jobProgress: BacktestJobStatus | null
  activeJobId: string | null
  signalRConnected: boolean
  onBack: () => void
}

type Tab = 'overview' | 'chart' | 'trades'

// ── Column chooser types ──────────────────────────────────────────────────────

type ColumnKey =
  | 'dir' | 'entryDate' | 'exitDate' | 'qty'
  | 'entryPrice' | 'exitPrice'
  | 'buyValue' | 'buyBrokerage' | 'sellValue' | 'sellBrokerage'
  | 'grossPnl' | 'netPnl' | 'exitReason' | 'equity' | 'rMultiple'
  | 'holdingBars' | 'mae' | 'mfe'

interface ColDef { key: ColumnKey; label: string; defaultVisible: boolean }

const ALL_COLUMNS: ColDef[] = [
  { key: 'dir',          label: 'Dir',            defaultVisible: true  },
  { key: 'entryDate',    label: 'Entry Date',      defaultVisible: true  },
  { key: 'exitDate',     label: 'Exit Date',       defaultVisible: true  },
  { key: 'qty',          label: 'Qty',             defaultVisible: true  },
  { key: 'entryPrice',   label: 'Entry ₹',         defaultVisible: true  },
  { key: 'exitPrice',    label: 'Exit ₹',          defaultVisible: true  },
  { key: 'buyValue',     label: 'Buy Value',        defaultVisible: false },
  { key: 'buyBrokerage', label: 'Buy Brokerage',    defaultVisible: false },
  { key: 'sellValue',    label: 'Sell Value',       defaultVisible: false },
  { key: 'sellBrokerage',label: 'Sell Brokerage',   defaultVisible: false },
  { key: 'grossPnl',     label: 'Gross P&L',        defaultVisible: false },
  { key: 'netPnl',       label: 'Net P&L',          defaultVisible: true  },
  { key: 'exitReason',   label: 'Exit Reason',      defaultVisible: true  },
  { key: 'equity',       label: 'Equity',           defaultVisible: true  },
  { key: 'rMultiple',    label: 'R',                defaultVisible: true  },
  { key: 'holdingBars',  label: 'Bars Held',        defaultVisible: false },
  { key: 'mae',          label: 'MAE',              defaultVisible: false },
  { key: 'mfe',          label: 'MFE',              defaultVisible: false },
]

const DEFAULT_VISIBLE = new Set<ColumnKey>(
  ALL_COLUMNS.filter(c => c.defaultVisible).map(c => c.key)
)

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

  const result: BacktestResult | null = jobProgress?.result ?? loadedResult

  const chartBars: BacktestChartBar[] =
    (result?.chartSample?.length ?? 0) > 0 ? result!.chartSample! : liveChartBars

  const trades: BacktestTradeResult[] = result?.trades ?? []

  // Switch to chart tab when a run starts (live chart) or finishes (signal review)
  const prevIsRunning = useRef(isRunning)
  useEffect(() => {
    if (!prevIsRunning.current && isRunning) setTab('chart')          // run started
    if (prevIsRunning.current && !isRunning && result) setTab('chart') // run finished
    prevIsRunning.current = isRunning
  }, [isRunning, !!result]) // eslint-disable-line react-hooks/exhaustive-deps

  // Load result from API for already-Backtested scenarios
  useEffect(() => {
    if (isRunning || result || loading) return
    if (scenario.status !== ScenarioStatus.Backtested) return
    setLoading(true)
    backtestApi
      .byDefinition(strategy.id)
      .then(async resp => {
        const runs = resp.data?.data ?? []
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
    try { await backtestApi.cancel(activeJobId) }
    catch { setStopError('Cancel request failed — try again.') }
    finally { setStopping(false) }
  }

  const equityCurve = useMemo(() => {
    if (!trades.length) return []
    let equity = scenario.capital
    const pts: { label: string; equity: number }[] = [{ label: 'Start', equity }]
    for (const t of trades) {
      equity += t.netPnl
      pts.push({
        label: new Date(t.exitTime).toLocaleDateString('en-IN', { month: 'short', day: 'numeric' }),
        equity: Math.round(equity),
      })
    }
    return pts
  }, [trades, scenario.capital])

  const hasResult = !!result
  const returnPct = result ? (result.totalReturn ?? 0) * 100 : null
  const ddPct     = result ? (result.maxDrawdown ?? 0) * 100 : null

  return (
    <div style={{ display: 'flex', flexDirection: 'column', minHeight: 0 }}>

      {/* ── Header ── */}
      <div style={{
        display: 'flex', alignItems: 'center', gap: 10, padding: '8px 14px',
        borderBottom: `1px solid ${C.border}`, flexWrap: 'wrap', flexShrink: 0,
      }}>
        <button
          onClick={onBack}
          style={{ background: 'none', border: 'none', color: C.textMuted, cursor: 'pointer', fontSize: 13, padding: '2px 4px' }}
        >←</button>
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
                : 'Starting…'}
            </span>
            {!signalRConnected && <span style={{ fontSize: 10, color: C.textDim }}>(polling)</span>}
            <button
              onClick={handleStop}
              disabled={stopping}
              style={{
                marginLeft: 'auto', background: C.redBg, color: C.red,
                border: `1px solid ${C.red44}`, borderRadius: 4,
                padding: '4px 14px', cursor: stopping ? 'not-allowed' : 'pointer',
                fontSize: 11, fontWeight: 700, opacity: stopping ? 0.5 : 1,
              }}
            >{stopping ? 'Stopping…' : '⏹ Stop'}</button>
          </>
        )}
        {stopError && <span style={{ fontSize: 11, color: C.red }}>{stopError}</span>}
      </div>

      {/* ── Progress bar ── */}
      {isRunning && (
        <div style={{ position: 'relative', height: 3, background: C.surface3, flexShrink: 0 }}>
          <div style={{
            position: 'absolute', inset: '0 auto 0 0',
            width: `${jobProgress?.progressPct ?? 0}%`,
            background: C.amber, transition: 'width 0.4s ease',
          }} />
        </div>
      )}

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
                    : returnPct !== null ? `${returnPct >= 0 ? '+' : ''}${returnPct.toFixed(1)}%` : '—'
                }
                color={
                  isRunning && jobProgress
                    ? (jobProgress.currentEquity >= scenario.capital ? C.green : C.red)
                    : (returnPct ?? 0) >= 0 ? C.green : C.red
                }
              />
              <MetricBox label="Max DD" value={ddPct !== null ? `-${ddPct.toFixed(1)}%` : '—'}
                color={ddPct !== null && ddPct > 0 ? C.red : C.textMuted} />
              <MetricBox label="Sharpe"  value={result?.sharpeRatio != null ? result.sharpeRatio.toFixed(2) : '—'} />
              <MetricBox label="Win Rate" value={result ? `${((result.winRate ?? 0) * 100).toFixed(0)}%` : '—'} />
              <MetricBox label="PF"       value={result?.profitFactor != null ? result.profitFactor.toFixed(2) : '—'} />
              <MetricBox
                label="Trades"
                value={isRunning && jobProgress ? String(jobProgress.tradesSoFar) : result ? String(result.totalTrades) : '—'}
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
                {t === 'trades' && (
                  <span style={{ marginLeft: 4, fontSize: 10, color: C.textDim }}>
                    ({isRunning && jobProgress ? jobProgress.tradesSoFar : trades.length})
                  </span>
                )}
                {t === 'chart' && isRunning && (
                  <span style={{ marginLeft: 4, fontSize: 9, color: C.amber }}>● live</span>
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
              <TradesTab
                trades={trades}
                initialCapital={scenario.capital}
                isRunning={isRunning}
                tradesSoFar={jobProgress?.tradesSoFar}
              />
            )}
          </div>
        </>
      )}

      {!loading && !isRunning && !hasResult && (
        <div style={{
          padding: 40, display: 'flex', alignItems: 'center', justifyContent: 'center',
          flexDirection: 'column', gap: SP.sm,
        }}>
          <div style={{ fontSize: 13, color: C.textMuted }}>No results yet.</div>
          <div style={{ fontSize: 11, color: C.textDim }}>Run the backtest to see results here.</div>
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
      <span style={{ fontSize: 9, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
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

      <div>
        <SectionLabel>Equity Curve</SectionLabel>
        {equityCurve.length >= 2 ? (
          <ResponsiveContainer width="100%" height={200}>
            <LineChart data={equityCurve} margin={{ top: 4, right: 20, bottom: 0, left: 0 }}>
              <CartesianGrid strokeDasharray="3 3" stroke={C.border2} />
              <XAxis dataKey="label" tick={{ fill: C.textMuted, fontSize: 9 }} tickLine={false} interval="preserveStartEnd" />
              <YAxis tick={{ fill: C.textMuted, fontSize: 9 }} tickLine={false} axisLine={false}
                tickFormatter={(v: number) => `₹${(v / 1000).toFixed(0)}K`} width={52} />
              <Tooltip
                contentStyle={{ background: C.surface2, border: `1px solid ${C.border}`, fontSize: 11 }}
                formatter={(v: number) => [`₹${v.toLocaleString('en-IN')}`, 'Equity']}
              />
              <ReferenceLine y={scenario.capital} stroke={C.textDim} strokeDasharray="4 2" strokeWidth={1} />
              <Line type="monotone" dataKey="equity" stroke={C.green} dot={false} strokeWidth={2} isAnimationActive={false} />
            </LineChart>
          </ResponsiveContainer>
        ) : (
          <Placeholder>
            {isRunning
              ? 'Equity curve available after first trade closes'
              : 'No completed trades to plot'}
          </Placeholder>
        )}
      </div>

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

      {result && (
        <div>
          <SectionLabel>Statistics</SectionLabel>
          <div style={{
            display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)',
            gap: 1, background: C.border, border: `1px solid ${C.border}`,
            borderRadius: 6, overflow: 'hidden',
          }}>
            {([
              ['Total Return',   `${((result.totalReturn ?? 0) * 100).toFixed(2)}%`],
              ['Final Equity',   `₹${Math.round(result.finalEquity ?? 0).toLocaleString('en-IN')}`],
              ['Net P&L',        `₹${Math.round(result.totalPnl ?? 0).toLocaleString('en-IN')}`],
              ['Sharpe',         (result.sharpeRatio ?? 0).toFixed(2)],
              ['Sortino',        result.sortinoRatio != null ? result.sortinoRatio.toFixed(2) : '—'],
              ['Calmar',         result.calmarRatio  != null ? result.calmarRatio.toFixed(2)  : '—'],
              ['Win Rate',       `${((result.winRate ?? 0) * 100).toFixed(1)}%`],
              ['Profit Factor',  result.profitFactor != null ? result.profitFactor.toFixed(2) : '—'],
              ['Max DD',         `-${((result.maxDrawdown ?? 0) * 100).toFixed(1)}%`],
              ['Total Trades',   String(result.totalTrades)],
              ['Wins',           result.winCount != null ? String(result.winCount) : '—'],
              ['Losses',         result.lossCount != null ? String(result.lossCount) : '—'],
              ['Avg Win',        result.avgWin  != null ? `₹${Math.round(result.avgWin).toLocaleString()}` : '—'],
              ['Avg Loss',       result.avgLoss != null ? `₹${Math.round(result.avgLoss).toLocaleString()}` : '—'],
              ['Expectancy',     result.expectancyPerTrade != null ? `₹${result.expectancyPerTrade.toFixed(0)}` : '—'],
            ] as [string, string][]).map(([lbl, val]) => (
              <div key={lbl} style={{ padding: '8px 12px', background: C.surface2 }}>
                <div style={{ fontSize: 9, color: C.textMuted, textTransform: 'uppercase', marginBottom: 2 }}>{lbl}</div>
                <div style={{ fontSize: 13, fontFamily: F.mono, fontWeight: 600, color: C.text }}>{val}</div>
              </div>
            ))}
          </div>
        </div>
      )}

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
                    }}>{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {result!.monthlyBreakdown!.map(m => (
                  <tr key={`${m.year}-${m.month}`} style={{ borderBottom: `1px solid ${C.border2}` }}>
                    <td style={{ padding: '5px 10px', color: C.text }}>
                      {new Date(m.year, m.month - 1).toLocaleString('en-IN', { month: 'short' })} {m.year}
                    </td>
                    <td style={{ padding: '5px 10px', fontFamily: F.mono, textAlign: 'right', color: m.pnl >= 0 ? C.green : C.red }}>
                      {m.pnl >= 0 ? '+' : ''}₹{Math.round(m.pnl).toLocaleString('en-IN')}
                    </td>
                    <td style={{ padding: '5px 10px', textAlign: 'right', color: C.text }}>{m.trades}</td>
                    <td style={{ padding: '5px 10px', textAlign: 'right', color: C.text }}>{(m.winRate * 100).toFixed(0)}%</td>
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

// ── Candlestick chart with zoom/pan ───────────────────────────────────────────

const IND_COLORS = ['#60a5fa', '#f59e0b', '#a78bfa', '#34d399', '#fb923c', '#e879f9']

function CandlestickChart({ bars, maximized, onToggleMaximize }: {
  bars: BacktestChartBar[]
  maximized?: boolean
  onToggleMaximize?: () => void
}) {
  const totalLen = bars.length
  const INIT_WINDOW = Math.min(totalLen, 300)

  const [viewStart, setViewStart] = useState(Math.max(0, totalLen - INIT_WINDOW))
  const [viewEnd,   setViewEnd]   = useState(Math.max(0, totalLen - 1))

  // Tracks whether bars were empty on the last render — used to detect a new run starting.
  // Using "went to zero" rather than firstBarMs because liveChartBars is a rolling window
  // where bars[0].timeMs changes on every update, which would snap the view back constantly.
  const hadZeroBars = useRef(totalLen === 0)
  const prevLen     = useRef(totalLen)
  useEffect(() => {
    if (totalLen === 0) {
      hadZeroBars.current = true
      prevLen.current = 0
      return
    }
    if (hadZeroBars.current) {
      // Bars transitioned 0 → N: new run started (or first load). Reset view.
      hadZeroBars.current = false
      setViewStart(Math.max(0, totalLen - INIT_WINDOW))
      setViewEnd(totalLen - 1)
      prevLen.current = totalLen
      return
    }
    // Normal incremental update: auto-scroll only if the view was pinned to the right edge
    const wasAtEnd = viewEnd >= prevLen.current - 1
    const added    = totalLen - prevLen.current
    prevLen.current = totalLen
    if (wasAtEnd && added > 0) {
      setViewEnd(totalLen - 1)
      setViewStart(vs => Math.max(0, vs + added))
    }
  }, [totalLen]) // eslint-disable-line react-hooks/exhaustive-deps

  const isDragging    = useRef(false)
  const dragStartX    = useRef(0)
  const dragStartVs   = useRef(0)
  const dragStartVe   = useRef(0)

  const W = maximized ? 1400 : 900
  const H = maximized ? 540  : 320
  const PL = 60, PR = 12, PT = 10, PB = 30
  const chartW = W - PL - PR

  // Clamp view to valid range
  const vs = Math.max(0, Math.min(viewStart, totalLen - 1))
  const ve = Math.max(vs, Math.min(viewEnd, totalLen - 1))

  // Aggregate visible slice to ≤400 candles for performance
  const MAX_VISIBLE = 400
  const sliceLen = ve - vs + 1
  const stride   = Math.max(1, Math.ceil(sliceLen / MAX_VISIBLE))

  const visible: BacktestChartBar[] = []
  for (let i = vs; i <= ve; i += stride) {
    const end = Math.min(i + stride - 1, ve)
    const g   = bars.slice(i, end + 1)
    if (g.length === 0) continue
    if (g.length === 1) { visible.push(g[0]); continue }
    const sig = g.find(b => b.signal)
    visible.push({
      timeMs:     g[0].timeMs,
      open:       g[0].open,
      high:       Math.max(...g.map(b => b.high)),
      low:        Math.min(...g.map(b => b.low)),
      close:      g[g.length - 1].close,
      volume:     g.reduce((s, b) => s + b.volume, 0),
      signal:     sig?.signal ?? null,
      signalPrice: sig?.signalPrice,
      stopLoss:   g[g.length - 1].stopLoss,
      takeProfit: g[g.length - 1].takeProfit,
      indicators: g[g.length - 1].indicators,
    })
  }

  const prices  = visible.flatMap(b => [b.high, b.low])
  const rawMin  = prices.length ? Math.min(...prices) : 0
  const rawMax  = prices.length ? Math.max(...prices) : 1
  const pad     = (rawMax - rawMin) * 0.05 || 1
  const minP    = rawMin - pad
  const maxP    = rawMax + pad
  const pRange  = maxP - minP

  const toY   = (p: number) => PT + (H - PT - PB) * (1 - (p - minP) / pRange)
  const barW  = chartW / Math.max(1, visible.length)
  const toX   = (i: number) => PL + (i + 0.5) * barW
  const bodyW = Math.max(1, barW * 0.6)

  const indNames = [...new Set(visible.flatMap(b => Object.keys(b.indicators ?? {})))]
  const yTicks   = Array.from({ length: 5 }, (_, k) => minP + pRange * k / 4)

  const fmtIST = (ms: number) =>
    new Date(ms).toLocaleDateString('en-IN', { timeZone: 'Asia/Kolkata', day: '2-digit', month: 'short' })

  // ── Event handlers ──────────────────────────────────────────────────────────

  function handleWheel(e: React.WheelEvent<SVGSVGElement>) {
    e.preventDefault()
    const rangeLen = ve - vs + 1
    const factor   = e.deltaY > 0 ? 1.25 : 0.8
    const newLen   = Math.round(Math.min(totalLen, Math.max(10, rangeLen * factor)))
    const svgRect  = e.currentTarget.getBoundingClientRect()
    const xFrac    = Math.max(0, Math.min(1, (e.clientX - svgRect.left - PL) / chartW))
    const anchor   = vs + Math.round(rangeLen * xFrac) // bar index under mouse
    let ns = Math.round(anchor - newLen * xFrac)
    let ne = ns + newLen - 1
    if (ns < 0) { ne -= ns; ns = 0 }
    if (ne >= totalLen) { ns -= (ne - totalLen + 1); ne = totalLen - 1 }
    setViewStart(Math.max(0, ns))
    setViewEnd(Math.min(totalLen - 1, ne))
  }

  function handleMouseDown(e: React.MouseEvent<SVGSVGElement>) {
    if (e.button !== 0) return
    isDragging.current  = true
    dragStartX.current  = e.clientX
    dragStartVs.current = vs
    dragStartVe.current = ve
    e.preventDefault()
  }

  function handleMouseMove(e: React.MouseEvent<SVGSVGElement>) {
    if (!isDragging.current) return
    const rangeLen   = dragStartVe.current - dragStartVs.current + 1
    const barsPerPx  = rangeLen / chartW
    const shift      = Math.round(-(e.clientX - dragStartX.current) * barsPerPx)
    let ns = dragStartVs.current + shift
    let ne = dragStartVe.current + shift
    if (ns < 0) { ne -= ns; ns = 0 }
    if (ne >= totalLen) { ns -= (ne - totalLen + 1); ne = totalLen - 1 }
    setViewStart(Math.max(0, ns))
    setViewEnd(Math.min(totalLen - 1, ne))
  }

  function stopDrag() { isDragging.current = false }

  function resetView() {
    setViewStart(Math.max(0, totalLen - INIT_WINDOW))
    setViewEnd(totalLen - 1)
  }

  function zoomBy(factor: number) {
    const rangeLen = ve - vs + 1
    const newLen   = Math.round(Math.min(totalLen, Math.max(10, rangeLen * factor)))
    const anchor   = Math.round((vs + ve) / 2)
    let ns = Math.round(anchor - newLen / 2)
    let ne = ns + newLen - 1
    if (ns < 0) { ne -= ns; ns = 0 }
    if (ne >= totalLen) { ns -= (ne - totalLen + 1); ne = totalLen - 1 }
    setViewStart(Math.max(0, ns))
    setViewEnd(Math.min(totalLen - 1, ne))
  }

  function panBy(bars: number) {
    let ns = vs + bars
    let ne = ve + bars
    if (ns < 0) { ne -= ns; ns = 0 }
    if (ne >= totalLen) { ns -= (ne - totalLen + 1); ne = totalLen - 1 }
    setViewStart(Math.max(0, ns))
    setViewEnd(Math.min(totalLen - 1, ne))
  }

  function handleKeyDown(e: React.KeyboardEvent) {
    const rangeLen = ve - vs + 1
    const step = Math.max(1, Math.round(rangeLen * 0.1))
    if (e.key === 'ArrowLeft')  { e.preventDefault(); panBy(-step) }
    if (e.key === 'ArrowRight') { e.preventDefault(); panBy(step)  }
    if (e.key === '+' || e.key === '=') { e.preventDefault(); zoomBy(0.8)  }
    if (e.key === '-')                  { e.preventDefault(); zoomBy(1.25) }
    if (e.key === 'r' || e.key === 'R') { e.preventDefault(); resetView()  }
  }

  if (totalLen === 0) {
    return <Placeholder>No bars to display.</Placeholder>
  }

  const btnStyle: React.CSSProperties = {
    background: C.surface2, border: `1px solid ${C.border}`, color: C.textMuted,
    cursor: 'pointer', fontSize: 13, padding: '2px 9px', borderRadius: 3,
    lineHeight: 1.4, fontWeight: 700,
  }

  return (
    <div onKeyDown={handleKeyDown} tabIndex={0} style={{ outline: 'none' }}>
      {/* Toolbar */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginBottom: 6 }}>
        <button onClick={() => zoomBy(0.8)}  style={btnStyle} title="Zoom in (+)">+</button>
        <button onClick={() => zoomBy(1.25)} style={btnStyle} title="Zoom out (−)">−</button>
        <button onClick={() => panBy(-(ve - vs + 1))} style={btnStyle} title="Pan left (←)">◀</button>
        <button onClick={() => panBy(ve - vs + 1)}    style={btnStyle} title="Pan right (→)">▶</button>
        <button onClick={resetView} style={{ ...btnStyle, fontWeight: 400, fontSize: 11 }} title="Reset view (R)">Reset</button>
        <span style={{ fontSize: 10, color: C.textMuted, marginLeft: 4 }}>
          {fmtIST(bars[vs]?.timeMs ?? 0)} – {fmtIST(bars[ve]?.timeMs ?? 0)}
          <span style={{ color: C.textDim }}> ({sliceLen.toLocaleString()} bars)</span>
        </span>
        {onToggleMaximize && (
          <button
            onClick={onToggleMaximize}
            style={{ ...btnStyle, marginLeft: 'auto', fontSize: 11, padding: '2px 10px' }}
            title={maximized ? 'Exit fullscreen' : 'Fullscreen'}
          >
            {maximized ? '⤡ Exit' : '⤢ Expand'}
          </button>
        )}
      </div>
      <div style={{ fontSize: 9, color: C.textDim, marginBottom: 4 }}>
        Scroll · Drag · Arrow keys · +/− to zoom · R to reset
      </div>

      <div style={{ overflowX: 'auto' }}>
        <svg
          width={W} height={H}
          style={{ display: 'block', userSelect: 'none', cursor: isDragging.current ? 'grabbing' : 'crosshair' }}
          onWheel={handleWheel}
          onMouseDown={handleMouseDown}
          onMouseMove={handleMouseMove}
          onMouseUp={stopDrag}
          onMouseLeave={stopDrag}
        >
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

          {/* Candles */}
          {visible.map((b, i) => {
            const x       = toX(i)
            const isGreen = b.close >= b.open
            const col     = isGreen ? '#00d07a' : '#ff4757'
            const yOpen   = toY(b.open)
            const yClose  = toY(b.close)
            const bodyTop = Math.min(yOpen, yClose)
            const bodyH   = Math.max(1, Math.abs(yOpen - yClose))
            return (
              <g key={i}>
                <line x1={x} y1={toY(b.high)} x2={x} y2={toY(b.low)}
                  stroke={col} strokeWidth={Math.max(0.5, Math.min(1, barW * 0.15))} />
                <rect x={x - bodyW / 2} y={bodyTop} width={bodyW} height={bodyH}
                  fill={col} opacity={0.82} />
                {b.stopLoss != null && b.stopLoss > 0 && (
                  <line x1={x - bodyW} y1={toY(b.stopLoss)} x2={x + bodyW} y2={toY(b.stopLoss)}
                    stroke="#ff4757" strokeWidth={0.8} strokeDasharray="2 2" opacity={0.6} />
                )}
                {b.takeProfit != null && b.takeProfit > 0 && (
                  <line x1={x - bodyW} y1={toY(b.takeProfit)} x2={x + bodyW} y2={toY(b.takeProfit)}
                    stroke="#00d07a" strokeWidth={0.8} strokeDasharray="2 2" opacity={0.6} />
                )}
                {b.signal === 'BUY' && (() => {
                  const ty = toY(b.low) + 14
                  return (
                    <polygon points={`${x},${ty - 8} ${x - 5},${ty + 2} ${x + 5},${ty + 2}`}
                      fill="#00d07a" stroke="#003820" strokeWidth={0.5}>
                      <title>Entry ▲ ₹{(b.signalPrice ?? b.close).toFixed(2)}</title>
                    </polygon>
                  )
                })()}
                {(b.signal === 'SELL' || b.signal === 'EXIT') && (() => {
                  const ty = toY(b.high) - 14
                  return (
                    <polygon points={`${x},${ty + 8} ${x - 5},${ty - 2} ${x + 5},${ty - 2}`}
                      fill="#ff4757" stroke="#380000" strokeWidth={0.5}>
                      <title>Exit ▼ ₹{(b.signalPrice ?? b.close).toFixed(2)}</title>
                    </polygon>
                  )
                })()}
              </g>
            )
          })}

          {/* Indicator lines */}
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

          {/* X-axis date labels */}
          {visible.map((b, i) => {
            const step = Math.max(1, Math.floor(visible.length / 8))
            if (i % step !== 0) return null
            return (
              <text key={i} x={toX(i)} y={H - 8} fill={C.textMuted} fontSize={8} textAnchor="middle">
                {fmtIST(b.timeMs)}
              </text>
            )
          })}
        </svg>
      </div>

      {/* Legend + hints */}
      <div style={{ display: 'flex', gap: 16, marginTop: 6, flexWrap: 'wrap', alignItems: 'center' }}>
        {indNames.length > 0 && indNames.map((name, ci) => (
          <span key={name} style={{ display: 'flex', alignItems: 'center', gap: 4, fontSize: 10, color: C.textMuted }}>
            <span style={{ width: 18, height: 2, display: 'inline-block', background: IND_COLORS[ci % IND_COLORS.length] }} />
            {name}
          </span>
        ))}
        <span style={{ marginLeft: 'auto', display: 'flex', gap: 16, fontSize: 10, color: C.textDim }}>
          <span style={{ color: '#00d07a' }}>▲ Entry</span>
          <span style={{ color: '#ff4757' }}>▼ Exit</span>
          <span style={{ color: '#ff475788' }}>— — SL</span>
          <span style={{ color: '#00d07a88' }}>— — TP</span>
        </span>
      </div>
    </div>
  )
}

function ChartTab({ chartBars, isRunning }: { chartBars: BacktestChartBar[]; isRunning: boolean }) {
  const [maximized, setMaximized] = useState(false)

  if (!chartBars.length) {
    return (
      <Placeholder>
        {isRunning ? 'Waiting for first chart batch from the engine…' : 'No chart data available.'}
      </Placeholder>
    )
  }

  const entryCount = chartBars.filter(b => b.signal === 'BUY').length
  const exitCount  = chartBars.filter(b => b.signal === 'SELL' || b.signal === 'EXIT').length

  const chart = (
    <CandlestickChart
      bars={chartBars}
      maximized={maximized}
      onToggleMaximize={() => setMaximized(m => !m)}
    />
  )

  return (
    <>
      {maximized && (
        <div style={{
          position: 'fixed', inset: 0, zIndex: 500,
          background: 'rgba(0,0,0,0.85)',
          display: 'flex', alignItems: 'center', justifyContent: 'center',
        }}
          onClick={e => { if (e.target === e.currentTarget) setMaximized(false) }}
        >
          <div style={{
            background: C.surface, border: `1px solid ${C.border}`, borderRadius: 8,
            padding: '16px 20px', maxWidth: '98vw', overflow: 'auto',
          }}>
            <div style={{ display: 'flex', gap: SP.md, fontSize: 10, marginBottom: SP.sm }}>
              {entryCount > 0 && <span style={{ color: C.green }}>▲ {entryCount} entries</span>}
              {exitCount  > 0 && <span style={{ color: C.red   }}>▼ {exitCount} exits</span>}
            </div>
            {chart}
          </div>
        </div>
      )}

      <div style={{ display: 'flex', flexDirection: 'column', gap: SP.lg }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <SectionLabel style={{ margin: 0 }}>
            {isRunning ? 'Live — last 200 bars (auto-scrolling)' : `${chartBars.length.toLocaleString()} bars (downsampled ≤2000)`}
          </SectionLabel>
          <div style={{ display: 'flex', gap: SP.md, fontSize: 10 }}>
            {entryCount > 0 && <span style={{ color: C.green }}>▲ {entryCount} entries</span>}
            {exitCount  > 0 && <span style={{ color: C.red   }}>▼ {exitCount} exits</span>}
          </div>
        </div>
        {!maximized && chart}
      </div>
    </>
  )
}

// ── Trades tab with column chooser ────────────────────────────────────────────

function TradesTab({ trades, initialCapital, isRunning, tradesSoFar }: {
  trades: BacktestTradeResult[]
  initialCapital: number
  isRunning?: boolean
  tradesSoFar?: number
}) {
  const [visibleCols, setVisibleCols] = useState<Set<ColumnKey>>(DEFAULT_VISIBLE)
  const [chooserOpen, setChooserOpen] = useState(false)
  const chooserRef = useRef<HTMLDivElement>(null)

  // Close chooser on outside click
  useEffect(() => {
    function onOutside(e: MouseEvent) {
      if (chooserRef.current && !chooserRef.current.contains(e.target as Node))
        setChooserOpen(false)
    }
    if (chooserOpen) document.addEventListener('mousedown', onOutside)
    return () => document.removeEventListener('mousedown', onOutside)
  }, [chooserOpen])

  function toggleCol(key: ColumnKey, show: boolean) {
    setVisibleCols(prev => {
      const next = new Set(prev)
      show ? next.add(key) : next.delete(key)
      return next
    })
  }

  const show = (key: ColumnKey) => visibleCols.has(key)

  if (!trades.length) {
    return (
      <Placeholder>
        {isRunning
          ? tradesSoFar
            ? `${tradesSoFar} trade${tradesSoFar === 1 ? '' : 's'} completed so far — full list available after run finishes`
            : 'No trades yet — engine is warming up'
          : 'No trades recorded.'}
      </Placeholder>
    )
  }

  let runningEquity = initialCapital
  const rows = trades.map((t, i) => {
    runningEquity += t.netPnl
    return { ...t, equity: runningEquity, idx: i + 1 }
  })

  const totalBuyValue  = rows.reduce((s, t) => s + t.entryPrice * t.quantity, 0)
  const totalSellValue = rows.reduce((s, t) => s + t.exitPrice  * t.quantity, 0)
  const totalBuyBrok   = rows.reduce((s, t) => s + (t.entryCommission ?? 0), 0)
  const totalSellBrok  = rows.reduce((s, t) => s + (t.exitCommission  ?? 0), 0)

  const th = (label: string, right = true) => (
    <th style={{
      padding: '5px 8px', textAlign: right ? 'right' : 'center',
      color: C.textMuted, fontWeight: 600,
      borderBottom: `1px solid ${C.border}`, fontSize: 10, whiteSpace: 'nowrap',
    }}>{label}</th>
  )

  return (
    <div>
      {/* Toolbar */}
      <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 8, position: 'relative' }} ref={chooserRef}>
        {/* Ledger summary */}
        {(show('buyValue') || show('buyBrokerage') || show('sellValue') || show('sellBrokerage')) && (
          <div style={{ display: 'flex', gap: 16, fontSize: 11, color: C.textMuted, alignItems: 'center', marginRight: 'auto' }}>
            {show('buyValue')     && <span>Buy: <b style={{ fontFamily: F.mono, color: C.text }}>₹{Math.round(totalBuyValue).toLocaleString('en-IN')}</b></span>}
            {show('buyBrokerage') && <span>Buy Brok: <b style={{ fontFamily: F.mono, color: C.text }}>₹{Math.round(totalBuyBrok).toLocaleString('en-IN')}</b></span>}
            {show('sellValue')    && <span>Sell: <b style={{ fontFamily: F.mono, color: C.text }}>₹{Math.round(totalSellValue).toLocaleString('en-IN')}</b></span>}
            {show('sellBrokerage')&& <span>Sell Brok: <b style={{ fontFamily: F.mono, color: C.text }}>₹{Math.round(totalSellBrok).toLocaleString('en-IN')}</b></span>}
          </div>
        )}

        <button
          onClick={() => setChooserOpen(o => !o)}
          style={{
            background: C.surface2, border: `1px solid ${C.border}`,
            color: C.textMuted, cursor: 'pointer', fontSize: 11,
            padding: '4px 10px', borderRadius: 4,
          }}
        >⚙ Columns</button>

        {chooserOpen && (
          <div style={{
            position: 'absolute', right: 0, top: '110%', zIndex: 100,
            background: C.surface2, border: `1px solid ${C.border}`,
            borderRadius: 6, padding: '6px 0', minWidth: 180,
            boxShadow: '0 4px 16px rgba(0,0,0,0.4)',
          }}>
            {ALL_COLUMNS.map(col => (
              <label key={col.key} style={{
                display: 'flex', gap: 8, alignItems: 'center',
                padding: '4px 14px', cursor: 'pointer', fontSize: 12, color: C.text,
              }}>
                <input
                  type="checkbox"
                  checked={visibleCols.has(col.key)}
                  onChange={e => toggleCol(col.key, e.target.checked)}
                  style={{ accentColor: C.blue }}
                />
                {col.label}
              </label>
            ))}
          </div>
        )}
      </div>

      {/* Table */}
      <div style={{ overflowX: 'auto' }}>
        <table style={{ borderCollapse: 'collapse', fontSize: 11, width: '100%', minWidth: 600 }}>
          <thead>
            <tr style={{ background: C.surface2 }}>
              <th style={{ padding: '5px 8px', textAlign: 'center', color: C.textMuted, fontWeight: 600, borderBottom: `1px solid ${C.border}`, fontSize: 10 }}>#</th>
              {show('dir')          && th('Dir', false)}
              {show('entryDate')    && th('Entry Date')}
              {show('exitDate')     && th('Exit Date')}
              {show('qty')          && th('Qty')}
              {show('entryPrice')   && th('Entry ₹')}
              {show('exitPrice')    && th('Exit ₹')}
              {show('buyValue')     && th('Buy Value')}
              {show('buyBrokerage') && th('Buy Brok')}
              {show('sellValue')    && th('Sell Value')}
              {show('sellBrokerage')&& th('Sell Brok')}
              {show('grossPnl')     && th('Gross P&L')}
              {show('netPnl')       && th('Net P&L')}
              {show('exitReason')   && th('Reason')}
              {show('equity')       && th('Equity')}
              {show('rMultiple')    && th('R')}
              {show('holdingBars')  && th('Bars')}
              {show('mae')          && th('MAE')}
              {show('mfe')          && th('MFE')}
            </tr>
          </thead>
          <tbody>
            {rows.map(t => {
              const isWin    = t.netPnl > 0
              const buyVal   = t.entryPrice * t.quantity
              const sellVal  = t.exitPrice  * t.quantity
              return (
                <tr key={t.idx} style={{ borderBottom: `1px solid ${C.border2}` }}>
                  <td style={{ padding: '4px 8px', textAlign: 'center', color: C.textDim, fontSize: 10 }}>{t.idx}</td>

                  {show('dir') && (
                    <td style={{ padding: '4px 8px', textAlign: 'center' }}>
                      <span style={{
                        fontSize: 9, fontWeight: 700, padding: '1px 5px', borderRadius: 2,
                        background: t.direction === 'BUY' ? C.green18 : C.redBg,
                        color:      t.direction === 'BUY' ? C.green   : C.red,
                        border: `1px solid ${t.direction === 'BUY' ? C.green44 : C.red44}`,
                      }}>{t.direction}</span>
                    </td>
                  )}

                  {show('entryDate') && (
                    <td style={{ padding: '4px 8px', textAlign: 'right', color: C.textMuted, fontSize: 10 }}>
                      {new Date(t.entryTime).toLocaleDateString('en-IN', { month: 'short', day: 'numeric', year: '2-digit' })}
                    </td>
                  )}
                  {show('exitDate') && (
                    <td style={{ padding: '4px 8px', textAlign: 'right', color: C.textMuted, fontSize: 10 }}>
                      {new Date(t.exitTime).toLocaleDateString('en-IN', { month: 'short', day: 'numeric', year: '2-digit' })}
                    </td>
                  )}

                  {show('qty') && (
                    <td style={{ padding: '4px 8px', textAlign: 'right', fontFamily: F.mono, color: C.text }}>{t.quantity}</td>
                  )}
                  {show('entryPrice') && (
                    <td style={{ padding: '4px 8px', textAlign: 'right', fontFamily: F.mono, color: C.text }}>₹{t.entryPrice.toFixed(2)}</td>
                  )}
                  {show('exitPrice') && (
                    <td style={{ padding: '4px 8px', textAlign: 'right', fontFamily: F.mono, color: C.text }}>₹{t.exitPrice.toFixed(2)}</td>
                  )}

                  {show('buyValue') && (
                    <td style={{ padding: '4px 8px', textAlign: 'right', fontFamily: F.mono, color: C.text }}>
                      ₹{Math.round(buyVal).toLocaleString('en-IN')}
                    </td>
                  )}
                  {show('buyBrokerage') && (
                    <td style={{ padding: '4px 8px', textAlign: 'right', fontFamily: F.mono, color: C.textMuted }}>
                      {t.entryCommission != null ? `₹${t.entryCommission.toFixed(2)}` : '—'}
                    </td>
                  )}
                  {show('sellValue') && (
                    <td style={{ padding: '4px 8px', textAlign: 'right', fontFamily: F.mono, color: C.text }}>
                      ₹{Math.round(sellVal).toLocaleString('en-IN')}
                    </td>
                  )}
                  {show('sellBrokerage') && (
                    <td style={{ padding: '4px 8px', textAlign: 'right', fontFamily: F.mono, color: C.textMuted }}>
                      {t.exitCommission != null ? `₹${t.exitCommission.toFixed(2)}` : '—'}
                    </td>
                  )}

                  {show('grossPnl') && (
                    <td style={{ padding: '4px 8px', textAlign: 'right', fontFamily: F.mono, color: t.grossPnl >= 0 ? C.green : C.red }}>
                      {t.grossPnl >= 0 ? '+' : ''}₹{Math.round(t.grossPnl).toLocaleString('en-IN')}
                    </td>
                  )}
                  {show('netPnl') && (
                    <td style={{ padding: '4px 8px', textAlign: 'right', fontFamily: F.mono, fontWeight: 700, color: isWin ? C.green : C.red }}>
                      {t.netPnl >= 0 ? '+' : ''}₹{Math.round(t.netPnl).toLocaleString('en-IN')}
                    </td>
                  )}

                  {show('exitReason') && (
                    <td style={{ padding: '4px 8px', textAlign: 'right', color: C.textMuted, fontSize: 10 }}>{t.exitReason}</td>
                  )}
                  {show('equity') && (
                    <td style={{ padding: '4px 8px', textAlign: 'right', fontFamily: F.mono, fontSize: 10, color: t.equity >= initialCapital ? C.green : C.red }}>
                      ₹{Math.round(t.equity).toLocaleString('en-IN')}
                    </td>
                  )}
                  {show('rMultiple') && (
                    <td style={{ padding: '4px 8px', textAlign: 'right', fontFamily: F.mono, fontSize: 10, color: (t.rMultiple ?? 0) >= 0 ? C.green : C.red }}>
                      {t.rMultiple != null ? `${t.rMultiple.toFixed(1)}R` : '—'}
                    </td>
                  )}
                  {show('holdingBars') && (
                    <td style={{ padding: '4px 8px', textAlign: 'right', fontFamily: F.mono, fontSize: 10, color: C.textMuted }}>
                      {t.holdingBars ?? '—'}
                    </td>
                  )}
                  {show('mae') && (
                    <td style={{ padding: '4px 8px', textAlign: 'right', fontFamily: F.mono, fontSize: 10, color: C.red }}>
                      {t.mae != null ? t.mae.toFixed(2) : '—'}
                    </td>
                  )}
                  {show('mfe') && (
                    <td style={{ padding: '4px 8px', textAlign: 'right', fontFamily: F.mono, fontSize: 10, color: C.green }}>
                      {t.mfe != null ? t.mfe.toFixed(2) : '—'}
                    </td>
                  )}
                </tr>
              )
            })}
          </tbody>

          {/* Totals footer */}
          <tfoot>
            <tr style={{ borderTop: `2px solid ${C.border}`, background: C.surface2 }}>
              <td colSpan={
                1 /* # */ +
                (show('dir') ? 1 : 0) + (show('entryDate') ? 1 : 0) + (show('exitDate') ? 1 : 0) +
                (show('qty') ? 1 : 0) + (show('entryPrice') ? 1 : 0) + (show('exitPrice') ? 1 : 0)
              } style={{ padding: '5px 8px', fontSize: 10, color: C.textMuted, fontWeight: 600 }}>
                Total ({rows.length} trades)
              </td>
              {show('buyValue') && (
                <td style={{ padding: '5px 8px', textAlign: 'right', fontFamily: F.mono, fontSize: 11, fontWeight: 700, color: C.text }}>
                  ₹{Math.round(totalBuyValue).toLocaleString('en-IN')}
                </td>
              )}
              {show('buyBrokerage') && (
                <td style={{ padding: '5px 8px', textAlign: 'right', fontFamily: F.mono, fontSize: 11, fontWeight: 700, color: C.textMuted }}>
                  ₹{Math.round(totalBuyBrok).toLocaleString('en-IN')}
                </td>
              )}
              {show('sellValue') && (
                <td style={{ padding: '5px 8px', textAlign: 'right', fontFamily: F.mono, fontSize: 11, fontWeight: 700, color: C.text }}>
                  ₹{Math.round(totalSellValue).toLocaleString('en-IN')}
                </td>
              )}
              {show('sellBrokerage') && (
                <td style={{ padding: '5px 8px', textAlign: 'right', fontFamily: F.mono, fontSize: 11, fontWeight: 700, color: C.textMuted }}>
                  ₹{Math.round(totalSellBrok).toLocaleString('en-IN')}
                </td>
              )}
              {show('grossPnl') && (
                <td style={{ padding: '5px 8px', textAlign: 'right', fontFamily: F.mono, fontSize: 11, fontWeight: 700, color: rows.reduce((s, t) => s + t.grossPnl, 0) >= 0 ? C.green : C.red }}>
                  ₹{Math.round(rows.reduce((s, t) => s + t.grossPnl, 0)).toLocaleString('en-IN')}
                </td>
              )}
              {show('netPnl') && (
                <td style={{ padding: '5px 8px', textAlign: 'right', fontFamily: F.mono, fontSize: 11, fontWeight: 700, color: rows.reduce((s, t) => s + t.netPnl, 0) >= 0 ? C.green : C.red }}>
                  ₹{Math.round(rows.reduce((s, t) => s + t.netPnl, 0)).toLocaleString('en-IN')}
                </td>
              )}
              {show('exitReason') && <td />}
              {show('equity')     && <td />}
              {show('rMultiple')  && <td />}
              {show('holdingBars')&& <td />}
              {show('mae')        && <td />}
              {show('mfe')        && <td />}
            </tr>
          </tfoot>
        </table>
      </div>
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
