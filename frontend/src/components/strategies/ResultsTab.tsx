import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { backtestApi, BacktestTradeLeg, BacktestTradeResult, strategyDomainApi } from '../../api/client'
import { RunMode, TradeRecord } from '../../types/strategy'
import { C, F, SP, TABLE_CELL } from '../../styles/tokens'
import { useEnums } from '../../context/EnumsContext'

interface Props {
  strategyId: string
}

// ── MAE/MFE Scatter ───────────────────────────────────────────────────────────

function MAEMFEScatterChart({ trades }: { trades: TradeRecord[] }) {
  const W = 340, H = 220, PAD = 30
  if (trades.length === 0) return <EmptyChartState label="MAE/MFE Scatter" />

  const maxMAE = Math.max(...trades.map(t => Math.abs(t.mae)), 1)
  const maxMFE = Math.max(...trades.map(t => t.mfe), 1)

  function tx(mae: number) { return PAD + (Math.abs(mae) / maxMAE) * (W - PAD * 2) }
  function ty(mfe: number) { return H - PAD - (mfe / maxMFE) * (H - PAD * 2) }

  // Typical stop/target distances
  const stopPct = trades.filter(t => t.exitReason === 'StopHit').length > 0
    ? Math.abs(trades.filter(t => t.exitReason === 'StopHit')[0]?.mae ?? maxMAE * 0.5)
    : maxMAE * 0.5
  const targetPct = trades.filter(t => t.exitReason === 'TargetHit').length > 0
    ? trades.filter(t => t.exitReason === 'TargetHit')[0]?.mfe ?? maxMFE * 0.5
    : maxMFE * 0.5

  return (
    <div>
      <div style={{ fontSize: 11, color: C.textMuted, marginBottom: 6 }}>MAE / MFE Scatter</div>
      <svg width={W} height={H} style={{ overflow: 'visible' }}>
        {/* Axes */}
        <line x1={PAD} y1={PAD} x2={PAD} y2={H - PAD} stroke={C.border} strokeWidth={1} />
        <line x1={PAD} y1={H - PAD} x2={W - PAD} y2={H - PAD} stroke={C.border} strokeWidth={1} />
        {/* Labels */}
        <text x={W / 2} y={H - 4} fill={C.textMuted} fontSize={9} textAnchor="middle">MAE (adverse excursion)</text>
        <text x={10} y={H / 2} fill={C.textMuted} fontSize={9} textAnchor="middle" transform={`rotate(-90, 10, ${H / 2})`}>MFE (favourable)</text>
        {/* Stop reference line */}
        <line x1={tx(stopPct)} y1={PAD} x2={tx(stopPct)} y2={H - PAD} stroke={C.red} strokeWidth={1} strokeDasharray="4,3" opacity={0.5} />
        {/* Target reference line */}
        <line x1={PAD} y1={ty(targetPct)} x2={W - PAD} y2={ty(targetPct)} stroke={C.green} strokeWidth={1} strokeDasharray="4,3" opacity={0.5} />
        {/* Dots */}
        {trades.map((t, i) => (
          <circle
            key={i}
            cx={tx(t.mae)} cy={ty(t.mfe)}
            r={3}
            fill={t.pnlR >= 0 ? C.green : C.red}
            opacity={0.7}
          >
            <title>{`${t.direction} | P&L: ${t.pnlR.toFixed(2)}R | MAE: ${t.mae.toFixed(2)} | MFE: ${t.mfe.toFixed(2)}`}</title>
          </circle>
        ))}
      </svg>
    </div>
  )
}

// ── P&L Histogram ─────────────────────────────────────────────────────────────

function PnLHistogram({ trades }: { trades: TradeRecord[] }) {
  if (trades.length === 0) return <EmptyChartState label="P&L Histogram" />

  const BIN = 0.25
  const min = Math.floor(Math.min(...trades.map(t => t.pnlR)) / BIN) * BIN
  const max = Math.ceil(Math.max(...trades.map(t => t.pnlR)) / BIN) * BIN
  const bins: { center: number; count: number }[] = []
  for (let b = min; b <= max; b += BIN) {
    bins.push({
      center: b,
      count: trades.filter(t => t.pnlR >= b && t.pnlR < b + BIN).length,
    })
  }
  const maxCount = Math.max(...bins.map(b => b.count), 1)
  const W = 340, H = 160, PAD = 28

  return (
    <div>
      <div style={{ fontSize: 11, color: C.textMuted, marginBottom: 6 }}>P&L Distribution (R-multiples)</div>
      <svg width={W} height={H}>
        {bins.map((b, i) => {
          const barW = Math.max(2, (W - PAD * 2) / bins.length - 1)
          const barH = (b.count / maxCount) * (H - PAD - 10)
          const x = PAD + i * ((W - PAD * 2) / bins.length)
          const y = H - PAD - barH
          return (
            <rect
              key={i}
              x={x} y={y} width={barW} height={barH}
              fill={b.center >= 0 ? C.green : C.red}
              opacity={0.75}
            >
              <title>{`${b.center.toFixed(2)}R — ${b.center + BIN > 0 ? b.center.toFixed(2) + 'R' : (b.center + BIN).toFixed(2) + 'R'}: ${b.count} trades`}</title>
            </rect>
          )
        })}
        {/* Zero line */}
        {bins.findIndex(b => b.center >= 0) >= 0 && (() => {
          const zeroIdx = bins.findIndex(b => b.center >= 0)
          const x = PAD + zeroIdx * ((W - PAD * 2) / bins.length)
          return <line x1={x} y1={10} x2={x} y2={H - PAD} stroke={C.textMuted} strokeWidth={1} strokeDasharray="3,2" />
        })()}
        <line x1={PAD} y1={H - PAD} x2={W - PAD} y2={H - PAD} stroke={C.border} strokeWidth={1} />
        <text x={W / 2} y={H - 4} fill={C.textMuted} fontSize={9} textAnchor="middle">P&L in R-multiples (0.25R bins)</text>
      </svg>
    </div>
  )
}

