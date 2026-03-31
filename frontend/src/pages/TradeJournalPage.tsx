import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { tradeJournalApi, TradeJournalEntry } from '../api/client'
import { C, TABLE_CELL, TABLE_HEADER_CELL, CONTENT_PAD } from '../styles/tokens'
import { formatInr, formatIst } from '../utils/datetime'

const th: React.CSSProperties = {
  padding: TABLE_HEADER_CELL,
  textAlign: 'left',
  fontSize: 11,
  fontWeight: 700,
  color: C.textMuted,
  textTransform: 'uppercase',
  letterSpacing: '0.05em',
  borderBottom: `1px solid ${C.border}`,
  whiteSpace: 'nowrap',
}

const td: React.CSSProperties = {
  padding: TABLE_CELL,
  fontSize: 13,
  color: C.text,
  borderBottom: `1px solid ${C.border2}`,
}

function pnlColor(v: number | undefined) {
  if (v === undefined) return C.textSub
  return v >= 0 ? C.green : C.red
}

function RMultipleBadge({ v }: { v?: number }) {
  if (v === undefined) return <span style={{ color: C.textMuted }}>—</span>
  const color = v >= 2 ? C.green : v >= 1 ? C.amber : v >= 0 ? C.textSub : C.red
  return <span style={{ color, fontVariantNumeric: 'tabular-nums' }}>{v.toFixed(2)}R</span>
}

function TaxBadge({ cls }: { cls?: string }) {
  if (!cls) return <span style={{ color: C.textMuted }}>—</span>
  const colors: Record<string, string> = {
    Speculative: C.amber,
    STCG: C.blue,
    LTCG: C.green,
  }
  return (
    <span style={{
      fontSize: 10,
      fontWeight: 700,
      padding: '2px 6px',
      borderRadius: 4,
      background: colors[cls] ?? C.surface2,
      color: '#fff',
    }}>{cls}</span>
  )
}

// ── Notes Editor ─────────────────────────────────────────────────────────────

function NotesEditor({ entry, onClose }: { entry: TradeJournalEntry; onClose: () => void }) {
  const qc = useQueryClient()
  const [notes, setNotes] = useState(entry.notes ?? '')
  const [tags, setTags] = useState((entry.tags ?? []).join(', '))

  const save = useMutation({
    mutationFn: () =>
      tradeJournalApi.updateNotes(
        entry.id,
        notes,
        tags.split(',').map(t => t.trim()).filter(Boolean)
      ),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['trade-journal'] })
      onClose()
    },
  })

  return (
    <div style={{
      position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.6)',
      display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 999,
    }}>
      <div style={{
        background: C.surface, border: `1px solid ${C.border}`,
        borderRadius: 10, padding: 24, width: 480,
      }}>
        <div style={{ fontSize: 14, fontWeight: 700, color: C.text, marginBottom: 16 }}>
          Edit Journal Notes — {entry.symbol}
        </div>

        <div style={{ marginBottom: 12 }}>
          <label style={{ fontSize: 11, color: C.textMuted, display: 'block', marginBottom: 4 }}>Notes</label>
          <textarea
            value={notes}
            onChange={e => setNotes(e.target.value)}
            rows={5}
            style={{
              width: '100%', boxSizing: 'border-box',
              background: C.surface2, border: `1px solid ${C.border}`,
              borderRadius: 6, color: C.text, fontSize: 13, padding: '8px 10px',
              resize: 'vertical',
            }}
          />
        </div>

        <div style={{ marginBottom: 20 }}>
          <label style={{ fontSize: 11, color: C.textMuted, display: 'block', marginBottom: 4 }}>
            Tags (comma-separated)
          </label>
          <input
            value={tags}
            onChange={e => setTags(e.target.value)}
            placeholder="e.g. breakout, earnings, momentum"
            style={{
              width: '100%', boxSizing: 'border-box',
              background: C.surface2, border: `1px solid ${C.border}`,
              borderRadius: 6, color: C.text, fontSize: 13, padding: '6px 10px',
            }}
          />
        </div>

        <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
          <button
            onClick={onClose}
            style={{
              padding: '6px 16px', borderRadius: 6, border: `1px solid ${C.border}`,
              background: 'transparent', color: C.textSub, cursor: 'pointer', fontSize: 13,
            }}
          >Cancel</button>
          <button
            onClick={() => save.mutate()}
            disabled={save.isPending}
            style={{
              padding: '6px 16px', borderRadius: 6, border: 'none',
              background: C.blue, color: '#fff', cursor: 'pointer', fontSize: 13,
            }}
          >{save.isPending ? 'Saving…' : 'Save'}</button>
        </div>
      </div>
    </div>
  )
}

