import { useState, useEffect, useRef } from 'react'
import { useQuery } from '@tanstack/react-query'
import { instrumentsApi, historicalApi, dataManagerApi, brokerApi, DataQualityReport, Instrument } from '../api/client'

// ─── Types ───────────────────────────────────────────────────────────────────

interface DownloadForm {
  timeframe: string
  fromDate: string
  toDate: string
  brokerName: string
}

type SortDir = 'asc' | 'desc'

// ─── Constants ───────────────────────────────────────────────────────────────

const TIMEFRAMES = ['1m', '3m', '5m', '15m', '30m', '1h', '1D']
const KNOWN_EXCHANGES = ['NSE', 'BSE', 'NFO', 'BFO', 'CDS', 'MCX', 'BCD']
const INSTRUMENT_TYPES = ['All Types', 'Equity', 'Futures', 'Options', 'Index']
// BROKERS is intentionally not hardcoded — derived from brokerApi.status() at runtime
const PAGE_SIZE = 50

const inp: React.CSSProperties = {
  padding: '7px 10px',
  background: '#0f0f1a',
  border: '1px solid #2d2d3f',
  borderRadius: 6,
  color: '#e2e8f0',
  fontSize: 13,
}

// ─── Column definitions ───────────────────────────────────────────────────────

interface ColDef {
  label: string
  sortKey?: string   // server sortBy value; undefined = not sortable
  width: string
}

const COLUMNS: ColDef[] = [
  { label: 'Internal Symbol', sortKey: 'symbol',   width: '2fr' },
  { label: 'Name',            sortKey: 'name',     width: '1.4fr' },
  { label: 'Trading Symbol',  sortKey: 'trading',  width: '1fr' },
  { label: 'Exchange',        sortKey: 'exchange', width: '80px' },
  { label: 'Type',            sortKey: 'type',     width: '80px' },
  { label: 'Brokers',                              width: '80px' },
  { label: 'Data Through',                         width: '110px' },
  { label: 'Status',                               width: '72px' },
  { label: 'Actions',                              width: '90px' },
]

const gridCols = COLUMNS.map(c => c.width).join(' ')

// ─── InstrumentsPage ──────────────────────────────────────────────────────────

