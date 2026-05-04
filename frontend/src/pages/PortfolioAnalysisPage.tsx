import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { tradeJournalApi, strategiesApi, PnlByDimension } from '../api/client'
import { formatInr } from '../utils/datetime'
import { C, F, TABLE_CELL, TABLE_HEADER_CELL } from '../styles/tokens'

// ── Helpers ───────────────────────────────────────────────────────────────────

function pnlColor(val: number) {
  return val >= 0 ? C.green : C.red
}

// ── Attribution table ─────────────────────────────────────────────────────────

function AttributionTable({ rows, title }: { rows: PnlByDimension[]; title: string }) {
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

  if (rows.length === 0) {
    return (
      <div style={{ background: C.surface, border: `1px solid ${C.border}`, borderRadius: 6, padding: 20 }}>
        <div style={{ fontSize: 12, fontWeight: 700, color: C.textSub, marginBottom: 12, textTransform: 'uppercase', letterSpacing: '0.04em' }}>{title}</div>
        <p style={{ color: C.textMuted, fontSize: 12, margin: 0 }}>No data</p>
      </div>
    )
  }

  return (
    <div style={{ background: C.surface, border: `1px solid ${C.border}`, borderRadius: 6, overflow: 'hidden' }}>
      <div style={{ padding: '10px 12px', borderBottom: `1px solid ${C.border}`, fontSize: 12, fontWeight: 700, color: C.textSub, textTransform: 'uppercase', letterSpacing: '0.04em' }}>
        {title}
      </div>
      <table style={{ width: '100%', borderCollapse: 'collapse' }}>
        <thead>
          <tr>
            <th style={thStyle}>Label</th>
            <th style={{ ...thStyle, textAlign: 'right' }}>Trades</th>
            <th style={{ ...thStyle, textAlign: 'right' }}>Win Rate</th>
            <th style={{ ...thStyle, textAlign: 'right' }}>Net P&L</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row, idx) => (
            <tr
              key={row.label}
              style={{
                background: idx % 2 === 0 ? C.surface : C.surface3,
                borderBottom: `1px solid ${C.border2}`,
              }}
            >
              <td style={{ ...tdStyle, fontWeight: 600, color: C.text }}>{row.label}</td>
              <td style={{ ...tdStyle, textAlign: 'right', color: C.textSub }}>{row.tradeCount}</td>
              <td style={{ ...tdStyle, textAlign: 'right', color: row.winRate >= 0.5 ? C.green : C.red }}>
                {(row.winRate * 100).toFixed(1)}%
              </td>
              <td style={{ ...tdStyle, textAlign: 'right', fontFamily: F.mono, fontWeight: 700, color: pnlColor(row.netPnl) }}>
                {row.netPnl >= 0 ? '+' : ''}{formatInr(row.netPnl)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

// ── Bar rows (exit type breakdown) ────────────────────────────────────────────

function ExitTypeSection({ rows }: { rows: PnlByDimension[] }) {
  const total = rows.reduce((s, r) => s + r.tradeCount, 0) || 1

  const exitColors: Record<string, string> = {
    STOP_LOSS: C.red,
    TAKE_PROFIT: C.green,
    TRAIL_STOP: C.blue,
    MANUAL: C.amber,
    TIME_EXIT: C.textSub,
    SIGNAL_EXIT: C.blue,
  }

  return (
    <div style={{ background: C.surface, border: `1px solid ${C.border}`, borderRadius: 6, overflow: 'hidden' }}>
      <div style={{ padding: '10px 12px', borderBottom: `1px solid ${C.border}`, fontSize: 12, fontWeight: 700, color: C.textSub, textTransform: 'uppercase', letterSpacing: '0.04em' }}>
        By Exit Type
      </div>
      <div style={{ padding: '10px 12px' }}>
        {rows.length === 0 && <p style={{ color: C.textMuted, fontSize: 12, margin: 0 }}>No data</p>}
        {rows.map(row => {
          const pct = (row.tradeCount / total) * 100
          const color = exitColors[row.label] ?? C.textSub
          return (
            <div key={row.label} style={{ marginBottom: 14 }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 5 }}>
                <span style={{ fontSize: 12, fontWeight: 600, color: C.text }}>{row.label}</span>
                <div style={{ display: 'flex', gap: 16, alignItems: 'center' }}>
                  <span style={{ fontSize: 11, color: C.textMuted }}>{row.tradeCount} trades ({pct.toFixed(0)}%)</span>
                  <span style={{ fontFamily: F.mono, fontSize: 12, fontWeight: 700, color: pnlColor(row.netPnl) }}>
                    {row.netPnl >= 0 ? '+' : ''}{formatInr(row.netPnl)}
                  </span>
                </div>
              </div>
              <div style={{ background: C.surface2, borderRadius: 3, height: 6, overflow: 'hidden' }}>
                <div style={{ width: `${pct}%`, height: '100%', background: color, borderRadius: 3, transition: 'width 0.3s' }} />
              </div>
            </div>
          )
        })}
      </div>
    </div>
  )
}

// ── Day of week section ───────────────────────────────────────────────────────

function DayOfWeekSection({ rows }: { rows: PnlByDimension[] }) {
  const DOW_ORDER = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']
  const sorted = [...rows].sort((a, b) => DOW_ORDER.indexOf(a.label) - DOW_ORDER.indexOf(b.label))

  return (
    <div style={{ background: C.surface, border: `1px solid ${C.border}`, borderRadius: 6, overflow: 'hidden' }}>
      <div style={{ padding: '10px 12px', borderBottom: `1px solid ${C.border}`, fontSize: 12, fontWeight: 700, color: C.textSub, textTransform: 'uppercase', letterSpacing: '0.04em' }}>
        By Day of Week
      </div>
      <div style={{ padding: '10px 12px' }}>
        {sorted.length === 0 && <p style={{ color: C.textMuted, fontSize: 12, margin: 0 }}>No data</p>}
        {sorted.map(row => (
          <div key={row.label} style={{
            display: 'flex', alignItems: 'center', justifyContent: 'space-between',
            padding: '6px 0',
            borderBottom: `1px solid ${C.border2}`,
          }}>
            <span style={{ fontSize: 12, fontWeight: 600, color: C.text, minWidth: 100 }}>{row.label}</span>
            <span style={{ fontSize: 11, color: C.textMuted, minWidth: 80, textAlign: 'right' }}>{row.tradeCount} trades</span>
            <span style={{ fontFamily: F.mono, fontSize: 12, fontWeight: 700, color: pnlColor(row.netPnl), minWidth: 100, textAlign: 'right' }}>
              {row.netPnl >= 0 ? '+' : ''}{formatInr(row.netPnl)}
            </span>
            <span style={{ fontSize: 11, color: row.winRate >= 0.5 ? C.green : C.red, minWidth: 60, textAlign: 'right' }}>
              {(row.winRate * 100).toFixed(1)}% W
            </span>
          </div>
        ))}
      </div>
    </div>
  )
}

// ── Page root ─────────────────────────────────────────────────────────────────

export function PortfolioAnalysisPage() {
  const [fromDate, setFromDate] = useState('')
  const [toDate,   setToDate]   = useState('')
  const [strategyInstanceId, setStrategyInstanceId] = useState('')

  const { data: attribution, isLoading, isError } = useQuery({
    queryKey: ['trade-journal-attribution', strategyInstanceId, fromDate, toDate],
    queryFn: () => tradeJournalApi.getAttribution({
      strategyInstanceId: strategyInstanceId || undefined,
      from: fromDate || undefined,
      to:   toDate   || undefined,
    }).then(r => r.data.data),
    staleTime: 60_000,
  })

  const { data: strategies } = useQuery({
    queryKey: ['strategies-list'],
    queryFn: () => strategiesApi.list().then(r => r.data.data ?? []),
    staleTime: 60_000,
  })

  const selectStyle: React.CSSProperties = {
    background: C.surface2,
    border: `1px solid ${C.border}`,
    borderRadius: 4,
    color: C.text,
    padding: '5px 8px',
    fontSize: 12,
    cursor: 'pointer',
  }

  const inputStyle: React.CSSProperties = {
    background: C.surface2,
    border: `1px solid ${C.border}`,
    borderRadius: 4,
    color: C.text,
    padding: '5px 8px',
    fontSize: 12,
    colorScheme: 'dark',
  }

  return (
    <div>
      {/* Page header */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 }}>
        <h2 style={{ margin: 0, fontSize: 18, fontWeight: 700 }}>Portfolio Analysis</h2>
      </div>

      {/* Filters */}
      <div style={{
        display: 'flex', gap: 10, alignItems: 'center',
        marginBottom: 16, flexWrap: 'wrap',
        background: C.surface,
        border: `1px solid ${C.border}`,
        borderRadius: 6,
        padding: '8px 12px',
      }}>
        <label style={{ fontSize: 11, color: C.textMuted, display: 'flex', alignItems: 'center', gap: 6 }}>
          From
          <input type="date" value={fromDate} onChange={e => setFromDate(e.target.value)} style={inputStyle} />
        </label>
        <label style={{ fontSize: 11, color: C.textMuted, display: 'flex', alignItems: 'center', gap: 6 }}>
          To
          <input type="date" value={toDate} onChange={e => setToDate(e.target.value)} style={inputStyle} />
        </label>
        <select value={strategyInstanceId} onChange={e => setStrategyInstanceId(e.target.value)} style={selectStyle}>
          <option value="">All Strategies</option>
          {(strategies ?? []).map(s => <option key={s.id} value={s.id}>{s.name}</option>)}
        </select>
        {(fromDate || toDate || strategyInstanceId) && (
          <button
            onClick={() => { setFromDate(''); setToDate(''); setStrategyInstanceId('') }}
            style={{ ...selectStyle, color: C.textMuted }}
          >
            Clear
          </button>
        )}
      </div>

      {isLoading && <p style={{ color: C.textMuted }}>Loading attribution data...</p>}
      {isError && <p style={{ color: C.red }}>Failed to load attribution data.</p>}

      {!isLoading && !isError && attribution && (
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16 }}>
          <AttributionTable rows={attribution.bySymbol} title="By Symbol" />
          <AttributionTable rows={attribution.byMonth} title="By Month" />
          <ExitTypeSection rows={attribution.byExitType} />
          <DayOfWeekSection rows={attribution.byDayOfWeek} />
        </div>
      )}

      {!isLoading && !isError && !attribution && (
        <div style={{
          background: C.surface,
          border: `1px solid ${C.border}`,
          borderRadius: 8,
          padding: 32,
          textAlign: 'center',
        }}>
          <p style={{ color: C.textMuted, fontSize: 14, margin: 0 }}>No attribution data available yet.</p>
          <p style={{ color: C.textDim, fontSize: 12, margin: '6px 0 0' }}>Completed trades will appear here once recorded in the journal.</p>
        </div>
      )}
    </div>
  )
}
