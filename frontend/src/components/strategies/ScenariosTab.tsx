import { useState, useEffect, useRef } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { strategyDomainApi, apiClient, backtestApi, BacktestJobStatus, strategiesApi, forwardTestApi } from '../../api/client'
import {
  OverrideSection, ParamRange,
  Scenario, ScenarioStatus, Strategy, RunMetrics,
} from '../../types/strategy'
import { C, F, SP, TABLE_CELL } from '../../styles/tokens'
import { ScenarioDrawer } from './ScenarioDrawer'
import { RightDrawer } from '../ui/RightDrawer'
import { useEnums } from '../../context/EnumsContext'
import { useBacktestSignalR } from '../../hooks/useBacktestSignalR'
import { BacktestResultViewer } from './BacktestResultViewer'

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

function MetricCell({ value, positive, fmt = 'num' }: {
  value: number | undefined
  positive?: boolean
  fmt?: 'pct' | 'num'
}) {
  if (value === undefined || value === null) return <td style={{ ...tdStyle, color: C.textMuted }}>—</td>
  const color = positive !== undefined ? (positive ? C.green : C.red) : C.text
  const display = fmt === 'pct'
    ? `${value >= 0 ? '+' : ''}${value.toFixed(1)}%`
    : value.toFixed(2)
  return (
    <td style={{ ...tdStyle, fontFamily: F.mono, color, textAlign: 'right' }}>
      {display}
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
          padding: SP.sm, background: C.blueBg, border: `1px solid ${C.blue44}`,
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

  // ── Visualization viewer ──────────────────────────────────────────────────
  // When set, replaces the scenario table with BacktestResultViewer
  const [viewingScenarioId, setViewingScenarioId] = useState<string | null>(null)
  const autoOpenedRunning = useRef(false)

  // ── Real-time job tracking ─────────────────────────────────────────────────
  const [activeJob, setActiveJob] = useState<{ scenarioId: string; jobId: string } | null>(null)
  const [jobProgress, setJobProgress] = useState<BacktestJobStatus | null>(null)
  // Keeps the last Failed/Cancelled status per scenarioId so the error stays
  // visible after activeJob is cleared. Wiped when a new job starts for that scenario.
  const [lastFailedProgress, setLastFailedProgress] = useState<Record<string, BacktestJobStatus>>({})

  // Save metrics to DB and reset state when a job finishes
  async function handleJobCompleted(status: BacktestJobStatus) {
    if (!activeJob) return
    const { scenarioId } = activeJob
    try {
      if (status.status === 'Completed' && status.result) {
        const metrics: RunMetrics = {
          returnPct:      (status.result.totalReturn  ?? 0) * 100,
          maxDrawdownPct: (status.result.maxDrawdown  ?? 0) * 100,
          sharpe:          status.result.sharpeRatio  ?? 0,
          winRate:        (status.result.winRate      ?? 0) * 100,
          profitFactor:    status.result.profitFactor ?? 0,
          tradeCount:      status.result.totalTrades  ?? 0,
          avgRPerTrade:    0,
          expectancy:      status.result.expectancyPerTrade ?? 0,
          avgRealisedRR:   0,
        }
        await apiClient.patch(
          `/strategy-definitions/${strategy.id}/scenarios/${scenarioId}`,
          { status: 'Backtested', lastMetricsJson: JSON.stringify(metrics), lastRunAt: new Date().toISOString() }
        )
      } else {
        // Failed or cancelled — reset to Draft and preserve error for display
        setLastFailedProgress(prev => ({ ...prev, [scenarioId]: status }))
        await apiClient.patch(
          `/strategy-definitions/${strategy.id}/scenarios/${scenarioId}`,
          { status: 'Draft' }
        )
      }
    } catch { /* ignore patch errors — query invalidation will still refresh UI */ }
    setActiveJob(null)
    setJobProgress(null)
    qc.invalidateQueries({ queryKey: ['scenarios', strategy.id] })
  }

  // ── SignalR real-time stream ───────────────────────────────────────────────
  const { isConnected: signalRConnected, liveChartBars } = useBacktestSignalR({
    jobId: activeJob?.jobId ?? null,
    onProgress: (status) => setJobProgress(status),
    onCompleted: (status) => { void handleJobCompleted(status) },
  })

  // ── HTTP polling — always runs while a job is active ─────────────────────
  // SignalR gives faster per-bar updates, but we can't rely on it exclusively:
  // the job may complete before the subscription is registered (race on fast datasets).
  // Both SignalR and polling update the same `jobProgress` state — last write wins,
  // which is harmless since they carry the same fields.
  useEffect(() => {
    if (!activeJob) return
    const { jobId } = activeJob
    const timer = setInterval(async () => {
      try {
        const resp = await backtestApi.status(jobId)
        const status = resp.data?.data
        if (!status) return
        setJobProgress(status)
        const done = status.status === 'Completed' || status.status === 'Failed' || status.status === 'Cancelled'
        if (done) {
          clearInterval(timer)
          void handleJobCompleted(status)
        }
      } catch { /* network error — keep polling */ }
    }, 1500)
    return () => clearInterval(timer)
  }, [activeJob?.jobId]) // eslint-disable-line react-hooks/exhaustive-deps

  const { data: scenarios = [], isLoading, error, refetch } = useQuery({
    queryKey: ['scenarios', strategy.id],
    queryFn: () => strategyDomainApi.listScenarios(strategy.id),
    // Refresh every 5 s while any scenario shows Running — catches backend-side
    // updates (e.g. status flipped to Backtested by BacktestJobManager) even if
    // the frontend's SignalR + polling somehow missed the completion event.
    refetchInterval: (query) => {
      const list = query.state.data as Scenario[] | undefined
      return list?.some(s => s.status === ScenarioStatus.Running) ? 5000 : false
    },
  })

  // When navigating back to the page after it fully unmounted, auto-open the viewer
  // for a Running scenario so the user doesn't have to manually click "View Backtest".
  // Guard with a ref so we only auto-open once per component lifetime.
  useEffect(() => {
    if (autoOpenedRunning.current || !scenarios.length) return
    const running = scenarios.find(s => s.status === ScenarioStatus.Running)
    if (running && !viewingScenarioId && !activeJob) {
      autoOpenedRunning.current = true
      setViewingScenarioId(running.id)
    }
  }, [scenarios]) // eslint-disable-line react-hooks/exhaustive-deps

  const deleteMut = useMutation({
    mutationFn: (sid: string) => strategyDomainApi.deleteScenario(strategy.id, sid),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['scenarios', strategy.id] }),
  })

  // Stop a running Forward Test or Live instance tied to a scenario
  const stopInstanceMut = useMutation({
    mutationFn: (instanceId: string) => strategiesApi.stop(instanceId, 'Stopped from Scenarios UI'),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['scenarios', strategy.id] }),
  })

  // Promote a Forward Test instance to Live (opens modal for broker + capital)
  const [deployingScenario, setDeployingScenario] = useState<Scenario | null>(null)
  const deployMut = useMutation({
    mutationFn: ({ instanceId, broker, capital }: { instanceId: string; broker: string; capital: number }) =>
      forwardTestApi.promoteToLive(instanceId, { brokerName: broker, allocatedCapital: capital }),
    onSuccess: () => {
      setDeployingScenario(null)
      qc.invalidateQueries({ queryKey: ['scenarios', strategy.id] })
    },
  })

  const runMut = useMutation({
    mutationFn: (sid: string) => strategyDomainApi.runScenario(strategy.id, sid),
    onSuccess: (result, sid) => {
      qc.invalidateQueries({ queryKey: ['scenarios', strategy.id] })
      if (result.jobId) {
        setActiveJob({ scenarioId: sid, jobId: result.jobId })
        setJobProgress(null)
        setLastFailedProgress(prev => { const { [sid]: _, ...rest } = prev; return rest })
        // Auto-open the visualization viewer so user can see live progress + chart
        setViewingScenarioId(sid)
      }
    },
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

  const COLS = ['Scenario', 'Capital', 'Range', 'Overrides', 'Return', 'DD', 'PF', 'Status', 'Actions']

  // ── Viewer mode: full-width BacktestResultViewer instead of the scenario table ──
  const viewingScenario = viewingScenarioId
    ? scenarios.find(s => s.id === viewingScenarioId) ?? null
    : null

  if (viewingScenario) {
    return (
      <>
        <BacktestResultViewer
          strategy={strategy}
          scenario={viewingScenario}
          liveChartBars={liveChartBars}
          jobProgress={activeJob?.scenarioId === viewingScenario.id ? jobProgress : (lastFailedProgress[viewingScenario.id] ?? null)}
          activeJobId={activeJob?.scenarioId === viewingScenario.id ? activeJob.jobId : null}
          signalRConnected={signalRConnected}
          onBack={() => setViewingScenarioId(null)}
        />
        {/* Keep drawers accessible even while viewing */}
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
      </>
    )
  }

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
                  strategy={strategy}
                  onEdit={() => openEdit(s.id)}
                  onRun={() => runMut.mutate(s.id)}
                  onView={() => setViewingScenarioId(s.id)}
                  onDelete={() => deleteMut.mutate(s.id)}
                  onPromote={() => setPromotingScenario(s)}
                  onStop={() => s.activeInstanceId && stopInstanceMut.mutate(s.activeInstanceId)}
                  onDeployLive={() => setDeployingScenario(s)}
                  onDismissError={() => setLastFailedProgress(prev => { const { [s.id]: _, ...rest } = prev; return rest })}
                  loading={runMut.isPending || deleteMut.isPending || stopInstanceMut.isPending}
                  colCount={COLS.length}
                  jobProgress={activeJob?.scenarioId === s.id ? jobProgress : (lastFailedProgress[s.id] ?? null)}
                  signalRConnected={signalRConnected}
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
                      style={{ background: C.blue11, cursor: 'pointer', borderBottom: `1px solid ${C.border}` }}
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
                        strategy={strategy}
                        onEdit={() => openEdit(s.id)}
                        onRun={() => runMut.mutate(s.id)}
                        onView={() => setViewingScenarioId(s.id)}
                        onDelete={() => deleteMut.mutate(s.id)}
                        onPromote={() => setPromotingScenario(s)}
                        onStop={() => s.activeInstanceId && stopInstanceMut.mutate(s.activeInstanceId)}
                        onDeployLive={() => setDeployingScenario(s)}
                        onDismissError={() => setLastFailedProgress(prev => { const { [s.id]: _, ...rest } = prev; return rest })}
                        loading={runMut.isPending || deleteMut.isPending || stopInstanceMut.isPending}
                        indented
                        colCount={COLS.length}
                        jobProgress={activeJob?.scenarioId === s.id ? jobProgress : (lastFailedProgress[s.id] ?? null)}
                        signalRConnected={signalRConnected}
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

      {/* Deploy Live modal — runs pre-flight checks, then creates a Live instance */}
      {deployingScenario && (
        <DeployLiveModal
          scenario={deployingScenario}
          onConfirm={(broker, capital) => {
            if (!deployingScenario.activeInstanceId) return
            deployMut.mutate({ instanceId: deployingScenario.activeInstanceId, broker, capital })
          }}
          onCancel={() => setDeployingScenario(null)}
          isLoading={deployMut.isPending}
          result={deployMut.data?.data?.data ?? null}
        />
      )}
    </div>
  )
}