// ── Main Page ─────────────────────────────────────────────────────────────────

export function TradeJournalPage() {
  const [page, setPage] = useState(1)
  const [symbol, setSymbol] = useState('')
  const [source, setSource] = useState('')
  const [editing, setEditing] = useState<TradeJournalEntry | null>(null)

  const pageSize = 50

  const { data, isLoading, isError } = useQuery({
    queryKey: ['trade-journal', page, symbol, source],
    queryFn: () => tradeJournalApi.list({
      page,
      pageSize,
      symbol: symbol || undefined,
      source: source || undefined,
    }),
  })

  const entries: TradeJournalEntry[] = data?.data?.data?.items ?? []
  const total: number = data?.data?.data?.totalCount ?? 0
  const totalPages = Math.max(1, Math.ceil(total / pageSize))

  return (
    <div style={{ padding: CONTENT_PAD }}>
      {editing && <NotesEditor entry={editing} onClose={() => setEditing(null)} />}

      <div style={{ display: 'flex', alignItems: 'center', gap: 16, marginBottom: 16 }}>
        <div style={{ fontSize: 18, fontWeight: 700, color: C.text }}>Trade Journal</div>
        <div style={{ flex: 1 }} />
        <input
          value={symbol}
          onChange={e => { setSymbol(e.target.value); setPage(1) }}
          placeholder="Filter by symbol…"
          style={{
            background: C.surface2, border: `1px solid ${C.border}`,
            borderRadius: 6, color: C.text, fontSize: 13, padding: '5px 10px', width: 160,
          }}
        />
        <select
          value={source}
          onChange={e => { setSource(e.target.value); setPage(1) }}
          style={{
            background: C.surface2, border: `1px solid ${C.border}`,
            borderRadius: 6, color: C.text, fontSize: 13, padding: '5px 10px',
          }}
        >
          <option value="">All sources</option>
          <option value="Backtest">Backtest</option>
          <option value="ForwardTest">Forward Test</option>
          <option value="Live">Live</option>
        </select>
      </div>

      <div style={{
        background: C.surface, border: `1px solid ${C.border}`,
        borderRadius: 8, overflow: 'hidden',
      }}>
        {isLoading && (
          <div style={{ padding: 32, textAlign: 'center', color: C.textMuted, fontSize: 13 }}>
            Loading journal…
          </div>
        )}

        {isError && (
          <div style={{ padding: 32, textAlign: 'center', color: C.red, fontSize: 13 }}>
            Failed to load trade journal.
          </div>
        )}

        {!isLoading && !isError && (
          <table style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead>
              <tr style={{ background: C.surface2 }}>
                <th style={th}>Symbol</th>
                <th style={th}>Dir</th>
                <th style={th}>Qty</th>
                <th style={th}>Entry</th>
                <th style={th}>Exit</th>
                <th style={th}>Entry Time</th>
                <th style={th}>Exit Time</th>
                <th style={{ ...th, textAlign: 'right' }}>Gross P&L</th>
                <th style={{ ...th, textAlign: 'right' }}>Net P&L</th>
                <th style={{ ...th, textAlign: 'right' }}>R-Multiple</th>
                <th style={th}>Tax</th>
                <th style={th}>Source</th>
                <th style={th}>Tags</th>
                <th style={th}></th>
              </tr>
            </thead>
            <tbody>
              {entries.length === 0 && (
                <tr>
                  <td colSpan={14} style={{ ...td, textAlign: 'center', color: C.textMuted, padding: 32 }}>
                    No journal entries found.
                  </td>
                </tr>
              )}
              {entries.map(e => (
                <tr key={e.id} style={{ cursor: 'default' }}>
                  <td style={{ ...td, fontWeight: 600 }}>{e.symbol}</td>
                  <td style={{ ...td, color: e.direction === 'BUY' ? C.green : C.red }}>
                    {e.direction}
                  </td>
                  <td style={td}>{e.quantity}</td>
                  <td style={{ ...td, fontVariantNumeric: 'tabular-nums' }}>
                    {formatInr(e.entryPrice)}
                  </td>
                  <td style={{ ...td, fontVariantNumeric: 'tabular-nums' }}>
                    {e.exitPrice !== undefined ? formatInr(e.exitPrice) : '—'}
                  </td>
                  <td style={{ ...td, fontSize: 12, color: C.textSub }}>
                    {formatIst(e.entryTime)}
                  </td>
                  <td style={{ ...td, fontSize: 12, color: C.textSub }}>
                    {e.exitTime ? formatIst(e.exitTime) : '—'}
                  </td>
                  <td style={{ ...td, textAlign: 'right', color: pnlColor(e.grossPnl), fontVariantNumeric: 'tabular-nums' }}>
                    {e.grossPnl !== undefined ? formatInr(e.grossPnl) : '—'}
                  </td>
                  <td style={{ ...td, textAlign: 'right', color: pnlColor(e.netPnl), fontVariantNumeric: 'tabular-nums' }}>
                    {e.netPnl !== undefined ? formatInr(e.netPnl) : '—'}
                  </td>
                  <td style={{ ...td, textAlign: 'right' }}>
                    <RMultipleBadge v={e.rMultiple} />
                  </td>
                  <td style={td}><TaxBadge cls={e.taxClassification} /></td>
                  <td style={{ ...td, fontSize: 11, color: C.textSub }}>{e.source ?? '—'}</td>
                  <td style={td}>
                    {(e.tags ?? []).length > 0 ? (
                      <div style={{ display: 'flex', gap: 4, flexWrap: 'wrap' }}>
                        {(e.tags ?? []).map(t => (
                          <span key={t} style={{
                            fontSize: 10, padding: '1px 6px', borderRadius: 3,
                            background: C.surface2, color: C.textSub,
                            border: `1px solid ${C.border}`,
                          }}>{t}</span>
                        ))}
                      </div>
                    ) : <span style={{ color: C.textMuted }}>—</span>}
                  </td>
                  <td style={td}>
                    <button
                      onClick={() => setEditing(e)}
                      style={{
                        fontSize: 11, padding: '3px 8px', borderRadius: 4,
                        background: 'transparent', border: `1px solid ${C.border}`,
                        color: C.textSub, cursor: 'pointer',
                      }}
                    >Edit</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {/* Pagination */}
      {totalPages > 1 && (
        <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginTop: 12 }}>
          <button
            disabled={page <= 1}
            onClick={() => setPage(p => p - 1)}
            style={{
              padding: '4px 12px', borderRadius: 5, border: `1px solid ${C.border}`,
              background: 'transparent', color: page > 1 ? C.text : C.textMuted, cursor: page > 1 ? 'pointer' : 'default',
            }}
          >Prev</button>
          <span style={{ fontSize: 12, color: C.textSub }}>
            Page {page} of {totalPages} ({total} entries)
          </span>
          <button
            disabled={page >= totalPages}
            onClick={() => setPage(p => p + 1)}
            style={{
              padding: '4px 12px', borderRadius: 5, border: `1px solid ${C.border}`,
              background: 'transparent', color: page < totalPages ? C.text : C.textMuted, cursor: page < totalPages ? 'pointer' : 'default',
            }}
          >Next</button>
        </div>
      )}
    </div>
  )
}
