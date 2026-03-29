import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { refreshFiltersApi } from '../api/client'
import { useState } from 'react'

/**
 * RefreshFiltersPage — controls what gets saved to the DB during a master-data refresh.
 *
 * Three independent filter groups:
 *   1. Exchange         — NSE, BSE, NFO, BFO, CDS, MCX, …
 *   2. Instrument Type  — Equity, Futures, Options, Index
 *   3. Symbol Category  — equity universe categories (Large-cap, Mid-cap, Z group, …)
 *
 * Changes are saved to app_config and take effect on the next refresh.
 */
export function RefreshFiltersPage() {
  const queryClient = useQueryClient()
  const [toast, setToast] = useState<{ text: string; type: 'success' | 'error' } | null>(null)

  // ── Fetch current filters ──────────────────────────────────────────────────
  const { data: resp, isLoading, isError } = useQuery({
    queryKey: ['refresh-filters'],
    queryFn: () => refreshFiltersApi.get().then(r => r.data.data!),
    staleTime: 30_000,
  })

  // ── Local state: which items are checked ───────────────────────────────────
  const [localExchanges, setLocalExchanges]     = useState<Set<string> | null>(null)
  const [localTypes, setLocalTypes]             = useState<Set<string> | null>(null)
  const [localCategories, setLocalCategories]   = useState<Set<string> | null>(null)
  const [isDirty, setIsDirty]                   = useState(false)

  // Resolve the active set (local override > server value)
  const activeExchanges   = localExchanges   ?? toSet(resp?.includedExchanges)
  const activeTypes       = localTypes       ?? toSet(resp?.includedInstrumentTypes)
  const activeCategories  = localCategories  ?? toSet(resp?.includedEquityCategories)

  // ── Save mutation ──────────────────────────────────────────────────────────
  const saveMutation = useMutation({
    mutationFn: () => refreshFiltersApi.update({
      includedExchanges:        [...activeExchanges].join(','),
      includedInstrumentTypes:  [...activeTypes].join(','),
      includedEquityCategories: [...activeCategories].join(','),
    }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['refresh-filters'] })
      setLocalExchanges(null)
      setLocalTypes(null)
      setLocalCategories(null)
      setIsDirty(false)
      showToast('Refresh filters saved', 'success')
    },
    onError: (err: any) => {
      showToast(`Save failed: ${err.response?.data?.error ?? err.message}`, 'error')
    },
  })

  // ── Reset mutation ─────────────────────────────────────────────────────────
  const resetMutation = useMutation({
    mutationFn: () => refreshFiltersApi.resetDefaults(),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['refresh-filters'] })
      setLocalExchanges(null)
      setLocalTypes(null)
      setLocalCategories(null)
      setIsDirty(false)
      showToast('Reset to defaults', 'success')
    },
    onError: (err: any) => {
      showToast(`Reset failed: ${err.response?.data?.error ?? err.message}`, 'error')
    },
  })

  const showToast = (text: string, type: 'success' | 'error') => {
    setToast({ text, type })
    setTimeout(() => setToast(null), 3000)
  }

  const handleDiscard = () => {
    setLocalExchanges(null)
    setLocalTypes(null)
    setLocalCategories(null)
    setIsDirty(false)
  }

  // ── Toggle helpers ─────────────────────────────────────────────────────────

  function toggleExchange(value: string) {
    const current = localExchanges ?? toSet(resp?.includedExchanges)
    const next = toggle(current, value)
    setLocalExchanges(next)
    setIsDirty(true)
  }

  function toggleType(value: string) {
    const current = localTypes ?? toSet(resp?.includedInstrumentTypes)
    const next = toggle(current, value)
    setLocalTypes(next)
    setIsDirty(true)
  }

  function toggleCategory(value: string) {
    const current = localCategories ?? toSet(resp?.includedEquityCategories)
    const next = toggle(current, value)
    setLocalCategories(next)
    setIsDirty(true)
  }

  const isSaving = saveMutation.isPending || resetMutation.isPending

  if (isLoading) return <div className="p-6 text-gray-500">Loading…</div>
  if (isError || !resp) return <div className="p-6 text-red-500">Failed to load refresh filters.</div>

  return (
    <div className="p-6 max-w-4xl">
      {/* Header */}
      <div className="mb-6 flex justify-between items-start">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Refresh Filters</h1>
          <p className="text-gray-600 mt-1">
            Choose what gets saved to the database when instruments are downloaded from the broker.
          </p>
        </div>
        <button
          onClick={() => { if (confirm('Reset all filters to defaults?')) resetMutation.mutate() }}
          disabled={isSaving}
          className="px-4 py-2 bg-gray-200 text-gray-700 rounded hover:bg-gray-300 disabled:opacity-50 text-sm"
        >
          Reset Defaults
        </button>
      </div>

      {/* Info banner */}
      <div className="mb-4 p-4 bg-blue-50 border border-blue-200 rounded text-sm text-blue-800 space-y-2">
        <p>
          <strong>How this works:</strong> During each refresh, only instruments matching <em>all three</em> filters
          below are saved to the database. Changes take effect on the next scheduled or manual refresh.
        </p>
        <ul className="list-disc list-inside space-y-1 text-blue-700">
          <li>
            <strong>Exchange</strong> — gates the download.  Instruments on unchecked exchanges are skipped entirely.
            Default: NSE + BSE + NFO.  Add <em>BFO</em> for BSE F&amp;O, <em>MCX</em> for commodities,
            <em> CDS</em> for currency derivatives.
          </li>
          <li>
            <strong>Instrument Type</strong> — filters within each exchange.
            <em> Index</em> includes Nifty 50, Sensex, VIX, etc. on any exchange.
            <em> Options</em> requires the underlying to also be in your Universe (under <em>Options Underlying</em>).
          </li>
          <li>
            <strong>Symbol Category</strong> — further filters equity instruments (NSE / BSE) down to the
            symbols tagged with those categories in your Universe page.
            MCX / CDS instruments are <em>not</em> restricted by this filter — they are included as long as
            their exchange and type are enabled above.
          </li>
        </ul>
      </div>

      {/* Toast */}
      {toast && (
        <div className={`fixed bottom-4 right-4 px-4 py-3 rounded shadow text-sm font-medium ${
          toast.type === 'success'
            ? 'bg-green-100 text-green-800 border border-green-300'
            : 'bg-red-100 text-red-800 border border-red-300'
        }`}>
          {toast.text}
        </div>
      )}

      <div className="space-y-6">
        {/* ── Exchange filter ── */}
        <FilterCard
          title="Exchange"
          description="Which exchanges are included in the download."
        >
          <CheckboxGroup
            items={resp.knownExchanges}
            active={activeExchanges}
            onToggle={toggleExchange}
            labelFn={exchangeLabel}
          />
          <SummaryLine
            active={activeExchanges}
            all={resp.knownExchanges}
            noun="exchange"
          />
        </FilterCard>

        {/* ── Instrument type filter ── */}
        <FilterCard
          title="Instrument Type"
          description="Which instrument types are saved from the downloaded data."
        >
          <CheckboxGroup
            items={resp.knownInstrumentTypes}
            active={activeTypes}
            onToggle={toggleType}
            labelFn={typeLabel}
          />
          <SummaryLine
            active={activeTypes}
            all={resp.knownInstrumentTypes}
            noun="type"
          />
        </FilterCard>

        {/* ── Symbol category filter ── */}
        <FilterCard
          title="Symbol Category"
          description={
            <>
              Which equity universe categories contribute symbols to the download on{' '}
              <strong>NSE / BSE</strong>.  Only symbols tagged with an enabled category in your
              Universe page are saved.  This filter does <strong>not</strong> apply to MCX, CDS,
              or NCDEX — those exchanges download all instruments matching the type filter above.
            </>
          }
        >
          <CheckboxGroup
            items={resp.knownEquityCategories}
            active={activeCategories}
            onToggle={toggleCategory}
            labelFn={categoryLabel}
          />
          {activeCategories.size === 0 && (
            <p className="text-xs text-amber-600 mt-2">
              No equity categories selected — equity instruments will not be saved.
              Derivatives (Futures/Options) are still controlled by their type filter above.
            </p>
          )}
          {activeCategories.size > 0 && (
            <SummaryLine
              active={activeCategories}
              all={resp.knownEquityCategories}
              noun="category"
            />
          )}
        </FilterCard>
      </div>

      {/* Save / Discard */}
      {isDirty && (
        <div className="mt-6 flex gap-3">
          <button
            onClick={() => saveMutation.mutate()}
            disabled={isSaving}
            className="px-6 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-50 font-medium"
          >
            {saveMutation.isPending ? 'Saving…' : 'Save Changes'}
          </button>
          <button
            onClick={handleDiscard}
            disabled={isSaving}
            className="px-6 py-2 bg-gray-200 text-gray-700 rounded hover:bg-gray-300 disabled:opacity-50"
          >
            Discard
          </button>
        </div>
      )}
    </div>
  )
}

