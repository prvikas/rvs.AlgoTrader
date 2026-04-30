import { useState, useMemo, useCallback } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import {
  instrumentsApi,
  brokerApi,
  RefreshPreviewDto,
  RefreshCommitResult,
  ExchangePreviewGroup,
} from '../api/client'
import { C, CHART } from '../styles/tokens'

// ── Types ─────────────────────────────────────────────────────────────────────

type Phase = 'idle' | 'downloading' | 'select' | 'saving' | 'done' | 'error'

// ── Constants ─────────────────────────────────────────────────────────────────

const BUCKET_ORDER = ['Equity', 'Index', 'Futures', 'Options', 'Other']

const BUCKET_META: Record<string, { icon: string; desc: string; color: string; alphaBg: string }> = {
  Equity:  { icon: '📈', desc: 'Stocks (EQ / STK)',           color: C.blue,        alphaBg: C.blue22      },
  Index:   { icon: '🏦', desc: 'Nifty 50, Sensex, VIX, …',  color: CHART.violet,  alphaBg: CHART.violetBg },
  Futures: { icon: '📊', desc: 'FUT / FUTIDX / FUTSTK / MCX', color: C.amber,       alphaBg: C.amber11     },
  Options: { icon: '🎯', desc: 'CE / PE options',             color: C.green,       alphaBg: C.green18     },
  Other:   { icon: '📦', desc: 'ETF, bonds, other',           color: C.textMuted,   alphaBg: C.surface3    },
}

const CATEGORY_META: Record<string, { icon: string; desc: string }> = {
  NSE_EQUITY: { icon: '🔵', desc: 'Default list — covers most active NSE stocks' },
  LARGE_CAP:  { icon: '🏢', desc: 'Nifty 100 / user-defined large-caps' },
  MID_CAP:    { icon: '🏬', desc: 'Nifty Midcap 150 / user-defined' },
  SMALL_CAP:  { icon: '🏪', desc: 'User-defined small-caps' },
  NSE_Z_GROUP:{ icon: '⚠️', desc: 'Trade-to-trade / Z-group (NSE BE series)' },
  NSE_B_GROUP:{ icon: '📋', desc: 'B-group / user-defined' },
}

// ── Main component ────────────────────────────────────────────────────────────

