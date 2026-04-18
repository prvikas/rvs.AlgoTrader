import React, { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { screenerApi, ScreenerResult } from '../api/client'
import { C, CONTENT_PAD, TABLE_CELL, TABLE_HEADER_CELL } from '../styles/tokens'
import { formatInr, formatIst } from '../utils/datetime'

// ── Signal badge ──────────────────────────────────────────────────────────────

function SignalBadge({ signal }: { signal: string }) {
  const cfg: Record<string, { bg: string; fg: string }> = {
    VCP_BREAKOUT:   { bg: C.greenBg,  fg: C.green  },
    NEAR_BREAKOUT:  { bg: C.amberBg,  fg: C.amber  },
    UPTREND:        { bg: C.blueBg,   fg: C.blue   },
  }
  const s = cfg[signal] ?? { bg: C.surface2, fg: C.textSub }
  const label = signal.replace(/_/g, ' ')
  return (
    <span style={{
      padding: '2px 8px', borderRadius: 3, fontSize: 10, fontWeight: 700,
      background: s.bg, color: s.fg, letterSpacing: '0.04em',
    }}>
      {label}
    </span>
  )
}

// ── Volume indicator ──────────────────────────────────────────────────────────

function VolumeIcon({ confirmed }: { confirmed: boolean }) {
  return (
    <span style={{
      fontSize: 11, fontWeight: 600,
      color: confirmed ? C.green : C.textMuted,
    }}>
      {confirmed ? '▲ Vol' : '—'}
    </span>
  )
}

// ── Compact sparkline-like price position bar ─────────────────────────────────

function PriceBar({ low, high, close }: { low: number; high: number; close: number }) {
  if (high <= low) return null
  const pct = ((close - low) / (high - low)) * 100
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 6, minWidth: 120 }}>
      <div style={{ fontSize: 10, color: C.textMuted, width: 28, textAlign: 'right' }}>
        {pct.toFixed(0)}%
      </div>
      <div style={{
        flex: 1, height: 6, background: C.surface2,
        borderRadius: 3, position: 'relative', overflow: 'hidden',
      }}>
        <div style={{
          position: 'absolute', left: 0, top: 0, bottom: 0,
          width: `${pct}%`, background: pct >= 90 ? C.green : pct >= 70 ? C.amber : C.blue,
          borderRadius: 3,
        }} />
      </div>
      <div style={{ fontSize: 10, color: C.textMuted, width: 20 }}>52w</div>
    </div>
  )
}

// ── Main Page ─────────────────────────────────────────────────────────────────

