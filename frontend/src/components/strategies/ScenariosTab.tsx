import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { strategyDomainApi } from '../../api/client'
import {
  OverrideSection, ParamRange,
  Scenario, ScenarioStatus, Strategy, RunMetrics,
} from '../../types/strategy'
import { C, F, SP, TABLE_CELL } from '../../styles/tokens'
import { ScenarioDrawer } from './ScenarioDrawer'
import { RightDrawer } from '../ui/RightDrawer'
import { useEnums } from '../../context/EnumsContext'

interface Props {
  strategy: Strategy
}

// ── Status chip ───────────────────────────────────────────────────────────────

function StatusChip({ status, promotionNotes }: { status: ScenarioStatus; promotionNotes?: string }) {
  const color = statusColor(status)
  const italic = status === ScenarioStatus.FwdTesting || status === ScenarioStatus.LiveCandidate
  const chip = (
    <span
      title={promotionNotes ? `Notes: ${promotionNotes}` : undefined}
      style={{
        fontSize: 10, fontWeight: 700, color,
        fontStyle: italic ? 'italic' : 'normal',
        padding: '2px 6px', borderRadius: 3,
        border: `1px solid ${color}44`, background: `${color}11`,
        cursor: promotionNotes ? 'help' : 'default',
      }}
    >
      {status === ScenarioStatus.Running && (
        <span style={{ display: 'inline-block', animation: 'pulse 1s infinite', marginRight: 3 }}>●</span>
      )}
      {status}
    </span>
  )
  return chip
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
  const parts = scenario.parameterOverrides.slice(0, 2).map(o =>
    `${o.paramKey} ${String(o.baseValue)}→${String(o.overrideValue)}`
  )
  const summary = parts.join('; ')
  return summary.length > 60 ? summary.slice(0, 57) + '...' : summary
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

// ── Promotion checklist modal ─────────────────────────────────────────────────

function robustnessChip(passed: boolean | null): React.ReactNode {
  if (passed === null) return <span style={{ color: C.textMuted, fontSize: 11 }}>—</span>
  return passed
    ? <span style={{ color: C.green, fontSize: 12 }}>✓</span>
    : <span style={{ color: C.red, fontSize: 12 }}>✗</span>
}

function PromotionChecklistModal({ scenario, onConfirm, onCancel }: {
  scenario: Scenario
  onConfirm: (notes: string) => void
  onCancel: () => void
}) {
  const [notes, setNotes] = useState('')
  const [notesError, setNotesError] = useState('')
  const metrics: RunMetrics | undefined = scenario.lastMetrics

  const checks = [
    {
      label: 'Backtest covers ≥ 6 months',
      auto: true,
      passed: (() => {
        const from = new Date(scenario.backtestRange.from)
        const to = new Date(scenario.backtestRange.to)
        return (to.getTime() - from.getTime()) / (1000 * 60 * 60 * 24 * 30) >= 6
      })(),
      display: `${scenario.backtestRange.from.slice(0, 10)} – ${scenario.backtestRange.to.slice(0, 10)}`,
    },
    {
      label: 'Trade count ≥ 30',
      auto: true,
      passed: (metrics?.tradeCount ?? 0) >= 30,
      display: metrics ? `${metrics.tradeCount} trades` : '—',
    },
    {
      label: 'WFE ≥ 0.65',
      auto: true,
      passed: metrics?.walkForwardEfficiency !== undefined ? metrics.walkForwardEfficiency >= 0.65 : null,
      display: metrics?.walkForwardEfficiency !== undefined ? metrics.walkForwardEfficiency.toFixed(2) : '—',
    },
    {
      label: 'Overfit score ≤ 0.10',
      auto: true,
      passed: metrics?.overfitScore !== undefined ? metrics.overfitScore <= 0.10 : null,
      display: metrics?.overfitScore !== undefined ? metrics.overfitScore.toFixed(2) : '—',
    },
    {
      label: 'Max drawdown acceptable',
      auto: false,
      passed: null as boolean | null,
      display: metrics ? `-${metrics.maxDrawdownPct.toFixed(1)}%` : '—',
    },
    {
      label: 'Parameter stability reviewed',
      auto: false,
      passed: null as boolean | null,
      display: metrics?.parameterStabilityScore !== undefined ? metrics.parameterStabilityScore.toFixed(2) : '—',
    },
    {
      label: 'MAE/MFE analysis reviewed',
      auto: false,
      passed: null as boolean | null,
      display: '',
    },
  ]

  const [manualChecks, setManualChecks] = useState<boolean[]>(checks.map(c => c.auto && c.passed === true))

  function toggleManual(i: number) {
    setManualChecks(prev => { const next = [...prev]; next[i] = !next[i]; return next })
  }

  function handleConfirm() {
    if (notes.trim().length < 20) {
      setNotesError('Research notes must be at least 20 characters.')
      return
    }
    setNotesError('')
    onConfirm(notes.trim())
  }

  return (
    <div style={{
      position: 'fixed', inset: 0, zIndex: 1000,
      background: 'rgba(0,0,0,0.6)', display: 'flex', alignItems: 'center', justifyContent: 'center',
    }}>
      <div style={{
        background: C.surface, border: `1px solid ${C.border}`, borderRadius: 10,
        width: 520, maxHeight: '90vh', overflowY: 'auto', padding: 28,
      }}>
        <h2 style={{ margin: '0 0 16px', fontSize: 16, fontWeight: 700 }}>
          Promote to Forward Test — {scenario.name}
        </h2>

        <table style={{ width: '100%', borderCollapse: 'collapse', marginBottom: 20, fontSize: 12 }}>
          <tbody>
            {checks.map((c, i) => {
              const checked = c.auto ? c.passed === true : manualChecks[i]
              const displayPassed = c.auto ? c.passed : (manualChecks[i] ? true : null)
              return (
                <tr key={i} style={{ borderBottom: `1px solid ${C.border2}` }}>
                  <td style={{ padding: '7px 0', width: 28 }}>
                    {c.auto
                      ? robustnessChip(c.passed)
                      : (
                          <input
                            type="checkbox"
                            checked={manualChecks[i]}
                            onChange={() => toggleManual(i)}
                          />
                        )
                    }
                  </td>
                  <td style={{ padding: '7px 8px', color: checked ? C.text : C.textMuted }}>
                    {c.label}
                  </td>
                  <td style={{ padding: '7px 0', textAlign: 'right', fontFamily: F.mono, color: C.textSub, fontSize: 11 }}>
                    {c.display}
                  </td>
                  <td style={{ width: 20, paddingLeft: 8 }}>
                    {robustnessChip(displayPassed)}
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>

        <div style={{ marginBottom: 16 }}>
          <label style={{ fontSize: 11, color: C.textMuted, display: 'block', marginBottom: 4 }}>
            Research notes <span style={{ color: C.red }}>*</span> (min 20 chars)
          </label>
          <textarea
            value={notes}
            onChange={e => setNotes(e.target.value)}
            placeholder="Summarise why this scenario is ready for forward testing…"
            rows={4}
            style={{
              width: '100%', background: C.surface2, border: `1px solid ${notesError ? C.red : C.border}`,
              color: C.text, borderRadius: 6, padding: SP.sm, fontSize: 12,
              resize: 'vertical', boxSizing: 'border-box',
            }}
          />
          {notesError && (
            <div style={{ fontSize: 11, color: C.red, marginTop: 4 }}>{notesError}</div>
          )}
        </div>

        <div style={{ display: 'flex', gap: SP.sm, justifyContent: 'flex-end' }}>
          <button onClick={onCancel} style={cancelBtnStyle}>Cancel</button>
          <button onClick={handleConfirm} style={primaryBtnStyle}>
            Promote to Forward Test →
          </button>
        </div>
      </div>
    </div>
  )
}

// ── Parameter Sweep drawer ────────────────────────────────────────────────────

function ParameterSweepDrawer({ strategy, onClose }: { strategy: Strategy; onClose: () => void }) {
  const qc = useQueryClient()
  const { enums } = useEnums()
  const sectionOptions = enums.overrideSection ?? []

  const [hypothesis, setHypothesis] = useState('')
  const [tag, setTag] = useState('')
  const [indicatorId, setIndicatorId] = useState(strategy.indicators[0]?.id ?? '')
  const [paramKey, setParamKey] = useState('')
  const [section, setSection] = useState<OverrideSection>(OverrideSection.Indicator)
  const [from, setFrom] = useState(1)
  const [to, setTo] = useState(10)
  const [step, setStep] = useState(1)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [generating, setGenerating] = useState(false)

  const selectedIndicator = strategy.indicators.find(i => i.id === indicatorId)
  const numericParams = selectedIndicator
    ? Object.entries(selectedIndicator.baseParams).filter(([, v]) => typeof v === 'number')
    : []

  const steps = step > 0 && to >= from ? Math.floor((to - from) / step) + 1 : 0

  function validate(): boolean {
    const e: Record<string, string> = {}
    if (!hypothesis.trim()) e.hypothesis = 'Hypothesis is required'
    if (!paramKey) e.paramKey = 'Parameter is required'
    if (step <= 0) e.step = 'Step must be > 0'
    if (to < from) e.to = 'To must be ≥ From'
    // Validate against allowedParamRanges
    if (selectedIndicator && paramKey) {
      const range = selectedIndicator.allowedParamRanges[paramKey] as ParamRange | undefined
      if (range) {
        if (from < range.min || from > range.max) e.from = `Must be in [${range.min}–${range.max}]`
        if (to < range.min || to > range.max) e.to = `Must be in [${range.min}–${range.max}]`
      }
    }
    setErrors(e)
    return Object.keys(e).length === 0
  }

  async function handleGenerate() {
    if (!validate()) return
    setGenerating(true)
    try {
      await strategyDomainApi.createParameterSweep(strategy.id, {
        label: `${paramKey} sweep`,
        hypothesis: hypothesis.trim(),
        paramKey,
        indicatorId: indicatorId || undefined,
        section,
        from,
        to,
        step,
        otherOverrides: [],
      })
      qc.invalidateQueries({ queryKey: ['scenarios', strategy.id] })
      onClose()
    } finally {
      setGenerating(false)
    }
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: SP.md }}>
      <div style={{
        fontSize: 11, color: C.textMuted, padding: SP.sm,
        background: C.surface2, borderRadius: 4,
      }}>
        Generates N scenarios with a single parameter varied across a range.
        All scenarios share a sweepGroupId and are validated against allowedParamRanges.
      </div>

      <Field label="Hypothesis *" error={errors.hypothesis}>
        <textarea
          value={hypothesis}
          onChange={e => setHypothesis(e.target.value)}
          placeholder="What do you expect to learn from this sweep?"
          rows={3}
          style={{ ...inputStyle, resize: 'vertical' }}
        />
      </Field>

      <Field label="Tag">
        <input value={tag} onChange={e => setTag(e.target.value)} placeholder="e.g. ema-period-sensitivity" style={inputStyle} />
      </Field>

      <Field label="Section">
        <select
          value={section}
          onChange={e => setSection(e.target.value as OverrideSection)}
          style={inputStyle}
        >
          {sectionOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
        </select>
      </Field>

      <Field label="Indicator">
        <select
          value={indicatorId}
          onChange={e => { setIndicatorId(e.target.value); setParamKey('') }}
          style={inputStyle}
        >
          <option value="">— no indicator —</option>
          {strategy.indicators.map(i => (
            <option key={i.id} value={i.id}>{i.type} ({i.timeframe})</option>
          ))}
        </select>
      </Field>

      <Field label="Parameter *" error={errors.paramKey}>
        <select
          value={paramKey}
          onChange={e => setParamKey(e.target.value)}
          style={inputStyle}
          disabled={numericParams.length === 0}
        >
          <option value="">— pick —</option>
          {numericParams.map(([k]) => <option key={k} value={k}>{k}</option>)}
        </select>
        {numericParams.length === 0 && indicatorId && (
          <div style={{ fontSize: 10, color: C.textMuted, marginTop: 2 }}>
            No numeric parameters on this indicator.
          </div>
        )}
      </Field>

      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: SP.sm }}>
        <Field label="From" error={errors.from}>
          <input type="number" value={from} onChange={e => setFrom(Number(e.target.value))} style={inputStyle} />
        </Field>
        <Field label="To" error={errors.to}>
          <input type="number" value={to} onChange={e => setTo(Number(e.target.value))} style={inputStyle} />
        </Field>
        <Field label="Step" error={errors.step}>
          <input type="number" min={0.01} step={0.01} value={step} onChange={e => setStep(Number(e.target.value))} style={inputStyle} />
        </Field>
      </div>

      {steps > 0 && (
        <div style={{
          padding: SP.sm, background: C.blueBg, border: `1px solid ${C.blue}44`,
          borderRadius: 4, fontSize: 12, color: C.blue,
        }}>
          Will generate <strong>{steps}</strong> scenario{steps !== 1 ? 's' : ''}
        </div>
      )}

      <div style={{ display: 'flex', gap: SP.sm, paddingTop: SP.sm, borderTop: `1px solid ${C.border}` }}>
        <button onClick={onClose} style={cancelBtnStyle}>Cancel</button>
        <button
          onClick={handleGenerate}
          disabled={generating || steps === 0}
          style={{ ...primaryBtnStyle, flex: 1, opacity: (generating || steps === 0) ? 0.6 : 1 }}
        >
          {generating ? '…' : `Generate ${steps > 0 ? steps : ''} Scenarios`}
        </button>
      </div>
    </div>
  )
}

// ── Main ScenariosTab ─────────────────────────────────────────────────────────

export function ScenariosTab({ strategy }: Props) {
  const qc = useQueryClient()
  const [drawerOpen, setDrawerOpen] = useState(false)
  const [sweepDrawerOpen, setSweepDrawerOpen] = useState(false)
  const [editingId, setEditingId] = useState<string | undefined>()
  const [promotingScenario, setPromotingScenario] = useState<Scenario | null>(null)
  const [collapsedGroups, setCollapsedGroups] = useState<Set<string>>(new Set())

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

  function toggleGroup(groupId: string) {
    setCollapsedGroups(prev => {
      const next = new Set(prev)
      if (next.has(groupId)) next.delete(groupId)
      else next.add(groupId)
      return next
    })
  }

  function handlePromote(s: Scenario, notes: string) {
    qc.setQueryData(['scenarios', strategy.id], (old: Scenario[] | undefined) =>
      old?.map(x => x.id === s.id
        ? { ...x, status: ScenarioStatus.FwdTesting, promotionNotes: notes }
        : x
      ) ?? []
    )
    setPromotingScenario(null)
  }

  // Group scenarios by sweepGroupId; ungrouped scenarios appear individually
  const sweepGroups = new Map<string, Scenario[]>()
  const standalone: Scenario[] = []
  for (const s of scenarios) {
    if (s.sweepGroupId) {
      const g = sweepGroups.get(s.sweepGroupId) ?? []
      g.push(s)
      sweepGroups.set(s.sweepGroupId, g)
    } else {
      standalone.push(s)
    }
  }

  const COLS = ['Name / Hypothesis', 'Capital', 'Range', 'Overrides', 'Return', 'DD', 'PF', 'Status', 'Actions']

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'flex-end', gap: SP.sm, marginBottom: SP.md }}>
        <button onClick={() => setSweepDrawerOpen(true)} style={secondaryBtnStyle}>+ Parameter Sweep</button>
        <button onClick={openCreate} style={primaryBtnStyle}>+ New Scenario</button>
      </div>

      {isLoading && <SkeletonTable cols={COLS.length} rows={3} />}
      {!isLoading && error && <ErrorMessage text="Failed to load scenarios." onRetry={refetch} />}
      {!isLoading && !error && scenarios.length === 0 && (
        <EmptyState message="No scenarios yet." action="Create your first scenario to start backtesting." onAction={openCreate} />
      )}

      {!isLoading && !error && scenarios.length > 0 && (
        <div style={{ overflowX: 'auto' }}>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
            <thead>
              <tr style={{ background: C.surface2 }}>
                {COLS.map(h => <th key={h} style={thStyle}>{h}</th>)}
              </tr>
            </thead>
            <tbody>
              {/* Standalone scenarios */}
              {standalone.map(s => (
                <ScenarioRow
                  key={s.id}
                  scenario={s}
                  onEdit={() => openEdit(s.id)}
                  onRun={() => runMut.mutate(s.id)}
                  onDelete={() => deleteMut.mutate(s.id)}
                  onPromote={() => setPromotingScenario(s)}
                  loading={runMut.isPending || deleteMut.isPending}
                />
              ))}

              {/* Sweep groups */}
              {Array.from(sweepGroups.entries()).map(([groupId, members]) => {
                const collapsed = collapsedGroups.has(groupId)
                const firstHypothesis = members[0]?.hypothesis ?? groupId
                const label = firstHypothesis.length > 40 ? firstHypothesis.slice(0, 37) + '…' : firstHypothesis
                return (
                  <React.Fragment key={groupId}>
                    {/* Group header row */}
                    <tr
                      style={{ background: `${C.blue}11`, cursor: 'pointer', borderBottom: `1px solid ${C.border}` }}
                      onClick={() => toggleGroup(groupId)}
                    >
                      <td colSpan={COLS.length} style={{ padding: '6px 10px' }}>
                        <span style={{ fontSize: 12, fontWeight: 600, color: C.blue }}>
                          {collapsed ? '▶' : '▼'}{' '}
                          {label}
                          <span style={{
                            marginLeft: 8, fontSize: 10, color: C.textMuted,
                            fontWeight: 400,
                          }}>
                            ({members.length} variant{members.length !== 1 ? 's' : ''})
                          </span>
                        </span>
                      </td>
                    </tr>
                    {!collapsed && members.map(s => (
                      <ScenarioRow
                        key={s.id}
                        scenario={s}
                        onEdit={() => openEdit(s.id)}
                        onRun={() => runMut.mutate(s.id)}
                        onDelete={() => deleteMut.mutate(s.id)}
                        onPromote={() => setPromotingScenario(s)}
                        loading={runMut.isPending || deleteMut.isPending}
                        indented
                      />
                    ))}
                  </React.Fragment>
                )
              })}
            </tbody>
          </table>
        </div>
      )}

      {/* Scenario edit/create drawer */}
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

      {/* Parameter Sweep drawer */}
      <RightDrawer
        isOpen={sweepDrawerOpen}
        title="Parameter Sweep"
        onClose={() => setSweepDrawerOpen(false)}
      >
        <ParameterSweepDrawer
          strategy={strategy}
          onClose={() => setSweepDrawerOpen(false)}
        />
      </RightDrawer>

      {/* Promotion checklist modal */}
      {promotingScenario && (
        <PromotionChecklistModal
          scenario={promotingScenario}
          onConfirm={(notes) => handlePromote(promotingScenario, notes)}
          onCancel={() => setPromotingScenario(null)}
        />
      )}
    </div>
  )
}