// ── Time-of-Day Heatmap ───────────────────────────────────────────────────────

function TimeOfDayHeatmap({ trades }: { trades: TradeRecord[] }) {
  if (trades.length === 0) return <EmptyChartState label="Time of Day Heatmap" />

  const HOURS = ['9:00', '9:30', '10:00', '10:30', '11:00', '11:30', '12:00', '12:30', '13:00', '13:30', '14:00', '14:30', '15:00', '15:30']
  const DAYS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri']

  // Build grid: avg P&L per cell
  const grid: (number | null)[][] = DAYS.map(() => HOURS.map(() => null))
  const counts: number[][] = DAYS.map(() => HOURS.map(() => 0))
  const sums: number[][] = DAYS.map(() => HOURS.map(() => 0))

  trades.forEach(t => {
    const d = new Date(t.entryTime)
    const dow = d.getDay() - 1 // 0=Mon
    if (dow < 0 || dow > 4) return
    const hour = d.getHours()
    const halfHour = d.getMinutes() >= 30 ? 1 : 0
    const bucket = (hour - 9) * 2 + halfHour
    if (bucket < 0 || bucket >= HOURS.length) return
    sums[dow][bucket] += t.pnlR
    counts[dow][bucket]++
  })
  for (let r = 0; r < DAYS.length; r++) {
    for (let c = 0; c < HOURS.length; c++) {
      if (counts[r][c] > 0) grid[r][c] = sums[r][c] / counts[r][c]
    }
  }

  const allVals = grid.flat().filter((v): v is number => v !== null)
  const absMax = allVals.length > 0 ? Math.max(Math.abs(Math.min(...allVals)), Math.abs(Math.max(...allVals)), 0.01) : 1
  const CELL_W = 22, CELL_H = 18, LABEL_W = 28, LABEL_H = 16

  function cellColor(v: number | null): string {
    if (v === null) return C.surface2
    const ratio = v / absMax // -1 to 1
    if (ratio > 0) return `rgba(0, 208, 122, ${Math.min(0.9, ratio * 0.8 + 0.1)})`
    return `rgba(255, 71, 87, ${Math.min(0.9, -ratio * 0.8 + 0.1)})`
  }

  const totalW = LABEL_W + HOURS.length * CELL_W
  const totalH = LABEL_H + DAYS.length * CELL_H + 12

  return (
    <div>
      <div style={{ fontSize: 11, color: C.textMuted, marginBottom: 6 }}>Time of Day Heatmap (avg R)</div>
      <svg width={totalW} height={totalH}>
        {/* Hour labels */}
        {HOURS.map((h, c) => (
          <text key={c} x={LABEL_W + c * CELL_W + CELL_W / 2} y={LABEL_H - 2}
            fill={C.textMuted} fontSize={7} textAnchor="middle">{h}</text>
        ))}
        {/* Day labels + cells */}
        {DAYS.map((day, r) => (
          <g key={r}>
            <text x={LABEL_W - 3} y={LABEL_H + r * CELL_H + CELL_H / 2 + 3}
              fill={C.textMuted} fontSize={8} textAnchor="end">{day}</text>
            {HOURS.map((_, c) => (
              <rect
                key={c}
                x={LABEL_W + c * CELL_W} y={LABEL_H + r * CELL_H}
                width={CELL_W - 1} height={CELL_H - 1}
                fill={cellColor(grid[r][c])}
                rx={2}
              >
                {grid[r][c] !== null && (
                  <title>{`${day} ${HOURS[c]}: avg ${grid[r][c]!.toFixed(2)}R (${counts[r][c]} trades)`}</title>
                )}
              </rect>
            ))}
          </g>
        ))}
      </svg>
    </div>
  )
}

// ── Streak Analysis ───────────────────────────────────────────────────────────

