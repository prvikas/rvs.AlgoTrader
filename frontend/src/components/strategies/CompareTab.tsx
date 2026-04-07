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
  higherIsBetter?: boolean
}

const METRIC_ROWS: MetricRow[] = [
  { key: 'returnPct',              label: 'Return',       format: v => `${v >= 0 ? '+' : ''}${v.toFixed(1)}%`,   higherIsBetter: true },
  { key: 'maxDrawdownPct',         label: 'Max DD',        format: v => `-${v.toFixed(1)}%`,                      higherIsBetter: false },
  { key: 'sharpe',                 label: 'Sharpe',        format: v => v.toFixed(2),                             higherIsBetter: true },
  { key: 'winRate',                label: 'Win %',         format: v => `${v.toFixed(0)}%`,                       higherIsBetter: true },
  { key: 'profitFactor',           label: 'PF',            format: v => v.toFixed(2),                             higherIsBetter: true },
  { key: 'tradeCount',             label: 'Trades',        format: v => String(v),                                higherIsBetter: true },
  { key: 'avgRPerTrade',           label: 'Avg R',         format: v => `${v.toFixed(2)} R`,                      higherIsBetter: true },
  { key: 'expectancy',             label: 'Expectancy',    format: v => `₹${v.toFixed(0)}`,                       higherIsBetter: true },
  { key: 'avgRealisedRR',          label: 'Realised R:R',  format: v => v.toFixed(2),                             higherIsBetter: true },
  { key: 'btToFtDegradationRatio', label: 'BT→FT',         format: v => v.toFixed(2),                             higherIsBetter: true },
]

interface RobustnessRow {
  key: keyof RunMetrics
  label: string
  format: (v: number) => string
  good: number
  warn: number
  lowerIsBetter?: boolean
}

const ROBUSTNESS_ROWS: RobustnessRow[] = [
  { key: 'walkForwardEfficiency',    label: 'Walk-Forward Efficiency',   format: v => v.toFixed(2),  good: 0.65, warn: 0.45 },
  { key: 'parameterStabilityScore', label: 'Parameter Stability Score', format: v => v.toFixed(2),  good: 0.60, warn: 0.40 },
  { key: 'monteCarloMedianReturn',  label: 'MC Median Return',          format: v => `${v.toFixed(1)}%`, good: 0, warn: -5 },
  { key: 'monteCarloPct5Return',    label: 'MC P5 Return',              format: v => `${v.toFixed(1)}%`, good: -5, warn: -15 },
  { key: 'overfitScore',            label: 'Overfit Score',             format: v => v.toFixed(2),  good: 0.10, warn: 0.20, lowerIsBetter: true },
  { key: 'degreesFreedomRatio',     label: 'Degrees of Freedom Ratio',  format: v => v.toFixed(1),  good: 15, warn: 8 },
]

interface FunnelFilter {
  label: string
  check: (m: RunMetrics) => boolean
  defaultOn: boolean
}

const FUNNEL_FILTERS: FunnelFilter[] = [
  { label: 'Return > 0%',         check: m => m.returnPct > 0,           defaultOn: true },
  { label: 'Profit Factor > 1.3', check: m => m.profitFactor > 1.3,     defaultOn: true },
  { label: 'Max DD < 20%',        check: m => m.maxDrawdownPct < 20,     defaultOn: true },
  { label: 'Trade count ≥ 30',    check: m => m.tradeCount >= 30,        defaultOn: true },
  { label: 'WFE ≥ 0.65',         check: m => (m.walkForwardEfficiency ?? 0) >= 0.65, defaultOn: true },
  { label: 'DoF Ratio ≥ 10',      check: m => (m.degreesFreedomRatio ?? 0) >= 10, defaultOn: false },
]

function robustnessColor(row: RobustnessRow, v: number): string {
  const { good, warn, lowerIsBetter } = row
  if (lowerIsBetter) {
    if (v <= good) return C.green
    if (v <= warn) return C.amber
    return C.red
  } else {
    if (v >= good) return C.green
    if (v >= warn) return C.amber
    return C.red
  }
}

