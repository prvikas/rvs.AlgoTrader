import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { useSearchParams } from 'react-router-dom'
import { strategyDomainApi } from '../api/client'
import { Strategy, StrategyStatus, TradingStyle } from '../types/strategy'
import { C, F, SP, CONTENT_PAD } from '../styles/tokens'
import { ScenariosTab } from '../components/strategies/ScenariosTab'
import { DeploymentsTab } from '../components/strategies/DeploymentsTab'
import { ResultsTab } from '../components/strategies/ResultsTab'
import { CompareTab } from '../components/strategies/CompareTab'
import { StrategyDefinitionPage } from './StrategyDefinitionPage'

type Tab = 'definition' | 'scenarios' | 'results' | 'compare' | 'deployments'

function StatusBadge({ status }: { status: StrategyStatus }) {
  const colorMap: Record<string, string> = {
    [StrategyStatus.Draft]:      C.textMuted,
    [StrategyStatus.Backtested]: C.blue,
    [StrategyStatus.FwdTesting]: C.blue,
    [StrategyStatus.Live]:       C.green,
    [StrategyStatus.Archived]:   C.textDim,
  }
  const borderMap: Record<string, string> = {
    [StrategyStatus.Draft]:      C.textMuted,
    [StrategyStatus.Backtested]: C.blue44,
    [StrategyStatus.FwdTesting]: C.blue44,
    [StrategyStatus.Live]:       C.green44,
    [StrategyStatus.Archived]:   C.textDim,
  }
  const bgMap: Record<string, string> = {
    [StrategyStatus.Draft]:      'transparent',
    [StrategyStatus.Backtested]: C.blue11,
    [StrategyStatus.FwdTesting]: C.blue11,
    [StrategyStatus.Live]:       C.green18,
    [StrategyStatus.Archived]:   'transparent',
  }
  const color = colorMap[status] ?? C.textMuted
  const borderColor = borderMap[status] ?? C.textMuted
  const bg = bgMap[status] ?? 'transparent'
  return (
    <span style={{
      fontSize: 9, fontWeight: 700, color, padding: '1px 5px', borderRadius: 2,
      border: `1px solid ${borderColor}`, background: bg, textTransform: 'uppercase',
    }}>
      {status}
    </span>
  )
}

function StyleBadge({ style }: { style: TradingStyle }) {
  return (
    <span style={{
      fontSize: 9, color: C.amber, padding: '1px 5px', borderRadius: 2,
      border: `1px solid ${C.amber44}`, background: C.amber11,
    }}>
      {style}
    </span>
  )
}

