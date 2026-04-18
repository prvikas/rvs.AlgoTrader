import React, { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { tradeJournalApi, PnlByDimension } from '../api/client'
import { C, CONTENT_PAD } from '../styles/tokens'
import { formatInr } from '../utils/datetime'

// ── Simple bar chart ──────────────────────────────────────────────────────────

function BarChart({ rows, title }: { rows: PnlByDimension[]; title: string }) {
  if (!rows.length) return null

  const maxAbs = Math.max(...rows.map(r => Math.abs(r.netPnl)), 1)

  return (
    <div style={{
      background: C.surface, border: `1px solid ${C.border}`,
      borderRadius: 8, padding: 16,
    }}>
      <div style={{ fontSize: 12, fontWeight: 700, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.06em', marginBottom: 12 }}>
        {title}
      </div>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
        {rows.map(r => {
          const pct = (Math.abs(r.netPnl) / maxAbs) * 100
          const positive = r.netPnl >= 0
          return (
            <div key={r.dimension} style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <div style={{ width: 90, fontSize: 12, color: C.textSub, textAlign: 'right', flexShrink: 0 }}>
                {r.label}
              </div>
              <div style={{ flex: 1, height: 18, background: C.surface2, borderRadius: 3, overflow: 'hidden', position: 'relative' }}>
                <div style={{
                  position: 'absolute',
                  left: positive ? '50%' : `calc(50% - ${pct / 2}%)`,
                  width: `${pct / 2}%`,
                  top: 0, bottom: 0,
                  background: positive ? C.green : C.red,
                  borderRadius: 2,
                }} />
                <div style={{
                  position: 'absolute',
                  left: '50%',
                  top: 0, bottom: 0,
                  width: 1,
                  background: C.border3,
                }} />
              </div>
              <div style={{
                width: 90, fontSize: 12, fontVariantNumeric: 'tabular-nums',
                color: positive ? C.green : C.red,
                textAlign: 'right', flexShrink: 0,
              }}>
                {formatInr(r.netPnl)}
              </div>
              <div style={{ width: 50, fontSize: 11, color: C.textMuted, textAlign: 'right', flexShrink: 0 }}>
                {(r.winRate * 100).toFixed(0)}% W
              </div>
            </div>
          )
        })}
      </div>
    </div>
  )
}

// ── Tax Lots section ──────────────────────────────────────────────────────────

function TaxLotsSection() {
  const currentFY = (() => {
    const now = new Date()
    const year = now.getMonth() >= 3 ? now.getFullYear() : now.getFullYear() - 1
    return `${year}-${String(year + 1).slice(2)}`
  })()

  const [fy, setFy] = useState(currentFY)

  const { data, isLoading, isError } = useQuery({
    queryKey: ['tax-lots', fy],
    queryFn: () => tradeJournalApi.getTaxLots(fy),
  })

  const lots = data?.data?.data ?? []

  const handleExport = async () => {
    try {
      const res = await tradeJournalApi.exportTaxLots(fy)
      const url = URL.createObjectURL(res.data as Blob)
      const a = document.createElement('a')
      a.href = url
      a.download = `tax-lots-${fy}.csv`
      a.click()
      URL.revokeObjectURL(url)
    } catch {
      // ignore
    }
  }

  return (
    <div style={{
      background: C.surface, border: `1px solid ${C.border}`,
      borderRadius: 8, padding: 16,
    }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 12 }}>
        <div style={{ fontSize: 12, fontWeight: 700, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.06em' }}>
          Tax Lots (ITR-3)
        </div>
        <select
          value={fy}
          onChange={e => setFy(e.target.value)}
          style={{
            background: C.surface2, border: `1px solid ${C.border}`,
            borderRadius: 5, color: C.text, fontSize: 12, padding: '3px 8px',
          }}
        >
          {[-1, 0, 1].map(d => {
            const now = new Date()
            const y = (now.getMonth() >= 3 ? now.getFullYear() : now.getFullYear() - 1) + d
            const label = `${y}-${String(y + 1).slice(2)}`
            return <option key={label} value={label}>{label}</option>
          })}
        </select>
        <div style={{ flex: 1 }} />
        <button
          onClick={handleExport}
          disabled={lots.length === 0}
          style={{
            padding: '4px 12px', borderRadius: 5, border: `1px solid ${C.border}`,
            background: lots.length > 0 ? C.blue : 'transparent',
            color: lots.length > 0 ? '#fff' : C.textMuted,
            cursor: lots.length > 0 ? 'pointer' : 'default', fontSize: 12,
          }}
        >Export CSV</button>
      </div>

      {isLoading && <div style={{ color: C.textMuted, fontSize: 13 }}>Loading…</div>}
      {isError && <div style={{ color: C.red, fontSize: 13 }}>Failed to load tax lots.</div>}

      {!isLoading && !isError && lots.length === 0 && (
        <div style={{ color: C.textMuted, fontSize: 13 }}>No closed trades for FY {fy}.</div>
      )}

      {lots.length > 0 && (
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ background: C.surface2 }}>
              {['Symbol', 'Open Date', 'Close Date', 'Qty', 'Buy Price', 'Sell Price', 'Gross P&L', 'Class', 'Days'].map(h => (
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
            {lots.map((lot, i) => (
              <tr key={i}>
                <td style={{ padding: '4px 10px', fontSize: 12, color: C.text, borderBottom: `1px solid ${C.border2}` }}>{lot.symbol}</td>
                <td style={{ padding: '4px 10px', fontSize: 12, color: C.textSub, borderBottom: `1px solid ${C.border2}` }}>{lot.openDate?.slice(0, 10)}</td>
                <td style={{ padding: '4px 10px', fontSize: 12, color: C.textSub, borderBottom: `1px solid ${C.border2}` }}>{lot.closeDate?.slice(0, 10)}</td>
                <td style={{ padding: '4px 10px', fontSize: 12, color: C.text, borderBottom: `1px solid ${C.border2}` }}>{lot.quantity}</td>
                <td style={{ padding: '4px 10px', fontSize: 12, fontVariantNumeric: 'tabular-nums', color: C.text, borderBottom: `1px solid ${C.border2}` }}>{formatInr(lot.buyPrice)}</td>
                <td style={{ padding: '4px 10px', fontSize: 12, fontVariantNumeric: 'tabular-nums', color: C.text, borderBottom: `1px solid ${C.border2}` }}>{formatInr(lot.sellPrice)}</td>
                <td style={{ padding: '4px 10px', fontSize: 12, fontVariantNumeric: 'tabular-nums', color: lot.grossPnl >= 0 ? C.green : C.red, borderBottom: `1px solid ${C.border2}` }}>{formatInr(lot.grossPnl)}</td>
                <td style={{ padding: '4px 10px', fontSize: 11, borderBottom: `1px solid ${C.border2}` }}>
                  <span style={{
                    padding: '1px 6px', borderRadius: 3, fontWeight: 700,
                    background: lot.classification === 'Speculative' ? C.amberBg : lot.classification === 'LTCG' ? C.greenBg : C.blueBg,
                    color: lot.classification === 'Speculative' ? C.amber : lot.classification === 'LTCG' ? C.green : C.blue,
                  }}>{lot.classification}</span>
                </td>
                <td style={{ padding: '4px 10px', fontSize: 12, color: C.textSub, borderBottom: `1px solid ${C.border2}` }}>{lot.holdingDays}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}

// ── Main Page ─────────────────────────────────────────────────────────────────

export function PortfolioAnalysisPage() {
  const [from, setFrom] = useState('')
  const [to,   setTo]   = useState('')

  const { data, isLoading, isError } = useQuery({
    queryKey: ['pnl-attribution', from, to],
    queryFn: () => tradeJournalApi.getAttribution({
      from: from || undefined,
      to:   to   || undefined,
    }),
  })

  const attr = data?.data?.data

  const inputStyle: React.CSSProperties = {
    background: C.surface2, border: `1px solid ${C.border}`,
    borderRadius: 5, color: C.text, fontSize: 12, padding: '4px 8px',
  }

  return (
    <div style={{ padding: CONTENT_PAD }}>
      {/* Header + date filter */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 16 }}>
        <div style={{ fontSize: 18, fontWeight: 700, color: C.text }}>P&amp;L Analysis</div>
        <div style={{ flex: 1 }} />
        <span style={{ fontSize: 12, color: C.textMuted }}>From</span>
        <input type="date" value={from} onChange={e => setFrom(e.target.value)} style={inputStyle} />
        <span style={{ fontSize: 12, color: C.textMuted }}>To</span>
        <input type="date" value={to}   onChange={e => setTo(e.target.value)}   style={inputStyle} />
        {(from || to) && (
          <button
            onClick={() => { setFrom(''); setTo('') }}
            style={{ ...inputStyle, cursor: 'pointer', color: C.textMuted }}
          >Clear</button>
        )}
      </div>

      {isLoading && (
        <div style={{ color: C.textMuted, fontSize: 13, padding: 32, textAlign: 'center' }}>
          Loading attribution data…
        </div>
      )}

      {isError && (
        <div style={{ color: C.red, fontSize: 13, padding: 32, textAlign: 'center' }}>
          Failed to load attribution data.
        </div>
      )}

      {attr && (
        <>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16, marginBottom: 16 }}>
            <BarChart rows={attr.bySymbol    ?? []} title="By Symbol" />
            <BarChart rows={attr.byMonth     ?? []} title="By Month" />
            <BarChart rows={attr.byDayOfWeek ?? []} title="By Day of Week" />
            <BarChart rows={attr.byExitType  ?? []} title="By Exit Type" />
          </div>
          {/* P9: cross-strategy breakdown — only shown when byStrategy is populated (all-strategies view) */}
          {(attr.byStrategy ?? []).length > 0 && (
            <div style={{ marginBottom: 16 }}>
              <BarChart rows={attr.byStrategy!} title="By Strategy" />
            </div>
          )}
        </>
      )}

      {!isLoading && !isError && !attr && (
        <div style={{ color: C.textMuted, fontSize: 13, padding: 32, textAlign: 'center' }}>
          No attribution data yet. Run backtests or forward tests to populate the trade journal.
        </div>
      )}

      <TaxLotsSection />
    </div>
  )
}