// ── Scenario row ──────────────────────────────────────────────────────────────

function ScenarioRow({
  scenario: s, strategy, onEdit, onRun, onView, onDelete, onPromote, onStop, onDeployLive, onDismissError,
  loading, indented, colCount, jobProgress, signalRConnected,
}: {
  scenario: Scenario
  strategy: Strategy
  onEdit: () => void
  onRun: () => void           // Backtest (historical only)
  onView: () => void          // View Backtest / View Results
  onDelete: () => void
  onPromote: () => void       // Open promotion checklist → Fwd Test
  onStop: () => void          // Stop running Forward Test or Live instance
  onDeployLive: () => void    // Open Deploy Live modal (LiveCandidate → Live)
  onDismissError: () => void
  loading: boolean
  indented?: boolean
  colCount: number
  jobProgress: BacktestJobStatus | null
  signalRConnected: boolean
}) {
  const isRunning  = s.status === ScenarioStatus.Running
  const isFwdTest  = s.status === ScenarioStatus.FwdTesting
  const isLiveCand = s.status === ScenarioStatus.LiveCandidate
  const isLive     = s.status === ScenarioStatus.Live

  // Symbol + timeframe from the parent strategy definition
  const symbol    = strategy.instruments[0] ?? '—'
  const timeframe = strategy.primaryTimeframe

  return (
    <React.Fragment>
      <tr style={{ borderBottom: isRunning ? 'none' : `1px solid ${C.border2}` }}>

        {/* ── Name / hypothesis / symbol+tf stacked ── */}
        <td style={{ ...tdStyle, paddingLeft: indented ? 24 : undefined }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
            {s.isBaseline && (
              <span style={{
                fontSize: 9, fontWeight: 700, color: C.amber,
                padding: '1px 5px', borderRadius: 2,
                border: `1px solid ${C.amber44}`, background: C.amber11,
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
              border: `1px solid ${C.blue44}`, background: C.blue11, marginTop: 2, display: 'inline-block',
            }}>
              {s.hypothesisTag}
            </span>
          )}
          {/* Instrument + timeframe inherited from strategy definition */}
          <div style={{ fontSize: 10, color: C.textDim, marginTop: 3, fontFamily: F.mono }}>
            {symbol} · {timeframe}
          </div>
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
        <MetricCell value={s.lastMetrics?.returnPct} positive={(s.lastMetrics?.returnPct ?? 0) >= 0} fmt="pct" />
        <MetricCell value={s.lastMetrics?.maxDrawdownPct} positive={false} fmt="pct" />
        <MetricCell value={s.lastMetrics?.profitFactor} />
        <td style={tdStyle}>
          <StatusChip status={s.status} promotionNotes={s.promotionNotes} />
        </td>

        {/* ── Lifecycle-aware actions ── */}
        <td style={tdStyle}>
          <div style={{ display: 'flex', gap: 4, flexWrap: 'wrap' }}>

            {/* Backtest: visible for Draft / Backtested / Archived — not while running or in live modes */}
            {!isRunning && !isFwdTest && !isLiveCand && !isLive && (
              <ActionBtn
                label="Backtest"
                onClick={onRun}
                loading={loading}
                title="Run historical backtest — does not affect live trading"
              />
            )}

            {/* View Backtest: shown while running (live progress) or when backtested */}
            {(isRunning || s.status === ScenarioStatus.Backtested) && (
              <ActionBtn label="View Backtest" onClick={onView} />
            )}

            {/* Promote to Forward Test: only after a successful backtest */}
            {s.status === ScenarioStatus.Backtested && (
              <ActionBtn
                label="→ Fwd Test"
                onClick={onPromote}
                title="Open promotion checklist — moves scenario to Forward Test"
              />
            )}

            {/* Forward Test actions */}
            {isFwdTest && (
              <>
                <ActionBtn label="View Results" onClick={onView} title="View forward-test results" />
                <ActionBtn
                  label="Stop Fwd Test"
                  onClick={onStop}
                  disabled={!s.activeInstanceId || loading}
                  title={s.activeInstanceId ? 'Stop the running forward test' : 'No active instance found'}
                  danger
                />
              </>
            )}

            {/* Live Candidate: deploy gate (runs pre-flight checks) */}
            {isLiveCand && (
              <>
                <ActionBtn
                  label="Deploy Live"
                  onClick={onDeployLive}
                  disabled={!s.activeInstanceId || loading}
                  title={s.activeInstanceId ? 'Run pre-flight checks and promote to live trading' : 'No forward test instance found'}
                />
                <ActionBtn label="View Results" onClick={onView} />
              </>
            )}

            {/* Live: view + stop */}
            {isLive && (
              <>
                <ActionBtn label="View Live" onClick={onView} title="View live trading results" />
                <ActionBtn
                  label="Stop Live"
                  onClick={onStop}
                  disabled={!s.activeInstanceId || loading}
                  title={s.activeInstanceId ? 'Stop the live trading instance' : 'No active instance found'}
                  danger
                />
              </>
            )}

            {/* Edit: disabled while running or in active forward/live modes */}
            {!isLive && (
              <ActionBtn
                label="Edit"
                onClick={onEdit}
                disabled={isRunning || isFwdTest}
                title={isFwdTest ? 'Stop forward test before editing' : undefined}
              />
            )}

            {/* Delete: blocked while running, in fwd test, or live */}
            <ActionBtn
              label="Delete"
              danger
              onClick={onDelete}
              loading={loading}
              disabled={isRunning || isFwdTest || isLive}
              title={
                isLive      ? 'Stop live trading before deleting'
                : isFwdTest ? 'Stop forward test before deleting'
                : isRunning ? 'Backtest is running'
                : undefined
              }
            />
          </div>
        </td>
      </tr>

      {/* ── Real-time backtest progress panel ── */}
      {isRunning && (
        <tr style={{ borderBottom: `1px solid ${C.border2}` }}>
          <td colSpan={colCount} style={{ padding: '10px 14px', background: C.amber11 }}>
            {jobProgress && (jobProgress.status === 'Running' || jobProgress.status === 'Downloading') ? (
              /* ── Active progress (SignalR / HTTP poll) ── */
              <div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 8 }}>
                  <span style={{
                    display: 'inline-block', width: 8, height: 8, borderRadius: '50%',
                    background: C.amber, flexShrink: 0,
                    animation: 'pulse 1.2s ease-in-out infinite',
                  }} />
                  <span style={{ fontSize: 12, color: C.amber, fontWeight: 600 }}>
                    {jobProgress.status === 'Downloading'
                      ? `Preparing historical data for ${symbol} (${timeframe})…`
                      : 'Backtest running…'}
                  </span>
                  {!signalRConnected && (
                    <span style={{ color: C.textDim, fontSize: 10 }}>(polling)</span>
                  )}
                </div>

                {/* Progress bar */}
                <div style={{ position: 'relative', height: 6, background: C.surface3, borderRadius: 3, marginBottom: 8, overflow: 'hidden' }}>
                  <div style={{
                    position: 'absolute', left: 0, top: 0, height: '100%',
                    width: `${jobProgress.progressPct}%`,
                    background: C.amber, borderRadius: 3,
                    transition: 'width 0.4s ease',
                  }} />
                </div>

                {/* Metrics row */}
                <div style={{ display: 'flex', gap: 24, flexWrap: 'wrap' }}>
                  <Stat label="Progress" value={`${jobProgress.progressPct.toFixed(1)}%`} />
                  {jobProgress.totalBars > 0 && (
                    <Stat label="Bar" value={`${jobProgress.currentBar.toLocaleString()} / ${jobProgress.totalBars.toLocaleString()}`} />
                  )}
                  <Stat label="Trades" value={String(jobProgress.tradesSoFar)} />
                  {jobProgress.currentEquity > 0 && (
                    <Stat
                      label="Equity"
                      value={`₹${Math.round(jobProgress.currentEquity).toLocaleString('en-IN')}`}
                      color={jobProgress.currentEquity >= s.capital ? C.green : C.red}
                    />
                  )}
                </div>
              </div>
            ) : jobProgress && (jobProgress.status === 'Failed' || jobProgress.status === 'Cancelled') ? (
              /* ── Error / cancelled ── */
              <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: 8 }}>
                <div>
                  <span style={{ fontSize: 12, color: C.red, fontWeight: 600 }}>
                    {jobProgress.status === 'Failed' ? '✗ Backtest failed' : '✗ Backtest cancelled'}
                  </span>
                  {jobProgress.error && (
                    <div style={{ fontSize: 11, color: C.textMuted, marginTop: 4 }}>
                      {jobProgress.error}
                      {(jobProgress.error.includes('401') || jobProgress.error.toLowerCase().includes('session expired') || jobProgress.error.toLowerCase().includes('unauthorized')) && (
                        <div style={{ marginTop: 6, color: C.amber, fontWeight: 600 }}>
                          Tip: Re-authenticate with MStock via Settings → Broker Login, then retry.
                        </div>
                      )}
                    </div>
                  )}
                </div>
                <button
                  onClick={onDismissError}
                  title="Dismiss"
                  style={{ flexShrink: 0, background: 'none', border: 'none', color: C.textDim, cursor: 'pointer', fontSize: 14, lineHeight: 1, padding: '0 2px' }}
                >×</button>
              </div>
            ) : (
              /* ── Waiting for first status event from engine ── */
              <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                <span style={{
                  display: 'inline-block', width: 8, height: 8, borderRadius: '50%',
                  background: C.amber, animation: 'pulse 1.2s ease-in-out infinite',
                }} />
                <span style={{ fontSize: 12, color: C.amber, fontWeight: 600 }}>Initialising backtest engine…</span>
                <span style={{ fontSize: 11, color: C.textMuted }}>
                  {signalRConnected ? 'Connected to backtest engine' : 'Connecting to backtest engine…'}
                </span>
              </div>
            )}
          </td>
        </tr>
      )}
    </React.Fragment>
  )
}

