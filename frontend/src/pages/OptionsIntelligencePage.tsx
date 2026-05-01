import { useState, useCallback } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  BarChart, Bar, XAxis, YAxis, Tooltip, ReferenceLine,
  ResponsiveContainer, Cell,
} from 'recharts'
import {
  optionsApi, strategiesApi,
  OptionStrike, OptionsRuleSignal,
} from '../api/client'
import { C, F, SP } from '../styles/tokens'
import { isMarketHours } from '../utils/datetime'

// ── Helpers ───────────────────────────────────────────────────────────────────

function fmtOI(v: number): string {
  if (v >= 10_00_000) return `${(v / 10_00_000).toFixed(1)}L`
  if (v >= 1_000)     return `${(v / 1_000).toFixed(0)}K`
  return String(v)
}

function fmtPrice(v: number): string {
  return v > 0 ? v.toFixed(2) : '—'
}

function severityColor(s: 'info' | 'warn' | 'alert'): string {
  return s === 'alert' ? C.red : s === 'warn' ? C.amber : C.green
}

const CE_COLOR = '#f87171'  // red-400
const PE_COLOR = '#34d399'  // emerald-400
const CE_DIM   = '#7f1d1d44'
const PE_DIM   = '#064e3b44'

// ── KPI card ──────────────────────────────────────────────────────────────────