export function StrategiesPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const qc = useQueryClient()
  const [search, setSearch] = useState('')
  const [tab, setTab] = useState<Tab>('scenarios')
  const [creating, setCreating] = useState(false)
  const [editingDefinition, setEditingDefinition] = useState(false)
  const [creatingType, setCreatingType] = useState<'equity' | 'options' | null>(null)

  const selectedId = searchParams.get('id') ?? undefined

  const { data: strategies = [], isLoading, error, refetch } = useQuery({
    queryKey: ['strategies'],
    queryFn: () => strategyDomainApi.listStrategies(),
    // Avoid spurious refetches that cause the list to flicker between the
    // optimistically-seeded cache and the in-flight server response.
    staleTime: 30_000,
    refetchOnWindowFocus: false,
  })

  const deleteMut = useMutation({
    mutationFn: (id: string) => strategyDomainApi.deleteStrategy(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['strategies'] })
      if (selectedId) setSearchParams({})
    },
  })

  const selectedStrategy = strategies.find(s => s.id === selectedId)

  const filtered = strategies.filter(s =>
    !search || s.name.toLowerCase().includes(search.toLowerCase())
  )

  function selectStrategy(id: string) {
    setSearchParams({ id })
    setCreating(false)
    setEditingDefinition(false)
    setTab('scenarios')
  }

  const TABS: { key: Tab; label: string }[] = [
    { key: 'definition',  label: 'Definition' },
    { key: 'scenarios',   label: 'Scenarios' },
    { key: 'results',     label: 'Results' },
    { key: 'compare',     label: 'Compare' },
    { key: 'deployments', label: 'Deployments' },
  ]

  return (
    <div style={{ display: 'flex', height: '100vh', gap: 0, overflow: 'hidden' }}>
      {/* Left sidebar */}
      <div style={{
        width: 240, flexShrink: 0, borderRight: `1px solid ${C.border}`,
        display: 'flex', flexDirection: 'column', overflow: 'hidden',
      }}>
        <div style={{ padding: '10px 12px', borderBottom: `1px solid ${C.border}` }}>
          <input
            value={search}
            onChange={e => setSearch(e.target.value)}
            placeholder="Search strategies…"
            style={{
              width: '100%', background: C.surface2, border: `1px solid ${C.border}`,
              color: C.text, borderRadius: 4, padding: '6px 8px', fontSize: 11,
              boxSizing: 'border-box', marginBottom: SP.sm,
            }}
          />
          <button
            onClick={() => { setCreating(true); setCreatingType(null); setEditingDefinition(false) }}
            style={primaryBtnStyle}
          >
            + New Strategy
          </button>
        </div>

        {isLoading && (
          <div style={{ padding: SP.md }}>
            {[1, 2, 3].map(i => (
              <div key={i} style={{ height: 60, background: C.surface2, borderRadius: 6, marginBottom: 8, opacity: 0.5 }} />
            ))}
          </div>
        )}

        {!isLoading && error && (
          <div style={{ padding: SP.md, fontSize: 11, color: C.red }}>
            <div style={{ marginBottom: 6 }}>Failed to load strategies.</div>
            <button
              onClick={() => refetch()}
              style={{
                fontSize: 10, padding: '3px 10px', borderRadius: 4, cursor: 'pointer',
                background: 'none', border: `1px solid ${C.red66}`, color: C.red,
              }}
            >
              Retry
            </button>
          </div>
        )}

        <div style={{ flex: 1, overflowY: 'auto' }}>
          {filtered.map(s => (
            <button
              key={s.id}
              onClick={() => selectStrategy(s.id)}
              style={{
                display: 'block', width: '100%', textAlign: 'left',
                padding: '10px 12px', background: s.id === selectedId ? C.surface2 : 'none',
                borderLeft: `3px solid ${s.id === selectedId ? C.blue : 'transparent'}`,
                border: 'none', cursor: 'pointer',
                borderBottom: `1px solid ${C.border2}`,
              }}
            >
              <div style={{ fontSize: 12, fontWeight: 600, color: C.text, marginBottom: 3 }}>{s.name}</div>
              <div style={{ display: 'flex', gap: 4, flexWrap: 'wrap', alignItems: 'center' }}>
                <span style={{ fontSize: 10, color: C.textMuted }}>{s.primaryTimeframe}</span>
                {s.instruments?.[0] && (
                  <span style={{ fontSize: 10, color: C.textDim, fontFamily: F.mono }}>{s.instruments[0]}</span>
                )}
                <StyleBadge style={s.tradingStyle} />
                <StatusBadge status={s.status} />
              </div>
            </button>
          ))}

          {!isLoading && filtered.length === 0 && !error && (
            <div style={{ padding: SP.lg, textAlign: 'center', fontSize: 11, color: C.textMuted }}>
              {search ? 'No strategies match.' : 'No strategies yet.'}
            </div>
          )}
        </div>
      </div>

      {/* Centre panel */}
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
        {/* Create new strategy — type chooser */}
        {creating && creatingType === null && (
          <div style={{ flex: 1, overflowY: 'auto', padding: CONTENT_PAD }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: SP.sm, marginBottom: SP.lg }}>
              <button
                onClick={() => setCreating(false)}
                style={{ background: 'none', border: 'none', color: C.textMuted, cursor: 'pointer', fontSize: 13 }}
              >
                ←
              </button>
              <h2 style={{ margin: 0, fontSize: 16, fontWeight: 700 }}>New Strategy
              </h2>
            </div>
            <div style={{ maxWidth: 600, margin: '0 auto' }}>
              <div style={{ fontSize: 13, color: C.textMuted, marginBottom: SP.lg, textAlign: 'center' }}>
                Choose the type of strategy to create
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: SP.lg }}>
                <button
                  onClick={() => { setCreatingType('equity'); setSearchParams({}) }}
                  style={{
                    background: C.surface2, border: `1px solid ${C.border}`, borderRadius: 10,
                    padding: '24px 20px', cursor: 'pointer', textAlign: 'left',
                    transition: 'border-color 0.15s',
                  }}
                  onMouseEnter={e => (e.currentTarget.style.borderColor = C.blue88)}
                  onMouseLeave={e => (e.currentTarget.style.borderColor = C.border)}
                >
                  <div style={{ fontSize: 28, marginBottom: 10 }}>📊</div>
                  <div style={{ fontSize: 14, fontWeight: 700, color: C.text, marginBottom: 6 }}>
                    Equity / Futures
                  </div>
                  <div style={{ fontSize: 11, color: C.textMuted, lineHeight: 1.5 }}>
                    Momentum, trend-following, swing and positional strategies on stocks, indices, or futures.
                  </div>
                </button>
                <button
                  onClick={() => { setCreatingType('options'); setSearchParams({}) }}
                  style={{
                    background: C.blueBg, border: `1px solid ${C.blue44}`, borderRadius: 10,
                    padding: '24px 20px', cursor: 'pointer', textAlign: 'left',
                    transition: 'border-color 0.15s',
                  }}
                  onMouseEnter={e => (e.currentTarget.style.borderColor = C.blue88)}
                  onMouseLeave={e => (e.currentTarget.style.borderColor = C.blue44)}
                >
                  <div style={{ fontSize: 28, marginBottom: 10 }}>🦋</div>
                  <div style={{ fontSize: 14, fontWeight: 700, color: C.blue, marginBottom: 6 }}>
                    Options Strategy
                  </div>
                  <div style={{ fontSize: 11, color: C.textMuted, lineHeight: 1.5 }}>
                    Spreads, straddles, condors, and custom multi-leg structures on index options (NIFTY, BANKNIFTY).
                  </div>
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Create new strategy (full-panel) */}
        {creating && creatingType !== null && (
          <div style={{ flex: 1, overflowY: 'auto', padding: CONTENT_PAD }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: SP.sm, marginBottom: SP.lg }}>
              <button
                onClick={() => setCreatingType(null)}
                style={{ background: 'none', border: 'none', color: C.textMuted, cursor: 'pointer', fontSize: 13 }}
              >
                ←
              </button>
              <h2 style={{ margin: 0, fontSize: 16, fontWeight: 700 }}>
                New {creatingType === 'options' ? 'Options' : 'Equity / Futures'} Strategy
              </h2>
            </div>
            <StrategyDefinitionPage
              strategyKind={creatingType}
              onSaved={(s: Strategy) => {
                qc.invalidateQueries({ queryKey: ['strategies'] })
                setCreating(false)
                setCreatingType(null)
                selectStrategy(s.id)
              }}
              onCancel={() => setCreatingType(null)}
            />
          </div>
        )}

        {/* Edit definition (full-panel) */}
        {!creating && editingDefinition && selectedStrategy && (
          <div style={{ flex: 1, overflowY: 'auto', padding: CONTENT_PAD }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: SP.sm, marginBottom: SP.lg }}>
              <button
                onClick={() => setEditingDefinition(false)}
                style={{ background: 'none', border: 'none', color: C.textMuted, cursor: 'pointer', fontSize: 13 }}
              >
                ←
              </button>
              <h2 style={{ margin: 0, fontSize: 16, fontWeight: 700 }}>Edit: {selectedStrategy.name}</h2>
            </div>
            <StrategyDefinitionPage
              strategyId={selectedStrategy.id}
              initialData={selectedStrategy}
              onSaved={() => {
                qc.invalidateQueries({ queryKey: ['strategies'] })
                setEditingDefinition(false)
              }}
              onCancel={() => setEditingDefinition(false)}
            />
          </div>
        )}

        {/* Strategy view with tabs */}
        {!creating && !editingDefinition && selectedStrategy && (
          <>
            {/* Strategy header */}
            <div style={{
              padding: '10px 16px', borderBottom: `1px solid ${C.border}`,
              display: 'flex', alignItems: 'center', gap: SP.md, flexShrink: 0,
            }}>
              <div style={{ flex: 1 }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: SP.sm }}>
                  <span style={{ fontSize: 15, fontWeight: 700 }}>{selectedStrategy.name}</span>
                  <span style={{ fontSize: 11, color: C.textMuted }}>{selectedStrategy.primaryTimeframe}</span>
                  {(selectedStrategy.instruments ?? []).map(i => (
                    <span key={i} style={{ fontSize: 10, color: C.textDim, fontFamily: F.mono }}>{i}</span>
                  ))}
                  <StyleBadge style={selectedStrategy.tradingStyle} />
                  <StatusBadge status={selectedStrategy.status} />
                </div>
              </div>
              <button
                onClick={() => setEditingDefinition(true)}
                style={secondaryBtnStyle}
              >
                Edit Definition
              </button>
              <button
                onClick={() => deleteMut.mutate(selectedStrategy.id)}
                disabled={deleteMut.isPending}
                style={{
                  background: C.redBg, color: C.red, border: `1px solid ${C.red44}`,
                  borderRadius: 5, padding: '5px 12px', cursor: 'pointer', fontSize: 11,
                }}
              >
                {deleteMut.isPending ? '…' : 'Delete'}
              </button>
            </div>

            {/* Tabs */}
            <div style={{
              display: 'flex', borderBottom: `1px solid ${C.border}`, flexShrink: 0,
            }}>
              {TABS.map(t => (
                <button
                  key={t.key}
                  onClick={() => setTab(t.key)}
                  style={{
                    padding: '7px 14px', background: 'none', cursor: 'pointer', fontSize: 12,
                    color: tab === t.key ? C.blue : C.textMuted,
                    borderBottom: `2px solid ${tab === t.key ? C.blue : 'transparent'}`,
                    border: 'none', borderBottomWidth: 2, marginBottom: -1,
                  }}
                >
                  {t.label}
                </button>
              ))}
            </div>

            {/* Tab content */}
            <div style={{ flex: 1, overflowY: 'auto', padding: CONTENT_PAD }}>
              {tab === 'definition' && (
                <StrategyDefinitionPage
                  key={selectedStrategy.updatedAt}
                  strategyId={selectedStrategy.id}
                  initialData={selectedStrategy}
                  onSaved={(s: Strategy) => {
                    qc.setQueryData<Strategy[]>(['strategies'], old =>
                      old ? old.map(x => x.id === s.id ? s : x) : [s]
                    )
                    // No invalidateQueries here: setQueryData is synchronous, and a
                    // concurrent refetch would overwrite the local update before the
                    // server confirms, causing a flicker.
                  }}
                  onCancel={() => {}}
                />
              )}
              {tab === 'scenarios' && <ScenariosTab strategy={selectedStrategy} />}
              {tab === 'results' && <ResultsTab strategyId={selectedStrategy.id} />}
              {tab === 'compare' && <CompareTab strategyId={selectedStrategy.id} />}
              {tab === 'deployments' && <DeploymentsTab strategy={selectedStrategy} />}
            </div>
          </>
        )}

        {/* Empty state — nothing selected */}
        {!creating && !editingDefinition && !selectedStrategy && (
          <div style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center', flexDirection: 'column', gap: SP.md }}>
            <div style={{ fontSize: 14, color: C.textMuted }}>Select a strategy or create a new one</div>
            <button
              onClick={() => { setCreating(true); setCreatingType(null) }}
              style={primaryBtnStyle}
            >
              + New Strategy
            </button>
          </div>
        )}
      </div>
    </div>
  )
}

const primaryBtnStyle: React.CSSProperties = {
  width: '100%', background: '#1e3a5f', color: '#93c5fd', border: '1px solid #2563eb44',
  borderRadius: 5, padding: '6px 14px', cursor: 'pointer', fontSize: 12, fontWeight: 700,
}

const secondaryBtnStyle: React.CSSProperties = {
  background: C.surface2, color: C.textSub, border: `1px solid ${C.border}`,
  borderRadius: 5, padding: '5px 12px', cursor: 'pointer', fontSize: 11,
}
