import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  tradeJournalApi, TradeJournalEntry, strategiesApi,
} from '../api/client'
import { formatInr, formatIst } from '../utils/datetime'
import { C, F, TABLE_CELL, TABLE_HEADER_CELL } from '../styles/tokens'

// ── Helpers ───────────────────────────────────────────────────────────────────

function pnlColor(val: number) {
  return val >= 0 ? C.green : C.red
}

function rMultipleLabel(r?: number): string {
  if (r == null) return '—'
  const sign = r >= 0 ? '+' : ''
  return `${sign}${r.toFixed(2)}R`
}

function rMultipleColor(r?: number): string {
  if (r == null) return C.textMuted
  return r >= 1 ? C.green : C.red
}

function directionBadge(direction: string) {
  const isBuy = direction?.toUpperCase() === 'BUY'
  return (
    <span style={{
      display: 'inline-block',
      padding: '2px 7px',
      borderRadius: 4,
      fontSize: 10,
      fontWeight: 700,
      letterSpacing: '0.05em',
      background: isBuy ? C.blueBg : C.redBg,
      color: isBuy ? C.blue : C.red,
      border: `1px solid ${isBuy ? C.blue : C.red}`,
    }}>
      {direction?.toUpperCase()}
    </span>
  )
}

function tagChip(tag: string, idx: number) {
  return (
    <span key={idx} style={{
      display: 'inline-block',
      padding: '1px 6px',
      borderRadius: 3,
      fontSize: 10,
      background: C.surface2,
      color: C.textSub,
      border: `1px solid ${C.border}`,
      marginRight: 4,
      marginBottom: 2,
    }}>
      {tag}
    </span>
  )
}

// ── Summary metrics at top ────────────────────────────────────────────────────