// ── Sub-components ─────────────────────────────────────────────────────────

function FilterCard({
  title,
  description,
  children,
}: {
  title: string
  description: React.ReactNode
  children: React.ReactNode
}) {
  return (
    <div className="border rounded-lg p-5 bg-white shadow-sm">
      <h2 className="text-base font-semibold text-gray-900 mb-1">{title}</h2>
      <p className="text-sm text-gray-500 mb-4">{description}</p>
      {children}
    </div>
  )
}

function CheckboxGroup({
  items,
  active,
  onToggle,
  labelFn,
}: {
  items: string[]
  active: Set<string>
  onToggle: (v: string) => void
  labelFn: (v: string) => { label: string; sub?: string }
}) {
  return (
    <div className="flex flex-wrap gap-3">
      {items.map(item => {
        const { label, sub } = labelFn(item)
        const checked = active.has(item)
        return (
          <label
            key={item}
            className={`flex items-start gap-2 px-3 py-2 rounded border cursor-pointer select-none transition-colors ${
              checked
                ? 'bg-blue-50 border-blue-400 text-blue-900'
                : 'bg-gray-50 border-gray-200 text-gray-600 hover:border-gray-400'
            }`}
          >
            <input
              type="checkbox"
              checked={checked}
              onChange={() => onToggle(item)}
              className="mt-0.5 accent-blue-600"
            />
            <span>
              <span className="font-medium text-sm">{label}</span>
              {sub && <span className="block text-xs text-gray-400 mt-0.5">{sub}</span>}
            </span>
          </label>
        )
      })}
    </div>
  )
}