export function InstrumentsPage({ onGoToRefresh }: { onGoToRefresh?: () => void }) {
  // ── Filter / sort / page state ───────────────────────────────────────────
  const [search, setSearch]                   = useState('')
  const [debouncedSearch, setDebouncedSearch] = useState('')
  const [exchange, setExchange]               = useState<string>('all')
  const [instrType, setInstrType]             = useState<string>('All Types')
  const [activeOnly, setActiveOnly]           = useState(true)
  const [mergeDuplicates, setMergeDuplicates] = useState(true)   // on by default
  const [page, setPage]                       = useState(1)
  const [sortBy, setSortBy]                   = useState<string>('symbol')
  const [sortDir, setSortDir]                 = useState<SortDir>('asc')

  // Download modal
  const todayStr = new Date().toISOString().slice(0, 10)
  const [downloadTarget, setDownloadTarget] = useState<Instrument | null>(null)
  const [downloadForm, setDownloadForm]     = useState<DownloadForm>({
    timeframe: '1D',
    fromDate: (() => {
      const d = new Date()
      d.setFullYear(d.getFullYear() - 2)
      return d.toISOString().slice(0, 10)
    })(),
    toDate: todayStr,
    brokerName: 'MStock',
  })
  const [downloadMsg, setDownloadMsg] = useState<{ type: 'ok' | 'err'; text: string } | null>(null)
  const [downloadPending, setDownloadPending] = useState(false)

  // Debounce search
  const debounce = useRef<ReturnType<typeof setTimeout> | null>(null)
  useEffect(() => {
    if (debounce.current) clearTimeout(debounce.current)
    debounce.current = setTimeout(() => {
      setDebouncedSearch(search)
      setPage(1)    // reset to page 1 on new search
    }, 350)
  }, [search])

  // Reset page when filters/sort change
  const changeExchange = (v: string) => { setExchange(v); setPage(1) }
  const changeInstrType = (v: string) => { setInstrType(v); setPage(1) }
  const changeActiveOnly = (v: boolean) => { setActiveOnly(v); setPage(1) }

  // ── Sort toggle ──────────────────────────────────────────────────────────
  const handleSort = (key: string) => {
    if (sortBy === key) {
      setSortDir(d => d === 'asc' ? 'desc' : 'asc')
    } else {
      setSortBy(key)
      setSortDir('asc')
    }
    setPage(1)
  }

  // ── Server-side data ─────────────────────────────────────────────────────
  const { data, isLoading, isFetching } = useQuery({
    queryKey: ['instruments', debouncedSearch, exchange, instrType, activeOnly, sortBy, sortDir, page],
    queryFn: () => instrumentsApi.list({
      search:         debouncedSearch || undefined,
      exchange:       exchange === 'all' ? undefined : exchange,
      instrumentType: instrType === 'All Types' ? undefined : instrType,
      active:         activeOnly ? true : undefined,
      sortBy,
      sortDir,
      page,
      pageSize: PAGE_SIZE,
    }).then(r => r.data.data ?? null),
    staleTime: 30_000,
    placeholderData: (prev) => prev,   // keep old data visible while fetching next page
  })

  // ── Broker status — pre-select connected broker in download modal ─────────
  const { data: brokerStatuses } = useQuery({
    queryKey: ['broker-status'],
    queryFn: () => brokerApi.status().then(r => r.data.data ?? []),
    staleTime: 60_000,
  })
  // Determine default broker (first authenticated one, prefer MStock)
  const connectedBroker = (() => {
    if (!brokerStatuses) return 'MStock'
    const authed = brokerStatuses.filter(b => b.isAuthenticated)
    return authed.find(b => b.brokerName === 'MStock')?.brokerName
        ?? authed.find(b => b.brokerName === 'Zerodha')?.brokerName
        ?? authed[0]?.brokerName
        ?? 'MStock'
  })()

  // ── Data quality / history-downloaded dates ───────────────────────────────
  const { data: qualityReports } = useQuery({
    queryKey: ['data-quality-all'],
    queryFn: () => dataManagerApi.qualityAll('1D').then(r => r.data.data ?? []),
    staleTime: 5 * 60_000,
  })
  // Build a fast lookup: internalSymbol → report
  const qualityMap: Record<string, DataQualityReport> = {}
  for (const q of qualityReports ?? []) qualityMap[q.internalSymbol] = q

  const rawInstruments = data?.items ?? []
  const totalCount     = data?.totalCount ?? 0
  const totalPages     = Math.max(1, Math.ceil(totalCount / PAGE_SIZE))

  // ── Duplicate detection + merge (same tradingSymbol+exchange from multiple broker refreshes) ──
  // Different brokers assign different internal codes to the same underlying script.
  // When mergeDuplicates=true (default) combine all broker tokens onto one row so the user
  // sees a single entry with all broker availability dots populated correctly.
  const tradingSymbolCounts: Record<string, number> = {}
  for (const inst of rawInstruments) {
    const key = `${inst.exchange}:${inst.tradingSymbol}`
    tradingSymbolCounts[key] = (tradingSymbolCounts[key] ?? 0) + 1
  }
  const duplicateGroupCount = Object.values(tradingSymbolCounts).filter(n => n > 1).length

  const instruments: Instrument[] = mergeDuplicates
    ? (() => {
        // Merge: group by tradingSymbol+exchange, union all brokerTokens.
        // Prefer the MStock row as the "primary" (for internalSymbol display), then Zerodha, then first seen.
        const groups = new Map<string, Instrument[]>()
        for (const inst of rawInstruments) {
          const key = `${inst.exchange}:${inst.tradingSymbol}`
          const g = groups.get(key) ?? []
          g.push(inst)
          groups.set(key, g)
        }
        return Array.from(groups.values()).map(rows => {
          // Pick primary row: prefer MStock, else Zerodha, else first
          const primary =
            rows.find(r => 'MStock'  in r.brokerTokens) ??
            rows.find(r => 'Zerodha' in r.brokerTokens) ??
            rows[0]
          // Merge all broker tokens into the primary
          const mergedTokens = Object.assign({}, ...rows.map(r => r.brokerTokens))
          return { ...primary, brokerTokens: mergedTokens }
        })
      })()
    : rawInstruments

  // ── Sync connected broker into modal default when modal opens ────────────
  // (runs whenever downloadTarget changes from null → instrument)
  useEffect(() => {
    if (downloadTarget) {
      setDownloadForm(f => ({ ...f, brokerName: connectedBroker }))
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [downloadTarget])

  // ── Download handler ──────────────────────────────────────────────────────
  const handleDownload = async () => {
    if (!downloadTarget) return
    setDownloadPending(true)
    setDownloadMsg(null)
    try {
      await historicalApi.downloadHistory(
        downloadTarget.internalSymbol,
        downloadForm.timeframe,
        downloadForm.fromDate,
        downloadForm.toDate,
        downloadForm.brokerName,
      )
      setDownloadMsg({
        type: 'ok',
        text: `Job queued — ${downloadTarget.internalSymbol} ${downloadForm.timeframe} via ${downloadForm.brokerName} (${downloadForm.fromDate} → ${downloadForm.toDate}).`,
      })
      setTimeout(() => setDownloadTarget(null), 3000)
    } catch (err: any) {
      setDownloadMsg({
        type: 'err',
        text: err?.response?.data?.error ?? 'Download failed. Check broker connection and logs.',
      })
    } finally {
      setDownloadPending(false)
    }
  }

  // ─────────────────────────────────────────────────────────────────────────────

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>

      {/* ── Page header ──────────────────────────────────────────────────────── */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: 12 }}>
        <div>
          <h2 style={{ fontSize: 20, fontWeight: 700, margin: 0 }}>Instruments</h2>
          <p style={{ fontSize: 12, color: '#64748b', margin: '4px 0 0 0' }}>
            Unified symbol master — NSE, BSE, NFO, BFO, CDS, MCX.
            {totalCount > 0 && (
              <> <strong style={{ color: '#60a5fa' }}>{totalCount.toLocaleString()} symbols</strong> match current filters.</>
            )}
          </p>
        </div>

        <button
          onClick={onGoToRefresh}
          style={{
            padding: '8px 18px',
            background: '#6366f1',
            color: '#fff', border: 'none', borderRadius: 6,
            fontSize: 13, fontWeight: 600,
            cursor: 'pointer',
            display: 'flex', alignItems: 'center', gap: 7,
          }}
        >
          ⬇ Download Master Data
        </button>
      </div>

      {/* ── Filter bar ───────────────────────────────────────────────────────── */}
      <div style={{
        background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 8,
        padding: '12px 16px', display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'center',
      }}>
        <input
          type="text"
          placeholder="Search symbol or name…"
          value={search}
          onChange={e => setSearch(e.target.value)}
          style={{ ...inp, flexGrow: 1, minWidth: 220 }}
        />

        <select value={exchange} onChange={e => changeExchange(e.target.value)} style={inp} aria-label="Exchange filter">
          <option value="all">All Exchanges</option>
          {KNOWN_EXCHANGES.map(ex => <option key={ex} value={ex}>{ex}</option>)}
        </select>

        <select value={instrType} onChange={e => changeInstrType(e.target.value)} style={inp} aria-label="Instrument type filter">
          {INSTRUMENT_TYPES.map(t => <option key={t} value={t}>{t}</option>)}
        </select>

        <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12, color: '#94a3b8', cursor: 'pointer', whiteSpace: 'nowrap' }}>
          <input type="checkbox" checked={activeOnly} onChange={e => changeActiveOnly(e.target.checked)} />
          Active only
        </label>

        {duplicateGroupCount > 0 && (
          <label
            title="Same script appears from multiple brokers with different internal codes. Merging combines all broker tokens into one row."
            style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12, cursor: 'pointer', whiteSpace: 'nowrap' }}
          >
            <input
              type="checkbox"
              checked={mergeDuplicates}
              onChange={e => setMergeDuplicates(e.target.checked)}
            />
            <span style={{
              padding: '2px 8px', borderRadius: 4, fontSize: 11, fontWeight: 600,
              background: mergeDuplicates ? '#14532d22' : '#7c2d1222',
              color: mergeDuplicates ? '#10b981' : '#f97316',
              border: `1px solid ${mergeDuplicates ? '#14532d44' : '#7c2d1244'}`,
            }}>
              {mergeDuplicates
                ? `✓ ${duplicateGroupCount} scripts merged (multi-broker)`
                : `${duplicateGroupCount} duplicate trading symbols — merge?`}
            </span>
          </label>
        )}

        {!isLoading && totalCount > 0 && (
          <span style={{ fontSize: 12, color: '#475569', whiteSpace: 'nowrap', marginLeft: 'auto' }}>
            {instruments.length.toLocaleString()} of {totalCount.toLocaleString()} shown
            {mergeDuplicates && instruments.length < rawInstruments.length && (
              <span style={{ color: '#10b981' }}> ({rawInstruments.length - instruments.length} merged)</span>
            )}
          </span>
        )}
      </div>

      {/* ── Instruments table ─────────────────────────────────────────────────── */}
      <div style={{ background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 8, overflow: 'hidden' }}>

        {/* Sortable header */}
        <div style={{
          display: 'grid',
          gridTemplateColumns: gridCols,
          background: '#161628',
          borderBottom: '1px solid #2d2d3f',
          padding: '8px 16px',
        }}>
          {COLUMNS.map(col => (
            <button
              key={col.label}
              onClick={col.sortKey ? () => handleSort(col.sortKey!) : undefined}
              disabled={!col.sortKey}
              style={{
                background: 'none', border: 'none', padding: 0,
                fontSize: 11, fontWeight: 700, color: sortBy === col.sortKey ? '#818cf8' : '#64748b',
                textTransform: 'uppercase', letterSpacing: '0.05em',
                cursor: col.sortKey ? 'pointer' : 'default',
                textAlign: 'left', display: 'flex', alignItems: 'center', gap: 4,
              }}
            >
              {col.label}
              {col.sortKey && (
                <span style={{ opacity: sortBy === col.sortKey ? 1 : 0.3 }}>
                  {sortBy === col.sortKey ? (sortDir === 'asc' ? '↑' : '↓') : '↕'}
                </span>
              )}
            </button>
          ))}
        </div>

        {/* Loading overlay — show spinner row when fetching */}
        {(isLoading || isFetching) && instruments.length === 0 && (
          <div style={{ padding: 32, textAlign: 'center', color: '#64748b', fontSize: 13 }}>
            <SpinIcon /> Loading instruments…
          </div>
        )}

        {/* Empty state */}
        {!isLoading && !isFetching && instruments.length === 0 && (
          <div style={{ padding: 32, textAlign: 'center', color: '#64748b', fontSize: 13 }}>
            {totalCount === 0
              ? <>No instruments found. Click <strong style={{ color: '#e2e8f0' }}>↻ Refresh Master Data</strong> to download from your brokers.</>
              : 'No instruments match the current filters.'}
          </div>
        )}

        {/* Rows — dim slightly while background-fetching (page change / sort) */}
        <div style={{ opacity: isFetching && instruments.length > 0 ? 0.6 : 1, transition: 'opacity 0.15s' }}>
          {instruments.map((inst, idx) => (
            <InstrumentRow
              key={inst.internalSymbol}
              instrument={inst}
              even={idx % 2 === 0}
              quality={qualityMap[inst.internalSymbol]}
              onDownload={() => { setDownloadTarget(inst); setDownloadMsg(null) }}
            />
          ))}
        </div>
      </div>

      {/* ── Pagination ──────────────────────────────────────────────────────── */}
      {totalPages > 1 && (
        <div style={{ display: 'flex', justifyContent: 'center', gap: 8, alignItems: 'center' }}>
          <button onClick={() => setPage(1)} disabled={page === 1} style={pageBtnStyle(page > 1)}>
            «
          </button>
          <button onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page === 1} style={pageBtnStyle(page > 1)}>
            ← Prev
          </button>

          {/* Page number pills */}
          {pageRange(page, totalPages).map((p, i) =>
            p === '…' ? (
              <span key={`ellipsis-${i}`} style={{ color: '#4b5563', fontSize: 12, padding: '0 4px' }}>…</span>
            ) : (
              <button
                key={p}
                onClick={() => setPage(Number(p))}
                style={{
                  padding: '5px 10px', borderRadius: 6,
                  border: `1px solid ${p === page ? '#6366f1' : '#2d2d3f'}`,
                  background: p === page ? '#312e81' : '#1a1a2e',
                  color: p === page ? '#a5b4fc' : '#64748b',
                  fontSize: 12, cursor: 'pointer',
                }}
              >
                {p}
              </button>
            )
          )}

          <button onClick={() => setPage(p => Math.min(totalPages, p + 1))} disabled={page >= totalPages} style={pageBtnStyle(page < totalPages)}>
            Next →
          </button>
          <button onClick={() => setPage(totalPages)} disabled={page >= totalPages} style={pageBtnStyle(page < totalPages)}>
            »
          </button>

          <span style={{ fontSize: 12, color: '#475569', marginLeft: 4 }}>
            {((page - 1) * PAGE_SIZE + 1).toLocaleString()}–{Math.min(page * PAGE_SIZE, totalCount).toLocaleString()} of {totalCount.toLocaleString()}
          </span>
        </div>
      )}

      {/* ── Download History Modal ──────────────────────────────────────────── */}
      {downloadTarget && (
        <div style={{
          position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.75)', zIndex: 300,
          display: 'flex', alignItems: 'center', justifyContent: 'center',
        }}>
          <div style={{
            background: '#1e1e2e', border: '1px solid #3b82f6', borderRadius: 10,
            padding: 28, width: 420, maxWidth: '95vw',
          }}>
            <h3 style={{ margin: '0 0 4px 0', fontSize: 15, fontWeight: 700 }}>↓ Download Historical Data</h3>
            <p style={{ fontSize: 12, color: '#64748b', margin: '0 0 20px 0' }}>
              Queues a Hangfire job to fetch candle data from your broker and store in TimescaleDB.
            </p>

            <div style={{ background: '#0f172a', borderRadius: 6, padding: '8px 12px', marginBottom: 16 }}>
              <span style={{ fontSize: 12, color: '#94a3b8' }}>Instrument: </span>
              <span style={{ fontSize: 13, fontWeight: 700, color: '#93c5fd' }}>{downloadTarget.internalSymbol}</span>
              {downloadTarget.name && (
                <span style={{ fontSize: 11, color: '#64748b', marginLeft: 8 }}>{downloadTarget.name}</span>
              )}
            </div>

            {downloadMsg && (
              <Msg type={downloadMsg.type} text={downloadMsg.text} onClose={() => setDownloadMsg(null)} />
            )}

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, marginBottom: 12 }}>
              <div>
                <label style={{ fontSize: 12, fontWeight: 600, color: '#94a3b8', display: 'block', marginBottom: 6 }}>Timeframe</label>
                <select
                  value={downloadForm.timeframe}
                  onChange={e => setDownloadForm({ ...downloadForm, timeframe: e.target.value })}
                  style={{ ...inp, width: '100%' }}
                >
                  {TIMEFRAMES.map(tf => <option key={tf} value={tf}>{tf}</option>)}
                </select>
              </div>
              <div>
                <label style={{ fontSize: 12, fontWeight: 600, color: '#94a3b8', display: 'block', marginBottom: 6 }}>
                  Broker
                  {connectedBroker && (
                    <span style={{ marginLeft: 6, fontSize: 10, color: '#10b981', fontWeight: 400 }}>
                      ● {connectedBroker} connected
                    </span>
                  )}
                </label>
                <select
                  value={downloadForm.brokerName}
                  onChange={e => setDownloadForm({ ...downloadForm, brokerName: e.target.value })}
                  style={{ ...inp, width: '100%' }}
                >
                  {(['MStock', 'Zerodha', 'Upstox'] as const).map(b => {
                    const status = brokerStatuses?.find(s => s.brokerName === b)
                    const connected = status?.isAuthenticated
                    return (
                      <option key={b} value={b}>
                        {b}{connected ? ' ✓' : ''}
                      </option>
                    )
                  })}
                </select>
              </div>
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, marginBottom: 16 }}>
              <div>
                <label style={{ fontSize: 12, fontWeight: 600, color: '#94a3b8', display: 'block', marginBottom: 6 }}>From Date</label>
                <input
                  type="date"
                  value={downloadForm.fromDate}
                  onChange={e => setDownloadForm({ ...downloadForm, fromDate: e.target.value })}
                  style={{ ...inp, width: '100%', boxSizing: 'border-box' }}
                />
              </div>
              <div>
                <label style={{ fontSize: 12, fontWeight: 600, color: '#94a3b8', display: 'block', marginBottom: 6 }}>To Date</label>
                <input
                  type="date"
                  value={downloadForm.toDate}
                  onChange={e => setDownloadForm({ ...downloadForm, toDate: e.target.value })}
                  style={{ ...inp, width: '100%', boxSizing: 'border-box' }}
                />
              </div>
            </div>

            <p style={{ fontSize: 11, color: '#64748b', marginBottom: 16, lineHeight: 1.5 }}>
              Broker API rate limits apply. The system automatically chunks the date range and
              respects per-broker limits. Large ranges may take a few minutes in the background.
            </p>

            <div style={{ display: 'flex', gap: 10, justifyContent: 'flex-end' }}>
              <button
                onClick={() => { setDownloadTarget(null); setDownloadMsg(null) }}
                style={{ padding: '8px 16px', background: '#2d2d3f', color: '#e2e8f0', border: 'none', borderRadius: 6, fontSize: 13, cursor: 'pointer' }}
              >
                Cancel
              </button>
              <button
                onClick={handleDownload}
                disabled={downloadPending || !downloadForm.fromDate || !downloadForm.toDate || !downloadForm.brokerName}
                style={{
                  padding: '8px 20px',
                  background: downloadPending ? '#4b5563' : '#3b82f6',
                  color: '#fff', border: 'none', borderRadius: 6, fontSize: 13, fontWeight: 600,
                  cursor: downloadPending ? 'not-allowed' : 'pointer',
                }}
              >
                {downloadPending ? '⟳ Queuing…' : '↓ Start Download'}
              </button>
            </div>
          </div>
        </div>
      )}

      <style>{`
        @keyframes progress-slide {
          0%   { transform: translateX(-100%) }
          100% { transform: translateX(350%) }
        }
      `}</style>
    </div>
  )
}

