import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { strategyDomainApi } from '../../api/client'
import { RunMetrics, RunMode, RunResult, Scenario } from '../../types/strategy'
import { C, F, SP } from '../../styles/tokens'

interface Props {
  strategyId: string
}

type CompareMode = 1 | 2 | 3

interface MetricRow {
  key: keyof RunMetrics
  label: string
  format: (v: number) => string
}

const METRIC_ROWS: MetricRow[] = [
  { key: 'returnPct',             label: 'Return',       format: v => `${v >= 0 ? '+' : ''}${v.toFixed(1)}%` },
  { key: 'maxDrawdownPct',        label: 'Max DD',       format: v => `-${v.toFixed(1)}%` },
  { key: 'sharpe',                label: 'Sharpe',       format: v => v.toFixed(2) },
  { key: 'winRate',               label: 'Win %',        format: v => `${v.toFixed(0)}%` },
  { key: 'profitFactor',          label: 'PF',           format: v => v.toFixed(2) },
  { key: 'tradeCount',            label: 'Trades',       format: v => String(v) },
  { key: 'avgRPerTrade',          label: 'Avg R',        format: v => `${v.toFixed(2)} R` },
  { key: 'expectancy',            label: 'Expectancy',   format: v => `₹${v.toFixed(0)}` },
  { key: 'avgRealisedRR',         label: 'Realised R:R', format: v => v.toFixed(2) },
  { key: 'btToFtDegradationRatio', label: 'BT→FT',       format: v => v.toFixed(2) },
]

