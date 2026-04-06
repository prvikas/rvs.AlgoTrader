import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { strategyDomainApi } from '../../api/client'
import { Scenario, ScenarioStatus, Strategy } from '../../types/strategy'
import { C, F, SP, TABLE_CELL } from '../../styles/tokens'
import { ScenarioDrawer } from './ScenarioDrawer'
import { RightDrawer } from '../ui/RightDrawer'

interface Props {
  strategy: Strategy
}

function StatusChip({ status }: { status: ScenarioStatus }) {
  const color = statusColor(status)
  const italic = status === ScenarioStatus.FwdTesting || status === ScenarioStatus.LiveCandidate
  return (
    <span style={{
      fontSize: 10, fontWeight: 700, color,
      fontStyle: italic ? 'italic' : 'normal',
      padding: '2px 6px', borderRadius: 3,
      border: `1px solid ${color}44`,
      background: `${color}11`,
    }}>
      {status === ScenarioStatus.Running && (
        <span style={{ display: 'inline-block', animation: 'pulse 1s infinite', marginRight: 3 }}>●</span>
      )}
      {status}
    </span>
  )
}

function statusColor(s: ScenarioStatus): string {
  switch (s) {
    case ScenarioStatus.Draft:         return C.textMuted
    case ScenarioStatus.Running:       return C.amber
    case ScenarioStatus.Backtested:    return C.blue
    case ScenarioStatus.FwdTesting:    return C.blue
    case ScenarioStatus.LiveCandidate: return C.green
    case ScenarioStatus.Live:          return C.green
    case ScenarioStatus.Archived:      return C.textDim
    default:                           return C.textMuted
  }
}

function overridesSummary(scenario: Scenario): string {
  if (scenario.parameterOverrides.length === 0) return 'No overrides'
  const parts = scenario.parameterOverrides.slice(0, 3).map(o =>
    `${o.paramKey} ${String(o.baseValue)}→${String(o.overrideValue)}`
  )
  const summary = parts.join('; ')
  return summary.length > 80 ? summary.slice(0, 77) + '...' : summary
}

function MetricCell({ value, positive }: { value: number | undefined; positive?: boolean }) {
  if (value === undefined) return <td style={{ ...tdStyle, color: C.textMuted }}>—</td>
  const color = positive !== undefined ? (positive ? C.green : C.red) : C.text
  return (
    <td style={{ ...tdStyle, fontFamily: F.mono, color, textAlign: 'right' }}>
      {value.toFixed(2)}
    </td>
  )
}