function StreakAnalysis({ trades }: { trades: TradeRecord[] }) {
  if (trades.length === 0) return <EmptyChartState label="Streak Analysis" />

  let maxLoss = 0, maxWin = 0, curLoss = 0, curWin = 0

  for (const t of trades) {
    if (t.pnlR < 0) {
      curWin = 0; curLoss++
      if (curLoss > maxLoss) maxLoss = curLoss
    } else {
      curLoss = 0; curWin++
      if (curWin > maxWin) maxWin = curWin
    }
  }
  // Simple avg: total trades / approximate count (a crude estimate for display)
  const lossRate = trades.filter(t => t.pnlR < 0).length / trades.length
  const worstStreak95 = lossRate > 0 ? Math.ceil(Math.log(0.05) / Math.log(1 - lossRate)) : 0

  const statCardStyle: React.CSSProperties = {
    background: C.surface2, borderRadius: 6, padding: '10px 14px', flex: 1,
  }

  return (
    <div>
      <div style={{ fontSize: 11, color: C.textMuted, marginBottom: 8 }}>Streak Analysis</div>
      <div style={{ display: 'flex', gap: SP.sm, flexWrap: 'wrap' }}>
        <div style={statCardStyle}>
          <div style={{ fontSize: 10, color: C.textMuted, marginBottom: 4 }}>Consecutive Losses</div>
          <div style={{ display: 'flex', gap: SP.lg }}>
            <span><span style={{ fontSize: 18, fontFamily: F.mono, color: C.red }}>{maxLoss}</span><span style={{ fontSize: 10, color: C.textDim, marginLeft: 4 }}>max</span></span>
            <span><span style={{ fontSize: 14, fontFamily: F.mono, color: C.textSub }}>{worstStreak95}</span><span style={{ fontSize: 10, color: C.textDim, marginLeft: 4 }}>95% CI worst</span></span>
            <span><span style={{ fontSize: 14, fontFamily: F.mono, color: C.textSub }}>{curLoss}</span><span style={{ fontSize: 10, color: C.textDim, marginLeft: 4 }}>current</span></span>
          </div>
        </div>
        <div style={statCardStyle}>
          <div style={{ fontSize: 10, color: C.textMuted, marginBottom: 4 }}>Consecutive Wins</div>
          <div style={{ display: 'flex', gap: SP.lg }}>
            <span><span style={{ fontSize: 18, fontFamily: F.mono, color: C.green }}>{maxWin}</span><span style={{ fontSize: 10, color: C.textDim, marginLeft: 4 }}>max</span></span>
            <span><span style={{ fontSize: 14, fontFamily: F.mono, color: C.textSub }}>{curWin}</span><span style={{ fontSize: 10, color: C.textDim, marginLeft: 4 }}>current</span></span>
          </div>
        </div>
      </div>
    </div>
  )
}

// ── Exit Reason Donut ─────────────────────────────────────────────────────────