export function CompareTab({ strategyId }: Props) {
  const [compareMode, setCompareMode] = useState<CompareMode>(1)
  const [selectedScenarios, setSelectedScenarios] = useState<string[]>([])
  const [runModeFilter, setRunModeFilter] = useState<RunMode | 'Both'>('Both')
  const [notes, setNotes] = useState('')

  const { data: scenarios = [] } = useQuery({
    queryKey: ['scenarios', strategyId],
    queryFn: () => strategyDomainApi.listScenarios(strategyId),
  })

  const { data: runs = [], isLoading } = useQuery({
    queryKey: ['runs', strategyId],
    queryFn: () => strategyDomainApi.listRuns(strategyId),
  })

  const selectedRuns = runs.filter(r => {
    if (selectedScenarios.length > 0 && !selectedScenarios.includes(r.scenarioId)) return false
    if (runModeFilter !== 'Both' && r.mode !== runModeFilter) return false
    return true
  }).slice(0, 5)

  function toggleScenario(id: string) {
    if (selectedScenarios.includes(id)) {
      setSelectedScenarios(prev => prev.filter(x => x !== id))
    } else if (selectedScenarios.length < 5) {
      setSelectedScenarios(prev => [...prev, id])
    }
  }

  const colors = [C.blue, C.green, C.amber, '#a78bfa', '#f97316']

  return (
    <div style={{ display: 'flex', gap: SP.lg, minHeight: 400 }}>
      {/* Left pane */}
      <div style={{
        width: 240, flexShrink: 0, display: 'flex', flexDirection: 'column', gap: SP.md,
        borderRight: `1px solid ${C.border}`, paddingRight: SP.md,
      }}>
        <div>
          <div style={{ fontSize: 11, fontWeight: 600, color: C.textSub, marginBottom: SP.sm }}>Compare Mode</div>
          {[
            { mode: 1 as CompareMode, label: 'BT vs FT (same scenario)' },
            { mode: 2 as CompareMode, label: 'Scenario A vs B' },
            { mode: 3 as CompareMode, label: 'Different deployments' },
          ].map(({ mode, label }) => (
            <label key={mode} style={{ display: 'flex', alignItems: 'center', gap: SP.xs, cursor: 'pointer', marginBottom: 6 }}>
              <input
                type="radio"
                checked={compareMode === mode}
                onChange={() => { setCompareMode(mode); setSelectedScenarios([]) }}
              />
              <span style={{ fontSize: 12, color: compareMode === mode ? C.text : C.textMuted }}>{label}</span>
            </label>
          ))}
        </div>

        <div>
          <div style={{ fontSize: 11, fontWeight: 600, color: C.textSub, marginBottom: SP.sm }}>
            Scenarios <span style={{ color: C.textDim }}>(max 5)</span>
          </div>
          {scenarios.map((s, i) => (
            <button
              key={s.id}
              onClick={() => toggleScenario(s.id)}
              style={{
                display: 'block', width: '100%', textAlign: 'left',
                padding: '4px 8px', marginBottom: 4, borderRadius: 4, cursor: 'pointer',
                fontSize: 11,
                background: selectedScenarios.includes(s.id) ? `${colors[selectedScenarios.indexOf(s.id)]}22` : C.surface2,
                color: selectedScenarios.includes(s.id) ? colors[selectedScenarios.indexOf(s.id)] : C.textMuted,
                border: `1px solid ${selectedScenarios.includes(s.id) ? colors[i % colors.length] + '66' : C.border}`,
              }}
            >
              {s.name}
            </button>
          ))}
        </div>

        <div>
          <div style={{ fontSize: 11, fontWeight: 600, color: C.textSub, marginBottom: SP.sm }}>Mode</div>
          {(['Both', RunMode.Backtest, RunMode.ForwardTest] as (RunMode | 'Both')[]).map(m => (
            <button
              key={m}
              onClick={() => setRunModeFilter(m)}
              style={{
                display: 'block', width: '100%', textAlign: 'left',
                padding: '4px 8px', marginBottom: 4, borderRadius: 4, cursor: 'pointer',
                fontSize: 11,
                background: runModeFilter === m ? C.blueBg : C.surface2,
                color: runModeFilter === m ? C.blue : C.textMuted,
                border: `1px solid ${runModeFilter === m ? C.blue + '66' : C.border}`,
              }}
            >
              {m === 'Both' ? 'Both' : m === RunMode.Backtest ? 'Backtest' : 'Forward Test'}
            </button>
          ))}
        </div>
      </div>

      {/* Right pane */}
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: SP.lg }}>
        {isLoading && (
          <div style={{ color: C.textMuted, fontSize: 12 }}>Loading runs…</div>
        )}

        {!isLoading && selectedRuns.length === 0 && (
          <div style={{ textAlign: 'center', padding: 40, color: C.textMuted, fontSize: 12 }}>
            Select scenarios on the left to compare results.
          </div>
        )}

        {selectedRuns.length > 0 && (
          <>
            {/* Metric table */}
            <div style={{ overflowX: 'auto' }}>
              <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
                <thead>
                  <tr style={{ background: C.surface2 }}>
                    <th style={thStyle}>Metric</th>
                    {selectedRuns.map((r, i) => {
                      const scenarioName = scenarios.find(s => s.id === r.scenarioId)?.name ?? r.scenarioId
                      return (
                        <th key={r.id} style={{ ...thStyle, color: colors[i % colors.length] }}>
                          {scenarioName}<br />
                          <span style={{ fontSize: 10, fontWeight: 400, color: C.textMuted }}>
                            {r.mode === RunMode.ForwardTest ? 'Fwd Test' : 'Backtest'}
                          </span>
                        </th>
                      )
                    })}
                    {selectedRuns.length >= 2 && <th style={thStyle}>Δ</th>}
                  </tr>
                </thead>
                <tbody>
                  {METRIC_ROWS.map(row => {
                    const values = selectedRuns.map(r => r.metrics[row.key] as number | undefined)
                    const definedValues = values.filter((v): v is number => v !== undefined)
                    const bestIdx = definedValues.length > 0
                      ? values.indexOf(Math.max(...definedValues.map(v => row.key === 'maxDrawdownPct' ? -v : v)))
                      : -1
                    const delta = selectedRuns.length >= 2
                      ? (() => {
                        const a = values[0]; const b = values[1]
                        if (a === undefined || b === undefined) return null
                        return b - a
                      })()
                      : null

                    return (
                      <tr key={row.key} style={{ borderBottom: `1px solid ${C.border2}` }}>
                        <td style={{ ...tdStyle, color: C.textMuted }}>{row.label}</td>
                        {values.map((v, i) => {
                          const isDegradation = row.key === 'btToFtDegradationRatio'
                          const isBad = isDegradation && v !== undefined && v < 0.7
                          const isBest = i === bestIdx && definedValues.length > 1

                          return (
                            <td key={i} style={{
                              ...tdStyle,
                              fontFamily: F.mono,
                              textAlign: 'right',
                              background: isBad ? C.redBg : (isBest ? C.greenBg : 'transparent'),
                              color: isBad ? C.red : (isBest && !isBad ? C.green : C.text),
                            }}>
                              {v === undefined ? '—' : row.format(v)}
                            </td>
                          )
                        })}
                        {selectedRuns.length >= 2 && (
                          <td style={{
                            ...tdStyle, fontFamily: F.mono, textAlign: 'right',
                            color: delta === null ? C.textMuted : (delta >= 0 ? C.green : C.red),
                          }}>
                            {delta === null ? '—' : `${delta >= 0 ? '+' : ''}${delta.toFixed(2)}`}
                          </td>
                        )}
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>

            {/* Equity curve placeholder */}
            <div style={{ background: C.surface2, borderRadius: 6, padding: SP.lg, minHeight: 120 }}>
              <div style={{ fontSize: 11, color: C.textMuted, marginBottom: SP.sm }}>Equity Curve</div>
              <EquityCurvePlaceholder runs={selectedRuns} colors={colors} scenarios={scenarios} />
            </div>

            {/* Notes */}
            <div>
              <div style={{ fontSize: 11, color: C.textMuted, marginBottom: 4 }}>Research Notes</div>
              <textarea
                value={notes}
                onChange={e => setNotes(e.target.value)}
                placeholder="Add comparison notes…"
                rows={4}
                style={{
                  width: '100%', background: C.surface2, border: `1px solid ${C.border}`,
                  color: C.text, borderRadius: 6, padding: SP.sm, fontSize: 12,
                  resize: 'vertical', boxSizing: 'border-box',
                }}
              />
            </div>
          </>
        )}
      </div>
    </div>
  )
}

function EquityCurvePlaceholder({ runs, colors, scenarios }: {
  runs: RunResult[]; colors: string[]; scenarios: Scenario[]
}) {
  return (
    <div style={{ display: 'flex', gap: SP.lg, alignItems: 'flex-end', height: 80 }}>
      {runs.map((r, i) => {
        const ret = r.metrics.returnPct
        const height = Math.max(10, Math.min(80, 40 + ret))
        const scenarioName = scenarios.find(s => s.id === r.scenarioId)?.name ?? r.scenarioId
        return (
          <div key={r.id} style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 4 }}>
            <div style={{ width: 60, height, background: colors[i % colors.length] + '33', border: `1px solid ${colors[i % colors.length]}66`, borderRadius: 3 }} />
            <span style={{ fontSize: 9, color: C.textMuted, maxWidth: 60, textAlign: 'center', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
              {scenarioName}
            </span>
            <span style={{ fontSize: 10, color: colors[i % colors.length], fontFamily: F.mono }}>
              {ret >= 0 ? '+' : ''}{ret.toFixed(1)}%
            </span>
          </div>
        )
      })}
      <div style={{ fontSize: 10, color: C.textDim, alignSelf: 'center' }}>
        (Chart library not yet wired — return bars shown)
      </div>
    </div>
  )
}

const thStyle: React.CSSProperties = {
  padding: '6px 10px', textAlign: 'left', fontSize: 11, color: C.textMuted, fontWeight: 600,
  borderBottom: `1px solid ${C.border}`,
}

const tdStyle: React.CSSProperties = { padding: '5px 10px', fontSize: 12, color: C.text }
