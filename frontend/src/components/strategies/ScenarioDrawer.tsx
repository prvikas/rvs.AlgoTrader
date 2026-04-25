import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { strategyDomainApi } from '../../api/client'
import {
  Broker, BrokerageConfig, DEFAULT_BROKERAGE_CONFIG, OverrideSection, ParameterOverride, ParamRange,
  Scenario, ScenarioStatus, Strategy,
} from '../../types/strategy'
import { C, SP } from '../../styles/tokens'
import { useEnums } from '../../context/EnumsContext'

interface Props {
  strategy: Strategy
  scenarioId?: string
  onClose: () => void
}

export function ScenarioDrawer({ strategy, scenarioId, onClose }: Props) {
  const qc = useQueryClient()
  const { enums } = useEnums()
  const brokerOptions = enums.broker ?? []

  const { data: existing } = useQuery({
    queryKey: ['scenarios', strategy.id],
    queryFn: () => strategyDomainApi.listScenarios(strategy.id),
    select: (list: Scenario[]) => list.find(s => s.id === scenarioId),
    enabled: !!scenarioId,
  })

  const [name, setName] = useState(existing?.name ?? '')
  const [description, setDescription] = useState(existing?.description ?? '')
  const [hypothesis, setHypothesis] = useState(existing?.hypothesis ?? '')
  const [hypothesisTag, setHypothesisTag] = useState(existing?.hypothesisTag ?? '')
  const [isBaseline, setIsBaseline] = useState(existing?.isBaseline ?? false)
  const [capital, setCapital] = useState(existing?.capital ?? 100000)
  const [broker, setBroker] = useState<Broker>(existing?.brokerAccount ?? Broker.MStock)
  const [fromDate, setFromDate] = useState(existing?.backtestRange.from ?? '2023-01-01')
  const [toDate, setToDate] = useState(existing?.backtestRange.to ?? '2025-12-31')
  const [overrides, setOverrides] = useState<ParameterOverride[]>(existing?.parameterOverrides ?? [])
  const [brokerage, setBrokerage] = useState<BrokerageConfig>(
    existing?.brokerageConfig ?? DEFAULT_BROKERAGE_CONFIG
  )
  const [errors, setErrors] = useState<Record<string, string>>({})

  const createMut = useMutation({
    mutationFn: (s: Omit<Scenario, 'id' | 'strategyId'>) =>
      strategyDomainApi.createScenario(strategy.id, s),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['scenarios', strategy.id] })
      onClose()
    },
  })

  const updateMut = useMutation({
    mutationFn: (s: Partial<Scenario>) =>
      strategyDomainApi.updateScenario(strategy.id, scenarioId!, s),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['scenarios', strategy.id] })
      onClose()
    },
  })

  function validate(): boolean {
    const e: Record<string, string> = {}
    if (!name.trim()) e.name = 'Name is required'
    if (capital <= 0) e.capital = 'Capital must be > 0'
    setErrors(e)
    return Object.keys(e).length === 0
  }

  function buildPayload(): Omit<Scenario, 'id' | 'strategyId'> {
    return {
      name: name.trim(),
      description: description.trim() || undefined,
      hypothesis: hypothesis.trim() || undefined,
      hypothesisTag: hypothesisTag.trim() || undefined,
      isBaseline,
      capital,
      brokerAccount: broker,
      backtestRange: { from: fromDate, to: toDate },
      parameterOverrides: overrides,
      brokerageConfig: brokerage,
      status: ScenarioStatus.Draft,
    }
  }

  function save() {
    if (!validate()) return
    if (scenarioId) updateMut.mutate(buildPayload())
    else createMut.mutate(buildPayload())
  }

  function saveAndRun() {
    if (!validate()) return
    const payload = buildPayload()
    if (scenarioId) {
      updateMut.mutate(payload, {
        onSuccess: async () => {
          await strategyDomainApi.runScenario(strategy.id, scenarioId)
          qc.invalidateQueries({ queryKey: ['scenarios', strategy.id] })
          onClose()
        },
      })
    } else {
      createMut.mutate(payload, {
        onSuccess: async (created: Scenario) => {
          await strategyDomainApi.runScenario(strategy.id, created.id)
          qc.invalidateQueries({ queryKey: ['scenarios', strategy.id] })
          onClose()
        },
      })
    }
  }

  function setOverride(idx: number, field: keyof ParameterOverride, val: ParameterOverride[keyof ParameterOverride]) {
    const next = [...overrides]
    ;(next[idx] as unknown as Record<string, unknown>)[field as string] = val
    setOverrides(next)
  }

  function removeOverride(idx: number) {
    setOverrides(overrides.filter((_, i) => i !== idx))
  }

  const isPending = createMut.isPending || updateMut.isPending

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: SP.md }}>

      {/* ── 1. Run configuration — FIRST so Name is immediately visible ── */}
      <section>
        <SectionLabel>SCENARIO DETAILS</SectionLabel>
        <div style={{ display: 'flex', flexDirection: 'column', gap: SP.sm }}>
          <Field label="Name *" error={errors.name}>
            <input value={name} onChange={e => setName(e.target.value)} style={inputStyle} placeholder="e.g. Base case — EMA 21" />
          </Field>
          <Field label="Description">
            <textarea
              value={description} onChange={e => setDescription(e.target.value)}
              rows={2} style={{ ...inputStyle, resize: 'vertical' }}
            />
          </Field>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: SP.sm }}>
            <Field label="Capital (₹) *" error={errors.capital}>
              <input
                type="number" min={1} value={capital}
                onChange={e => setCapital(Number(e.target.value))}
                style={inputStyle}
              />
            </Field>
            <Field label="Broker account">
              <select value={broker} onChange={e => setBroker(e.target.value as Broker)} style={inputStyle}>
                {brokerOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
              </select>
            </Field>
          </div>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: SP.sm }}>
            <Field label="Backtest from">
              <input type="date" value={fromDate} onChange={e => setFromDate(e.target.value)} style={inputStyle} />
            </Field>
            <Field label="Backtest to">
              <input type="date" value={toDate} onChange={e => setToDate(e.target.value)} style={inputStyle} />
            </Field>
          </div>
        </div>
      </section>

      {/* ── 2. Hypothesis ── */}
      <section>
        <SectionLabel>HYPOTHESIS (optional)</SectionLabel>
        <div style={{ display: 'flex', flexDirection: 'column', gap: SP.sm }}>
          <Field label="What are you testing?">
            <textarea
              value={hypothesis}
              onChange={e => setHypothesis(e.target.value)}
              placeholder="e.g. Shortening the EMA period will improve win rate in trending regimes…"
              rows={2}
              style={{ ...inputStyle, resize: 'vertical' }}
            />
          </Field>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr auto', gap: SP.sm, alignItems: 'end' }}>
            <Field label="Tag">
              <input
                value={hypothesisTag}
                onChange={e => setHypothesisTag(e.target.value)}
                placeholder="e.g. ema-sensitivity"
                style={inputStyle}
              />
            </Field>
            <label style={{
              display: 'flex', alignItems: 'center', gap: 6, cursor: 'pointer',
              padding: '6px 0', fontSize: 12, color: isBaseline ? C.green : C.textMuted,
            }}>
              <input type="checkbox" checked={isBaseline} onChange={e => setIsBaseline(e.target.checked)} />
              Baseline
            </label>
          </div>
        </div>
      </section>

      {/* ── 3. Brokerage & Costs ── */}
      <section>
        <SectionLabel>BROKERAGE & COSTS</SectionLabel>
        <div style={{ display: 'flex', flexDirection: 'column', gap: SP.sm }}>

          {/* Brokerage type toggle */}
          <div style={{ display: 'flex', gap: SP.sm }}>
            {(['flat', 'pct'] as const).map(t => (
              <button key={t} onClick={() => setBrokerage(b => ({ ...b, type: t }))} style={{
                flex: 1, padding: '5px 8px', fontSize: 11, borderRadius: 4, cursor: 'pointer',
                background: brokerage.type === t ? '#1e3a5f' : C.surface3,
                color:      brokerage.type === t ? '#93c5fd' : C.textMuted,
                border: `1px solid ${brokerage.type === t ? '#2563eb44' : C.border}`,
              }}>
                {t === 'flat' ? 'Flat ₹/order' : '% of turnover'}
              </button>
            ))}
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: SP.sm }}>
            {brokerage.type === 'flat' ? (
              <Field label="Brokerage (₹/side)">
                <input type="number" min={0} step={1} value={brokerage.flatPerSide}
                  onChange={e => setBrokerage(b => ({ ...b, flatPerSide: Number(e.target.value) }))}
                  style={inputStyle} />
              </Field>
            ) : (
              <Field label="Brokerage (% of turnover)">
                <input type="number" min={0} step={0.001} value={brokerage.pct}
                  onChange={e => setBrokerage(b => ({ ...b, pct: Number(e.target.value) }))}
                  style={inputStyle} />
              </Field>
            )}
            <Field label="Slippage (basis pts)">
              <input type="number" min={0} step={1} value={brokerage.slippageBps}
                onChange={e => setBrokerage(b => ({ ...b, slippageBps: Number(e.target.value) }))}
                style={inputStyle} />
            </Field>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: SP.sm }}>
            <Field label="STT (%)">
              <input type="number" min={0} step={0.001} value={brokerage.sttPct}
                onChange={e => setBrokerage(b => ({ ...b, sttPct: Number(e.target.value) }))}
                style={inputStyle} />
            </Field>
            <Field label="GST on brokerage (%)">
              <input type="number" min={0} step={0.1} value={brokerage.gstPct}
                onChange={e => setBrokerage(b => ({ ...b, gstPct: Number(e.target.value) }))}
                style={inputStyle} />
            </Field>
            <Field label="SEBI charges (%)">
              <input type="number" min={0} step={0.00001} value={brokerage.sebiChargesPct}
                onChange={e => setBrokerage(b => ({ ...b, sebiChargesPct: Number(e.target.value) }))}
                style={inputStyle} />
            </Field>
            <Field label="Stamp duty (%)">
              <input type="number" min={0} step={0.001} value={brokerage.stampDutyPct}
                onChange={e => setBrokerage(b => ({ ...b, stampDutyPct: Number(e.target.value) }))}
                style={inputStyle} />
            </Field>
          </div>

        </div>
      </section>

      {/* ── 5. Inherited notice ── */}
      <div style={{
        fontSize: 10, color: C.textMuted, fontStyle: 'italic',
        padding: SP.sm, background: C.surface2, borderRadius: 4,
      }}>
        Strategy: <strong style={{ color: C.textSub }}>{strategy.name}</strong> — indicators, rules, and exit structure are inherited and fixed. Only parameter values may be overridden below.
      </div>

      {/* ── 6. Inherited indicators read-only ── */}
      {strategy.indicators.length > 0 && (
        <section>
          <SectionLabel>INHERITED INDICATORS</SectionLabel>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 11 }}>
            <thead>
              <tr style={{ background: C.surface2 }}>
                {['Indicator', 'TF', 'Params'].map(h => (
                  <th key={h} style={thStyle}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {strategy.indicators.map(ind => (
                <tr key={ind.id} style={{ borderBottom: `1px solid ${C.border2}` }}>
                  <td style={tdStyle}>
                    {ind.label?.trim()
                      ? <><strong>{ind.label}</strong> <span style={{ color: C.textMuted, fontSize: 10 }}>({ind.type})</span></>
                      : ind.type
                    }
                  </td>
                  <td style={{ ...tdStyle, color: C.textMuted }}>{ind.timeframe}</td>
                  <td style={{ ...tdStyle, color: C.textDim, fontSize: 10 }}>
                    {Object.entries(ind.baseParams).map(([k, v]) => `${k}:${v}`).join(', ')}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      )}

      {/* ── 7. Parameter overrides ── */}
      <section>
        <SectionLabel>PARAMETER OVERRIDES</SectionLabel>
        {strategy.indicators.map(ind => {
          const numericParams = Object.entries(ind.baseParams).filter(([, v]) => typeof v === 'number')
          if (numericParams.length === 0) return null
          return (
            <div key={ind.id} style={{ marginBottom: SP.sm }}>
              <div style={{ fontSize: 11, fontWeight: 600, color: C.textSub, marginBottom: 4 }}>
                {ind.type} ({ind.timeframe}) — {ind.role}
              </div>
              {numericParams.map(([key, baseVal]) => {
                const range = ind.allowedParamRanges[key] as ParamRange | undefined
                const existing_override = overrides.find(
                  o => o.indicatorId === ind.id && o.paramKey === key
                )
                const overrideIdx = overrides.findIndex(
                  o => o.indicatorId === ind.id && o.paramKey === key
                )
                const isActive = !!existing_override

                return (
                  <div key={key} style={{
                    display: 'flex', alignItems: 'center', gap: SP.sm,
                    padding: '4px 0', borderBottom: `1px solid ${C.border2}`,
                  }}>
                    <input
                      type="checkbox"
                      checked={isActive}
                      onChange={e => {
                        if (e.target.checked) {
                          setOverrides([...overrides, {
                            section: OverrideSection.Indicator,
                            indicatorId: ind.id,
                            paramKey: key,
                            baseValue: baseVal as number,
                            overrideValue: baseVal as number,
                          }])
                        } else {
                          setOverrides(overrides.filter((_, i) => i !== overrideIdx))
                        }
                      }}
                    />
                    <span style={{ fontSize: 11, width: 80, color: C.textMuted }}>{key}</span>
                    <span style={{ fontSize: 11, color: C.textDim, width: 50 }}>{String(baseVal)}</span>
                    {isActive ? (
                      <>
                        <input
                          type="number" step={0.01}
                          value={Number(existing_override!.overrideValue)}
                          onChange={e => {
                            const v = Number(e.target.value)
                            const err = range && (v < range.min || v > range.max)
                              ? `Must be ${range.min}–${range.max}` : undefined
                            setErrors(prev => ({ ...prev, [`${ind.id}-${key}`]: err ?? '' }))
                            setOverride(overrideIdx, 'overrideValue', v)
                          }}
                          style={{ width: 70, ...inputStyle }}
                        />
                        {errors[`${ind.id}-${key}`] && (
                          <span style={{ fontSize: 10, color: C.red }}>{errors[`${ind.id}-${key}`]}</span>
                        )}
                        {range && (
                          <span style={{ fontSize: 10, color: C.textDim }}>
                            [{range.min}–{range.max}]
                          </span>
                        )}
                      </>
                    ) : (
                      <span style={{ fontSize: 10, color: C.textDim }}>
                        {range ? `Allowed: ${range.min}–${range.max}` : ''}
                      </span>
                    )}
                  </div>
                )
              })}
            </div>
          )
        })}

        {overrides.filter(o => !o.indicatorId).length > 0 && (
          <div style={{ marginTop: SP.sm }}>
            <div style={{ fontSize: 11, fontWeight: 600, color: C.textSub, marginBottom: 4 }}>Other Overrides</div>
            {overrides.filter(o => !o.indicatorId).map((o, i) => (
              <div key={i} style={{ display: 'flex', gap: SP.sm, alignItems: 'center', padding: '4px 0' }}>
                <span style={{ fontSize: 11, color: C.textMuted, flex: 1 }}>{o.section} / {o.paramKey}</span>
                <span style={{ fontSize: 11, color: C.textDim }}>{String(o.baseValue)}</span>
                <span style={{ fontSize: 11, color: C.text }}>→ {String(o.overrideValue)}</span>
                <button onClick={() => removeOverride(overrides.indexOf(o))} style={deleteBtnStyle}>×</button>
              </div>
            ))}
          </div>
        )}
      </section>

      {/* Footer */}
      <div style={{ display: 'flex', gap: SP.sm, paddingTop: SP.sm, borderTop: `1px solid ${C.border}` }}>
        <button onClick={onClose} style={cancelBtnStyle}>Cancel</button>
        <button onClick={save} disabled={isPending} style={secondaryBtnStyle}>
          {isPending ? '…' : 'Save Scenario'}
        </button>
        <button onClick={saveAndRun} disabled={isPending} style={primaryBtnStyle}>
          {isPending ? '…' : 'Save & Run Backtest'}
        </button>
      </div>
    </div>
  )
}

function SectionLabel({ children }: { children: React.ReactNode }) {
  return (
    <div style={{
      fontSize: 10, fontWeight: 700, color: C.textMuted, letterSpacing: '0.05em',
      marginBottom: SP.sm, paddingBottom: 4, borderBottom: `1px solid ${C.border}`,
    }}>
      {children}
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

const inputStyle: React.CSSProperties = {
  width: '100%', background: C.surface3, border: `1px solid ${C.border}`,
  color: C.text, borderRadius: 4, padding: '6px 8px', fontSize: 12,
  boxSizing: 'border-box',
}

const thStyle: React.CSSProperties = {
  padding: '5px 8px', textAlign: 'left', fontSize: 10,
  color: C.textMuted, fontWeight: 600,
}

const tdStyle: React.CSSProperties = { padding: '5px 8px', fontSize: 11, color: C.text }

const primaryBtnStyle: React.CSSProperties = {
  flex: 1, background: '#1e3a5f', color: '#93c5fd', border: '1px solid #2563eb44',
  borderRadius: 5, padding: '7px 14px', cursor: 'pointer', fontSize: 12, fontWeight: 700,
}

const secondaryBtnStyle: React.CSSProperties = {
  flex: 1, background: C.surface2, color: C.textSub, border: `1px solid ${C.border}`,
  borderRadius: 5, padding: '7px 14px', cursor: 'pointer', fontSize: 12,
}

const cancelBtnStyle: React.CSSProperties = {
  background: 'none', color: C.textMuted, border: `1px solid ${C.border}`,
  borderRadius: 5, padding: '7px 14px', cursor: 'pointer', fontSize: 12,
}

const deleteBtnStyle: React.CSSProperties = {
  background: 'none', border: 'none', color: C.textMuted, cursor: 'pointer', fontSize: 14,
}