function ExitReasonDonut({ trades }: { trades: TradeRecord[] }) {
  if (trades.length === 0) return <EmptyChartState label="Exit Reason Donut" />

  const reasons: TradeRecord['exitReason'][] = ['StopHit', 'TargetHit', 'TrailingStop', 'SessionEnd', 'Manual']
  const colors = [C.red, C.green, C.amber, C.blue, C.textSub]
  const counts = reasons.map(r => trades.filter(t => t.exitReason === r).length)
  const total = counts.reduce((a, b) => a + b, 0)
  const R = 50, cx = 80, cy = 65

  let startAngle = -Math.PI / 2
  const slices = counts.map((count, i) => {
    const pct = total > 0 ? count / total : 0
    const angle = pct * 2 * Math.PI
    const endAngle = startAngle + angle
    const sa = startAngle, ea = endAngle
    startAngle = endAngle
    if (pct === 0) return null
    const x1 = cx + R * Math.cos(sa), y1 = cy + R * Math.sin(sa)
    const x2 = cx + R * Math.cos(ea), y2 = cy + R * Math.sin(ea)
    const large = angle > Math.PI ? 1 : 0
    // Label
    const midAngle = sa + angle / 2
    const lx = cx + (R + 16) * Math.cos(midAngle)
    const ly = cy + (R + 16) * Math.sin(midAngle)
    return { i, path: `M ${cx} ${cy} L ${x1} ${y1} A ${R} ${R} 0 ${large} 1 ${x2} ${y2} Z`, lx, ly, pct, count }
  })

  return (
    <div>
      <div style={{ fontSize: 11, color: C.textMuted, marginBottom: 6 }}>Exit Reason</div>
      <div style={{ display: 'flex', gap: SP.lg, alignItems: 'center', flexWrap: 'wrap' }}>
        <svg width={160} height={130}>
          {slices.map(s => s && (
            <g key={s.i}>
              <path d={s.path} fill={colors[s.i]} opacity={0.85}>
                <title>{reasons[s.i]}: {s.count} ({(s.pct * 100).toFixed(0)}%)</title>
              </path>
              {s.pct > 0.08 && (
                <text x={s.lx} y={s.ly} fill={C.text} fontSize={8} textAnchor="middle">{(s.pct * 100).toFixed(0)}%</text>
              )}
            </g>
          ))}
        </svg>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
          {reasons.map((r, i) => (
            <div key={r} style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 11 }}>
              <div style={{ width: 10, height: 10, borderRadius: 2, background: colors[i], flexShrink: 0 }} />
              <span style={{ color: C.textSub }}>{r}</span>
              <span style={{ fontFamily: F.mono, color: C.text, marginLeft: 4 }}>
                {counts[i]} ({total > 0 ? ((counts[i] / total) * 100).toFixed(0) : 0}%)
              </span>
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}

// ── Empty state ───────────────────────────────────────────────────────────────

function EmptyChartState({ label }: { label: string }) {
  return (
    <div style={{
      height: 100, display: 'flex', alignItems: 'center', justifyContent: 'center',
      background: C.surface2, borderRadius: 6, fontSize: 11, color: C.textDim,
    }}>
      {label} — select a run to load data
    </div>
  )
}

// ── Professional Trades Table ─────────────────────────────────────────────────

function TradesTable({ trades }: { trades: BacktestTradeResult[] }) {
  const [expandedIdx, setExpandedIdx] = useState<number | null>(null)
  const [sortKey, setSortKey]         = useState<keyof BacktestTradeResult>('entryTime')
  const [sortAsc, setSortAsc]         = useState(true)

  if (trades.length === 0) return null

  function toggleSort(key: keyof BacktestTradeResult) {
    if (sortKey === key) setSortAsc(a => !a)
    else { setSortKey(key); setSortAsc(true) }
  }

  const sorted = [...trades].sort((a, b) => {
    const va = a[sortKey] ?? 0, vb = b[sortKey] ?? 0
    return sortAsc
      ? (va < vb ? -1 : va > vb ? 1 : 0)
      : (va > vb ? -1 : va < vb ? 1 : 0)
  })

  const isSpread = (t: BacktestTradeResult) =>
    t.direction.includes('SPREAD') || t.direction.includes('CREDIT') || t.direction.includes('DEBIT')

  const dirColor = (d: string) =>
    d === 'BUY' || d === 'CREDIT-SPREAD' ? C.green : C.red

  const pnlColor = (v: number) => v >= 0 ? C.green : C.red

  const rColor = (r?: number) =>
    r == null ? C.textMuted : r >= 2 ? C.green : r >= 0 ? '#f0b429' : C.red

  const fmt = (v: number, dec = 2) => v.toLocaleString('en-IN', { maximumFractionDigits: dec, minimumFractionDigits: dec })
  const fmtDate = (iso: string) => new Date(iso).toLocaleString('en-IN', { timeZone: 'Asia/Kolkata', day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit', hour12: false })

  const SortTh = ({ label, k }: { label: string; k: keyof BacktestTradeResult }) => (
    <th
      onClick={() => toggleSort(k)}
      style={{ ...tradeTh, cursor: 'pointer', userSelect: 'none', whiteSpace: 'nowrap' }}
    >
      {label}{sortKey === k ? (sortAsc ? ' ▲' : ' ▼') : ''}
    </th>
  )

  return (
    <div>
      <div style={{ fontSize: 11, color: C.textMuted, marginBottom: 6 }}>
        Click column headers to sort · Click row to expand spread legs
      </div>
      <div style={{ overflowX: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 11 }}>
          <thead>
            <tr style={{ background: C.surface2 }}>
              <th style={tradeTh}>#</th>
              <SortTh label="Date (IST)" k="entryTime" />
              <SortTh label="Dir" k="direction" />
              <SortTh label="Qty" k="quantity" />
              <SortTh label="Entry ₹" k="entryPrice" />
              <SortTh label="Exit ₹" k="exitPrice" />
              <SortTh label="SL ₹" k="stopLoss" />
              <SortTh label="Target ₹" k="takeProfit" />
              <SortTh label="Bars" k="holdingBars" />
              <SortTh label="Brokerage ₹" k="totalCost" />
              <SortTh label="Slip ₹" k="slippageAmount" />
              <SortTh label="Gross ₹" k="grossPnl" />
              <SortTh label="Net ₹" k="netPnl" />
              <SortTh label="R" k="rMultiple" />
              <SortTh label="MAE" k="mae" />
              <SortTh label="MFE" k="mfe" />
              <th style={tradeTh}>Exit</th>
            </tr>
          </thead>
          <tbody>
            {sorted.map((t, idx) => {
              const legs: BacktestTradeLeg[] = t.legsJson
                ? (() => { try { return JSON.parse(t.legsJson) } catch { return [] } })()
                : []
              const hasLegs = legs.length > 0 && isSpread(t)
              const isExpanded = expandedIdx === idx

              return (
                <React.Fragment key={idx}>
                  <tr
                    onClick={() => hasLegs ? setExpandedIdx(isExpanded ? null : idx) : undefined}
                    style={{
                      borderBottom: `1px solid ${C.border2}`,
                      background: isExpanded ? C.blue0a : 'transparent',
                      cursor: hasLegs ? 'pointer' : 'default',
                    }}
                  >
                    <td style={{ ...tradeTd, color: C.textDim }}>{idx + 1}</td>
                    <td style={{ ...tradeTd, color: C.textSub, whiteSpace: 'nowrap' }}>
                      {fmtDate(t.entryTime)}
                    </td>
                    <td style={{ ...tradeTd, fontWeight: 600, color: dirColor(t.direction) }}>
                      {hasLegs ? `${isExpanded ? '▼' : '▶'} ` : ''}
                      {t.direction.replace('-SPREAD', '')}
                    </td>
                    <td style={{ ...tradeTd, textAlign: 'right', fontFamily: F.mono }}>{t.quantity}</td>
                    <td style={{ ...tradeTd, textAlign: 'right', fontFamily: F.mono }}>{fmt(t.entryPrice)}</td>
                    <td style={{ ...tradeTd, textAlign: 'right', fontFamily: F.mono }}>{fmt(t.exitPrice)}</td>
                    <td style={{ ...tradeTd, textAlign: 'right', fontFamily: F.mono, color: C.red, opacity: 0.8 }}>
                      {t.stopLoss ? fmt(t.stopLoss) : '—'}
                    </td>
                    <td style={{ ...tradeTd, textAlign: 'right', fontFamily: F.mono, color: C.green, opacity: 0.8 }}>
                      {t.takeProfit ? fmt(t.takeProfit) : '—'}
                    </td>
                    <td style={{ ...tradeTd, textAlign: 'right', fontFamily: F.mono, color: C.textMuted }}>
                      {t.holdingBars ?? '—'}
                    </td>
                    <td style={{ ...tradeTd, textAlign: 'right', fontFamily: F.mono, color: C.amber }}>
                      {t.totalCost != null ? fmt(t.totalCost) : '—'}
                    </td>
                    <td style={{ ...tradeTd, textAlign: 'right', fontFamily: F.mono, color: C.textDim }}>
                      {t.slippageAmount != null && t.slippageAmount > 0 ? fmt(t.slippageAmount) : '—'}
                    </td>
                    <td style={{ ...tradeTd, textAlign: 'right', fontFamily: F.mono, color: pnlColor(t.grossPnl) }}>
                      {t.grossPnl >= 0 ? '+' : ''}{fmt(t.grossPnl)}
                    </td>
                    <td style={{ ...tradeTd, textAlign: 'right', fontFamily: F.mono, fontWeight: 700, color: pnlColor(t.netPnl) }}>
                      {t.netPnl >= 0 ? '+' : ''}{fmt(t.netPnl)}
                    </td>
                    <td style={{ ...tradeTd, textAlign: 'right', fontFamily: F.mono, color: rColor(t.rMultiple) }}>
                      {t.rMultiple != null ? `${t.rMultiple >= 0 ? '+' : ''}${fmt(t.rMultiple)}R` : '—'}
                    </td>
                    <td style={{ ...tradeTd, textAlign: 'right', fontFamily: F.mono, color: C.red, opacity: 0.7 }}>
                      {t.mae != null ? fmt(t.mae) : '—'}
                    </td>
                    <td style={{ ...tradeTd, textAlign: 'right', fontFamily: F.mono, color: C.green, opacity: 0.7 }}>
                      {t.mfe != null ? fmt(t.mfe) : '—'}
                    </td>
                    <td style={{ ...tradeTd, color: C.textMuted, fontSize: 10 }}>{t.exitReason}</td>
                  </tr>

                  {/* ── Per-leg expansion row for spreads ── */}
                  {isExpanded && hasLegs && (
                    <tr>
                      <td colSpan={17} style={{ padding: '0 0 8px 32px', background: C.blue06 }}>
                        <div style={{ fontSize: 10, color: C.textMuted, padding: '6px 0 4px' }}>
                          Leg Breakdown — {legs.length} legs
                        </div>
                        <table style={{ borderCollapse: 'collapse', fontSize: 10 }}>
                          <thead>
                            <tr>
                              {['Leg', 'Strike', 'Type', 'Dir', 'Premium ₹', 'Expiry', 'Brokerage ₹'].map(h => (
                                <th key={h} style={{ ...tradeTh, fontSize: 10, background: C.surface3 }}>{h}</th>
                              ))}
                            </tr>
                          </thead>
                          <tbody>
                            {legs.map((leg, li) => (
                              <tr key={li} style={{ borderBottom: `1px solid ${C.border2}` }}>
                                <td style={{ ...tradeTd, color: C.textDim }}>{li + 1}</td>
                                <td style={{ ...tradeTd, fontFamily: F.mono, fontWeight: 600 }}>{fmt(leg.strike, 0)}</td>
                                <td style={{ ...tradeTd, color: leg.type === 'CE' ? C.blue : C.amber, fontWeight: 600 }}>{leg.type}</td>
                                <td style={{ ...tradeTd, color: leg.direction === 'SELL' ? C.red : C.green, fontWeight: 600 }}>{leg.direction}</td>
                                <td style={{ ...tradeTd, textAlign: 'right', fontFamily: F.mono }}>{fmt(leg.premium)}</td>
                                <td style={{ ...tradeTd, color: C.textMuted }}>{leg.expiry}</td>
                                <td style={{ ...tradeTd, textAlign: 'right', fontFamily: F.mono, color: C.amber }}>{fmt(leg.brokerage)}</td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </td>
                    </tr>
                  )}
                </React.Fragment>
              )
            })}
          </tbody>
        </table>
      </div>
    </div>
  )
}

const tradeTh: React.CSSProperties = {
  padding: '5px 8px', textAlign: 'left', fontSize: 10, color: C.textMuted,
  fontWeight: 600, borderBottom: `1px solid ${C.border}`, background: C.surface2,
}
const tradeTd: React.CSSProperties = { padding: '4px 8px', fontSize: 11, color: C.text }

// ── Trade Analysis section ────────────────────────────────────────────────────

function TradeAnalysisSection({ strategyId: _strategyId, scenarioId, runId }: {
  strategyId: string; scenarioId: string; runId: string
}) {
  // All runs from listRuns now have real UUID IDs; fetch trades from the saved-run endpoint.
  const { data: realTrades, isLoading, error } = useQuery({
    queryKey: ['bt-trades-real', runId],
    queryFn: async () => {
      const r = await backtestApi.trades(runId)
      return r.data?.data ?? []
    },
    enabled: runId.length > 0,
  })

  if (error) {
    console.error('Failed to load trades for run', runId, error)
  }

  const btTrades: BacktestTradeResult[] = realTrades ?? []

  // Map real BacktestTradeResult → TradeRecord for MAE/MFE scatter, histogram, heatmap, etc.
  // Exit reason mapping: backend uses SCREAMING_SNAKE_CASE; TradeRecord uses PascalCase union.
  const toChartExitReason = (r: string): TradeRecord['exitReason'] => {
    if (r === 'STOP_LOSS')  return 'StopHit'
    if (r === 'TRAIL_STOP') return 'TrailingStop'
    if (r === 'TAKE_PROFIT') return 'TargetHit'
    if (r === 'END_OF_DATA' || r === 'SESSION_END') return 'SessionEnd'
    return 'Manual'
  }
  const chartTrades: TradeRecord[] = btTrades.map((t, i) => ({
    id:          `${i}`,
    scenarioId:  scenarioId,
    runId:       runId,
    entryTime:   t.entryTime,
    exitTime:    t.exitTime,
    direction:   t.direction === 'BUY' ? 'Long' : 'Short',
    entryPrice:  t.entryPrice,
    exitPrice:   t.exitPrice,
    stopPrice:   t.stopLoss    ?? 0,
    targetPrice: t.takeProfit  ?? 0,
    mae:         t.mae         ?? 0,
    mfe:         t.mfe         ?? 0,
    pnlR:        t.rMultiple   ?? 0,
    pnlAbsolute: t.netPnl,
    barsHeld:    t.holdingBars ?? 0,
    exitReason:  toChartExitReason(t.exitReason),
  }))

  if (isLoading) {
    return (
      <div style={{ padding: SP.lg, color: C.textMuted, fontSize: 12, textAlign: 'center' }}>
        Loading trade data…
      </div>
    )
  }

  return (
    <div style={{
      marginTop: SP.lg, borderTop: `1px solid ${C.border}`,
      paddingTop: SP.lg, display: 'flex', flexDirection: 'column', gap: SP.xl,
    }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <div style={{ fontSize: 12, fontWeight: 700, color: C.text }}>
          Trade Breakdown — {btTrades.length} trades
        </div>
        {btTrades.length > 0 && (
          <div style={{ display: 'flex', gap: 16, fontSize: 11 }}>
            <TradeStat label="Avg Brokerage" value={`₹${(btTrades.reduce((s, t) => s + (t.totalCost ?? 0), 0) / btTrades.length).toFixed(0)}`} />
            <TradeStat label="Total Slippage" value={`₹${btTrades.reduce((s, t) => s + (t.slippageAmount ?? 0), 0).toFixed(0)}`} />
            <TradeStat label="Total Cost" value={`₹${btTrades.reduce((s, t) => s + (t.totalCost ?? 0) + (t.slippageAmount ?? 0), 0).toFixed(0)}`} />
            <TradeStat label="Avg Hold" value={`${(btTrades.reduce((s, t) => s + (t.holdingBars ?? 0), 0) / btTrades.length).toFixed(1)} bars`} />
          </div>
        )}
      </div>

      {btTrades.length === 0 ? (
        <div style={{ color: C.textMuted, fontSize: 12, textAlign: 'center', padding: SP.lg }}>
          No trade data available for this run yet.
        </div>
      ) : (
        <>
          {/* Full trades table */}
          <TradesTable trades={btTrades} />

          {/* Charts section — rendered when trade data has MAE/MFE/R values */}
          {chartTrades.length > 0 && (
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: SP.xl, marginTop: SP.lg }}>
              <MAEMFEScatterChart trades={chartTrades} />
              <PnLHistogram trades={chartTrades} />
              <TimeOfDayHeatmap trades={chartTrades} />
              <ExitReasonDonut trades={chartTrades} />
              <div style={{ gridColumn: '1 / -1' }}>
                <StreakAnalysis trades={chartTrades} />
              </div>
            </div>
          )}
        </>
      )}
    </div>
  )
}

function TradeStat({ label, value }: { label: string; value: string }) {
  return (
    <div style={{ textAlign: 'right' }}>
      <div style={{ fontSize: 9, color: C.textDim }}>{label}</div>
      <div style={{ fontFamily: F.mono, color: C.text, fontSize: 11 }}>{value}</div>
    </div>
  )
}

// ── Main ResultsTab ───────────────────────────────────────────────────────────

export function ResultsTab({ strategyId }: Props) {
  const { enums } = useEnums()
  const runModeOptions = enums.runMode ?? []

  const [selectedScenarios, setSelectedScenarios] = useState<string[]>([])
  const [modeFilter, setModeFilter] = useState<RunMode | 'All'>('All')
  const [fromDate, setFromDate] = useState('')
  const [toDate, setToDate] = useState('')
  const [expandedRunId, setExpandedRunId] = useState<string | null>(null)

  const { data: scenarios = [] } = useQuery({
    queryKey: ['scenarios', strategyId],
    queryFn: () => strategyDomainApi.listScenarios(strategyId),
  })

  const { data: runs = [], isLoading, error, refetch } = useQuery({
    queryKey: ['runs', strategyId],
    queryFn: () => strategyDomainApi.listRuns(strategyId),
  })

  const toDateStr = (iso: string) => iso.slice(0, 10)

  const filtered = runs.filter(r => {
    if (selectedScenarios.length > 0 && !selectedScenarios.includes(r.scenarioId)) return false
    if (modeFilter !== 'All' && r.mode !== modeFilter) return false
    if (fromDate && toDateStr(r.dateRange.from) < fromDate) return false
    if (toDate && toDateStr(r.dateRange.to) > toDate) return false
    return true
  })

  function toggleScenario(id: string) {
    setSelectedScenarios(prev =>
      prev.includes(id) ? prev.filter(x => x !== id) : [...prev, id]
    )
  }

  return (
    <div>
      {/* Filters bar */}
      <div style={{
        display: 'flex', gap: SP.md, alignItems: 'center', flexWrap: 'wrap',
        marginBottom: SP.md, padding: SP.sm,
        background: C.surface2, borderRadius: 6,
      }}>
        <div>
          <label style={{ fontSize: 10, color: C.textMuted, display: 'block', marginBottom: 2 }}>Scenario</label>
          <div style={{ display: 'flex', gap: SP.xs, flexWrap: 'wrap' }}>
            {scenarios.map(s => (
              <button
                key={s.id}
                onClick={() => toggleScenario(s.id)}
                style={{
                  fontSize: 10, padding: '2px 8px', borderRadius: 3, cursor: 'pointer',
                  background: selectedScenarios.includes(s.id) ? C.blueBg : C.surface3,
                  color: selectedScenarios.includes(s.id) ? C.blue : C.textMuted,
                  border: `1px solid ${selectedScenarios.includes(s.id) ? C.blue66 : C.border}`,
                }}
              >
                {s.isBaseline && <span style={{ color: C.amber, marginRight: 4 }}>●</span>}
                {s.name}
              </button>
            ))}
          </div>
        </div>

        <div>
          <label style={{ fontSize: 10, color: C.textMuted, display: 'block', marginBottom: 2 }}>Mode</label>
          <div style={{ display: 'flex', gap: 4 }}>
            {(['All', ...runModeOptions.map(o => o.value)] as (RunMode | 'All')[]).map(m => (
              <button
                key={m}
                onClick={() => setModeFilter(m)}
                style={{
                  fontSize: 10, padding: '2px 8px', borderRadius: 3, cursor: 'pointer',
                  background: modeFilter === m ? C.blueBg : C.surface3,
                  color: modeFilter === m ? C.blue : C.textMuted,
                  border: `1px solid ${modeFilter === m ? C.blue66 : C.border}`,
                }}
              >
                {m === 'All' ? 'All' : runModeOptions.find(o => o.value === m)?.label ?? m}
              </button>
            ))}
          </div>
        </div>

        <div style={{ display: 'flex', gap: SP.sm, alignItems: 'flex-end' }}>
          <div>
            <label style={{ fontSize: 10, color: C.textMuted, display: 'block', marginBottom: 2 }}>From</label>
            <input type="date" value={fromDate} onChange={e => setFromDate(e.target.value)} style={filterInput} />
          </div>
          <div>
            <label style={{ fontSize: 10, color: C.textMuted, display: 'block', marginBottom: 2 }}>To</label>
            <input type="date" value={toDate} onChange={e => setToDate(e.target.value)} style={filterInput} />
          </div>
        </div>
      </div>

      {isLoading && <SkeletonRows />}

      {!isLoading && error && (
        <div style={{ color: C.red, fontSize: 12 }}>
          Failed to load results.{' '}
          <button onClick={refetch as () => void} style={{ color: C.blue, background: 'none', border: 'none', cursor: 'pointer', fontSize: 12 }}>Retry?</button>
        </div>
      )}

      {!isLoading && !error && filtered.length === 0 && (
        <div style={{ textAlign: 'center', padding: 40, color: C.textMuted, fontSize: 12 }}>
          No run results yet. Run a scenario to generate results.
        </div>
      )}

      {!isLoading && !error && filtered.length > 0 && (
        <div style={{ overflowX: 'auto' }}>
          <div style={{ fontSize: 11, color: C.textDim, marginBottom: 6 }}>
            Click a row to expand trade analysis
          </div>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
            <thead>
              <tr style={{ background: C.surface2 }}>
                {['', 'Scenario', 'Mode', 'Date Range', 'Return', 'Max DD', 'Sharpe', 'Win%', 'PF', 'Trades'].map(h => (
                  <th key={h} style={thStyle}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {filtered.map(r => {
                const scenarioName = scenarios.find(s => s.id === r.scenarioId)?.name ?? r.scenarioId
                const isExpanded = expandedRunId === r.id
                return (
                  <React.Fragment key={r.id}>
                    <tr
                      style={{
                        borderBottom: `1px solid ${C.border2}`,
                        cursor: 'pointer',
                        background: isExpanded ? C.blue08 : 'transparent',
                      }}
                      onClick={() => setExpandedRunId(isExpanded ? null : r.id)}
                    >
                      <td style={{ ...tdStyle, color: C.textMuted, width: 20 }}>
                        {isExpanded ? '▼' : '▶'}
                      </td>
                      <td style={tdStyle}>{scenarioName}</td>
                      <td style={{ ...tdStyle, color: r.mode === RunMode.ForwardTest ? C.blue : C.textSub }}>
                        {r.mode === RunMode.ForwardTest ? 'Fwd Test' : 'Backtest'}
                      </td>
                      <td style={{ ...tdStyle, color: C.textMuted }}>
                        {r.dateRange.from.slice(0, 10)} – {r.dateRange.to.slice(0, 10)}
                      </td>
                      <NumericCell value={r.metrics.returnPct} pct colored />
                      <NumericCell value={r.metrics.maxDrawdownPct} pct negative />
                      <NumericCell value={r.metrics.sharpe} decimals={2} />
                      <NumericCell value={r.metrics.winRate} pct />
                      <NumericCell value={r.metrics.profitFactor} decimals={2} />
                      <td style={{ ...tdStyle, fontFamily: F.mono, textAlign: 'right' }}>{r.metrics.tradeCount}</td>
                    </tr>
                    {isExpanded && (
                      <tr>
                        <td colSpan={10} style={{ padding: '0 16px 20px', background: C.blue04 }}>
                          <TradeAnalysisSection
                            strategyId={strategyId}
                            scenarioId={r.scenarioId}
                            runId={r.id}
                          />
                        </td>
                      </tr>
                    )}
                  </React.Fragment>
                )}
              )}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

// ── Helpers ───────────────────────────────────────────────────────────────────

import React from 'react'

function NumericCell({ value, pct, colored, negative, decimals = 1 }: {
  value: number; pct?: boolean; colored?: boolean; negative?: boolean; decimals?: number
}) {
  const color = negative ? C.red : (colored ? (value >= 0 ? C.green : C.red) : C.text)
  const formatted = pct
    ? `${value >= 0 && !negative ? '+' : ''}${value.toFixed(decimals)}%`
    : value.toFixed(decimals)
  return (
    <td style={{ ...tdStyle, fontFamily: F.mono, textAlign: 'right', color }}>{formatted}</td>
  )
}

function SkeletonRows() {
  return (
    <div style={{ display: 'flex', flexDirection: 'column' }}>
      {[1, 2, 3].map(i => (
        <div
          key={i}
          style={{
            height: 32,
            background: C.surface2,
            borderRadius: 4,
            opacity: 0.5,
            marginBottom: i === 3 ? 0 : 6,
          }}
        />
      ))}
    </div>
  )
}

const filterInput: React.CSSProperties = {
  background: C.surface3, border: `1px solid ${C.border}`,
  color: C.text, borderRadius: 4, padding: '4px 6px', fontSize: 11,
}

const thStyle: React.CSSProperties = {
  padding: TABLE_CELL, textAlign: 'left', fontSize: 11, color: C.textMuted, fontWeight: 600,
  borderBottom: `1px solid ${C.border}`,
}

const tdStyle: React.CSSProperties = { padding: TABLE_CELL, fontSize: 12, color: C.text }

// SP.xl not in tokens — inline it
const SP_xl = '24px'
Object.assign(SP, { xl: SP_xl })
