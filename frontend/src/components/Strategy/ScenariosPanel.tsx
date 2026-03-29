import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  scenariosApi,
  backtestApi,
  StrategyScenario,
  ScenarioComparisonRow,
} from '../../api/client'
import { C, SP, TABLE_CELL, TABLE_HEADER_CELL } from '../../styles/tokens'
import { formatInr } from '../../utils/datetime'
import ScenarioEditorDrawer from './ScenarioEditorDrawer'

interface Props {
  instanceId: string
  strategyType: string
  instanceName: string
  baseParametersJson?: string
  /** Run form shared fields — inherited from the parent backtest panel */
  defaultSymbol?: string
  defaultTimeframe?: string
  defaultFromDate?: string
  defaultToDate?: string
  defaultCapital?: number
}

const STATUS_COLOR: Record<string, string> = {
  Draft:       C.textMuted,
  Backtested:  C.blue,
  ForwardTest: C.amber,
  Live:        C.green,
  Archived:    C.textMuted,
}

const STATUS_BG: Record<string, string> = {
  Draft:       C.surface2,
  Backtested:  C.blueBg,
  ForwardTest: C.amberBg,
  Live:        C.greenBg,
  Archived:    C.surface2,
}

function pct(v?: number) {
  return v != null ? `${(v * 100).toFixed(1)}%` : '—'
}
function fmt2(v?: number) {
  return v != null ? v.toFixed(2) : '—'
}
function fmtN(v?: number) {
  return v != null ? String(v) : '—'
}

