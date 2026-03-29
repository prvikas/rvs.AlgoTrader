import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { universeApi, InstrumentUniverseEntry } from '../api/client'

// All valid category values (must match UniverseCategories.All on the backend)
const CATEGORIES = [
  'NSE_EQUITY',
  'LARGE_CAP',
  'MID_CAP',
  'SMALL_CAP',
  'NSE_Z_GROUP',
  'NSE_B_GROUP',
  'OPTIONS_UNDERLYING',
] as const
type Category = (typeof CATEGORIES)[number]

// Which exchanges are valid for each category
const EXCHANGES: Record<Category, string[]> = {
  NSE_EQUITY:         ['NSE', 'BSE'],
  LARGE_CAP:          ['NSE', 'BSE'],
  MID_CAP:            ['NSE', 'BSE'],
  SMALL_CAP:          ['NSE', 'BSE'],
  NSE_Z_GROUP:        ['NSE'],
  NSE_B_GROUP:        ['NSE', 'BSE'],
  OPTIONS_UNDERLYING: ['NFO', 'BFO', 'NSE'],
}

const CATEGORY_LABELS: Record<string, string> = {
  NSE_EQUITY:         'NSE Equity',
  LARGE_CAP:          'Large-cap',
  MID_CAP:            'Mid-cap',
  SMALL_CAP:          'Small-cap',
  NSE_Z_GROUP:        'Z Group',
  NSE_B_GROUP:        'B Group',
  OPTIONS_UNDERLYING: 'Options Underlying',
}

const CATEGORY_COLORS: Record<string, string> = {
  NSE_EQUITY:         '#3b82f6',
  LARGE_CAP:          '#6366f1',
  MID_CAP:            '#8b5cf6',
  SMALL_CAP:          '#a78bfa',
  NSE_Z_GROUP:        '#ef4444',
  NSE_B_GROUP:        '#f97316',
  OPTIONS_UNDERLYING: '#10b981',
}

// ── styles ────────────────────────────────────────────────────────────────────
const S = {
  page: {
    padding: '24px',
    color: '#e2e8f0',
    height: '100%',
    overflowY: 'auto' as const,
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: 20,
    flexWrap: 'wrap' as const,
    gap: 12,
  },
  title: { fontSize: 22, fontWeight: 700, margin: 0 },
  subtitle: { fontSize: 13, color: '#8b8b9f', marginTop: 4 },
  card: {
    background: '#1e1e2e',
    border: '1px solid #2d2d3f',
    borderRadius: 10,
    padding: 20,
    marginBottom: 20,
  },
  filterRow: {
    display: 'flex',
    gap: 10,
    marginBottom: 16,
    flexWrap: 'wrap' as const,
    alignItems: 'center',
  },
  select: {
    background: '#0f0f1a',
    border: '1px solid #2d2d3f',
    color: '#e2e8f0',
    borderRadius: 6,
    padding: '6px 10px',
    fontSize: 13,
  },
  input: {
    background: '#0f0f1a',
    border: '1px solid #2d2d3f',
    color: '#e2e8f0',
    borderRadius: 6,
    padding: '6px 10px',
    fontSize: 13,
    width: 180,
  },
  btn: (variant: 'primary' | 'success' | 'danger' | 'ghost') => ({
    padding: '6px 14px',
    fontSize: 13,
    borderRadius: 6,
    cursor: 'pointer',
    fontWeight: 600,
    background: variant === 'primary'  ? '#3b82f6'
               : variant === 'success' ? '#10b981'
               : variant === 'danger'  ? '#ef4444'
               : 'transparent',
    color: variant === 'ghost' ? '#8b8b9f' : '#fff',
    border: variant === 'ghost' ? '1px solid #2d2d3f' : 'none',
  } as React.CSSProperties),
  table: { width: '100%', borderCollapse: 'collapse' as const, fontSize: 13 },
  th: {
    textAlign: 'left' as const,
    padding: '8px 12px',
    color: '#8b8b9f',
    borderBottom: '1px solid #2d2d3f',
    fontWeight: 600,
    fontSize: 12,
    textTransform: 'uppercase' as const,
    letterSpacing: '0.05em',
  },
  td: { padding: '8px 12px', borderBottom: '1px solid #1a1a2a' },
  badge: (color: string) => ({
    display: 'inline-block',
    padding: '2px 8px',
    borderRadius: 4,
    fontSize: 11,
    fontWeight: 600,
    background: `${color}22`,
    color,
    border: `1px solid ${color}44`,
  } as React.CSSProperties),
  tag: (active: boolean) => ({
    display: 'inline-block',
    padding: '2px 8px',
    borderRadius: 4,
    fontSize: 11,
    fontWeight: 600,
    background: active ? '#10b98122' : '#6b728022',
    color:      active ? '#10b981'   : '#6b7280',
    border:     active ? '1px solid #10b98144' : '1px solid #6b728044',
  } as React.CSSProperties),
  emptyRow: { textAlign: 'center' as const, padding: 40, color: '#8b8b9f' },
}