export function MasterDataRefreshPage() {
  const [phase,          setPhase]          = useState<Phase>('idle')
  const [selectedBroker, setSelectedBroker] = useState('MStock')
  const [preview,        setPreview]        = useState<RefreshPreviewDto | null>(null)
  const [result,         setResult]         = useState<RefreshCommitResult | null>(null)
  const [errorMsg,       setErrorMsg]       = useState('')

  // ── Filter selections (default: all checked) ──────────────────────────────
  const [selExchanges, setSelExchanges]     = useState<Set<string>>(new Set())
  const [selTypes,     setSelTypes]         = useState<Set<string>>(new Set())
  const [selCategories,setSelCategories]    = useState<Set<string>>(new Set())

  // Track whether the user has customised from the "all selected" default
  const [userEdited, setUserEdited]         = useState(false)

  // ── Broker list ────────────────────────────────────────────────────────────
  const { data: brokerStatuses } = useQuery({
    queryKey: ['broker-status'],
    queryFn: () => brokerApi.status().then(r => Array.isArray(r.data.data) ? r.data.data : []),
    staleTime: 30_000,
  })
  const availableBrokers = brokerStatuses
    ?.filter(b => b.isAuthenticated)
    .map(b => b.brokerName) ?? ['MStock']

  // ── Step 1: Download ──────────────────────────────────────────────────────
  const previewMutation = useMutation({
    mutationFn: () => instrumentsApi.preview(selectedBroker),
    onMutate: () => { setPhase('downloading'); setErrorMsg('') },
    onSuccess: (res) => {
      const p = res.data.data!
      setPreview(p)
      setSelExchanges(new Set(p.exchanges.map(e => e.exchange)))
      setSelTypes(
        new Set(
          p.exchanges.flatMap(e => e.types.map(t => t.bucket))
            .filter((v, i, a) => a.indexOf(v) === i)
        )
      )
      setSelCategories(new Set(p.equityCategories.map(c => c.category)))
      setUserEdited(false)
      setPhase('select')
    },
    onError: (err: any) => {
      setErrorMsg(err.response?.data?.error ?? err.message ?? 'Download failed')
      setPhase('error')
    },
  })

  // ── Step 2: Save ──────────────────────────────────────────────────────────
  const commitMutation = useMutation({
    mutationFn: () => instrumentsApi.commit({
      stagingToken:             preview!.stagingToken,
      includedExchanges:        [...selExchanges],
      includedInstrumentTypes:  [...selTypes],
      includedEquityCategories: [...selCategories],
    }),
    onMutate: () => { setPhase('saving') },
    onSuccess: (res) => {
      setResult(res.data.data!)
      setPhase('done')
    },
    onError: (err: any) => {
      setErrorMsg(err.response?.data?.error ?? err.message ?? 'Save failed')
      setPhase('error')
    },
  })

  // ── Live count calculation ─────────────────────────────────────────────────
  const estimatedCount = useMemo(() => {
    if (!preview) return 0
    return preview.exchanges
      .filter(eg => selExchanges.has(eg.exchange))
      .flatMap(eg => eg.types.filter(t => selTypes.has(t.bucket)))
      .reduce((sum, t) => sum + t.count, 0)
  }, [preview, selExchanges, selTypes])

  // ── Toggle helpers ────────────────────────────────────────────────────────
  const toggle = useCallback(
    (set: Set<string>, value: string, setter: (s: Set<string>) => void) => {
      const next = new Set(set)
      if (next.has(value)) next.delete(value)
      else next.add(value)
      setter(next)
      setUserEdited(true)
    }, [])

  const reset = () => {
    setPhase('idle')
    setPreview(null)
    setResult(null)
    setErrorMsg('')
    setUserEdited(false)
  }

  // ── Render ────────────────────────────────────────────────────────────────
  return (
    <div style={S.page}>
      {/* Header */}
      <div style={S.header}>
        <div>
          <h1 style={S.title}>Master Data Refresh</h1>
          <p style={S.subtitle}>
            Download instruments from your broker and choose exactly what to save.
          </p>
        </div>
        {phase !== 'idle' && phase !== 'downloading' && (
          <button style={S.btn('ghost')} onClick={reset}>
            ↩ Start over
          </button>
        )}
      </div>

      {/* ── Step indicators ────────────────────────────────────────────────── */}
      <StepBar phase={phase} />

      {/* ── PHASE: idle ───────────────────────────────────────────────────── */}
      {phase === 'idle' && (
        <div style={S.card}>
          <h2 style={S.cardTitle}>Choose broker and download</h2>
          <p style={{ fontSize: 13, color: C.textSub, marginBottom: 20 }}>
            This will fetch the full scrip master from the selected broker (no data is saved yet).
            You will then choose what to include before anything is written to the database.
          </p>
          <div style={{ display: 'flex', gap: 12, alignItems: 'center', flexWrap: 'wrap' }}>
            <select
              style={S.select}
              value={selectedBroker}
              onChange={e => setSelectedBroker(e.target.value)}
            >
              {(availableBrokers.length > 0 ? availableBrokers : ['MStock', 'Zerodha', 'Upstox'])
                .map(b => <option key={b} value={b}>{b}</option>)}
            </select>
            <button
              style={S.btn('primary')}
              onClick={() => previewMutation.mutate()}
            >
              ⬇ Download from {selectedBroker}
            </button>
          </div>
          {availableBrokers.length === 0 && (
            <p style={{ marginTop: 12, fontSize: 12, color: C.amber }}>
              ⚠ No authenticated brokers found. Log in via the Broker panel first.
            </p>
          )}
        </div>
      )}

      {/* ── PHASE: downloading ────────────────────────────────────────────── */}
      {phase === 'downloading' && (
        <div style={{ ...S.card, textAlign: 'center', padding: '48px 32px' }}>
          <div style={S.spinner} />
          <p style={{ marginTop: 20, color: C.textSub, fontSize: 14 }}>
            Downloading instrument master from <strong style={{ color: C.text }}>{selectedBroker}</strong>…
          </p>
          <p style={{ fontSize: 12, color: C.textMuted, marginTop: 8 }}>
            This usually takes 5–15 seconds. Nothing is saved yet.
          </p>
        </div>
      )}

      {/* ── PHASE: select ─────────────────────────────────────────────────── */}
      {phase === 'select' && preview && (
        <>
          {/* Download summary banner */}
          <div style={S.infoBanner}>
            <span style={{ fontSize: 18 }}>⬇</span>
            <span>
              Downloaded <strong>{preview.totalDownloaded.toLocaleString()}</strong> instruments
              from <strong>{preview.brokerName}</strong>.
              Token expires in <strong>{preview.expiresInMinutes} min</strong>.
              Choose what to save below.
            </span>
          </div>

          {/* ── Exchange selection ── */}
          <SectionCard title="1 · Exchanges" subtitle="Which exchanges to include">
            <div style={S.chipRow}>
              {preview.exchanges.map(eg => (
                <FilterChip
                  key={eg.exchange}
                  label={eg.exchange}
                  count={eg.total}
                  active={selExchanges.has(eg.exchange)}
                  onToggle={() => toggle(selExchanges, eg.exchange, setSelExchanges)}
                  color={C.blue}
                  alphaBg={C.blue22}
                />
              ))}
            </div>
          </SectionCard>

          {/* ── Instrument type selection ── */}
          <SectionCard
            title="2 · Instrument Types"
            subtitle="Which types to save from the selected exchanges"
          >
            <div style={S.chipRow}>
              {BUCKET_ORDER
                .filter(b => preview.exchanges.some(e => e.types.some(t => t.bucket === b)))
                .map(bucket => {
                  const count = preview.exchanges
                    .filter(e => selExchanges.has(e.exchange))
                    .flatMap(e => e.types.filter(t => t.bucket === bucket))
                    .reduce((s, t) => s + t.count, 0)
                  const meta = BUCKET_META[bucket] ?? { icon: '📦', desc: bucket, color: C.textMuted, alphaBg: C.surface3 }
                  return (
                    <FilterChip
                      key={bucket}
                      label={bucket}
                      sublabel={meta.desc}
                      count={count}
                      active={selTypes.has(bucket)}
                      onToggle={() => toggle(selTypes, bucket, setSelTypes)}
                      color={meta.color}
                      alphaBg={meta.alphaBg}
                      icon={meta.icon}
                    />
                  )
                })}
            </div>
          </SectionCard>

          {/* ── Equity category (symbol) reference ── */}
          {(selExchanges.has('NSE') || selExchanges.has('BSE')) &&
           selTypes.has('Equity') &&
           preview.equityCategories.length > 0 && (
            <SectionCard
              title="3 · Equity Universe Categories (reference)"
              subtitle="All NSE / BSE equities are saved in passthrough mode — these categories show which symbols are in your trading universe"
            >
              <p style={{ fontSize: 12, color: C.green, marginBottom: 12, lineHeight: 1.5 }}>
                ✓ Passthrough mode active — <strong>all equities</strong> from the selected exchanges will be saved regardless of universe membership.
                The counts below show how many downloaded symbols match each universe category.
              </p>
              {preview.equityCategories.every(c => c.matchCount === 0) ? (
                <p style={{ fontSize: 13, color: C.amber, lineHeight: 1.5 }}>
                  ⚠ No downloaded symbols match any Universe category yet.
                  Go to the <strong>Universe</strong> page to add symbols with their category (Large-cap, Mid-cap, etc.).
                </p>
              ) : (
                <div style={S.chipRow}>
                  {preview.equityCategories.map(cat => {
                    const meta = CATEGORY_META[cat.category] ?? { icon: '📋', desc: cat.category }
                    return (
                      <FilterChip
                        key={cat.category}
                        label={cat.label}
                        sublabel={meta.desc}
                        count={cat.matchCount}
                        active
                        onToggle={() => {/* informational only */}}
                        color={CHART.violet}
                        alphaBg={CHART.violetBg}
                        icon={meta.icon}
                      />
                    )
                  })}
                </div>
              )}
            </SectionCard>
          )}

          {/* ── Exchange × type breakdown table ── */}
          <SectionCard
            title="Preview breakdown"
            subtitle="Instruments that will be saved given current selections"
            collapsible
          >
            <BreakdownTable
              exchanges={preview.exchanges}
              selExchanges={selExchanges}
              selTypes={selTypes}
            />
          </SectionCard>

          {/* ── Save bar ── */}
          <div style={S.saveBar}>
            <div style={{ fontSize: 15, fontWeight: 600, color: C.text }}>
              {estimatedCount.toLocaleString()} instruments will be saved
              {userEdited
                ? ` (${(preview.totalDownloaded - estimatedCount).toLocaleString()} skipped)`
                : ' — all selected'}
            </div>
            <div style={{ display: 'flex', gap: 10 }}>
              <button
                style={S.btn('ghost')}
                onClick={reset}
              >
                Cancel
              </button>
              <button
                style={{ ...S.btn('success'), minWidth: 160 }}
                disabled={estimatedCount === 0}
                onClick={() => commitMutation.mutate()}
              >
                💾 Save {estimatedCount.toLocaleString()} instruments
              </button>
            </div>
          </div>
        </>
      )}

      {/* ── PHASE: saving ─────────────────────────────────────────────────── */}
      {phase === 'saving' && (
        <div style={{ ...S.card, textAlign: 'center', padding: '48px 32px' }}>
          <div style={S.spinner} />
          <p style={{ marginTop: 20, color: C.textSub, fontSize: 14 }}>
            Saving <strong style={{ color: C.text }}>{estimatedCount.toLocaleString()}</strong> instruments to the database…
          </p>
        </div>
      )}

      {/* ── PHASE: done ───────────────────────────────────────────────────── */}
      {phase === 'done' && result && (
        <div style={S.card}>
          <div style={{ fontSize: 22, marginBottom: 16 }}>✅ Saved successfully</div>
          <div style={{ display: 'flex', gap: 16, flexWrap: 'wrap', marginBottom: 24 }}>
            {[
              { label: 'Total saved',    value: result.saved,        color: C.green       },
              { label: 'New entries',    value: result.newCount,     color: C.blue        },
              { label: 'Updated',        value: result.updatedCount, color: CHART.violet  },
              { label: 'Skipped',        value: result.skipped,      color: C.textMuted   },
            ].map(s => (
              <div key={s.label} style={S.statBox}>
                <div style={{ fontSize: 26, fontWeight: 700, color: s.color }}>
                  {s.value.toLocaleString()}
                </div>
                <div style={{ fontSize: 12, color: C.textSub, marginTop: 2 }}>{s.label}</div>
              </div>
            ))}
          </div>
          <button style={S.btn('primary')} onClick={reset}>
            ↩ Refresh again
          </button>
        </div>
      )}

      {/* ── PHASE: error ──────────────────────────────────────────────────── */}
      {phase === 'error' && (
        <div style={{ ...S.card, borderColor: C.red44 }}>
          <div style={{ fontSize: 16, fontWeight: 600, color: C.red, marginBottom: 8 }}>
            ❌ Error
          </div>
          <p style={{ fontSize: 13, color: C.red, marginBottom: 16 }}>{errorMsg}</p>
          <button style={S.btn('ghost')} onClick={reset}>Try again</button>
        </div>
      )}
    </div>
  )
}