function SummaryMetrics({ entries }: { entries: TradeJournalEntry[] }) {
  const totalPnl = entries.reduce((s, e) => s + (e.netPnl ?? 0), 0)
  const wins = entries.filter(e => (e.netPnl ?? 0) > 0)
  const winRate = entries.length > 0 ? (wins.length / entries.length) * 100 : 0
  const rValues = entries.map(e => e.rMultiple).filter(r => r != null) as number[]
  const avgR = rValues.length > 0 ? rValues.reduce((s, r) => s + r, 0) / rValues.length : null

  const metrics = [
    { label: 'Total P&L', value: formatInr(totalPnl), color: pnlColor(totalPnl) },
    { label: 'Win Rate', value: `${winRate.toFixed(1)}%`, color: winRate >= 50 ? C.green : C.red },
    { label: 'Avg R-Multiple', value: avgR != null ? rMultipleLabel(avgR) : '—', color: avgR != null ? rMultipleColor(avgR) : C.textMuted },
    { label: 'Trade Count', value: String(entries.length), color: C.text },
  ]

  return (
    <div style={{ display: 'flex', gap: 12, marginBottom: 16, flexWrap: 'wrap' }}>
      {metrics.map(m => (
        <div key={m.label} style={{
          background: C.surface,
          border: `1px solid ${C.border}`,
          borderRadius: 6,
          padding: '10px 16px',
          minWidth: 130,
        }}>
          <div style={{ fontSize: 10, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.05em', marginBottom: 4, fontWeight: 600 }}>
            {m.label}
          </div>
          <div style={{ fontSize: 20, fontWeight: 800, color: m.color, fontFamily: F.mono }}>
            {m.value}
          </div>
        </div>
      ))}
    </div>
  )
}

// ── Trade detail drawer ───────────────────────────────────────────────────────

function TradeDrawer({ trade, onClose }: { trade: TradeJournalEntry; onClose: () => void }) {
  const qc = useQueryClient()
  const [notes, setNotes] = useState(trade.notes ?? '')
  const [tagInput, setTagInput] = useState((trade.tags ?? []).join(', '))
  const [saved, setSaved] = useState(false)

  const mutation = useMutation({
    mutationFn: () => {
      const tags = tagInput.split(',').map(t => t.trim()).filter(Boolean)
      return tradeJournalApi.updateNotes(trade.id, notes || '', tags)
    },
    onSuccess: () => {
      setSaved(true)
      setTimeout(() => setSaved(false), 2000)
      qc.invalidateQueries({ queryKey: ['trade-journal'] })
    },
  })

  const rows: Array<{ label: string; value: React.ReactNode }> = [
    { label: 'Symbol', value: trade.symbol },
    { label: 'Direction', value: directionBadge(trade.direction) },
    { label: 'Quantity', value: trade.quantity },
    { label: 'Entry Price', value: formatInr(trade.entryPrice) },
    { label: 'Exit Price', value: formatInr(trade.exitPrice) },
    { label: 'Entry Time', value: formatIst(trade.entryTime) },
    { label: 'Exit Time', value: formatIst(trade.exitTime) },
    { label: 'Holding Days', value: trade.holdingDays },
    { label: 'Gross P&L', value: <span style={{ color: pnlColor(trade.grossPnl ?? 0), fontFamily: F.mono }}>{formatInr(trade.grossPnl ?? 0)}</span> },
    { label: 'Net P&L', value: <span style={{ color: pnlColor(trade.netPnl ?? 0), fontFamily: F.mono, fontWeight: 700 }}>{formatInr(trade.netPnl ?? 0)}</span> },
    { label: 'R-Multiple', value: <span style={{ color: rMultipleColor(trade.rMultiple), fontFamily: F.mono }}>{rMultipleLabel(trade.rMultiple)}</span> },
    { label: 'Initial Risk', value: trade.initialRisk != null ? formatInr(trade.initialRisk) : '—' },
    { label: 'MAE', value: trade.mae != null ? formatInr(trade.mae) : '—' },
    { label: 'MFE', value: trade.mfe != null ? formatInr(trade.mfe) : '—' },
    { label: 'Exit Reason', value: trade.exitReason },
    { label: 'Entry Reason', value: trade.entryReason ?? '—' },
    { label: 'Tax Class', value: trade.taxClassification },
    { label: 'Source', value: trade.source },
  ]

  return (
    <>
      {/* Overlay */}
      <div
        onClick={onClose}
        style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.5)', zIndex: 1000 }}
      />
      {/* Drawer */}
      <div style={{
        position: 'fixed', top: 0, right: 0, bottom: 0,
        width: 520,
        background: C.surface,
        borderLeft: `1px solid ${C.border}`,
        zIndex: 1001,
        display: 'flex', flexDirection: 'column',
        overflowY: 'auto',
      }}>
        {/* Header */}
        <div style={{
          padding: '12px 16px',
          borderBottom: `1px solid ${C.border}`,
          display: 'flex', alignItems: 'center', justifyContent: 'space-between',
          flexShrink: 0,
        }}>
          <div>
            <div style={{ fontWeight: 700, fontSize: 15, color: C.text }}>{trade.symbol}</div>
            <div style={{ fontSize: 11, color: C.textMuted, marginTop: 2 }}>{trade.id}</div>
          </div>
          <button
            onClick={onClose}
            style={{ background: 'transparent', border: 'none', color: C.textMuted, cursor: 'pointer', fontSize: 18, padding: 4 }}
          >
            x
          </button>
        </div>

        {/* Detail rows */}
        <div style={{ padding: '12px 16px', flex: 1 }}>
          <table style={{ width: '100%', borderCollapse: 'collapse', marginBottom: 20 }}>
            <tbody>
              {rows.map(r => (
                <tr key={r.label} style={{ borderBottom: `1px solid ${C.border2}` }}>
                  <td style={{ ...tdStyle, color: C.textMuted, width: 140, fontWeight: 600 }}>{r.label}</td>
                  <td style={{ ...tdStyle, color: C.text }}>{r.value}</td>
                </tr>
              ))}
            </tbody>
          </table>

          {/* Tags */}
          <div style={{ marginBottom: 16 }}>
            <div style={{ fontSize: 11, color: C.textMuted, marginBottom: 6, fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.05em' }}>Tags (comma-separated)</div>
            <input
              value={tagInput}
              onChange={e => setTagInput(e.target.value)}
              placeholder="trend, breakout, high-volume"
              style={inputStyle}
            />
          </div>

          {/* Notes */}
          <div style={{ marginBottom: 16 }}>
            <div style={{ fontSize: 11, color: C.textMuted, marginBottom: 6, fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.05em' }}>Notes</div>
            <textarea
              value={notes}
              onChange={e => setNotes(e.target.value)}
              rows={5}
              placeholder="Trade rationale, observations, lessons..."
              style={{ ...inputStyle, resize: 'vertical', fontFamily: F.sans }}
            />
          </div>

          <div style={{ display: 'flex', gap: 10, alignItems: 'center' }}>
            <button
              onClick={() => mutation.mutate()}
              disabled={mutation.isPending}
              style={{
                padding: '8px 18px',
                background: C.blue,
                color: '#fff',
                border: 'none',
                borderRadius: 5,
                cursor: mutation.isPending ? 'not-allowed' : 'pointer',
                fontWeight: 700,
                fontSize: 13,
              }}
            >
              {mutation.isPending ? 'Saving...' : 'Save'}
            </button>
            {saved && <span style={{ color: C.green, fontSize: 12 }}>Saved</span>}
            {mutation.isError && <span style={{ color: C.red, fontSize: 12 }}>Save failed</span>}
          </div>
        </div>
      </div>
    </>
  )
}

const tdStyle: React.CSSProperties = { padding: TABLE_CELL, fontSize: 13, verticalAlign: 'top' }
const inputStyle: React.CSSProperties = {
  width: '100%',
  background: C.surface2,
  border: `1px solid ${C.border}`,
  borderRadius: 5,
  color: C.text,
  padding: '7px 10px',
  fontSize: 13,
  fontFamily: F.sans,
  boxSizing: 'border-box',
}

// ── Page root ─────────────────────────────────────────────────────────────────

const EXIT_REASONS = ['', 'STOP_LOSS', 'TAKE_PROFIT', 'TRAIL_STOP', 'MANUAL', 'TIME_EXIT', 'SIGNAL_EXIT']
const SOURCES = ['', 'Live', 'ForwardTest', 'Backtest', 'Manual']

export function TradeJournalPage() {
  const [symbol, setSymbol] = useState('')
  const [exitReason, setExitReason] = useState('')
  const [source, setSource] = useState('')
  const [strategyInstanceId, setStrategyInstanceId] = useState('')
  const [page, setPage] = useState(1)
  const [selectedTrade, setSelectedTrade] = useState<TradeJournalEntry | null>(null)

  const pageSize = 50

  const { data, isLoading, isError } = useQuery({
    queryKey: ['trade-journal', symbol, exitReason, source, strategyInstanceId, page],
    queryFn: () => tradeJournalApi.list({
      symbol: symbol || undefined,
      source: source || undefined,
      page,
      pageSize,
    }).then(r => r.data.data),
    staleTime: 30_000,
  })

  const { data: strategies } = useQuery({
    queryKey: ['strategies-list'],
    queryFn: () => strategiesApi.list().then(r => r.data.data ?? []),
    staleTime: 60_000,
  })

  const entries = data?.items ?? []
  const totalCount = data?.totalCount ?? 0
  const totalPages = Math.ceil(totalCount / pageSize) || 1

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

  const selectStyle: React.CSSProperties = {
    background: C.surface2,
    border: `1px solid ${C.border}`,
    borderRadius: 4,
    color: C.text,
    padding: '5px 8px',
    fontSize: 12,
    cursor: 'pointer',
  }

  return (
    <div>
      {/* Page header */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 }}>
        <h2 style={{ margin: 0, fontSize: 18, fontWeight: 700 }}>Trade Journal</h2>
        <button
          onClick={() => { const now = new Date(); const y = now.getMonth() >= 3 ? now.getFullYear() : now.getFullYear() - 1; tradeJournalApi.exportTaxLots(`${y}-${String(y+1).slice(2)}`) }}
          style={{
            padding: '6px 14px',
            background: C.surface2,
            border: `1px solid ${C.border}`,
            borderRadius: 5,
            color: C.text,
            cursor: 'pointer',
            fontSize: 12,
            fontWeight: 600,
          }}
        >
          Export Tax CSV
        </button>
      </div>

      {/* Summary metrics */}
      <SummaryMetrics entries={entries} />

      {/* Filter bar */}
      <div style={{
        display: 'flex', gap: 10, alignItems: 'center',
        marginBottom: 14, flexWrap: 'wrap',
        background: C.surface,
        border: `1px solid ${C.border}`,
        borderRadius: 6,
        padding: '8px 12px',
      }}>
        <input
          value={symbol}
          onChange={e => { setSymbol(e.target.value); setPage(1) }}
          placeholder="Symbol search..."
          style={{ ...selectStyle, minWidth: 160 }}
        />

        <select value={exitReason} onChange={e => { setExitReason(e.target.value); setPage(1) }} style={selectStyle}>
          <option value="">All Exit Reasons</option>
          {EXIT_REASONS.filter(Boolean).map(r => <option key={r} value={r}>{r}</option>)}
        </select>

        <select value={source} onChange={e => { setSource(e.target.value); setPage(1) }} style={selectStyle}>
          <option value="">All Sources</option>
          {SOURCES.filter(Boolean).map(s => <option key={s} value={s}>{s}</option>)}
        </select>

        <select value={strategyInstanceId} onChange={e => { setStrategyInstanceId(e.target.value); setPage(1) }} style={selectStyle}>
          <option value="">All Strategies</option>
          {(strategies ?? []).map(s => <option key={s.id} value={s.id}>{s.name}</option>)}
        </select>

        {(symbol || exitReason || source || strategyInstanceId) && (
          <button
            onClick={() => { setSymbol(''); setExitReason(''); setSource(''); setStrategyInstanceId(''); setPage(1) }}
            style={{ ...selectStyle, color: C.textMuted }}
          >
            Clear
          </button>
        )}

        <span style={{ marginLeft: 'auto', fontSize: 11, color: C.textMuted }}>
          {totalCount} trades
        </span>
      </div>

      {/* Trade table */}
      {isLoading && <p style={{ color: C.textMuted }}>Loading trades...</p>}
      {isError && <p style={{ color: C.red }}>Failed to load trade journal.</p>}

      {!isLoading && !isError && (
        <>
          <div style={{ background: C.surface, border: `1px solid ${C.border}`, borderRadius: 6, overflow: 'hidden', marginBottom: 12 }}>
            <table style={{ width: '100%', borderCollapse: 'collapse' }}>
              <thead>
                <tr>
                  {['Symbol', 'Dir', 'Entry Time', 'Exit Time', 'Entry', 'Exit', 'Net P&L', 'R-Multiple', 'Exit Reason', 'Tags'].map(h => (
                    <th key={h} style={thStyle}>{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {entries.length === 0 && (
                  <tr>
                    <td colSpan={10} style={{ padding: '24px 12px', textAlign: 'center', color: C.textMuted, fontSize: 13 }}>
                      No trades found.
                    </td>
                  </tr>
                )}
                {entries.map((e, idx) => (
                  <tr
                    key={e.id}
                    onClick={() => setSelectedTrade(e)}
                    style={{
                      background: idx % 2 === 0 ? C.surface : C.surface3,
                      cursor: 'pointer',
                      borderBottom: `1px solid ${C.border2}`,
                      transition: 'background 0.1s',
                    }}
                    onMouseEnter={ev => (ev.currentTarget.style.background = C.surface2)}
                    onMouseLeave={ev => (ev.currentTarget.style.background = idx % 2 === 0 ? C.surface : C.surface3)}
                  >
                    <td style={{ ...tdStyle, fontWeight: 600, color: C.text }}>{e.symbol}</td>
                    <td style={tdStyle}>{directionBadge(e.direction)}</td>
                    <td style={{ ...tdStyle, color: C.textSub, fontFamily: F.mono, fontSize: 11 }}>{formatIst(e.entryTime)}</td>
                    <td style={{ ...tdStyle, color: C.textSub, fontFamily: F.mono, fontSize: 11 }}>{formatIst(e.exitTime)}</td>
                    <td style={{ ...tdStyle, fontFamily: F.mono, fontSize: 12 }}>{formatInr(e.entryPrice)}</td>
                    <td style={{ ...tdStyle, fontFamily: F.mono, fontSize: 12 }}>{formatInr(e.exitPrice)}</td>
                    <td style={{ ...tdStyle, fontFamily: F.mono, fontWeight: 700, color: pnlColor(e.netPnl ?? 0) }}>
                      {(e.netPnl ?? 0) >= 0 ? '+' : ''}{formatInr(e.netPnl ?? 0)}
                    </td>
                    <td style={{ ...tdStyle, fontFamily: F.mono, color: rMultipleColor(e.rMultiple) }}>
                      {rMultipleLabel(e.rMultiple)}
                    </td>
                    <td style={{ ...tdStyle, color: C.textSub, fontSize: 11 }}>{e.exitReason}</td>
                    <td style={tdStyle}>
                      {(e.tags ?? []).slice(0, 2).map((t, i) => tagChip(t, i))}
                      {(e.tags ?? []).length > 2 && <span style={{ fontSize: 10, color: C.textMuted }}>+{(e.tags ?? []).length - 2}</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* Pagination */}
          {totalPages > 1 && (
            <div style={{ display: 'flex', gap: 8, alignItems: 'center', justifyContent: 'center' }}>
              <button
                onClick={() => setPage(p => Math.max(1, p - 1))}
                disabled={page === 1}
                style={{ ...selectStyle, opacity: page === 1 ? 0.4 : 1 }}
              >
                Prev
              </button>
              <span style={{ fontSize: 12, color: C.textMuted }}>Page {page} of {totalPages}</span>
              <button
                onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                disabled={page === totalPages}
                style={{ ...selectStyle, opacity: page === totalPages ? 0.4 : 1 }}
              >
                Next
              </button>
            </div>
          )}
        </>
      )}

      {/* Trade detail drawer */}
      {selectedTrade && (
        <TradeDrawer
          trade={selectedTrade}
          onClose={() => setSelectedTrade(null)}
        />
      )}
    </div>
  )
}