function SummaryLine({
  active,
  all,
  noun,
}: {
  active: Set<string>
  all: string[]
  noun: string
}) {
  if (active.size === all.length) {
    return <p className="text-xs text-gray-400 mt-2">All {noun}s included</p>
  }
  return (
    <p className="text-xs text-gray-500 mt-2">
      {active.size} of {all.length} {noun}{active.size !== 1 ? 's' : ''} selected
    </p>
  )
}

// ── Helpers ────────────────────────────────────────────────────────────────

function toSet(csv?: string): Set<string> {
  if (!csv) return new Set()
  return new Set(
    csv.split(',')
      .map(v => v.trim())
      .filter(Boolean)
  )
}

function toggle(set: Set<string>, value: string): Set<string> {
  const next = new Set(set)
  if (next.has(value)) next.delete(value)
  else next.add(value)
  return next
}

function exchangeLabel(v: string): { label: string; sub?: string } {
  const map: Record<string, { label: string; sub?: string }> = {
    NSE:   { label: 'NSE',   sub: 'Equity / Debt' },
    BSE:   { label: 'BSE',   sub: 'Equity' },
    NFO:   { label: 'NFO',   sub: 'F&O on NSE' },
    BFO:   { label: 'BFO',   sub: 'F&O on BSE' },
    CDS:   { label: 'CDS',   sub: 'Currency Derivatives' },
    MCX:   { label: 'MCX',   sub: 'Commodities' },
    NCDEX: { label: 'NCDEX', sub: 'Agri Commodities' },
    BCD:   { label: 'BCD',   sub: 'BSE Currency' },
  }
  return map[v] ?? { label: v }
}

function typeLabel(v: string): { label: string; sub?: string } {
  const map: Record<string, { label: string; sub?: string }> = {
    Equity:  { label: 'Equity',  sub: 'Stocks (EQ / STK)' },
    Futures: { label: 'Futures', sub: 'FUT / FUTIDX / FUTSTK' },
    Options: { label: 'Options', sub: 'CE / PE / OPT' },
    Index:   { label: 'Index',   sub: 'NIFTY, SENSEX, …' },
  }
  return map[v] ?? { label: v }
}

function categoryLabel(v: string): { label: string; sub?: string } {
  const map: Record<string, { label: string; sub?: string }> = {
    NSE_EQUITY:      { label: 'NSE Equity',  sub: 'Default universe list' },
    LARGE_CAP:       { label: 'Large-cap',   sub: 'Nifty 100 / user-defined' },
    MID_CAP:         { label: 'Mid-cap',     sub: 'Nifty Midcap 150 / user-defined' },
    SMALL_CAP:       { label: 'Small-cap',   sub: 'User-defined' },
    NSE_Z_GROUP:     { label: 'Z Group',     sub: 'Trade-to-trade (NSE BE series)' },
    NSE_B_GROUP:     { label: 'B Group',     sub: 'User-defined' },
  }
  return map[v] ?? { label: v }
}