// ── Sub-components ────────────────────────────────────────────────────────────

function StepBar({ phase }: { phase: Phase }) {
  const steps = ['Download', 'Select', 'Save']
  const activeIdx = phase === 'idle' ? -1
    : phase === 'downloading' ? 0
    : phase === 'select' ? 1
    : phase === 'saving' ? 2
    : phase === 'done' ? 3
    : -1

  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 0, marginBottom: 24 }}>
      {steps.map((step, i) => (
        <div key={step} style={{ display: 'flex', alignItems: 'center', flex: i < steps.length - 1 ? 1 : 0 }}>
          <div style={{
            display: 'flex', alignItems: 'center', gap: 8,
            color: i <= activeIdx ? C.text : C.textMuted,
          }}>
            <div style={{
              width: 28, height: 28, borderRadius: '50%', display: 'flex',
              alignItems: 'center', justifyContent: 'center', fontSize: 13, fontWeight: 700,
              background: i < activeIdx ? C.green
                : i === activeIdx ? C.blue
                : C.surface,
              border: `2px solid ${i <= activeIdx ? (i < activeIdx ? C.green : C.blue) : C.border2}`,
            }}>
              {i < activeIdx ? '✓' : i + 1}
            </div>
            <span style={{ fontSize: 13, fontWeight: i === activeIdx ? 700 : 400 }}>{step}</span>
          </div>
          {i < steps.length - 1 && (
            <div style={{
              flex: 1, height: 2, margin: '0 12px',
              background: i < activeIdx ? C.green : C.border2,
            }} />
          )}
        </div>
      ))}
    </div>
  )
}