// ── Scenario row ──────────────────────────────────────────────────────────────

function ScenarioRow({ scenario: s, onEdit, onRun, onDelete, onPromote, loading, indented }: {
  scenario: Scenario
  onEdit: () => void
  onRun: () => void
  onDelete: () => void
  onPromote: () => void
  loading: boolean
  indented?: boolean
}) {
  return (
    <tr style={{ borderBottom: `1px solid ${C.border2}` }}>
      <td style={{ ...tdStyle, paddingLeft: indented ? 24 : undefined }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
          {s.isBaseline && (
            <span style={{
              fontSize: 9, fontWeight: 700, color: C.amber,
              padding: '1px 5px', borderRadius: 2,
              border: `1px solid ${C.amber}44`, background: `${C.amber}11`,
            }}>BASE</span>
          )}
          <span style={{ fontWeight: 600 }}>{s.name}</span>
        </div>
        {s.hypothesis && (
          <div
            title={s.hypothesis}
            style={{ fontSize: 10, color: C.textMuted, marginTop: 2, maxWidth: 200, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
          >
            {s.hypothesis.length > 60 ? s.hypothesis.slice(0, 57) + '…' : s.hypothesis}
          </div>
        )}
        {s.hypothesisTag && (
          <span style={{
            fontSize: 9, color: C.blue, padding: '1px 4px', borderRadius: 2,
            border: `1px solid ${C.blue}44`, background: `${C.blue}11`, marginTop: 2, display: 'inline-block',
          }}>
            {s.hypothesisTag}
          </span>
        )}
      </td>
      <td style={{ ...tdStyle, fontFamily: F.mono, textAlign: 'right' }}>
        ₹{(s.capital / 1000).toFixed(0)}K
      </td>
      <td style={{ ...tdStyle, color: C.textMuted }}>
        {s.backtestRange.from.slice(0, 10)} – {s.backtestRange.to.slice(0, 10)}
      </td>
      <td style={{ ...tdStyle, color: C.textSub, maxWidth: 180 }} title={
        s.parameterOverrides.map(o => `${o.paramKey}: ${o.baseValue}→${o.overrideValue}`).join(', ')
      }>
        {overridesSummary(s)}
      </td>
      <MetricCell value={s.lastMetrics?.returnPct} positive={(s.lastMetrics?.returnPct ?? 0) >= 0} />
      <MetricCell value={s.lastMetrics?.maxDrawdownPct} positive={false} />
      <MetricCell value={s.lastMetrics?.profitFactor} />
      <td style={tdStyle}>
        <StatusChip status={s.status} promotionNotes={s.promotionNotes} />
      </td>
      <td style={tdStyle}>
        <div style={{ display: 'flex', gap: 4, flexWrap: 'wrap' }}>
          <ActionBtn label="Run" onClick={onRun} loading={loading} />
          <ActionBtn label="Edit" onClick={onEdit} />
          <ActionBtn label="→ Fwd" onClick={onPromote} />
          <ActionBtn label="Delete" danger onClick={onDelete} loading={loading} />
        </div>
      </td>
    </tr>
  )
}

// ── Helpers ───────────────────────────────────────────────────────────────────

import React from 'react'

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

function Field({ label, error, children }: { label: string; error?: string; children: React.ReactNode }) {
  return (
    <div>
      <label style={{ fontSize: 11, color: C.textMuted, display: 'block', marginBottom: 3 }}>{label}</label>
      {children}
      {error && <div style={{ fontSize: 10, color: C.red, marginTop: 2 }}>{error}</div>}
    </div>
  )
}

// ── Styles ────────────────────────────────────────────────────────────────────

const thStyle: React.CSSProperties = {
  padding: TABLE_CELL, textAlign: 'left', fontSize: 11,
  color: C.textMuted, fontWeight: 600, borderBottom: `1px solid ${C.border}`,
}

const tdStyle: React.CSSProperties = { padding: TABLE_CELL, fontSize: 12, color: C.text }

const primaryBtnStyle: React.CSSProperties = {
  background: '#1e3a5f', color: '#93c5fd', border: '1px solid #2563eb44',
  borderRadius: 5, padding: '6px 14px', cursor: 'pointer', fontSize: 12, fontWeight: 700,
}

const secondaryBtnStyle: React.CSSProperties = {
  background: C.surface2, color: C.textSub, border: `1px solid ${C.border}`,
  borderRadius: 5, padding: '6px 14px', cursor: 'pointer', fontSize: 12,
}

const cancelBtnStyle: React.CSSProperties = {
  background: 'none', color: C.textMuted, border: `1px solid ${C.border}`,
  borderRadius: 5, padding: '7px 14px', cursor: 'pointer', fontSize: 12,
}

const inputStyle: React.CSSProperties = {
  width: '100%', background: C.surface3, border: `1px solid ${C.border}`,
  color: C.text, borderRadius: 4, padding: '6px 8px', fontSize: 12,
  boxSizing: 'border-box',
}