export default function ScenariosPanel({
  instanceId, strategyType, instanceName, baseParametersJson,
  defaultSymbol = '', defaultTimeframe = '1d',
  defaultFromDate = '', defaultToDate = '', defaultCapital = 100_000,
}: Props) {
  const qc = useQueryClient()
  const [drawerOpen, setDrawerOpen] = useState(false)
  const [editingScenario, setEditingScenario] = useState<StrategyScenario | undefined>()
  const [view, setView] = useState<'list' | 'compare'>('list')
  const [runConfig, setRunConfig] = useState({
    symbol:    defaultSymbol,
    timeframe: defaultTimeframe,
    fromDate:  defaultFromDate,
    toDate:    defaultToDate,
    capital:   defaultCapital,
  })
  const [runningJobs, setRunningJobs] = useState<Record<string, string>>({}) // scenarioId→jobId
  const [runError, setRunError] = useState<string | null>(null)

  // List
  const { data: listResp, isLoading } = useQuery({
    queryKey: ['scenarios', instanceId],
    queryFn: () => scenariosApi.list(instanceId),
    refetchInterval: Object.keys(runningJobs).length > 0 ? 3000 : false,
  })
  const scenarios: StrategyScenario[] = listResp?.data?.data ?? []

  // Compare
  const { data: compareResp, isLoading: compareLoading } = useQuery({
    queryKey: ['scenarios-compare', instanceId],
    queryFn: () => scenariosApi.compare(instanceId),
    enabled: view === 'compare',
  })
  const compareRows: ScenarioComparisonRow[] = compareResp?.data?.data ?? []

  // Delete
  const deleteMut = useMutation({
    mutationFn: (id: string) => scenariosApi.delete(instanceId, id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['scenarios', instanceId] }),
  })

  // Run all / selected
  const runMut = useMutation({
    mutationFn: (ids?: string[]) =>
      scenariosApi.run(instanceId, {
        internalSymbol:    runConfig.symbol,
        timeframe:         runConfig.timeframe,
        fromDate:          runConfig.fromDate,
        toDate:            runConfig.toDate,
        initialCapital:    runConfig.capital,
        scenarioIds:       ids,
      }),
    onSuccess: resp => {
      const jobs: Record<string, string> = {}
      const results = resp.data?.data ?? []
      results.forEach(r => { jobs[r.scenarioId] = r.jobId })
      setRunningJobs(jobs)
      setRunError(null)
      // Poll jobs until done
      pollJobs(jobs)
    },
    onError: (e: Error) => setRunError(e.message),
  })

  // Promote
  const promoteMut = useMutation({
    mutationFn: ({ id, target }: { id: string; target: string }) =>
      scenariosApi.promote(instanceId, id, target),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['scenarios', instanceId] })
      qc.invalidateQueries({ queryKey: ['scenarios-compare', instanceId] })
    },
  })

  function pollJobs(jobs: Record<string, string>) {
    const ids = Object.values(jobs)
    let pending = ids.length
    ids.forEach(jobId => {
      const interval = setInterval(async () => {
        try {
          const r = await backtestApi.status(jobId)
          const s = r.data?.data?.status
          if (s === 'Completed' || s === 'Failed' || s === 'Cancelled') {
            clearInterval(interval)
            pending--
            if (pending === 0) {
              setRunningJobs({})
              qc.invalidateQueries({ queryKey: ['scenarios', instanceId] })
              qc.invalidateQueries({ queryKey: ['scenarios-compare', instanceId] })
            }
          }
        } catch { clearInterval(interval) }
      }, 2500)
    })
  }

  const isRunning = (s: StrategyScenario) =>
    Object.keys(runningJobs).includes(s.id)

  const canRun = runConfig.symbol && runConfig.fromDate && runConfig.toDate

  return (
    <div style={{ padding: 0 }}>
      {/* Toolbar */}
      <div style={{
        display: 'flex', alignItems: 'center', gap: SP.md,
        padding: '10px 16px', borderBottom: `1px solid ${C.border}`,
        background: C.surface,
      }}>
        <span style={{ fontSize: 13, fontWeight: 600, color: C.text, flex: 1 }}>
          Scenarios — {instanceName}
        </span>
        {/* View toggle */}
        {['list', 'compare'].map(v => (
          <button key={v} onClick={() => setView(v as 'list' | 'compare')} style={{
            padding: '4px 12px', borderRadius: 5, fontSize: 12, cursor: 'pointer',
            background: view === v ? C.blue : C.surface2,
            color: view === v ? '#fff' : C.textSub,
            border: `1px solid ${view === v ? C.blue : C.border}`,
          }}>
            {v === 'list' ? 'Scenarios' : 'Compare'}
          </button>
        ))}
        <button onClick={() => { setEditingScenario(undefined); setDrawerOpen(true) }} style={{
          padding: '4px 14px', background: C.green, border: 'none',
          borderRadius: 5, color: '#000', fontSize: 12, fontWeight: 600, cursor: 'pointer',
        }}>
          + New Scenario
        </button>
      </div>

      {/* Run config bar */}
      <div style={{
        display: 'flex', gap: SP.md, padding: '8px 16px', flexWrap: 'wrap',
        borderBottom: `1px solid ${C.border}`, background: C.surface2, alignItems: 'center',
      }}>
        {[
          { label: 'Symbol', key: 'symbol', type: 'text', placeholder: 'e.g. NSE:RELIANCE' },
          { label: 'TF', key: 'timeframe', type: 'text', placeholder: '1d' },
          { label: 'From', key: 'fromDate', type: 'date', placeholder: '' },
          { label: 'To', key: 'toDate', type: 'date', placeholder: '' },
        ].map(f => (
          <label key={f.key} style={{ display: 'flex', alignItems: 'center', gap: 5 }}>
            <span style={{ fontSize: 11, color: C.textMuted }}>{f.label}</span>
            <input
              type={f.type}
              value={(runConfig as Record<string, string | number>)[f.key] as string}
              placeholder={f.placeholder}
              onChange={e => setRunConfig(c => ({ ...c, [f.key]: e.target.value }))}
              style={{
                padding: '4px 8px', background: C.surface, border: `1px solid ${C.border}`,
                borderRadius: 5, color: C.text, fontSize: 12, width: f.key === 'symbol' ? 160 : 110,
              }}
            />
          </label>
        ))}
        <label style={{ display: 'flex', alignItems: 'center', gap: 5 }}>
          <span style={{ fontSize: 11, color: C.textMuted }} title="Used when a scenario has no capital set">Default Capital</span>
          <input
            type="number"
            value={runConfig.capital}
            onChange={e => setRunConfig(c => ({ ...c, capital: Number(e.target.value) }))}
            style={{
              padding: '4px 8px', background: C.surface, border: `1px solid ${C.border}`,
              borderRadius: 5, color: C.text, fontSize: 12, width: 100,
            }}
          />
        </label>
        <button
          onClick={() => runMut.mutate(undefined)}
          disabled={!canRun || runMut.isPending || Object.keys(runningJobs).length > 0}
          style={{
            padding: '5px 16px', background: C.blue, border: 'none', borderRadius: 5,
            color: '#fff', fontSize: 12, fontWeight: 600,
            cursor: !canRun || runMut.isPending ? 'not-allowed' : 'pointer',
            opacity: !canRun || runMut.isPending ? 0.5 : 1,
          }}
        >
          {runMut.isPending || Object.keys(runningJobs).length > 0 ? 'Running…' : 'Run All'}
        </button>
        {runError && (
          <span style={{ fontSize: 11, color: C.red }}>{runError}</span>
        )}
      </div>

      {/* ── LIST VIEW ─────────────────────────────────────────────────── */}
      {view === 'list' && (
        <div>
          {isLoading && (
            <div style={{ padding: 20, color: C.textMuted, fontSize: 13 }}>Loading…</div>
          )}
          {!isLoading && scenarios.length === 0 && (
            <div style={{ padding: 24, textAlign: 'center', color: C.textMuted, fontSize: 13 }}>
              No scenarios yet. Create one to start parameter testing.
            </div>
          )}
          {scenarios.map(s => (
            <div key={s.id} style={{
              display: 'flex', alignItems: 'center', gap: SP.md,
              padding: TABLE_CELL, borderBottom: `1px solid ${C.border2}`,
              background: isRunning(s) ? C.blueBg : 'transparent',
              transition: 'background 0.2s',
            }}>
              {/* Status badge */}
              <span style={{
                fontSize: 11, fontWeight: 600, padding: '2px 8px', borderRadius: 4,
                color: STATUS_COLOR[s.status] ?? C.textMuted,
                background: STATUS_BG[s.status] ?? C.surface2,
                whiteSpace: 'nowrap',
              }}>
                {isRunning(s) ? 'Running…' : s.status}
              </span>

              {/* Name + description */}
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ fontSize: 13, fontWeight: 600, color: C.text }}>{s.name}</div>
                {s.description && (
                  <div style={{ fontSize: 11, color: C.textMuted, marginTop: 1 }}>
                    {s.description}
                  </div>
                )}
                {s.parametersJsonOverride && s.parametersJsonOverride !== '{}' && (
                  <div
                    title={`Effective params: ${s.effectiveParametersJson}`}
                    style={{
                      marginTop: 4, fontSize: 11, color: C.textSub,
                      fontFamily: 'monospace', background: C.surface2,
                      padding: '2px 6px', borderRadius: 4, display: 'inline-block',
                      maxWidth: '100%', overflow: 'hidden', textOverflow: 'ellipsis',
                      whiteSpace: 'nowrap', cursor: 'help',
                    }}
                  >
                    {s.parametersJsonOverride}
                  </div>
                )}
              </div>

              {/* Allocated capital badge */}
              {s.allocatedCapital != null && (
                <span style={{
                  fontSize: 11, color: C.textSub, whiteSpace: 'nowrap',
                  background: C.surface2, padding: '2px 7px', borderRadius: 4,
                }}>
                  {formatInr(s.allocatedCapital)}
                </span>
              )}

              {/* Backtest result link */}
              {s.lastBacktestRunId && (
                <span style={{ fontSize: 11, color: C.blue, whiteSpace: 'nowrap' }}>
                  has result
                </span>
              )}

              {/* Actions */}
              <div style={{ display: 'flex', gap: SP.sm, flexShrink: 0 }}>
                {/* Run single */}
                <button
                  onClick={() => runMut.mutate([s.id])}
                  disabled={!canRun || runMut.isPending || isRunning(s)}
                  title="Backtest this scenario"
                  style={{
                    padding: '3px 10px', background: C.blueBg,
                    border: `1px solid ${C.blue}`, borderRadius: 4,
                    color: C.blue, fontSize: 11, cursor: 'pointer',
                  }}
                >
                  Run
                </button>

                {/* Edit */}
                <button
                  onClick={() => { setEditingScenario(s); setDrawerOpen(true) }}
                  disabled={s.status === 'Live'}
                  style={{
                    padding: '3px 10px', background: C.surface2,
                    border: `1px solid ${C.border}`, borderRadius: 4,
                    color: C.textSub, fontSize: 11, cursor: 'pointer',
                  }}
                >
                  Edit
                </button>

                {/* Promote */}
                {s.status === 'Backtested' && (
                  <button
                    onClick={() => promoteMut.mutate({ id: s.id, target: 'ForwardTest' })}
                    style={{
                      padding: '3px 10px', background: C.amberBg,
                      border: `1px solid ${C.amber}`, borderRadius: 4,
                      color: C.amber, fontSize: 11, cursor: 'pointer',
                    }}
                  >
                    → Fwd Test
                  </button>
                )}
                {s.status === 'ForwardTest' && (
                  <button
                    onClick={() => promoteMut.mutate({ id: s.id, target: 'Live' })}
                    style={{
                      padding: '3px 10px', background: C.greenBg,
                      border: `1px solid ${C.green}`, borderRadius: 4,
                      color: C.green, fontSize: 11, cursor: 'pointer',
                    }}
                  >
                    → Live
                  </button>
                )}
                {s.status === 'Live' && (
                  <button
                    onClick={() => promoteMut.mutate({ id: s.id, target: 'Archived' })}
                    style={{
                      padding: '3px 10px', background: C.surface2,
                      border: `1px solid ${C.border}`, borderRadius: 4,
                      color: C.textMuted, fontSize: 11, cursor: 'pointer',
                    }}
                  >
                    Archive
                  </button>
                )}

                {/* Delete */}
                <button
                  onClick={() => {
                    if (confirm(`Delete scenario "${s.name}"?`)) deleteMut.mutate(s.id)
                  }}
                  disabled={s.status === 'Live'}
                  style={{
                    padding: '3px 10px', background: C.redBg,
                    border: `1px solid ${C.red}`, borderRadius: 4,
                    color: C.red, fontSize: 11, cursor: 'pointer',
                    opacity: s.status === 'Live' ? 0.4 : 1,
                  }}
                >
                  Delete
                </button>
              </div>
            </div>
          ))}
        </div>
      )}

      {/* ── COMPARE VIEW ──────────────────────────────────────────────── */}
      {view === 'compare' && (
        <div style={{ overflowX: 'auto' }}>
          {compareLoading && (
            <div style={{ padding: 20, color: C.textMuted, fontSize: 13 }}>Loading…</div>
          )}
          {!compareLoading && compareRows.length === 0 && (
            <div style={{ padding: 24, textAlign: 'center', color: C.textMuted, fontSize: 13 }}>
              No scenarios to compare.
            </div>
          )}
          {compareRows.length > 0 && (
            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
              <thead>
                <tr style={{ background: C.surface2 }}>
                  {['Scenario', 'Status', 'Capital', 'Params', 'Return', 'Drawdown', 'Sharpe', 'Win%', 'Trades', 'PF', 'Expectancy'].map(h => (
                    <th key={h} style={{
                      padding: TABLE_HEADER_CELL, textAlign: 'right',
                      color: C.textMuted, fontWeight: 600, whiteSpace: 'nowrap',
                      borderBottom: `1px solid ${C.border}`,
                      ...(h === 'Scenario' || h === 'Status' || h === 'Params' ? { textAlign: 'left' } : {}),
                    }}>
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {compareRows.map(r => (
                  <tr key={r.scenarioId} style={{ borderBottom: `1px solid ${C.border2}` }}>
                    <td style={{ padding: TABLE_CELL, color: C.text, fontWeight: 500 }}>
                      {r.scenarioName}
                    </td>
                    <td style={{ padding: TABLE_CELL }}>
                      <span style={{
                        fontSize: 11, fontWeight: 600, padding: '2px 7px', borderRadius: 4,
                        color: STATUS_COLOR[r.status] ?? C.textMuted,
                        background: STATUS_BG[r.status] ?? C.surface2,
                      }}>
                        {r.status}
                      </span>
                    </td>
                    <td style={{ padding: TABLE_CELL, textAlign: 'right', color: C.textSub, fontFamily: 'monospace' }}>
                      {r.allocatedCapital != null ? formatInr(r.allocatedCapital) : <span style={{ color: C.textMuted }}>—</span>}
                    </td>
                    <td style={{ padding: TABLE_CELL }}>
                      <span
                        title={r.effectiveParametersJson}
                        style={{
                          fontSize: 11, color: C.textSub, fontFamily: 'monospace',
                          background: C.surface2, padding: '1px 5px', borderRadius: 3,
                          display: 'inline-block', maxWidth: 200,
                          overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
                          cursor: 'help',
                        }}
                      >
                        {r.effectiveParametersJson}
                      </span>
                    </td>
                    <td style={{
                      padding: TABLE_CELL, textAlign: 'right',
                      color: r.totalReturn != null ? (r.totalReturn >= 0 ? C.green : C.red) : C.textMuted,
                      fontFamily: 'monospace',
                    }}>
                      {pct(r.totalReturn)}
                    </td>
                    <td style={{
                      padding: TABLE_CELL, textAlign: 'right',
                      color: r.maxDrawdown != null ? C.red : C.textMuted,
                      fontFamily: 'monospace',
                    }}>
                      {pct(r.maxDrawdown)}
                    </td>
                    <td style={{ padding: TABLE_CELL, textAlign: 'right', color: C.text, fontFamily: 'monospace' }}>
                      {fmt2(r.sharpeRatio)}
                    </td>
                    <td style={{ padding: TABLE_CELL, textAlign: 'right', color: C.text, fontFamily: 'monospace' }}>
                      {pct(r.winRate)}
                    </td>
                    <td style={{ padding: TABLE_CELL, textAlign: 'right', color: C.text, fontFamily: 'monospace' }}>
                      {fmtN(r.totalTrades)}
                    </td>
                    <td style={{ padding: TABLE_CELL, textAlign: 'right', color: C.text, fontFamily: 'monospace' }}>
                      {fmt2(r.profitFactor)}
                    </td>
                    <td style={{ padding: TABLE_CELL, textAlign: 'right', color: C.text, fontFamily: 'monospace' }}>
                      {fmt2(r.expectancyPerTrade)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}

      {/* Scenario editor drawer */}
      <ScenarioEditorDrawer
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        instanceId={instanceId}
        strategyType={strategyType}
        baseParametersJson={baseParametersJson}
        scenario={editingScenario}
        onSaved={() => {
          qc.invalidateQueries({ queryKey: ['scenarios', instanceId] })
          qc.invalidateQueries({ queryKey: ['scenarios-compare', instanceId] })
        }}
      />
    </div>
  )
}