function SectionCard({
  title, subtitle, children, collapsible,
}: {
  title: string; subtitle?: string; children: React.ReactNode; collapsible?: boolean
}) {
  const [open, setOpen] = useState(true)

  return (
    <div style={{ ...S.card, marginBottom: 16 }}>
      <div
        style={{
          display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start',
          marginBottom: open ? 16 : 0, cursor: collapsible ? 'pointer' : 'default',
        }}
        onClick={collapsible ? () => setOpen(o => !o) : undefined}
      >
        <div>
          <h2 style={S.cardTitle}>{title}</h2>
          {subtitle && <p style={{ fontSize: 12, color: C.textSub, marginTop: 2 }}>{subtitle}</p>}
        </div>
        {collapsible && (
          <span style={{ color: C.textMuted, fontSize: 12 }}>{open ? '▲ hide' : '▼ show'}</span>
        )}
      </div>
      {open && children}
    </div>
  )
}

function FilterChip({
  label, sublabel, count, active, onToggle, color, alphaBg, icon,
}: {
  label: string; sublabel?: string; count: number
  active: boolean; onToggle: () => void; color: string; alphaBg: string; icon?: string
}) {
  return (
    <button
      onClick={onToggle}
      style={{
        display: 'flex', flexDirection: 'column', alignItems: 'flex-start',
        padding: '10px 14px', borderRadius: 8, cursor: 'pointer',
        border: `2px solid ${active ? color : C.border2}`,
        background: active ? alphaBg : C.bg,
        color: active ? C.text : C.textMuted,
        transition: 'all 0.15s', minWidth: 110,
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginBottom: 4 }}>
        {icon && <span style={{ fontSize: 14 }}>{icon}</span>}
        <span style={{ fontSize: 13, fontWeight: 700 }}>{label}</span>
        {active && <span style={{ fontSize: 10, color, fontWeight: 700 }}>✓</span>}
      </div>
      <div style={{
        fontSize: 18, fontWeight: 700, color: active ? color : C.textMuted, lineHeight: 1,
      }}>
        {count.toLocaleString()}
      </div>
      {sublabel && (
        <div style={{ fontSize: 10, color: C.textMuted, marginTop: 4, lineHeight: 1.3 }}>
          {sublabel}
        </div>
      )}
    </button>
  )
}