function KpiCard({
  label, value, color, sub,
}: { label: string; value: string; color?: string; sub?: string }) {
  return (
    <div style={{
      flex: 1, minWidth: 120,
      background: C.surface, border: `1px solid ${C.border}`,
      borderRadius: 6, padding: '10px 14px',
    }}>
      <div style={{ fontSize: 10, color: C.textMuted, marginBottom: 4, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
        {label}
      </div>
      <div style={{ fontSize: 20, fontWeight: 800, fontFamily: F.mono, color: color ?? C.text }}>
        {value}
      </div>
      {sub && <div style={{ fontSize: 10, color: C.textDim, marginTop: 2 }}>{sub}</div>}
    </div>
  )
}

// ── Bias banner ───────────────────────────────────────────────────────────────

function BiasBanner({ bias: _bias, score }: { bias: string; score: number }) {
  const isBull   = score > 20
  const isBear   = score < -20
  const bg       = isBull ? 'linear-gradient(90deg,#064e3b,#065f46)'
                 : isBear ? 'linear-gradient(90deg,#7f1d1d,#991b1b)'
                 : 'linear-gradient(90deg,#1c1917,#292524)'
  const label    = isBull ? 'BULLISH BIAS' : isBear ? 'BEARISH BIAS' : 'NEUTRAL'
  const arrow    = isBull ? '▲' : isBear ? '▼' : '◆'
  const barColor = isBull ? C.green : isBear ? C.red : C.amber
  const pct      = Math.abs(score)   // 0–100

  return (
    <div style={{ background: bg, borderRadius: 6, padding: '12px 16px', marginBottom: SP.md }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 8 }}>
        <span style={{ fontSize: 15, fontWeight: 800, color: barColor, letterSpacing: '0.06em' }}>
          {arrow} {label}
        </span>
        <span style={{ fontSize: 12, color: C.textMuted, fontFamily: F.mono }}>
          Score: {score > 0 ? '+' : ''}{score.toFixed(0)} / 100
        </span>
      </div>
      {/* Score bar */}
      <div style={{ height: 6, background: '#ffffff18', borderRadius: 3, overflow: 'hidden' }}>
        <div style={{
          height: '100%', width: `${pct}%`,
          background: barColor, borderRadius: 3,
          transition: 'width 0.4s ease',
        }} />
      </div>
    </div>
  )
}

// ── Rule signal row ───────────────────────────────────────────────────────────

function RuleRow({ sig }: { sig: OptionsRuleSignal }) {
  const dotColor = sig.triggered ? severityColor(sig.severity) : C.textDim
  return (
    <div style={{
      display: 'flex', alignItems: 'flex-start', gap: 10,
      padding: '7px 0', borderBottom: `1px solid ${C.border2}`,
      opacity: sig.triggered ? 1 : 0.5,
    }}>
      <span style={{ color: dotColor, fontSize: 10, marginTop: 2, flexShrink: 0 }}>●</span>
      <div style={{ flex: 1 }}>
        <div style={{ fontSize: 12, fontWeight: 600, color: C.text }}>{sig.ruleName}</div>
        <div style={{ fontSize: 11, color: C.textMuted, marginTop: 1 }}>{sig.reason}</div>
      </div>
      {sig.value !== undefined && (
        <span style={{
          fontSize: 10, fontFamily: F.mono, color: dotColor,
          background: `${dotColor}18`, border: `1px solid ${dotColor}44`,
          padding: '1px 6px', borderRadius: 3, flexShrink: 0,
        }}>
          {sig.value.toFixed(2)}
        </span>
      )}
    </div>
  )
}

// ── Option chain table ────────────────────────────────────────────────────────

function ChainTable({ strikes, maxCeOI, maxPeOI }: {
  strikes: OptionStrike[]
  maxCeOI: number
  maxPeOI: number
}) {
  const th = (align?: 'right' | 'left'): React.CSSProperties => ({
    padding: '5px 8px', fontSize: 10, color: C.textMuted,
    fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.04em',
    textAlign: align ?? 'right', whiteSpace: 'nowrap', borderBottom: `1px solid ${C.border}`,
  })
  const tdCe = (extra?: React.CSSProperties): React.CSSProperties => ({
    padding: '4px 8px', fontSize: 11, color: CE_COLOR, fontFamily: F.mono,
    textAlign: 'right', ...extra,
  })
  const tdPe = (extra?: React.CSSProperties): React.CSSProperties => ({
    padding: '4px 8px', fontSize: 11, color: PE_COLOR, fontFamily: F.mono,
    textAlign: 'right', ...extra,
  })
  const tdStrike = (atm: boolean): React.CSSProperties => ({
    padding: '4px 10px', fontSize: 12, fontWeight: atm ? 800 : 600,
    color: atm ? C.amber : C.text, fontFamily: F.mono,
    textAlign: 'center', background: atm ? `${C.amber}18` : undefined,
  })

  return (
    <div style={{ overflowX: 'auto', overflowY: 'auto', maxHeight: 400 }}>
      <table style={{ width: '100%', borderCollapse: 'collapse' }}>
        <thead>
          <tr style={{ background: C.surface2 }}>
            <th style={{ ...th(), color: CE_COLOR }}>CE ΔOI</th>
            <th style={{ ...th(), color: CE_COLOR }}>CE OI</th>
            <th style={{ ...th(), color: CE_COLOR }}>CE IV%</th>
            <th style={{ ...th(), color: CE_COLOR }}>CE LTP</th>
            <th style={{ ...th('left'), color: C.amber, textAlign: 'center' }}>STRIKE</th>
            <th style={{ ...th(), color: PE_COLOR }}>PE LTP</th>
            <th style={{ ...th(), color: PE_COLOR }}>PE IV%</th>
            <th style={{ ...th(), color: PE_COLOR }}>PE OI</th>
            <th style={{ ...th(), color: PE_COLOR }}>PE ΔOI</th>
          </tr>
        </thead>
        <tbody>
          {strikes.map(s => {
            const ceMax = s.ceOI === maxCeOI && maxCeOI > 0
            const peMax = s.peOI === maxPeOI && maxPeOI > 0
            const rowBg = s.isAtm ? `${C.amber}0d` : undefined
            const ceDeltaArrow = s.ceDeltaOI > 0 ? '▲' : s.ceDeltaOI < 0 ? '▼' : ''
            const peDeltaArrow = s.peDeltaOI > 0 ? '▲' : s.peDeltaOI < 0 ? '▼' : ''
            return (
              <tr key={s.strike} style={{ background: rowBg }}>
                <td style={{ ...tdCe(), color: s.ceDeltaOI > 0 ? CE_COLOR : C.textMuted }}>
                  {ceDeltaArrow} {fmtOI(Math.abs(s.ceDeltaOI))}
                </td>
                <td style={{
                  ...tdCe(),
                  borderLeft: ceMax ? `2px solid ${CE_COLOR}` : undefined,
                  fontWeight: ceMax ? 700 : undefined,
                }}>
                  {fmtOI(s.ceOI)}
                </td>
                <td style={tdCe()}>{s.ceIV > 0 ? s.ceIV.toFixed(1) : '—'}</td>
                <td style={tdCe()}>{fmtPrice(s.ceLTP)}</td>
                <td style={tdStrike(s.isAtm)}>{s.strike.toFixed(0)}</td>
                <td style={tdPe()}>{fmtPrice(s.peLTP)}</td>
                <td style={tdPe()}>{s.peIV > 0 ? s.peIV.toFixed(1) : '—'}</td>
                <td style={{
                  ...tdPe(),
                  borderRight: peMax ? `2px solid ${PE_COLOR}` : undefined,
                  fontWeight: peMax ? 700 : undefined,
                }}>
                  {fmtOI(s.peOI)}
                </td>
                <td style={{ ...tdPe(), color: s.peDeltaOI > 0 ? PE_COLOR : C.textMuted }}>
                  {peDeltaArrow} {fmtOI(Math.abs(s.peDeltaOI))}
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

// ── OI bar chart ──────────────────────────────────────────────────────────────

function OIChart({ strikes, atmStrike }: { strikes: OptionStrike[]; atmStrike: number }) {
  const data = strikes.map(s => ({
    strike: s.strike,
    ceOI: s.ceOI,
    peOI: s.peOI,
  }))
  return (
    <ResponsiveContainer width="100%" height={220}>
      <BarChart data={data} margin={{ top: 4, right: 8, left: 0, bottom: 0 }}>
        <XAxis dataKey="strike" tick={{ fontSize: 9, fill: C.textMuted }}
          tickFormatter={v => String(v)} interval="preserveStartEnd" />
        <YAxis tick={{ fontSize: 9, fill: C.textMuted }} tickFormatter={fmtOI} width={42} />
        <Tooltip
          contentStyle={{ background: C.surface, border: `1px solid ${C.border}`, fontSize: 11 }}
          formatter={(v: number, name: string) => [fmtOI(v), name === 'ceOI' ? 'CE OI' : 'PE OI']}
        />
        <ReferenceLine x={atmStrike} stroke={C.amber} strokeDasharray="3 3" strokeWidth={1.5} />
        <Bar dataKey="ceOI" fill={CE_COLOR} opacity={0.75} radius={[2, 2, 0, 0]} />
        <Bar dataKey="peOI" fill={PE_COLOR} opacity={0.75} radius={[2, 2, 0, 0]} />
      </BarChart>
    </ResponsiveContainer>
  )
}

// ── Delta OI chart ────────────────────────────────────────────────────────────

function DeltaOIChart({ strikes, atmStrike }: { strikes: OptionStrike[]; atmStrike: number }) {
  const data = strikes.map(s => ({
    strike: s.strike,
    ceDelta: s.ceDeltaOI,
    peDelta: s.peDeltaOI,
  }))
  return (
    <ResponsiveContainer width="100%" height={220}>
      <BarChart data={data} margin={{ top: 4, right: 8, left: 0, bottom: 0 }}>
        <XAxis dataKey="strike" tick={{ fontSize: 9, fill: C.textMuted }}
          tickFormatter={v => String(v)} interval="preserveStartEnd" />
        <YAxis tick={{ fontSize: 9, fill: C.textMuted }} tickFormatter={fmtOI} width={42} />
        <Tooltip
          contentStyle={{ background: C.surface, border: `1px solid ${C.border}`, fontSize: 11 }}
          formatter={(v: number, name: string) => [fmtOI(v), name === 'ceDelta' ? 'CE ΔOI' : 'PE ΔOI']}
        />
        <ReferenceLine x={atmStrike} stroke={C.amber} strokeDasharray="3 3" strokeWidth={1.5} />
        <Bar dataKey="ceDelta" radius={[2, 2, 0, 0]}>
          {data.map((_, i) => (
            <Cell key={i} fill={data[i].ceDelta >= 0 ? CE_COLOR : CE_DIM} opacity={0.8} />
          ))}
        </Bar>
        <Bar dataKey="peDelta" radius={[2, 2, 0, 0]}>
          {data.map((_, i) => (
            <Cell key={i} fill={data[i].peDelta >= 0 ? PE_COLOR : PE_DIM} opacity={0.8} />
          ))}
        </Bar>
      </BarChart>
    </ResponsiveContainer>
  )
}

// ── IV skew table ─────────────────────────────────────────────────────────────

function IVSkewTable({ strikes }: { strikes: OptionStrike[] }) {
  const rows = strikes.filter(s => s.ceIV > 0 || s.peIV > 0)
  const th = (): React.CSSProperties => ({
    padding: '5px 10px', fontSize: 10, color: C.textMuted, fontWeight: 600,
    textTransform: 'uppercase', letterSpacing: '0.04em', textAlign: 'right',
    borderBottom: `1px solid ${C.border}`,
  })
  return (
    <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 11 }}>
      <thead>
        <tr style={{ background: C.surface2 }}>
          <th style={{ ...th(), textAlign: 'center' }}>Strike</th>
          <th style={{ ...th(), color: CE_COLOR }}>CE IV%</th>
          <th style={{ ...th(), color: PE_COLOR }}>PE IV%</th>
          <th style={th()}>Skew (PE−CE)</th>
          <th style={{ ...th(), textAlign: 'left' }}>Interpretation</th>
        </tr>
      </thead>
      <tbody>
        {rows.map(s => {
          const skew = s.peIV - s.ceIV
          const skewColor = skew > 1 ? PE_COLOR : skew < -1 ? CE_COLOR : C.textMuted
          const interp = skew > 2 ? 'Protective buying'
            : skew < -2 ? 'Call skew'
            : 'Balanced'
          return (
            <tr key={s.strike} style={{ borderBottom: `1px solid ${C.border2}`, background: s.isAtm ? `${C.amber}0d` : undefined }}>
              <td style={{ padding: '4px 10px', textAlign: 'center', fontFamily: F.mono, fontWeight: s.isAtm ? 700 : 400, color: s.isAtm ? C.amber : C.text }}>{s.strike}</td>
              <td style={{ padding: '4px 10px', textAlign: 'right', fontFamily: F.mono, color: CE_COLOR }}>{s.ceIV > 0 ? s.ceIV.toFixed(1) : '—'}</td>
              <td style={{ padding: '4px 10px', textAlign: 'right', fontFamily: F.mono, color: PE_COLOR }}>{s.peIV > 0 ? s.peIV.toFixed(1) : '—'}</td>
              <td style={{ padding: '4px 10px', textAlign: 'right', fontFamily: F.mono, color: skewColor }}>{skew !== 0 ? (skew > 0 ? '+' : '') + skew.toFixed(1) : '—'}</td>
              <td style={{ padding: '4px 10px', fontSize: 10, color: C.textMuted }}>{interp}</td>
            </tr>
          )
        })}
      </tbody>
    </table>
  )
}

// ── Main page ─────────────────────────────────────────────────────────────────

export function OptionsIntelligencePage() {
  const qc = useQueryClient()

  // Fetch running strategy instances to derive active symbols
  const { data: instancesData } = useQuery({
    queryKey: ['strategies'],
    queryFn: () => strategiesApi.list().then(r => r.data.data ?? []),
    staleTime: 30_000,
  })

  const runningSymbols = [...new Set(
    (instancesData ?? [])
      .filter(s => s.status === 'Running')
      .map(s => s.internalSymbol)
  )]

  const [manualSymbol, setManualSymbol] = useState('')
  const [selectedSymbol, setSelectedSymbol] = useState<string | null>(null)
  const [selectedExpiry, setSelectedExpiry] = useState('nearest')
  const [rulesOpen, setRulesOpen] = useState(true)

  // Resolved symbol: running strategy tab → manual input → nothing
  const activeSymbol = selectedSymbol
    ?? (runningSymbols[0] ?? (manualSymbol.trim() || null))

  // Expiries dropdown
  const { data: expiriesData } = useQuery({
    queryKey: ['options-expiries', activeSymbol],
    queryFn: () => optionsApi.getExpiries(activeSymbol!).then(r => r.data.data ?? []),
    enabled: !!activeSymbol,
    staleTime: 5 * 60_000,
  })

  // Main intelligence query — auto-refresh every 30s during market hours
  const { data, isLoading, error, dataUpdatedAt } = useQuery({
    queryKey: ['options-intelligence', activeSymbol, selectedExpiry],
    queryFn: () => optionsApi.getIntelligence(activeSymbol!, selectedExpiry)
      .then(r => r.data.data),
    enabled: !!activeSymbol,
    staleTime: 25_000,
    refetchInterval: isMarketHours() ? 30_000 : false,
  })

  const handleRefresh = useCallback(() => {
    qc.invalidateQueries({ queryKey: ['options-intelligence', activeSymbol] })
  }, [qc, activeSymbol])

  const snap = data?.snapshot
  const signals = data?.signals ?? []
  const maxCeOI = Math.max(0, ...(snap?.strikes.map(s => s.ceOI) ?? []))
  const maxPeOI = Math.max(0, ...(snap?.strikes.map(s => s.peOI) ?? []))

  const pcrColor = !snap ? C.text
    : snap.pcr > 1.2 ? C.green
    : snap.pcr < 0.85 ? C.red
    : C.amber

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <div style={{ padding: '12px 16px', minHeight: '100%', maxWidth: 1600 }}>

      {/* ── Header ── */}
      <div style={{
        display: 'flex', alignItems: 'center', gap: 12,
        marginBottom: SP.md, flexWrap: 'wrap',
      }}>
        {/* Symbol tabs */}
        <div style={{ display: 'flex', gap: 4 }}>
          {runningSymbols.map(sym => (
            <button key={sym}
              onClick={() => { setSelectedSymbol(sym); setManualSymbol('') }}
              style={{
                padding: '4px 12px', borderRadius: 4, fontSize: 12, fontWeight: 700,
                cursor: 'pointer', border: 'none',
                background: (selectedSymbol ?? runningSymbols[0]) === sym ? C.blue : C.surface2,
                color: (selectedSymbol ?? runningSymbols[0]) === sym ? '#fff' : C.textMuted,
              }}
            >
              {sym}
            </button>
          ))}
        </div>

        {/* Manual symbol input when no strategies running */}
        {runningSymbols.length === 0 && (
          <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
            <span style={{ fontSize: 11, color: C.amber }}>No running strategies —</span>
            <input
              value={manualSymbol}
              onChange={e => { setManualSymbol(e.target.value.toUpperCase()); setSelectedSymbol(null) }}
              placeholder="Enter symbol (e.g. NIFTY 50)"
              style={{
                background: C.surface2, border: `1px solid ${C.border}`,
                color: C.text, borderRadius: 4, padding: '4px 8px', fontSize: 11, width: 200,
              }}
            />
          </div>
        )}

        {/* Title + spot */}
        <div style={{ flex: 1, textAlign: 'center' }}>
          <span style={{ fontSize: 14, fontWeight: 700, color: C.text }}>Options Intelligence</span>
          {snap && (
            <span style={{ marginLeft: 12, fontSize: 13, fontFamily: F.mono, color: C.textMuted }}>
              ₹{snap.spotPrice.toFixed(2)}
            </span>
          )}
        </div>

        {/* Expiry selector */}
        <select
          value={selectedExpiry}
          onChange={e => setSelectedExpiry(e.target.value)}
          style={{
            background: C.surface2, border: `1px solid ${C.border}`,
            color: C.text, borderRadius: 4, padding: '4px 8px', fontSize: 11,
          }}
        >
          <option value="nearest">Nearest Weekly</option>
          <option value="next">Nearest Monthly</option>
          {(expiriesData ?? [])
            .filter(e => e !== 'nearest' && e !== 'next')
            .map(e => <option key={e} value={e}>{e}</option>)}
        </select>

        {/* Refresh */}
        <button
          onClick={handleRefresh}
          disabled={isLoading}
          style={{
            padding: '4px 12px', borderRadius: 4, fontSize: 11,
            background: C.surface2, border: `1px solid ${C.border}`,
            color: isLoading ? C.textDim : C.text, cursor: 'pointer',
          }}
        >
          {isLoading ? '⟳ Loading…' : '⟳ Refresh'}
        </button>
      </div>

      {/* ── No symbol state ── */}
      {!activeSymbol && (
        <div style={{
          textAlign: 'center', padding: 48, color: C.textMuted, fontSize: 13,
          border: `1px dashed ${C.border}`, borderRadius: 8,
        }}>
          No active strategies running. Enter a symbol above to view options data.
        </div>
      )}

      {/* ── Market closed notice ── */}
      {activeSymbol && !isMarketHours() && !snap && !isLoading && (
        <div style={{
          padding: '8px 14px', borderRadius: 6, marginBottom: SP.md, fontSize: 11,
          background: `${C.amber}18`, border: `1px solid ${C.amber44}`, color: C.amber,
        }}>
          Market closed — showing last available snapshot.
        </div>
      )}

      {/* ── Error ── */}
      {error && (
        <div style={{
          padding: '8px 14px', borderRadius: 6, marginBottom: SP.md, fontSize: 11,
          background: `${C.red}18`, border: `1px solid ${C.red}44`, color: C.red,
        }}>
          Failed to load options data. The broker API may be unavailable outside market hours.
        </div>
      )}

      {/* ── Loading skeleton ── */}
      {isLoading && !snap && (
        <div style={{ display: 'flex', gap: 8, marginBottom: SP.md }}>
          {[1, 2, 3, 4, 5, 6].map(i => (
            <div key={i} style={{
              flex: 1, height: 72, borderRadius: 6,
              background: C.surface2, opacity: 0.5,
              animation: 'pulse 1.5s infinite',
            }} />
          ))}
        </div>
      )}

      {snap && data && (
        <>
          {/* ── Bias banner ── */}
          <BiasBanner bias={data.overallBias} score={data.biasScore} />

          {/* ── KPI row ── */}
          <div style={{ display: 'flex', gap: 8, marginBottom: SP.md, flexWrap: 'wrap' }}>
            <KpiCard label="PCR" value={snap.pcr.toFixed(2)} color={pcrColor}
              sub={snap.pcr > 1.2 ? 'Bullish' : snap.pcr < 0.85 ? 'Bearish' : 'Neutral'} />
            <KpiCard label="PCR Max" value={snap.pcrMax > 0 ? snap.pcrMax.toFixed(2) : '—'}
              sub="Session high" />
            <KpiCard label="Pin IV" value={snap.pinIV > 0 ? `${snap.pinIV.toFixed(1)}%` : '—'}
              sub={`Strike ₹${snap.maxPainStrike.toFixed(0)}`} />
            <KpiCard label="Avg IV" value={snap.totalIV > 0 ? `${snap.totalIV.toFixed(1)}%` : '—'}
              color={snap.totalIV > 25 ? C.red : snap.totalIV > 20 ? C.amber : C.green}
              sub={snap.totalIV > 25 ? 'Elevated' : snap.totalIV > 20 ? 'Above normal' : 'Normal'} />
            <KpiCard label="CE OI" value={fmtOI(snap.totalCeOI)}
              color={CE_COLOR} sub="Total call OI" />
            <KpiCard label="PE OI" value={fmtOI(snap.totalPeOI)}
              color={PE_COLOR} sub="Total put OI" />
          </div>

          {/* ── Rule engine ── */}
          <div style={{
            background: C.surface, border: `1px solid ${C.border}`,
            borderRadius: 6, marginBottom: SP.md, overflow: 'hidden',
          }}>
            <button
              onClick={() => setRulesOpen(o => !o)}
              style={{
                width: '100%', display: 'flex', alignItems: 'center',
                justifyContent: 'space-between',
                padding: '10px 14px', background: 'none', border: 'none',
                cursor: 'pointer', color: C.text,
              }}
            >
              <span style={{ fontSize: 12, fontWeight: 700 }}>
                Rule Engine Signals — {activeSymbol}
                <span style={{ marginLeft: 8, fontSize: 10, color: C.textMuted }}>
                  ({signals.filter(s => s.triggered).length} triggered)
                </span>
              </span>
              <span style={{ fontSize: 10, color: C.textMuted }}>{rulesOpen ? '▲' : '▼'}</span>
            </button>
            {rulesOpen && (
              <div style={{ padding: '0 14px 12px' }}>
                {signals.map(s => <RuleRow key={s.ruleId} sig={s} />)}
              </div>
            )}
          </div>

          {/* ── Chain table ── */}
          <div style={{
            background: C.surface, border: `1px solid ${C.border}`,
            borderRadius: 6, marginBottom: SP.md, overflow: 'hidden',
          }}>
            <div style={{ padding: '10px 14px', borderBottom: `1px solid ${C.border}`, fontSize: 12, fontWeight: 700 }}>
              Option Chain — {activeSymbol} · {snap.expiry}
              <span style={{ marginLeft: 12, fontSize: 10, color: C.textMuted }}>
                ATM ₹{snap.atmStrike.toFixed(0)} · Max Pain ₹{snap.maxPainStrike.toFixed(0)}
              </span>
            </div>
            <ChainTable strikes={snap.strikes} maxCeOI={maxCeOI} maxPeOI={maxPeOI} />
          </div>

          {/* ── Charts row ── */}
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, marginBottom: SP.md }}>
            <div style={{ background: C.surface, border: `1px solid ${C.border}`, borderRadius: 6, padding: '10px 14px' }}>
              <div style={{ fontSize: 12, fontWeight: 700, marginBottom: 8, color: C.text }}>OI by Strike</div>
              <div style={{ display: 'flex', gap: 12, marginBottom: 6, fontSize: 10 }}>
                <span style={{ color: CE_COLOR }}>■ CE OI</span>
                <span style={{ color: PE_COLOR }}>■ PE OI</span>
              </div>
              <OIChart strikes={snap.strikes} atmStrike={snap.atmStrike} />
            </div>
            <div style={{ background: C.surface, border: `1px solid ${C.border}`, borderRadius: 6, padding: '10px 14px' }}>
              <div style={{ fontSize: 12, fontWeight: 700, marginBottom: 8, color: C.text }}>Delta OI — Intraday Buildup</div>
              <div style={{ display: 'flex', gap: 12, marginBottom: 6, fontSize: 10 }}>
                <span style={{ color: CE_COLOR }}>■ CE ΔOI</span>
                <span style={{ color: PE_COLOR }}>■ PE ΔOI</span>
              </div>
              <DeltaOIChart strikes={snap.strikes} atmStrike={snap.atmStrike} />
            </div>
          </div>

          {/* ── IV skew ── */}
          <div style={{
            background: C.surface, border: `1px solid ${C.border}`,
            borderRadius: 6, overflow: 'hidden',
          }}>
            <div style={{ padding: '10px 14px', borderBottom: `1px solid ${C.border}`, fontSize: 12, fontWeight: 700 }}>
              IV Skew
            </div>
            <IVSkewTable strikes={snap.strikes} />
          </div>

          {/* Last updated */}
          {dataUpdatedAt > 0 && (
            <div style={{ marginTop: 8, fontSize: 10, color: C.textDim, textAlign: 'right' }}>
              Updated {new Date(dataUpdatedAt).toLocaleTimeString('en-IN')}
              {!isMarketHours() && ' — market closed, auto-refresh paused'}
            </div>
          )}
        </>
      )}
    </div>
  )
}
