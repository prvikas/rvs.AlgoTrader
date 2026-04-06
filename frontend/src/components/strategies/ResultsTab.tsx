import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { strategyDomainApi } from '../../api/client'
import { RunMode } from '../../types/strategy'
import { C, F, SP, TABLE_CELL } from '../../styles/tokens'
import { useEnums } from '../../context/EnumsContext'

interface Props {
  strategyId: string
}

export function ResultsTab({ strategyId }: Props) {
  const { enums } = useEnums()
  const runModeOptions = enums.runMode ?? []

  const [selectedScenarios, setSelectedScenarios] = useState<string[]>([])
  const [modeFilter, setModeFilter] = useState<RunMode | 'All'>('All')
  const [fromDate, setFromDate] = useState('')
  const [toDate, setToDate] = useState('')

  const { data: scenarios = [] } = useQuery({
    queryKey: ['scenarios', strategyId],
    queryFn: () => strategyDomainApi.listScenarios(strategyId),
  })

  const { data: runs = [], isLoading, error, refetch } = useQuery({
    queryKey: ['runs', strategyId],
    queryFn: () => strategyDomainApi.listRuns(strategyId),
  })

  const filtered = runs.filter(r => {
    if (selectedScenarios.length > 0 && !selectedScenarios.includes(r.scenarioId)) return false
    if (modeFilter !== 'All' && r.mode !== modeFilter) return false
    if (fromDate && r.dateRange.from < fromDate) return false
    if (toDate && r.dateRange.to > toDate) return false
    return true
  })

  function toggleScenario(id: string) {
    setSelectedScenarios(prev =>
      prev.includes(id) ? prev.filter(x => x !== id) : [...prev, id]
    )
  }

  return (
    <div>
      {/* Filters bar */}
      <div style={{
        display: 'flex', gap: SP.md, alignItems: 'center', flexWrap: 'wrap',
        marginBottom: SP.md, padding: SP.sm,
        background: C.surface2, borderRadius: 6,
      }}>
        <div>
          <label style={{ fontSize: 10, color: C.textMuted, display: 'block', marginBottom: 2 }}>Scenario</label>
          <div style={{ display: 'flex', gap: SP.xs, flexWrap: 'wrap' }}>
            {scenarios.map(s => (
              <button
                key={s.id}
                onClick={() => toggleScenario(s.id)}
                style={{
                  fontSize: 10, padding: '2px 8px', borderRadius: 3, cursor: 'pointer',
                  background: selectedScenarios.includes(s.id) ? C.blueBg : C.surface3,
                  color: selectedScenarios.includes(s.id) ? C.blue : C.textMuted,
                  border: `1px solid ${selectedScenarios.includes(s.id) ? C.blue + '66' : C.border}`,
                }}
              >
                {s.name}
              </button>
            ))}
          </div>
        </div>

        <div>
          <label style={{ fontSize: 10, color: C.textMuted, display: 'block', marginBottom: 2 }}>Mode</label>
          <div style={{ display: 'flex', gap: 4 }}>
            {(['All', ...runModeOptions.map(o => o.value)] as (RunMode | 'All')[]).map(m => (
              <button
                key={m}
                onClick={() => setModeFilter(m)}
                style={{
                  fontSize: 10, padding: '2px 8px', borderRadius: 3, cursor: 'pointer',
                  background: modeFilter === m ? C.blueBg : C.surface3,
                  color: modeFilter === m ? C.blue : C.textMuted,
                  border: `1px solid ${modeFilter === m ? C.blue + '66' : C.border}`,
                }}
              >
                {m === 'All' ? 'All' : runModeOptions.find(o => o.value === m)?.label ?? m}
              </button>
            ))}
          </div>
        </div>

        <div style={{ display: 'flex', gap: SP.sm, alignItems: 'flex-end' }}>
          <div>
            <label style={{ fontSize: 10, color: C.textMuted, display: 'block', marginBottom: 2 }}>From</label>
            <input type="date" value={fromDate} onChange={e => setFromDate(e.target.value)} style={filterInput} />
          </div>
          <div>
            <label style={{ fontSize: 10, color: C.textMuted, display: 'block', marginBottom: 2 }}>To</label>
            <input type="date" value={toDate} onChange={e => setToDate(e.target.value)} style={filterInput} />
          </div>
        </div>
      </div>

      {isLoading && <SkeletonRows />}

      {!isLoading && error && (
        <div style={{ color: C.red, fontSize: 12 }}>
          Failed to load results.{' '}
          <button onClick={refetch as () => void} style={{ color: C.blue, background: 'none', border: 'none', cursor: 'pointer', fontSize: 12 }}>Retry?</button>
        </div>
      )}

      {!isLoading && !error && filtered.length === 0 && (
        <div style={{ textAlign: 'center', padding: 40, color: C.textMuted, fontSize: 12 }}>
          No run results yet. Run a scenario to generate results.
        </div>
      )}

      {!isLoading && !error && filtered.length > 0 && (
        <div style={{ overflowX: 'auto' }}>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
            <thead>
              <tr style={{ background: C.surface2 }}>
                {['Scenario', 'Mode', 'Date Range', 'Return', 'Max DD', 'Sharpe', 'Win%', 'PF', 'Trades'].map(h => (
                  <th key={h} style={thStyle}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {filtered.map(r => {
                const scenarioName = scenarios.find(s => s.id === r.scenarioId)?.name ?? r.scenarioId
                return (
                  <tr key={r.id} style={{ borderBottom: `1px solid ${C.border2}` }}>
                    <td style={tdStyle}>{scenarioName}</td>
                    <td style={{ ...tdStyle, color: r.mode === RunMode.ForwardTest ? C.blue : C.textSub }}>
                      {r.mode === RunMode.ForwardTest ? 'Fwd Test' : 'Backtest'}
                    </td>
                    <td style={{ ...tdStyle, color: C.textMuted }}>
                      {r.dateRange.from.slice(0, 10)} – {r.dateRange.to.slice(0, 10)}
                    </td>
                    <NumericCell value={r.metrics.returnPct} pct colored />
                    <NumericCell value={r.metrics.maxDrawdownPct} pct negative />
                    <NumericCell value={r.metrics.sharpe} decimals={2} />
                    <NumericCell value={r.metrics.winRate} pct />
                    <NumericCell value={r.metrics.profitFactor} decimals={2} />
                    <td style={{ ...tdStyle, fontFamily: F.mono, textAlign: 'right' }}>{r.metrics.tradeCount}</td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

function NumericCell({ value, pct, colored, negative, decimals = 1 }: {
  value: number; pct?: boolean; colored?: boolean; negative?: boolean; decimals?: number
}) {
  const color = negative ? C.red : (colored ? (value >= 0 ? C.green : C.red) : C.text)
  const formatted = pct
    ? `${value >= 0 && !negative ? '+' : ''}${value.toFixed(decimals)}%`
    : value.toFixed(decimals)
  return (
    <td style={{ ...tdStyle, fontFamily: F.mono, textAlign: 'right', color }}>{formatted}</td>
  )
}

function SkeletonRows() {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
      {[1, 2, 3].map(i => (
        <div key={i} style={{ height: 32, background: C.surface2, borderRadius: 4, opacity: 0.5 }} />
      ))}
    </div>
  )
}

const filterInput: React.CSSProperties = {
  background: C.surface3, border: `1px solid ${C.border}`,
  color: C.text, borderRadius: 4, padding: '4px 6px', fontSize: 11,
}

const thStyle: React.CSSProperties = {
  padding: TABLE_CELL, textAlign: 'left', fontSize: 11, color: C.textMuted, fontWeight: 600,
  borderBottom: `1px solid ${C.border}`,
}

const tdStyle: React.CSSProperties = { padding: TABLE_CELL, fontSize: 12, color: C.text }