function robustnessChip(row: RobustnessRow, v: number | undefined) {
  if (v === undefined) return <span style={{ color: C.textMuted }}>—</span>
  const color = robustnessColor(row, v)
  const icon = color === C.green ? '✓' : color === C.amber ? '⚠' : '✗'
  return (
    <span style={{
      fontSize: 10, fontWeight: 700, color,
      padding: '1px 5px', borderRadius: 3,
      border: `1px solid ${color}44`, background: `${color}11`,
    }}>
      {icon}
    </span>
  )
}

export function CompareTab({ strategyId }: Props) {
  const [compareMode, setCompareMode] = useState<CompareMode>(1)
  const [selectedScenarios, setSelectedScenarios] = useState<string[]>([])
  const [runModeFilter, setRunModeFilter] = useState<RunMode | 'Both'>('Both')
  const [notes, setNotes] = useState('')
  const [funnelEnabled, setFunnelEnabled] = useState<boolean[]>(FUNNEL_FILTERS.map(f => f.defaultOn))
  const [funnelApplied, setFunnelApplied] = useState<boolean[]>(FUNNEL_FILTERS.map(f => f.defaultOn))
  const [regimeExpanded, setRegimeExpanded] = useState(false)

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

  // Research funnel — applied filters determine which runs are "survivors"
  function isFiltered(run: RunResult): boolean {
    return funnelApplied.some((on, i) => on && !FUNNEL_FILTERS[i].check(run.metrics))
  }

  const survivorCount = selectedRuns.filter(r => !isFiltered(r)).length

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
      {/* ── Left pane ─────────────────────────────────────── */}
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
              {s.isBaseline && <span style={{ color: C.amber, marginRight: 4, fontSize: 9 }}>BASE</span>}
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

        {/* Research Funnel */}
        <div>
          <div style={{ fontSize: 11, fontWeight: 600, color: C.textSub, marginBottom: SP.sm }}>Research Funnel</div>
          {FUNNEL_FILTERS.map((f, i) => (
            <label key={i} style={{ display: 'flex', alignItems: 'center', gap: 6, cursor: 'pointer', marginBottom: 5 }}>
              <input
                type="checkbox"
                checked={funnelEnabled[i]}
                onChange={e => {
                  const next = [...funnelEnabled]; next[i] = e.target.checked; setFunnelEnabled(next)
                }}
              />
              <span style={{ fontSize: 11, color: C.textMuted }}>{f.label}</span>
            </label>
          ))}
          <button
            onClick={() => setFunnelApplied([...funnelEnabled])}
            style={{
              width: '100%', background: C.blueBg, color: C.blue,
              border: `1px solid ${C.blue}44`, borderRadius: 4,
              padding: '5px 0', cursor: 'pointer', fontSize: 11, fontWeight: 600, marginTop: 4,
            }}
          >
            Apply Filters
          </button>
          {selectedRuns.length > 0 && (
            <div style={{ fontSize: 11, color: C.textSub, marginTop: 6, textAlign: 'center' }}>
              <span style={{ color: survivorCount === selectedRuns.length ? C.green : C.amber }}>
                {survivorCount} / {selectedRuns.length}
              </span>
              {' '}scenarios pass filters
            </div>
          )}
        </div>
      </div>

      {/* ── Right pane ─────────────────────────────────────── */}
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
            {/* Performance metric table */}
            <div style={{ overflowX: 'auto' }}>
              <div style={{ fontSize: 11, fontWeight: 600, color: C.textSub, marginBottom: 6 }}>Performance Metrics</div>
              <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
                <thead>
                  <tr style={{ background: C.surface2 }}>
                    <th style={thStyle}>Metric</th>
                    {selectedRuns.map((r, i) => {
                      const scenarioName = scenarios.find(s => s.id === r.scenarioId)?.name ?? r.scenarioId
                      const filtered = isFiltered(r)
                      return (
                        <th key={r.id} style={{ ...thStyle, color: filtered ? C.textDim : colors[i % colors.length] }}>
                          {scenarioName}
                          {filtered && (
                            <span style={{
                              marginLeft: 6, fontSize: 9, color: C.textDim,
                              padding: '1px 4px', border: `1px solid ${C.border}`, borderRadius: 2,
                            }}>FILTERED</span>
                          )}
                          <br />
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
                      ? values.indexOf(
                          row.higherIsBetter === false
                            ? Math.min(...definedValues)
                            : Math.max(...definedValues)
                        )
                      : -1
                    const delta = selectedRuns.length >= 2
                      ? (() => { const a = values[0]; const b = values[1]; return (a !== undefined && b !== undefined) ? b - a : null })()
                      : null

                    return (
                      <tr key={row.key} style={{ borderBottom: `1px solid ${C.border2}` }}>
                        <td style={{ ...tdStyle, color: C.textMuted }}>{row.label}</td>
                        {values.map((v, i) => {
                          const isDegradation = row.key === 'btToFtDegradationRatio'
                          const isBad = isDegradation && v !== undefined && v < 0.7
                          const isBest = i === bestIdx && definedValues.length > 1
                          const filtered = isFiltered(selectedRuns[i])
                          return (
                            <td key={i} style={{
                              ...tdStyle, fontFamily: F.mono, textAlign: 'right',
                              background: isBad ? C.redBg : (isBest ? C.greenBg : 'transparent'),
                              color: filtered ? C.textDim : isBad ? C.red : isBest ? C.green : C.text,
                              opacity: filtered ? 0.5 : 1,
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
            <div style={{ background: C.surface2, borderRadius: 6, padding: SP.lg, minHeight: 100 }}>
              <div style={{ fontSize: 11, color: C.textMuted, marginBottom: SP.sm }}>Equity Curve</div>
              <EquityCurvePlaceholder runs={selectedRuns} colors={colors} scenarios={scenarios} />
            </div>

            {/* Robustness Metrics section */}
            <div>
              <div style={{ fontSize: 11, fontWeight: 600, color: C.textSub, marginBottom: 6 }}>Robustness Metrics</div>
              <div style={{
                fontSize: 10, color: C.textMuted, marginBottom: 8,
                padding: `${SP.xs} ${SP.sm}`, background: C.surface2, borderRadius: 4,
              }}>
                Backend-computed metrics. Monte Carlo values show spinner while computing.
                Chips: <span style={{ color: C.green }}>✓ pass</span> / <span style={{ color: C.amber }}>⚠ marginal</span> / <span style={{ color: C.red }}>✗ fail</span>
              </div>
              <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
                <thead>
                  <tr style={{ background: C.surface2 }}>
                    <th style={thStyle}>Metric</th>
                    {selectedRuns.map((r, i) => {
                      const name = scenarios.find(s => s.id === r.scenarioId)?.name ?? r.scenarioId
                      return <th key={r.id} style={{ ...thStyle, color: colors[i % colors.length] }}>{name}</th>
                    })}
                    {selectedRuns.length >= 2 && <th style={thStyle}>Δ</th>}
                    <th style={thStyle}>Threshold</th>
                  </tr>
                </thead>
                <tbody>
                  {ROBUSTNESS_ROWS.map(row => {
                    const values = selectedRuns.map(r => r.metrics[row.key] as number | undefined)
                    const delta = selectedRuns.length >= 2
                      ? (() => { const a = values[0]; const b = values[1]; return (a !== undefined && b !== undefined) ? b - a : null })()
                      : null
                    const isMC = row.key === 'monteCarloMedianReturn' || row.key === 'monteCarloPct5Return'
                    const thresholdLabel = row.lowerIsBetter
                      ? `≤${row.good} ✓ | ≤${row.warn} ⚠`
                      : `≥${row.good} ✓ | ≥${row.warn} ⚠`

                    return (
                      <tr key={row.key} style={{ borderBottom: `1px solid ${C.border2}` }}>
                        <td style={{ ...tdStyle, color: C.textMuted }}>{row.label}</td>
                        {values.map((v, i) => {
                          const color = v !== undefined ? robustnessColor(row, v) : C.textMuted
                          const isBest = selectedRuns.length > 1 && v !== undefined
                            && v === (row.lowerIsBetter ? Math.min(...values.filter((x): x is number => x !== undefined)) : Math.max(...values.filter((x): x is number => x !== undefined)))
                          return (
                            <td key={i} style={{
                              ...tdStyle, fontFamily: F.mono, textAlign: 'right',
                              color, background: isBest && values.filter(x => x !== undefined).length > 1 ? (color === C.green ? C.greenBg : 'transparent') : 'transparent',
                            }}>
                              {isMC && v === undefined
                                ? <span style={{ color: C.textDim }}>⟳</span>
                                : v === undefined ? '—' : row.format(v)
                              }
                              {' '}{v !== undefined && robustnessChip(row, v)}
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
                        <td style={{ ...tdStyle, fontSize: 10, color: C.textDim }}>{thresholdLabel}</td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>

            {/* Regime breakdown accordion */}
            {selectedRuns.some(r => r.metrics.regimeBreakdown && Object.keys(r.metrics.regimeBreakdown).length > 0) && (
              <div>
                <button
                  onClick={() => setRegimeExpanded(p => !p)}
                  style={{
                    background: 'none', border: `1px solid ${C.border}`, color: C.textSub,
                    borderRadius: 4, padding: '5px 12px', cursor: 'pointer', fontSize: 11,
                    display: 'flex', alignItems: 'center', gap: 6,
                  }}
                >
                  {regimeExpanded ? '▼' : '▶'} Regime Breakdown
                </button>
                {regimeExpanded && (
                  <div style={{ marginTop: SP.sm, overflowX: 'auto' }}>
                    {(() => {
                      const allRegimes = new Set<string>()
                      selectedRuns.forEach(r => {
                        if (r.metrics.regimeBreakdown) {
                          Object.keys(r.metrics.regimeBreakdown).forEach(k => allRegimes.add(k))
                        }
                      })
                      return (
                        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 11 }}>
                          <thead>
                            <tr style={{ background: C.surface2 }}>
                              <th style={thStyle}>Regime</th>
                              {selectedRuns.map((r, i) => (
                                <th key={r.id} style={{ ...thStyle, color: colors[i % colors.length] }}>
                                  {scenarios.find(s => s.id === r.scenarioId)?.name ?? r.scenarioId}
                                </th>
                              ))}
                            </tr>
                          </thead>
                          <tbody>
                            {Array.from(allRegimes).map(regime => (
                              <tr key={regime} style={{ borderBottom: `1px solid ${C.border2}` }}>
                                <td style={{ ...tdStyle, color: C.textSub }}>{regime}</td>
                                {selectedRuns.map(r => {
                                  const m = r.metrics.regimeBreakdown?.[regime]
                                  return (
                                    <td key={r.id} style={{ ...tdStyle, fontFamily: F.mono, textAlign: 'right' }}>
                                      {m ? (
                                        <span>
                                          <span style={{ color: m.returnPct >= 0 ? C.green : C.red }}>
                                            {m.returnPct >= 0 ? '+' : ''}{m.returnPct.toFixed(1)}%
                                          </span>
                                          <span style={{ color: C.textMuted, fontSize: 10, marginLeft: 4 }}>
                                            ({m.tradeCount}t)
                                          </span>
                                        </span>
                                      ) : '—'}
                                    </td>
                                  )
                                })}
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      )
                    })()}
                  </div>
                )}
              </div>
            )}

            {/* Research notes */}
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
        (Chart library not yet wired)
      </div>
    </div>
  )
}

const thStyle: React.CSSProperties = {
  padding: '6px 10px', textAlign: 'left', fontSize: 11, color: C.textMuted, fontWeight: 600,
  borderBottom: `1px solid ${C.border}`,
}

const tdStyle: React.CSSProperties = { padding: '5px 10px', fontSize: 12, color: C.text }

import React from 'react'
