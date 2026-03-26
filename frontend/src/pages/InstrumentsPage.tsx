import { useState, useEffect, useRef, useCallback } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { instrumentsApi, historicalApi, brokerApi, Instrument } from '../api/client'

// ─── Types ───────────────────────────────────────────────────────────────────

interface DownloadForm {
  timeframe: string
  fromDate: string
}

type BrokerStep = 'idle' | 'loading' | 'done' | 'error'

interface RefreshProgress {
  active: boolean
  steps: { broker: string; status: BrokerStep; error?: string }[]
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
  { label: 'Name',            sortKey: 'name',     width: '1.6fr' },
  { label: 'Trading Symbol',  sortKey: 'trading',  width: '1fr' },
  { label: 'Exchange',        sortKey: 'exchange', width: '90px' },
  { label: 'Type',            sortKey: 'type',     width: '90px' },
  { label: 'Brokers',                              width: '90px' },
  { label: 'Status',                               width: '72px' },
  { label: 'Actions',                              width: '90px' },
]

const gridCols = COLUMNS.map(c => c.width).join(' ')

// ─── InstrumentsPage ──────────────────────────────────────────────────────────

export function InstrumentsPage() {
  const qc = useQueryClient()

  // ── Filter / sort / page state ───────────────────────────────────────────
  const [search, setSearch]                   = useState('')
  const [debouncedSearch, setDebouncedSearch] = useState('')
  const [exchange, setExchange]               = useState<string>('all')
  const [instrType, setInstrType]             = useState<string>('All Types')
  const [activeOnly, setActiveOnly]           = useState(true)
  const [page, setPage]                       = useState(1)
  const [sortBy, setSortBy]                   = useState<string>('symbol')
  const [sortDir, setSortDir]                 = useState<SortDir>('asc')

  // Broker list — fetched live so no hardcoded names here
  const { data: brokerStatuses } = useQuery({
    queryKey: ['broker-status'],
    queryFn: () => brokerApi.status().then(r => Array.isArray(r.data.data) ? r.data.data : []),
    staleTime: 15_000,
  })
  const allBrokers = (brokerStatuses ?? []).map(s => s.brokerName)

  // Refresh progress state
  const [refreshProgress, setRefreshProgress] = useState<RefreshProgress>({
    active: false,
    steps: [],  // populated when handleRefresh runs and broker list is known
  })

  // Download modal
  const [downloadTarget, setDownloadTarget] = useState<Instrument | null>(null)
  const [downloadForm, setDownloadForm]     = useState<DownloadForm>({
    timeframe: '5m',
    fromDate: (() => {
      const d = new Date()
      d.setMonth(d.getMonth() - 6)
      return d.toISOString().slice(0, 10)
    })(),
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

  const instruments = data?.items ?? []
  const totalCount  = data?.totalCount ?? 0
  const totalPages  = Math.max(1, Math.ceil(totalCount / PAGE_SIZE))

  // ── Refresh logic ─────────────────────────────────────────────────────────

  const handleRefresh = useCallback(async () => {
    // Fetch live broker status — determines which brokers to refresh
    let allBrokerNames: string[]
    let authenticatedBrokers: string[]
    try {
      const res = await brokerApi.status()
      const statuses = Array.isArray(res.data.data) ? res.data.data : []
      allBrokerNames        = statuses.map(s => s.brokerName)
      authenticatedBrokers  = statuses.filter(s => s.isAuthenticated).map(s => s.brokerName)
    } catch {
      allBrokerNames       = []
      authenticatedBrokers = []
    }

    if (authenticatedBrokers.length === 0) {
      setRefreshProgress({
        active: false,
        steps: allBrokerNames.map(b => ({
          broker: b,
          status: 'error' as BrokerStep,
          error: 'Not authenticated — log in first',
        })),
      })
      return
    }

    setRefreshProgress({
      active: true,
      steps: allBrokerNames.map(b => ({
        broker: b,
        status: (authenticatedBrokers.includes(b) ? 'idle' : 'error') as BrokerStep,
        error: authenticatedBrokers.includes(b) ? undefined : 'Not authenticated — skipped',
      })),
    })

    for (let i = 0; i < allBrokerNames.length; i++) {
      const broker = allBrokerNames[i]
      if (!authenticatedBrokers.includes(broker)) continue

      setRefreshProgress(prev => ({
        ...prev,
        steps: prev.steps.map((s, idx) => idx === i ? { ...s, status: 'loading' } : s),
      }))

      try {
        await instrumentsApi.refresh(broker)
        setRefreshProgress(prev => ({
          ...prev,
          steps: prev.steps.map((s, idx) => idx === i ? { ...s, status: 'done' } : s),
        }))
      } catch (err: any) {
        const errText = err?.response?.data?.error ?? `${broker} refresh failed`
        setRefreshProgress(prev => ({
          ...prev,
          steps: prev.steps.map((s, idx) => idx === i ? { ...s, status: 'error', error: errText } : s),
        }))
      }
    }

    setRefreshProgress(prev => ({ ...prev, active: false }))
    setTimeout(() => {
      qc.invalidateQueries({ queryKey: ['instruments'] })
    }, 500)
  }, [qc])

  const isRefreshing = refreshProgress.active
  const anyStepDone  = refreshProgress.steps.some(s => s.status !== 'idle')

  // ── Download handler ──────────────────────────────────────────────────────

  const handleDownload = async () => {
    if (!downloadTarget) return
    setDownloadPending(true)
    setDownloadMsg(null)
    try {
      await historicalApi.downloadHistory(
        downloadTarget.internalSymbol, downloadForm.timeframe, downloadForm.fromDate)
      setDownloadMsg({
        type: 'ok',
        text: `Download job queued for ${downloadTarget.internalSymbol} (${downloadForm.timeframe} from ${downloadForm.fromDate}). Check Hangfire for progress.`,
      })
      setTimeout(() => setDownloadTarget(null), 3000)
    } catch (err: any) {
      setDownloadMsg({
        type: 'err',
        text: err?.response?.data?.error ?? 'Download failed. Ensure historical data service is running.',
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
          onClick={handleRefresh}
          disabled={isRefreshing}
          style={{
            padding: '8px 18px',
            background: isRefreshing ? '#4b5563' : '#6366f1',
            color: '#fff', border: 'none', borderRadius: 6,
            fontSize: 13, fontWeight: 600,
            cursor: isRefreshing ? 'not-allowed' : 'pointer',
            display: 'flex', alignItems: 'center', gap: 7,
          }}
        >
          <span style={{ fontSize: 16, lineHeight: 1 }}>{isRefreshing ? '⟳' : '↻'}</span>
          {isRefreshing ? 'Refreshing…' : 'Refresh Master Data'}
        </button>
      </div>

      {/* ── Refresh progress panel ───────────────────────────────────────────── */}
      {anyStepDone && (
        <div style={{ background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 8, padding: '14px 18px' }}>
          <div style={{ fontSize: 12, fontWeight: 700, color: '#94a3b8', marginBottom: 12, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
            Refresh Progress
          </div>
          <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
            {refreshProgress.steps.map(step => (
              <div key={step.broker} style={{
                display: 'flex', alignItems: 'center', gap: 8,
                background: '#0f0f1a',
                border: `1px solid ${step.status === 'done' ? '#16a34a' : step.status === 'error' ? '#dc2626' : step.status === 'loading' ? '#6366f1' : '#2d2d3f'}`,
                borderRadius: 6, padding: '8px 14px', minWidth: 160,
              }}>
                <span style={{ fontSize: 16 }}>
                  {step.status === 'idle' ? '○' : step.status === 'loading' ? <SpinIcon /> : step.status === 'done' ? '✓' : '✕'}
                </span>
                <div>
                  <div style={{ fontSize: 13, fontWeight: 600, color: '#e2e8f0' }}>{step.broker}</div>
                  <div style={{ fontSize: 11, color: step.status === 'done' ? '#86efac' : step.status === 'error' ? '#fca5a5' : step.status === 'loading' ? '#a5b4fc' : '#4b5563' }}>
                    {step.status === 'idle' ? 'Waiting' : step.status === 'loading' ? 'Downloading…' : step.status === 'done' ? 'Done' : (step.error ?? 'Error')}
                  </div>
                </div>
              </div>
            ))}
          </div>

          {isRefreshing && (
            <div style={{ marginTop: 14, height: 4, background: '#2d2d3f', borderRadius: 4, overflow: 'hidden' }}>
              <div style={{ height: '100%', background: '#6366f1', animation: 'progress-slide 1.4s ease-in-out infinite', width: '40%' }} />
            </div>
          )}

          {!isRefreshing && (
            <button
              onClick={() => setRefreshProgress({ active: false, steps: allBrokers.map(b => ({ broker: b, status: 'idle' })) })}
              style={{ marginTop: 10, background: 'none', border: 'none', color: '#64748b', fontSize: 12, cursor: 'pointer' }}
            >
              Dismiss
            </button>
          )}
        </div>
      )}

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

        {!isLoading && totalCount > 0 && (
          <span style={{ fontSize: 12, color: '#475569', whiteSpace: 'nowrap', marginLeft: 'auto' }}>
            {totalCount.toLocaleString()} result{totalCount !== 1 ? 's' : ''}
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

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, marginBottom: 20 }}>
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
                <label style={{ fontSize: 12, fontWeight: 600, color: '#94a3b8', display: 'block', marginBottom: 6 }}>From Date</label>
                <input
                  type="date"
                  value={downloadForm.fromDate}
                  onChange={e => setDownloadForm({ ...downloadForm, fromDate: e.target.value })}
                  style={{ ...inp, width: '100%', boxSizing: 'border-box' }}
                />
              </div>
            </div>

            <p style={{ fontSize: 11, color: '#64748b', marginBottom: 16, lineHeight: 1.5 }}>
              Broker API rate limits apply (Zerodha: max 60 days/request, 3 req/s).
              The system chunks and respects rate limits automatically. Large ranges may take a few minutes.
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
                disabled={downloadPending || !downloadForm.fromDate}
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

function InstrumentRow({ instrument: inst, even, onDownload }: {
  instrument: Instrument; even: boolean; onDownload: () => void
}) {
  return (
    <div style={{
      display: 'grid',
      gridTemplateColumns: gridCols,
      padding: '8px 16px',
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
            padding: '4px 10px', background: '#1e3a5f', color: '#60a5fa',
            border: '1px solid #1e4a7f', borderRadius: 4, fontSize: 11,
            fontWeight: 600, cursor: 'pointer',
          }}
          title="Download historical candle data"
        >
          ↓ History
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