export function ScenariosTab({ strategy }: Props) {
  const qc = useQueryClient()
  const [drawerOpen, setDrawerOpen] = useState(false)
  const [editingId, setEditingId] = useState<string | undefined>()

  const { data: scenarios = [], isLoading, error, refetch } = useQuery({
    queryKey: ['scenarios', strategy.id],
    queryFn: () => strategyDomainApi.listScenarios(strategy.id),
  })

  const deleteMut = useMutation({
    mutationFn: (sid: string) => strategyDomainApi.deleteScenario(strategy.id, sid),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['scenarios', strategy.id] }),
  })

  const runMut = useMutation({
    mutationFn: (sid: string) => strategyDomainApi.runScenario(strategy.id, sid),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['scenarios', strategy.id] }),
  })

  function openCreate() { setEditingId(undefined); setDrawerOpen(true) }
  function openEdit(id: string) { setEditingId(id); setDrawerOpen(true) }
  function closeDrawer() { setDrawerOpen(false); setEditingId(undefined) }

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: SP.md }}>
        <button onClick={openCreate} style={primaryBtnStyle}>+ New Scenario</button>
      </div>

      {isLoading && <SkeletonTable cols={9} rows={3} />}

      {!isLoading && error && (
        <ErrorMessage text="Failed to load scenarios." onRetry={refetch} />
      )}

      {!isLoading && !error && scenarios.length === 0 && (
        <EmptyState message="No scenarios yet." action="Create your first scenario to start backtesting." onAction={openCreate} />
      )}

      {!isLoading && !error && scenarios.length > 0 && (
        <div style={{ overflowX: 'auto' }}>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
            <thead>
              <tr style={{ background: C.surface2 }}>
                {['Name', 'Capital', 'Backtest Range', 'Overrides', 'Return', 'DD', 'PF', 'Status', 'Actions'].map(h => (
                  <th key={h} style={thStyle}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {scenarios.map(s => (
                <tr key={s.id} style={{ borderBottom: `1px solid ${C.border2}` }}>
                  <td style={tdStyle}>{s.name}</td>
                  <td style={{ ...tdStyle, fontFamily: F.mono, textAlign: 'right' }}>
                    ₹{(s.capital / 1000).toFixed(0)}K
                  </td>
                  <td style={{ ...tdStyle, color: C.textMuted }}>
                    {s.backtestRange.from.slice(0, 10)} – {s.backtestRange.to.slice(0, 10)}
                  </td>
                  <td style={{ ...tdStyle, color: C.textSub, maxWidth: 200 }} title={
                    s.parameterOverrides.map(o => `${o.paramKey}: ${o.baseValue}→${o.overrideValue}`).join(', ')
                  }>
                    {overridesSummary(s)}
                  </td>
                  <MetricCell value={s.lastMetrics?.returnPct} positive={(s.lastMetrics?.returnPct ?? 0) >= 0} />
                  <MetricCell value={s.lastMetrics?.maxDrawdownPct} positive={false} />
                  <MetricCell value={s.lastMetrics?.profitFactor} />
                  <td style={tdStyle}><StatusChip status={s.status} /></td>
                  <td style={tdStyle}>
                    <div style={{ display: 'flex', gap: 6 }}>
                      <ActionBtn label="Run" onClick={() => runMut.mutate(s.id)} loading={runMut.isPending} />
                      <ActionBtn label="Edit" onClick={() => openEdit(s.id)} />
                      <ActionBtn label="Delete" danger onClick={() => deleteMut.mutate(s.id)} loading={deleteMut.isPending} />
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <RightDrawer
        isOpen={drawerOpen}
        title={editingId ? 'Edit Scenario' : 'New Scenario'}
        onClose={closeDrawer}
      >
        <ScenarioDrawer
          strategy={strategy}
          scenarioId={editingId}
          onClose={closeDrawer}
        />
      </RightDrawer>
    </div>
  )
}

function ActionBtn({ label, onClick, loading, danger }: {
  label: string; onClick: () => void; loading?: boolean; danger?: boolean
}) {
  return (
    <button
      onClick={onClick}
      disabled={loading}
      style={{
        background: danger ? C.redBg : C.surface2,
        color: danger ? C.red : C.textSub,
        border: `1px solid ${danger ? C.red + '44' : C.border}`,
        borderRadius: 4, padding: '3px 8px', cursor: loading ? 'not-allowed' : 'pointer',
        fontSize: 11, opacity: loading ? 0.6 : 1,
      }}
    >
      {loading ? '…' : label}
    </button>
  )
}

function SkeletonTable({ cols, rows }: { cols: number; rows: number }) {
  return (
    <table style={{ width: '100%', borderCollapse: 'collapse' }}>
      <tbody>
        {Array.from({ length: rows }).map((_, r) => (
          <tr key={r} style={{ borderBottom: `1px solid ${C.border2}` }}>
            {Array.from({ length: cols }).map((__, c) => (
              <td key={c} style={{ padding: TABLE_CELL }}>
                <div style={{ height: 14, background: C.surface2, borderRadius: 3, opacity: 0.5 }} />
              </td>
            ))}
          </tr>
        ))}
      </tbody>
    </table>
  )
}

function ErrorMessage({ text, onRetry }: { text: string; onRetry: () => void }) {
  return (
    <div style={{ padding: SP.lg, color: C.red, fontSize: 12 }}>
      {text}{' '}
      <button onClick={onRetry} style={{ color: C.blue, background: 'none', border: 'none', cursor: 'pointer', fontSize: 12 }}>
        Retry?
      </button>
    </div>
  )
}

function EmptyState({ message, action, onAction }: { message: string; action: string; onAction: () => void }) {
  return (
    <div style={{ textAlign: 'center', padding: 40, color: C.textMuted }}>
      <div style={{ marginBottom: SP.sm }}>{message}</div>
      <div style={{ fontSize: 11, marginBottom: SP.md, color: C.textDim }}>{action}</div>
      <button onClick={onAction} style={primaryBtnStyle}>+ New Scenario</button>
    </div>
  )
}

const thStyle: React.CSSProperties = {
  padding: TABLE_CELL, textAlign: 'left', fontSize: 11,
  color: C.textMuted, fontWeight: 600, borderBottom: `1px solid ${C.border}`,
}

const tdStyle: React.CSSProperties = {
  padding: TABLE_CELL, fontSize: 12, color: C.text,
}

const primaryBtnStyle: React.CSSProperties = {
  background: '#1e3a5f', color: '#93c5fd', border: '1px solid #2563eb44',
  borderRadius: 5, padding: '6px 14px', cursor: 'pointer', fontSize: 12, fontWeight: 700,
}