export function ScreenerPage() {
  const [signal,      setSignal]      = useState<string>('')
  const [minRsScore,  setMinRsScore]  = useState(50)
  const [maxResults,  setMaxResults]  = useState(50)
  const [sortCol,     setSortCol]     = useState<keyof ScreenerResult>('rsScore')
  const [sortAsc,     setSortAsc]     = useState(false)

  const { data, isLoading, isError, refetch, isFetching } = useQuery({
    queryKey: ['screener', signal, minRsScore, maxResults],
    queryFn: () => screenerApi.scan({
      signal:     signal || undefined,
      minRsScore,
      maxResults,
    }).then(r => r.data.data ?? []),
    staleTime: 5 * 60 * 1000,
  })

  const results: ScreenerResult[] = (data ?? []).slice().sort((a, b) => {
    const av = a[sortCol] as number | string | boolean
    const bv = b[sortCol] as number | string | boolean
    if (av < bv) return sortAsc ? -1 :  1
    if (av > bv) return sortAsc ?  1 : -1
    return 0
  })

  function toggleSort(col: keyof ScreenerResult) {
    if (sortCol === col) setSortAsc(v => !v)
    else { setSortCol(col); setSortAsc(false) }
  }

  function SortIcon({ col }: { col: keyof ScreenerResult }) {
    if (sortCol !== col) return <span style={{ color: C.textMuted, fontSize: 9 }}> ⇅</span>
    return <span style={{ color: C.blue, fontSize: 9 }}> {sortAsc ? '↑' : '↓'}</span>
  }

  const inputStyle: React.CSSProperties = {
    background: C.surface2, border: `1px solid ${C.border}`,
    borderRadius: 5, color: C.text, fontSize: 12, padding: '4px 8px',
  }

  const thStyle: React.CSSProperties = {
    padding: TABLE_HEADER_CELL,
    textAlign: 'left', fontSize: 10, fontWeight: 700, color: C.textMuted,
    textTransform: 'uppercase', letterSpacing: '0.05em',
    borderBottom: `1px solid ${C.border}`,
    cursor: 'pointer', userSelect: 'none',
  }

  return (
    <div style={{ padding: CONTENT_PAD }}>
      {/* Header + controls */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 16 }}>
        <div style={{ fontSize: 18, fontWeight: 700, color: C.text }}>Screener</div>
        {results.length > 0 && (
          <div style={{ fontSize: 12, color: C.textMuted }}>
            {results.length} result{results.length !== 1 ? 's' : ''}
          </div>
        )}
        <div style={{ flex: 1 }} />

        {/* Signal filter */}
        <select value={signal} onChange={e => setSignal(e.target.value)} style={inputStyle}>
          <option value="">All Signals</option>
          <option value="VCP_BREAKOUT">VCP Breakout</option>
          <option value="NEAR_BREAKOUT">Near Breakout</option>
          <option value="UPTREND">Uptrend</option>
        </select>

        {/* RS score */}
        <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
          <span style={{ fontSize: 12, color: C.textMuted }}>Min RS</span>
          <input
            type="number" min={0} max={100} step={5}
            value={minRsScore}
            onChange={e => setMinRsScore(Number(e.target.value))}
            style={{ ...inputStyle, width: 60, textAlign: 'center' }}
          />
        </div>

        {/* Max results */}
        <select value={maxResults} onChange={e => setMaxResults(Number(e.target.value))} style={inputStyle}>
          <option value={25}>Top 25</option>
          <option value={50}>Top 50</option>
          <option value={100}>Top 100</option>
          <option value={200}>Top 200</option>
        </select>

        <button
          onClick={() => refetch()}
          disabled={isFetching}
          style={{
            padding: '4px 14px', borderRadius: 5, border: `1px solid ${C.border}`,
            background: C.surface2, color: isFetching ? C.textMuted : C.text,
            cursor: isFetching ? 'default' : 'pointer', fontSize: 12,
          }}
        >
          {isFetching ? 'Scanning…' : 'Scan'}
        </button>
      </div>

      {/* State messages */}
      {isLoading && (
        <div style={{ color: C.textMuted, fontSize: 13, padding: 48, textAlign: 'center' }}>
          Running screener…
        </div>
      )}

      {isError && (
        <div style={{ color: C.red, fontSize: 13, padding: 32, textAlign: 'center' }}>
          Screener failed. Ensure historical candle data is loaded.
        </div>
      )}

      {!isLoading && !isError && results.length === 0 && (
        <div style={{
          color: C.textMuted, fontSize: 13, padding: 48, textAlign: 'center',
          background: C.surface, borderRadius: 8, border: `1px solid ${C.border}`,
        }}>
          No symbols matched the current filters.
        </div>
      )}

      {/* Results table */}
      {results.length > 0 && (
        <div style={{
          background: C.surface, border: `1px solid ${C.border}`,
          borderRadius: 8, overflow: 'hidden',
        }}>
          <table style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead>
              <tr style={{ background: C.surface2 }}>
                <th onClick={() => toggleSort('symbol')}         style={thStyle}>Symbol <SortIcon col="symbol" /></th>
                <th onClick={() => toggleSort('signal')}         style={thStyle}>Signal <SortIcon col="signal" /></th>
                <th onClick={() => toggleSort('lastClose')}      style={{ ...thStyle, textAlign: 'right' as const }}>Last Close <SortIcon col="lastClose" /></th>
                <th onClick={() => toggleSort('rsScore')}        style={{ ...thStyle, textAlign: 'right' as const }}>RS Score <SortIcon col="rsScore" /></th>
                <th style={thStyle}>52w Position</th>
                <th onClick={() => toggleSort('sma200')}         style={{ ...thStyle, textAlign: 'right' as const }}>SMA 200 <SortIcon col="sma200" /></th>
                <th onClick={() => toggleSort('sma50')}          style={{ ...thStyle, textAlign: 'right' as const }}>SMA 50 <SortIcon col="sma50" /></th>
                <th onClick={() => toggleSort('volumeConfirmed')} style={thStyle}>Volume <SortIcon col="volumeConfirmed" /></th>
                <th onClick={() => toggleSort('scannedAt')}      style={thStyle}>Scanned <SortIcon col="scannedAt" /></th>
              </tr>
            </thead>
            <tbody>
              {results.map(r => (
                <tr
                  key={r.symbol}
                  style={{ borderBottom: `1px solid ${C.border2}` }}
                  onMouseEnter={e => (e.currentTarget.style.background = C.surface2)}
                  onMouseLeave={e => (e.currentTarget.style.background = '')}
                >
                  <td style={{ padding: TABLE_CELL, fontWeight: 700, color: C.text }}>{r.symbol}</td>
                  <td style={{ padding: TABLE_CELL }}><SignalBadge signal={r.signal} /></td>
                  <td style={{ padding: TABLE_CELL, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                    {formatInr(r.lastClose)}
                  </td>
                  <td style={{ padding: TABLE_CELL, textAlign: 'right' }}>
                    <span style={{
                      fontVariantNumeric: 'tabular-nums',
                      color: r.rsScore >= 80 ? C.green : r.rsScore >= 60 ? C.amber : C.textSub,
                      fontWeight: r.rsScore >= 80 ? 700 : 400,
                    }}>
                      {r.rsScore.toFixed(1)}
                    </span>
                  </td>
                  <td style={{ padding: TABLE_CELL }}>
                    <PriceBar low={r.yearLow} high={r.yearHigh} close={r.lastClose} />
                  </td>
                  <td style={{ padding: TABLE_CELL, textAlign: 'right', fontVariantNumeric: 'tabular-nums', color: C.textSub }}>
                    {formatInr(r.sma200)}
                  </td>
                  <td style={{ padding: TABLE_CELL, textAlign: 'right', fontVariantNumeric: 'tabular-nums', color: C.textSub }}>
                    {formatInr(r.sma50)}
                  </td>
                  <td style={{ padding: TABLE_CELL }}>
                    <VolumeIcon confirmed={r.volumeConfirmed} />
                  </td>
                  <td style={{ padding: TABLE_CELL, color: C.textMuted, fontSize: 11 }}>
                    {formatIst(r.scannedAt)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