function BreakdownTable({
  exchanges, selExchanges, selTypes,
}: {
  exchanges: ExchangePreviewGroup[]
  selExchanges: Set<string>
  selTypes: Set<string>
}) {
  const allBuckets = [...new Set(
    exchanges.flatMap(e => e.types.map(t => t.bucket))
  )].sort((a, b) => BUCKET_ORDER.indexOf(a) - BUCKET_ORDER.indexOf(b))

  return (
    <div style={{ overflowX: 'auto' }}>
      <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
        <thead>
          <tr>
            <th style={{ ...S.th, textAlign: 'left' }}>Exchange</th>
            {allBuckets.map(b => (
              <th key={b} style={{
                ...S.th, textAlign: 'right',
                color: selTypes.has(b) ? C.text : C.textMuted,
              }}>
                {b}
              </th>
            ))}
            <th style={{ ...S.th, textAlign: 'right' }}>Total</th>
          </tr>
        </thead>
        <tbody>
          {exchanges.map(eg => {
            const rowDimmed = !selExchanges.has(eg.exchange)
            const rowTotal = eg.types
              .filter(t => selExchanges.has(eg.exchange) && selTypes.has(t.bucket))
              .reduce((s, t) => s + t.count, 0)

            return (
              <tr key={eg.exchange} style={{ opacity: rowDimmed ? 0.35 : 1 }}>
                <td style={S.td}>
                  <span style={{
                    fontWeight: 700, fontSize: 12,
                    color: selExchanges.has(eg.exchange) ? C.text : C.textMuted,
                  }}>
                    {eg.exchange}
                  </span>
                </td>
                {allBuckets.map(bucket => {
                  const row = eg.types.find(t => t.bucket === bucket)
                  const dim = !selTypes.has(bucket) || rowDimmed
                  return (
                    <td key={bucket} style={{ ...S.td, textAlign: 'right', color: dim ? C.textMuted : C.textSub }}>
                      {row ? row.count.toLocaleString() : '—'}
                    </td>
                  )
                })}
                <td style={{ ...S.td, textAlign: 'right', fontWeight: 700, color: C.green }}>
                  {rowTotal > 0 ? rowTotal.toLocaleString() : '—'}
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

// ── Styles ────────────────────────────────────────────────────────────────────

const S = {
  page: {
    padding: '24px',
    color: C.text,
    maxWidth: 900,
    overflowY: 'auto' as const,
  },
  header: {
    display: 'flex',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    marginBottom: 24,
    flexWrap: 'wrap' as const,
    gap: 12,
  },
  title: { fontSize: 22, fontWeight: 700, margin: 0 },
  subtitle: { fontSize: 13, color: C.textSub, marginTop: 4 },
  card: {
    background: C.surface,
    border: `1px solid ${C.border2}`,
    borderRadius: 10,
    padding: '20px 24px',
    marginBottom: 16,
  },
  cardTitle: { fontSize: 15, fontWeight: 700, margin: 0 },
  chipRow: {
    display: 'flex',
    flexWrap: 'wrap' as const,
    gap: 10,
  },
  infoBanner: {
    display: 'flex',
    alignItems: 'center',
    gap: 12,
    background: C.greenBg,
    border: `1px solid ${C.green}`,
    borderRadius: 8,
    padding: '12px 16px',
    marginBottom: 16,
    fontSize: 13,
    color: C.green,
  },
  saveBar: {
    position: 'sticky' as const,
    bottom: 0,
    background: C.bg,
    border: `1px solid ${C.border2}`,
    borderRadius: 10,
    padding: '16px 24px',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: 16,
    flexWrap: 'wrap' as const,
    marginTop: 8,
  },
  statBox: {
    background: C.bg,
    border: `1px solid ${C.border2}`,
    borderRadius: 8,
    padding: '12px 20px',
    minWidth: 110,
  },
  select: {
    background: C.bg,
    border: `1px solid ${C.border2}`,
    color: C.text,
    borderRadius: 6,
    padding: '8px 12px',
    fontSize: 14,
    minWidth: 160,
  },
  btn: (variant: 'primary' | 'success' | 'ghost') => ({
    padding: '8px 18px',
    fontSize: 13,
    borderRadius: 6,
    border: variant === 'ghost' ? `1px solid ${C.border2}` : 'none',
    cursor: 'pointer',
    fontWeight: 600,
    background: variant === 'primary' ? C.blue
      : variant === 'success' ? C.green
      : 'transparent',
    color: variant === 'ghost' ? C.textSub : 'white',
    opacity: 1,
  } as React.CSSProperties),
  spinner: {
    width: 40, height: 40,
    border: `3px solid ${C.border2}`,
    borderTop: `3px solid ${C.blue}`,
    borderRadius: '50%',
    margin: '0 auto',
    animation: 'spin 0.8s linear infinite',
  },
  th: {
    padding: '8px 10px',
    color: C.textMuted,
    borderBottom: `1px solid ${C.border2}`,
    fontWeight: 600,
    fontSize: 11,
    textTransform: 'uppercase' as const,
    letterSpacing: '0.05em',
  },
  td: {
    padding: '6px 10px',
    borderBottom: `1px solid ${C.surface}`,
    color: C.textSub,
  },
}