function Stat({ label, value, color }: { label: string; value: string; color?: string }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
      <span style={{ fontSize: 9, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.05em' }}>{label}</span>
      <span style={{ fontSize: 12, fontFamily: F.mono, color: color ?? C.text, fontWeight: 600 }}>{value}</span>
    </div>
  )
}

// ── Helpers ───────────────────────────────────────────────────────────────────

import React from 'react'

function ActionBtn({ label, onClick, loading, danger, disabled, title }: {
  label: string; onClick: () => void; loading?: boolean; danger?: boolean; disabled?: boolean; title?: string
}) {
  const isDisabled = loading || disabled
  return (
    <button
      onClick={onClick}
      disabled={isDisabled}
      title={title}
      style={{
        background: danger ? C.redBg : C.surface2,
        color: danger ? C.red : C.textSub,
        border: `1px solid ${danger ? C.red44 : C.border}`,
        borderRadius: 4, padding: '3px 8px', cursor: isDisabled ? 'not-allowed' : 'pointer',
        fontSize: 11, opacity: isDisabled ? 0.4 : 1,
      }}
    >
      {loading ? '…' : label}
    </button>
  )
}

// ── Deploy Live Modal ─────────────────────────────────────────────────────────

function DeployLiveModal({ scenario, onConfirm, onCancel, isLoading, result }: {
  scenario: Scenario
  onConfirm: (broker: string, capital: number) => void
  onCancel: () => void
  isLoading: boolean
  result: import('../../api/client').PromoteToLiveResult | null
}) {
  const [broker,  setBroker]  = useState('MStock')
  const [capital, setCapital] = useState(scenario.capital)

  const overlay: React.CSSProperties = {
    position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.7)',
    display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000,
  }
  const box: React.CSSProperties = {
    background: '#0d0d17', border: `1px solid #2a2a3a`, borderRadius: 8,
    padding: 28, width: 480, maxWidth: '90vw',
  }
  const label: React.CSSProperties = { fontSize: 11, color: '#888', marginBottom: 4, display: 'block' }
  const input: React.CSSProperties = {
    width: '100%', background: '#12121e', border: '1px solid #2a2a3a', borderRadius: 4,
    color: '#e0e0e0', fontSize: 13, padding: '6px 10px', boxSizing: 'border-box',
  }

  return (
    <div style={overlay}>
      <div style={box}>
        <div style={{ fontSize: 15, fontWeight: 700, marginBottom: 16 }}>
          Deploy Live — {scenario.name}
        </div>

        {/* Pre-flight result */}
        {result && (
          <div style={{ marginBottom: 16 }}>
            {result.checks.map(c => (
              <div key={c.name} style={{ display: 'flex', gap: 8, alignItems: 'flex-start', marginBottom: 6 }}>
                <span style={{ color: c.passed ? '#00d07a' : '#ff4757', fontSize: 13, minWidth: 14 }}>
                  {c.passed ? '✓' : '✗'}
                </span>
                <div>
                  <span style={{ fontSize: 12, color: c.passed ? '#e0e0e0' : '#ff4757' }}>{c.name}</span>
                  {c.reason && <div style={{ fontSize: 11, color: '#888', marginTop: 2 }}>{c.reason}</div>}
                </div>
              </div>
            ))}
            {result.error && (
              <div style={{ color: '#ff4757', fontSize: 12, marginTop: 8 }}>{result.error}</div>
            )}
            {result.success && (
              <div style={{ color: '#00d07a', fontSize: 13, marginTop: 8, fontWeight: 600 }}>
                ✓ Live instance created — strategy is now deployed.
              </div>
            )}
          </div>
        )}

        {!result?.success && (
          <>
            <div style={{ marginBottom: 12 }}>
              <label style={label}>Broker</label>
              <select value={broker} onChange={e => setBroker(e.target.value)} style={input}>
                <option>MStock</option>
                <option>Zerodha</option>
                <option>Upstox</option>
              </select>
            </div>
            <div style={{ marginBottom: 20 }}>
              <label style={label}>Allocated Capital (₹)</label>
              <input
                type="number" value={capital} min={1000} step={1000}
                onChange={e => setCapital(Number(e.target.value))}
                style={input}
              />
            </div>
          </>
        )}

        <div style={{ display: 'flex', gap: 10, justifyContent: 'flex-end' }}>
          <button onClick={onCancel} style={{ padding: '6px 16px', background: 'transparent', border: '1px solid #2a2a3a', borderRadius: 4, color: '#888', cursor: 'pointer', fontSize: 13 }}>
            {result?.success ? 'Close' : 'Cancel'}
          </button>
          {!result?.success && (
            <button
              onClick={() => onConfirm(broker, capital)}
              disabled={isLoading || capital <= 0}
              style={{ padding: '6px 16px', background: '#00d07a22', border: '1px solid #00d07a', borderRadius: 4, color: '#00d07a', cursor: isLoading ? 'not-allowed' : 'pointer', fontSize: 13, opacity: isLoading ? 0.5 : 1 }}
            >
              {isLoading ? 'Running checks…' : 'Run Pre-flight & Deploy'}
            </button>
          )}
        </div>
      </div>
    </div>
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