// ─── InstrumentRow ────────────────────────────────────────────────────────────

function InstrumentRow({ instrument: inst, even, quality, onDownload }: {
  instrument: Instrument
  even: boolean
  quality?: DataQualityReport
  onDownload: () => void
}) {
  return (
    <div style={{
      display: 'grid',
      gridTemplateColumns: gridCols,
      padding: '5px 16px',
      borderBottom: '1px solid #1e1e2e',
      background: even ? '#1a1a2e' : '#1c1c2e',
      alignItems: 'center',
    }}>
      {/* Internal symbol */}
      <div style={{ fontSize: 12, fontWeight: 600, color: '#e2e8f0', fontFamily: 'monospace' }}>
        {inst.internalSymbol}
      </div>

      {/* Name */}
      <div
        style={{ fontSize: 12, color: '#94a3b8', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
        title={inst.name}
      >
        {inst.name || inst.tradingSymbol}
      </div>

      {/* Trading symbol */}
      <div style={{ fontSize: 12, color: '#64748b', fontFamily: 'monospace' }}>{inst.tradingSymbol}</div>

      {/* Exchange badge */}
      <div>
        <span style={{
          fontSize: 11, padding: '2px 7px', borderRadius: 4, fontWeight: 600,
          background: exchangeBg(inst.exchange), color: exchangeFg(inst.exchange),
        }}>
          {inst.exchange}
        </span>
      </div>

      {/* Instrument type */}
      <div style={{ fontSize: 11, color: '#818cf8' }}>{inst.instrumentType || '—'}</div>

      {/* Broker availability dots */}
      <div style={{ display: 'flex', gap: 4 }}>
        <BrokerDot label="Z" active={'Zerodha' in inst.brokerTokens} color="#3b82f6" />
        <BrokerDot label="U" active={'Upstox'  in inst.brokerTokens} color="#f59e0b" />
        <BrokerDot label="M" active={'MStock'  in inst.brokerTokens} color="#10b981" />
      </div>

      {/* Data Through (history downloaded) */}
      <div>
        {quality?.hasData ? (
          <span
            title={`${quality.totalCandles.toLocaleString()} candles · ${quality.gapCount} gaps · from ${quality.firstCandle}`}
            style={{ fontSize: 11, color: '#10b981', fontFamily: 'monospace', cursor: 'help' }}
          >
            {quality.lastCandle}
          </span>
        ) : (
          <span style={{ fontSize: 11, color: '#374151' }}>No data</span>
        )}
      </div>

      {/* Status */}
      <div>
        <span style={{
          fontSize: 10, fontWeight: 700, padding: '2px 6px', borderRadius: 4,
          background: inst.isActive ? '#14532d' : '#2d1a1a',
          color: inst.isActive ? '#86efac' : '#fca5a5',
        }}>
          {inst.isActive ? 'Active' : 'Inactive'}
        </span>
      </div>

      {/* Actions */}
      <div>
        <button
          onClick={onDownload}
          style={{
            padding: '4px 10px',
            background: quality?.hasData ? '#14353a' : '#1e3a5f',
            color:      quality?.hasData ? '#34d399'  : '#60a5fa',
            border: `1px solid ${quality?.hasData ? '#1e5a4f' : '#1e4a7f'}`,
            borderRadius: 4, fontSize: 11,
            fontWeight: 600, cursor: 'pointer',
          }}
          title={quality?.hasData ? `Last candle: ${quality.lastCandle} — click to update` : 'Download historical candle data'}
        >
          {quality?.hasData ? '↻ Update' : '↓ History'}
        </button>
      </div>
    </div>
  )
}

// ─── Broker availability dot ──────────────────────────────────────────────────

function BrokerDot({ label, active, color }: { label: string; active: boolean; color: string }) {
  return (
    <span
      title={active ? `${label === 'Z' ? 'Zerodha' : label === 'U' ? 'Upstox' : 'MStock'} token available` : 'No token'}
      style={{
        display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
        width: 18, height: 18, borderRadius: '50%', fontSize: 9, fontWeight: 700,
        background: active ? color + '33' : '#1a1a2e',
        color: active ? color : '#374151',
        border: `1px solid ${active ? color + '66' : '#2d2d3f'}`,
      }}
    >
      {label}
    </span>
  )
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

function exchangeBg(ex: string): string {
  switch (ex?.toUpperCase()) {
    case 'NSE': return '#0f3460'
    case 'BSE': return '#1a1a4e'
    case 'NFO': return '#1e3a2f'
    case 'BFO': return '#3b1f00'
    case 'CDS': return '#2d1a40'
    case 'MCX': return '#3b2200'
    default:    return '#1e2d3f'
  }
}

function exchangeFg(ex: string): string {
  switch (ex?.toUpperCase()) {
    case 'NSE': return '#60a5fa'
    case 'BSE': return '#818cf8'
    case 'NFO': return '#34d399'
    case 'BFO': return '#fb923c'
    case 'CDS': return '#c084fc'
    case 'MCX': return '#fbbf24'
    default:    return '#94a3b8'
  }
}

/** Generate page numbers with ellipsis: [1, 2, …, 8, 9, 10] */
function pageRange(current: number, total: number): (number | '…')[] {
  if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1)
  const pages: (number | '…')[] = []
  const add = (n: number) => { if (!pages.includes(n)) pages.push(n) }
  add(1)
  if (current > 3) pages.push('…')
  for (let i = Math.max(2, current - 1); i <= Math.min(total - 1, current + 1); i++) add(i)
  if (current < total - 2) pages.push('…')
  add(total)
  return pages
}

function SpinIcon() {
  return (
    <span style={{ display: 'inline-block', animation: 'spin 0.8s linear infinite', fontSize: 16 }}>
      ⟳
      <style>{`@keyframes spin { from { transform: rotate(0deg) } to { transform: rotate(360deg) } }`}</style>
    </span>
  )
}

function Msg({ type, text, onClose }: { type: 'ok' | 'err'; text: string; onClose: () => void }) {
  const ok = type === 'ok'
  return (
    <div style={{
      padding: '10px 14px', borderRadius: 6, fontSize: 12, lineHeight: 1.5, marginBottom: 8,
      background: ok ? '#14532d' : '#7f1d1d',
      border: `1px solid ${ok ? '#16a34a' : '#dc2626'}`,
      color: ok ? '#86efac' : '#fca5a5',
      display: 'flex', justifyContent: 'space-between', alignItems: 'center',
    }}>
      <span>{ok ? '✓' : '✕'} {text}</span>
      <button onClick={onClose} style={{ background: 'none', border: 'none', color: 'inherit', cursor: 'pointer', fontSize: 14, marginLeft: 8 }}>×</button>
    </div>
  )
}

function pageBtnStyle(enabled: boolean): React.CSSProperties {
  return {
    padding: '5px 10px', borderRadius: 6, border: '1px solid #2d2d3f',
    background: enabled ? '#1e3a5f' : '#1a1a2e',
    color: enabled ? '#93c5fd' : '#4b5563',
    fontSize: 12, cursor: enabled ? 'pointer' : 'not-allowed',
  }
}