// ── component ─────────────────────────────────────────────────────────────────
export function UniversePage() {
  const qc = useQueryClient()

  // filter state
  const [filterCategory, setFilterCategory] = useState<string>('')
  const [filterActive,   setFilterActive]   = useState<string>('')
  const [search,         setSearch]         = useState('')

  // add form state
  const [newSymbol,   setNewSymbol]   = useState('')
  const [newExchange, setNewExchange] = useState('NSE')
  const [newCategory, setNewCategory] = useState<Category>('NSE_EQUITY')
  const [addError,    setAddError]    = useState('')

  const { data, isLoading } = useQuery({
    queryKey: ['universe', filterCategory, filterActive],
    queryFn: () =>
      universeApi
        .list({
          category: filterCategory || undefined,
          active:   filterActive === '' ? undefined : filterActive === 'true',
          pageSize: 500,
        })
        .then(r => r.data.data?.items ?? []),
  })

  const createMutation = useMutation({
    mutationFn: () =>
      universeApi.create({ symbol: newSymbol.trim().toUpperCase(), exchange: newExchange, category: newCategory }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['universe'] })
      setNewSymbol('')
      setAddError('')
    },
    onError: (e: any) => {
      setAddError(e?.response?.data?.error ?? e?.message ?? 'Failed to add entry')
    },
  })

  const toggleMutation = useMutation({
    mutationFn: ({ id, isActive }: { id: string; isActive: boolean }) =>
      universeApi.update(id, { isActive }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['universe'] }),
  })

  const deleteMutation = useMutation({
    mutationFn: (id: string) => universeApi.delete(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['universe'] }),
  })

  const seedMutation = useMutation({
    mutationFn: () => universeApi.seedDefaults(),
    onSuccess: (res) => {
      qc.invalidateQueries({ queryKey: ['universe'] })
      const added = res.data.data ?? 0
      alert(added > 0 ? `Added ${added} default entries.` : 'All defaults already present — nothing to add.')
    },
  })

  // filter rows client-side for instant search
  const rows = (data ?? []).filter(r =>
    !search || r.symbol.toLowerCase().includes(search.toLowerCase())
  )

  const equityCount     = (data ?? []).filter(r => r.category !== 'OPTIONS_UNDERLYING').length
  const underlyingCount = (data ?? []).filter(r => r.category === 'OPTIONS_UNDERLYING').length
  const activeCount     = (data ?? []).filter(r => r.isActive).length

  return (
    <div style={S.page}>
      {/* Header */}
      <div style={S.header}>
        <div>
          <h1 style={S.title}>Instrument Universe</h1>
          <p style={S.subtitle}>
            Controls which symbols are stored during broker master-data refresh.
            Indexes are always included regardless of this list.
          </p>
        </div>
        <button
          style={S.btn('success')}
          onClick={() => seedMutation.mutate()}
          disabled={seedMutation.isPending}
        >
          {seedMutation.isPending ? 'Seeding…' : '🌱 Seed Nifty 50 Defaults'}
        </button>
      </div>

      {/* Stats */}
      <div style={{ display: 'flex', gap: 12, marginBottom: 20, flexWrap: 'wrap' }}>
        {[
          { label: 'Total',    value: data?.length ?? 0,  color: '#e2e8f0' },
          { label: 'Active',   value: activeCount,         color: '#10b981' },
          { label: 'Equity Symbols', value: equityCount,    color: '#3b82f6' },
          { label: 'Underlyings', value: underlyingCount,  color: '#10b981' },
        ].map(s => (
          <div key={s.label} style={{ background: '#1e1e2e', border: '1px solid #2d2d3f', borderRadius: 8, padding: '10px 18px', minWidth: 100 }}>
            <div style={{ fontSize: 22, fontWeight: 700, color: s.color }}>{s.value}</div>
            <div style={{ fontSize: 12, color: '#8b8b9f' }}>{s.label}</div>
          </div>
        ))}
      </div>

      {/* Add form */}
      <div style={S.card}>
        <div style={{ fontSize: 14, fontWeight: 600, marginBottom: 12 }}>Add Symbol</div>
        <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'flex-end' }}>
          <div>
            <div style={{ fontSize: 11, color: '#8b8b9f', marginBottom: 4 }}>SYMBOL</div>
            <input
              style={{ ...S.input, width: 140, textTransform: 'uppercase' }}
              placeholder="e.g. RELIANCE"
              value={newSymbol}
              onChange={e => setNewSymbol(e.target.value)}
              onKeyDown={e => e.key === 'Enter' && newSymbol.trim() && createMutation.mutate()}
            />
          </div>
          <div>
            <div style={{ fontSize: 11, color: '#8b8b9f', marginBottom: 4 }}>CATEGORY</div>
            <select
              style={S.select}
              value={newCategory}
              onChange={e => {
                const cat = e.target.value as Category
                setNewCategory(cat)
                setNewExchange((EXCHANGES[cat] ?? ['NSE'])[0])
              }}
            >
              {CATEGORIES.map(c => (
                <option key={c} value={c}>{CATEGORY_LABELS[c] ?? c}</option>
              ))}
            </select>
          </div>
          <div>
            <div style={{ fontSize: 11, color: '#8b8b9f', marginBottom: 4 }}>EXCHANGE</div>
            <select
              style={S.select}
              value={newExchange}
              onChange={e => setNewExchange(e.target.value)}
            >
              {(EXCHANGES[newCategory] ?? ['NSE']).map(ex => (
                <option key={ex} value={ex}>{ex}</option>
              ))}
            </select>
          </div>
          <button
            style={S.btn('primary')}
            onClick={() => { if (newSymbol.trim()) createMutation.mutate() }}
            disabled={!newSymbol.trim() || createMutation.isPending}
          >
            {createMutation.isPending ? 'Adding…' : '+ Add'}
          </button>
        </div>
        {addError && <div style={{ marginTop: 8, fontSize: 12, color: '#ef4444' }}>{addError}</div>}
      </div>

      {/* Filters + Table */}
      <div style={S.card}>
        <div style={S.filterRow}>
          <input
            style={S.input}
            placeholder="Search symbol…"
            value={search}
            onChange={e => setSearch(e.target.value)}
          />
          <select style={S.select} value={filterCategory} onChange={e => setFilterCategory(e.target.value)}>
            <option value="">All Categories</option>
            {CATEGORIES.map(c => <option key={c} value={c}>{CATEGORY_LABELS[c]}</option>)}
          </select>
          <select style={S.select} value={filterActive} onChange={e => setFilterActive(e.target.value)}>
            <option value="">All Status</option>
            <option value="true">Active</option>
            <option value="false">Inactive</option>
          </select>
          <span style={{ marginLeft: 'auto', fontSize: 13, color: '#8b8b9f' }}>
            {rows.length} rows
          </span>
        </div>

        <table style={S.table}>
          <thead>
            <tr>
              <th style={S.th}>Symbol</th>
              <th style={S.th}>Exchange</th>
              <th style={S.th}>Category</th>
              <th style={S.th}>Status</th>
              <th style={S.th}>Added</th>
              <th style={S.th}>Actions</th>
            </tr>
          </thead>
          <tbody>
            {isLoading ? (
              <tr><td colSpan={6} style={S.emptyRow}>Loading…</td></tr>
            ) : rows.length === 0 ? (
              <tr>
                <td colSpan={6} style={S.emptyRow}>
                  No entries.{' '}
                  <button style={{ ...S.btn('ghost'), display: 'inline' }} onClick={() => seedMutation.mutate()}>
                    Seed Nifty 50 defaults
                  </button>
                </td>
              </tr>
            ) : (
              rows.map((row: InstrumentUniverseEntry) => (
                <tr key={row.id} style={{ opacity: row.isActive ? 1 : 0.5 }}>
                  <td style={S.td}>
                    <span style={{ fontWeight: 600, fontFamily: 'monospace' }}>{row.symbol}</span>
                  </td>
                  <td style={S.td}>
                    <span style={{ fontSize: 12, color: '#94a3b8' }}>{row.exchange}</span>
                  </td>
                  <td style={S.td}>
                    <span style={S.badge(CATEGORY_COLORS[row.category] ?? '#8b8b9f')}>
                      {CATEGORY_LABELS[row.category] ?? row.category}
                    </span>
                  </td>
                  <td style={S.td}>
                    <span style={S.tag(row.isActive)}>
                      {row.isActive ? 'Active' : 'Inactive'}
                    </span>
                  </td>
                  <td style={S.td}>
                    <span style={{ fontSize: 12, color: '#8b8b9f' }}>
                      {new Date(row.createdAt).toLocaleDateString()}
                    </span>
                  </td>
                  <td style={S.td}>
                    <div style={{ display: 'flex', gap: 8 }}>
                      <button
                        style={S.btn(row.isActive ? 'ghost' : 'success')}
                        onClick={() => toggleMutation.mutate({ id: row.id, isActive: !row.isActive })}
                        disabled={toggleMutation.isPending}
                        title={row.isActive ? 'Deactivate' : 'Activate'}
                      >
                        {row.isActive ? 'Disable' : 'Enable'}
                      </button>
                      <button
                        style={S.btn('danger')}
                        onClick={() => {
                          if (confirm(`Delete ${row.symbol} from universe?`)) {
                            deleteMutation.mutate(row.id)
                          }
                        }}
                        disabled={deleteMutation.isPending}
                        title="Remove from universe"
                      >
                        ✕
                      </button>
                    </div>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      {/* Passthrough notice */}
      {!isLoading && (data?.length ?? 0) === 0 && (
        <div style={{ background: '#78350f22', border: '1px solid #78350f', borderRadius: 8, padding: '12px 16px', fontSize: 13, color: '#fbbf24' }}>
          ⚠️  Universe is empty — instrument refresh will run in <strong>PASSTHROUGH mode</strong> and store all NSE/NFO instruments.
          Use <em>Seed Nifty 50 Defaults</em> to enable filtered mode.
        </div>
      )}
    </div>
  )
}
