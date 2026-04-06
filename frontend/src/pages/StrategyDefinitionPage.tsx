import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { strategyDomainApi } from '../api/client'
import {
  DayOfWeek, ExitCombineLogic, IndicatorConfig,
  KillSwitch, MoveStopTo, RRConfig, RiskControls,
  StartCondition, StopTargetConfig, StopType, Strategy, StrategyStatus,
  Timeframe, TrailingConfig, TrailingType, TradingStyle,
  ExitBehaviour,
} from '../types/strategy'
import { C, SP } from '../styles/tokens'
import { useEnums } from '../context/EnumsContext'
import { IndicatorModal } from '../components/strategies/IndicatorModal'
import { RuleGroupEditor } from '../components/strategies/RuleGroupEditor'

interface Props {
  strategyId?: string
  initialData?: Strategy
  onSaved?: (s: Strategy) => void
}

type SubTab = 'core' | 'rules' | 'risk'

let _groupId = 0
function newGroupId() { return `grp-${Date.now()}-${_groupId++}` }

function emptyBlock() {
  return {
    enabled: false, groupOperator: ExitCombineLogic.AND,
    groups: [{
      id: newGroupId(), label: 'Group 1',
      logicalOperator: ExitCombineLogic.AND, conditions: [],
    }],
  }
}

export function StrategyDefinitionPage({ strategyId, initialData, onSaved }: Props) {
  const navigate = useNavigate()
  const qc = useQueryClient()
  const { enums } = useEnums()

  const tfOptions   = enums.timeframe ?? []
  const styleOptions = enums.tradingStyle ?? []
  const logicOptions = enums.exitCombineLogic ?? []
  const dowOptions   = enums.dayOfWeek ?? []
  const stopOptions  = enums.stopType ?? []
  const trailOptions = enums.trailingType ?? []
  const scOptions    = enums.startCondition ?? []
  const moveStopOptions = enums.moveStopTo ?? []

  const [subTab, setSubTab] = useState<SubTab>('core')
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [indicatorModalOpen, setIndicatorModalOpen] = useState(false)
  const [editingIndicatorId, setEditingIndicatorId] = useState<string | undefined>()

  // Form state
  const [name, setName] = useState(initialData?.name ?? '')
  const [description, setDescription] = useState(initialData?.description ?? '')
  const [primaryTf, setPrimaryTf] = useState<Timeframe>(initialData?.primaryTimeframe ?? Timeframe.D1)
  const [instruments, setInstruments] = useState<string[]>(initialData?.instruments ?? [])
  const [instrumentInput, setInstrumentInput] = useState('')
  const [tradingStyle, setTradingStyle] = useState<TradingStyle>(initialData?.tradingStyle ?? TradingStyle.Swing)
  const [indicators, setIndicators] = useState<IndicatorConfig[]>(initialData?.indicators ?? [])

  const [exitBehaviour, setExitBehaviour] = useState<ExitBehaviour>(initialData?.exitBehaviour ?? {
    exitEndOfSession: true, exitAfterNBars: null, exitAtStopOrTargetOnly: false,
    combineLogic: ExitCombineLogic.OR,
    tradableDays: [DayOfWeek.Mon, DayOfWeek.Tue, DayOfWeek.Wed, DayOfWeek.Thu, DayOfWeek.Fri],
    sessionStart: '09:15', sessionEnd: '15:20',
  })

  const [riskControls, setRiskControls] = useState<RiskControls>(initialData?.riskControls ?? {
    maxRiskPerTradePercent: 1, maxTradesPerDay: 5,
  })
  const [killSwitchEnabled, setKillSwitchEnabled] = useState(!!initialData?.killSwitch)
  const [killSwitch, setKillSwitch] = useState<KillSwitch>(initialData?.killSwitch ?? {})

  const [longEntry, setLongEntry] = useState(initialData?.longEntry ?? emptyBlock())
  const [shortEntry, setShortEntry] = useState(initialData?.shortEntry ?? emptyBlock())
  const [longExit, setLongExit] = useState(initialData?.longExit ?? emptyBlock())
  const [shortExit, setShortExit] = useState(initialData?.shortExit ?? emptyBlock())

  const [stopLoss, setStopLoss] = useState<StopTargetConfig>(initialData?.stopLoss ?? {
    type: StopType.ATRMultiple, baseValue: 2, allowedRange: { min: 1, max: 4 },
  })
  const [profitTarget, setProfitTarget] = useState<StopTargetConfig>(initialData?.profitTarget ?? {
    type: StopType.ATRMultiple, baseValue: 4, allowedRange: { min: 2, max: 8 },
  })
  const [rrConfig, setRrConfig] = useState<RRConfig>(initialData?.rrConfig ?? {
    rrMin: 2, rrMax: 4, deriveTargetFromSL: true,
  })
  const [trailing, setTrailing] = useState<TrailingConfig>(initialData?.trailing ?? {
    enabled: false, trailingType: TrailingType.ATRMultiple,
    trailingParams: { multiplier: 1.5 },
    startCondition: StartCondition.Immediately,
    partialExits: [],
  })

  const createMut = useMutation({
    mutationFn: (s: Omit<Strategy, 'id' | 'createdAt' | 'updatedAt'>) =>
      strategyDomainApi.createStrategy(s),
    onSuccess: (created: Strategy) => {
      qc.invalidateQueries({ queryKey: ['strategies'] })
      onSaved?.(created)
      navigate(`/strategies?id=${created.id}`)
    },
  })

  const updateMut = useMutation({
    mutationFn: (s: Partial<Strategy>) =>
      strategyDomainApi.updateStrategy(strategyId!, s),
    onSuccess: (updated: Strategy) => {
      qc.invalidateQueries({ queryKey: ['strategies'] })
      onSaved?.(updated)
    },
  })

  function validate(): boolean {
    const e: Record<string, string> = {}
    if (!name.trim()) e.name = 'Name is required'
    if (!tradingStyle) e.tradingStyle = 'Required'
    const anyExit = exitBehaviour.exitEndOfSession || exitBehaviour.exitAfterNBars !== null || exitBehaviour.exitAtStopOrTargetOnly
    if (!anyExit) e.exitBehaviour = 'At least one exit behaviour must be enabled'
    if (riskControls.maxRiskPerTradePercent <= 0) e.maxRisk = 'Must be > 0'
    if (riskControls.maxTradesPerDay <= 0) e.maxTrades = 'Must be > 0'
    setErrors(e)
    return Object.keys(e).length === 0
  }

  function buildPayload(): Omit<Strategy, 'id' | 'createdAt' | 'updatedAt'> {
    return {
      name: name.trim(), description: description.trim() || undefined,
      primaryTimeframe: primaryTf, instruments, tradingStyle,
      status: StrategyStatus.Draft,
      indicators, longEntry, shortEntry, longExit, shortExit,
      exitBehaviour, stopLoss, profitTarget, rrConfig, trailing,
      riskControls,
      killSwitch: killSwitchEnabled ? killSwitch : undefined,
    }
  }

  function save(andGoToScenarios = false) {
    if (!validate()) return
    const payload = buildPayload()
    if (strategyId) {
      updateMut.mutate(payload, { onSuccess: (s: Strategy) => andGoToScenarios && navigate(`/strategies?id=${s.id}`) })
    } else {
      createMut.mutate(payload)
    }
  }

  function addInstrument() {
    const sym = instrumentInput.trim().toUpperCase()
    if (sym && !instruments.includes(sym)) {
      setInstruments(prev => [...prev, sym])
    }
    setInstrumentInput('')
  }

  function saveIndicator(ind: IndicatorConfig) {
    if (editingIndicatorId) {
      setIndicators(prev => prev.map(i => i.id === ind.id ? ind : i))
    } else {
      setIndicators(prev => [...prev, ind])
    }
  }

  function duplicateGroup(blockKey: 'longEntry' | 'shortEntry' | 'longExit' | 'shortExit', idx: number) {
    const setters = { longEntry: setLongEntry, shortEntry: setShortEntry, longExit: setLongExit, shortExit: setShortExit }
    const blocks = { longEntry, shortEntry, longExit, shortExit }
    const block = blocks[blockKey]
    const grp = { ...block.groups[idx], id: newGroupId(), label: block.groups[idx].label + ' (copy)' }
    const setter = setters[blockKey]
    setter({ ...block, groups: [...block.groups, grp] })
  }

  const isPending = createMut.isPending || updateMut.isPending

  const TABS: { key: SubTab; label: string }[] = [
    { key: 'core',  label: 'Core & Indicators' },
    { key: 'rules', label: 'Rules' },
    { key: 'risk',  label: 'Risk & Regime' },
  ]

  return (
    <div style={{ maxWidth: 1100, margin: '0 auto', padding: '0 16px' }}>
      {/* Sub-tab nav */}
      <div style={{ display: 'flex', gap: 0, marginBottom: SP.lg, borderBottom: `1px solid ${C.border}` }}>
        {TABS.map(t => (
          <button
            key={t.key}
            onClick={() => setSubTab(t.key)}
            style={{
              padding: '8px 16px', background: 'none', cursor: 'pointer', fontSize: 12,
              color: subTab === t.key ? C.blue : C.textMuted,
              borderBottom: `2px solid ${subTab === t.key ? C.blue : 'transparent'}`,
              border: 'none', borderBottomWidth: 2, marginBottom: -1,
            }}
          >
            {t.label}
          </button>
        ))}
      </div>

      {/* ── Sub-tab 1: Core & Indicators ── */}
      {subTab === 'core' && (
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1.4fr', gap: SP.xxl }}>
          {/* Left column */}
          <div style={{ display: 'flex', flexDirection: 'column', gap: SP.md }}>
            <Block title="Basic Details">
              <Field label="Strategy Name *" error={errors.name}>
                <input value={name} onChange={e => setName(e.target.value)} style={inputStyle} />
              </Field>
              <Field label="Primary Timeframe *">
                <select value={primaryTf} onChange={e => setPrimaryTf(e.target.value as Timeframe)} style={inputStyle}>
                  {tfOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
                </select>
              </Field>
              <Field label="Instruments *">
                <div style={{ display: 'flex', gap: SP.xs, marginBottom: 4, flexWrap: 'wrap' }}>
                  {instruments.map(sym => (
                    <span key={sym} style={{
                      fontSize: 11, padding: '2px 7px', borderRadius: 3,
                      background: C.surface2, border: `1px solid ${C.border}`,
                      color: C.text,
                    }}>
                      {sym}
                      <button
                        onClick={() => setInstruments(prev => prev.filter(x => x !== sym))}
                        style={{ marginLeft: 4, background: 'none', border: 'none', color: C.textMuted, cursor: 'pointer', fontSize: 12 }}
                      >
                        ×
                      </button>
                    </span>
                  ))}
                </div>
                <div style={{ display: 'flex', gap: SP.xs }}>
                  <input
                    value={instrumentInput}
                    onChange={e => setInstrumentInput(e.target.value)}
                    onKeyDown={e => e.key === 'Enter' && addInstrument()}
                    placeholder="NSE:AXISBANK"
                    style={{ ...inputStyle, flex: 1 }}
                  />
                  <button onClick={addInstrument} style={addBtnStyle}>Add</button>
                </div>
              </Field>
              <Field label="Trading Style *" error={errors.tradingStyle}>
                <select value={tradingStyle} onChange={e => setTradingStyle(e.target.value as TradingStyle)} style={inputStyle}>
                  {styleOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
                </select>
              </Field>
              <Field label="Description">
                <textarea value={description} onChange={e => setDescription(e.target.value)} rows={3} style={{ ...inputStyle, resize: 'vertical' }} />
              </Field>
            </Block>

            <Block title="Exit Behaviour" error={errors.exitBehaviour}>
              <label style={checkLabel}>
                <input type="checkbox" checked={exitBehaviour.exitEndOfSession}
                  onChange={e => setExitBehaviour(prev => ({ ...prev, exitEndOfSession: e.target.checked }))} />
                Exit at end of session
              </label>
              <div style={{ display: 'flex', alignItems: 'center', gap: SP.xs }}>
                <input type="checkbox"
                  checked={exitBehaviour.exitAfterNBars !== null}
                  onChange={e => setExitBehaviour(prev => ({ ...prev, exitAfterNBars: e.target.checked ? 20 : null }))} />
                <span style={{ fontSize: 12, color: C.text }}>Exit after</span>
                <input
                  type="number" min={1}
                  value={exitBehaviour.exitAfterNBars ?? ''}
                  disabled={exitBehaviour.exitAfterNBars === null}
                  onChange={e => setExitBehaviour(prev => ({ ...prev, exitAfterNBars: Number(e.target.value) }))}
                  style={{ ...inputStyle, width: 60 }}
                />
                <span style={{ fontSize: 12, color: C.text }}>bars</span>
              </div>
              <label style={checkLabel}>
                <input type="checkbox" checked={exitBehaviour.exitAtStopOrTargetOnly}
                  onChange={e => setExitBehaviour(prev => ({ ...prev, exitAtStopOrTargetOnly: e.target.checked }))} />
                Exit at stop/target only
              </label>
              <Field label="Combine rules with">
                <select
                  value={exitBehaviour.combineLogic}
                  onChange={e => setExitBehaviour(prev => ({ ...prev, combineLogic: e.target.value as ExitCombineLogic }))}
                  style={{ ...inputStyle, width: 120 }}
                >
                  {logicOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
                </select>
              </Field>
              <div style={{ marginTop: SP.sm }}>
                <div style={{ fontSize: 11, color: C.textMuted, marginBottom: 6 }}>Session filters</div>
                <div style={{ marginBottom: 6 }}>
                  <label style={{ fontSize: 11, color: C.textMuted, display: 'block', marginBottom: 4 }}>Tradable days</label>
                  <div style={{ display: 'flex', gap: 4, flexWrap: 'wrap' }}>
                    {dowOptions.map(o => (
                      <label key={o.value} style={{ display: 'flex', alignItems: 'center', gap: 3, cursor: 'pointer' }}>
                        <input
                          type="checkbox"
                          checked={exitBehaviour.tradableDays.includes(o.value as DayOfWeek)}
                          onChange={e => {
                            const d = o.value as DayOfWeek
                            setExitBehaviour(prev => ({
                              ...prev,
                              tradableDays: e.target.checked
                                ? [...prev.tradableDays, d]
                                : prev.tradableDays.filter(x => x !== d),
                            }))
                          }}
                        />
                        <span style={{ fontSize: 11, color: C.textSub }}>{o.label}</span>
                      </label>
                    ))}
                  </div>
                </div>
                <div style={{ display: 'flex', gap: SP.sm }}>
                  <Field label="Session start">
                    <input type="time" value={exitBehaviour.sessionStart ?? '09:15'}
                      onChange={e => setExitBehaviour(prev => ({ ...prev, sessionStart: e.target.value }))}
                      style={{ ...inputStyle, width: 100 }} />
                  </Field>
                  <Field label="Session end">
                    <input type="time" value={exitBehaviour.sessionEnd ?? '15:20'}
                      onChange={e => setExitBehaviour(prev => ({ ...prev, sessionEnd: e.target.value }))}
                      style={{ ...inputStyle, width: 100 }} />
                  </Field>
                </div>
              </div>
            </Block>
          </div>

          {/* Right column */}
          <div style={{ display: 'flex', flexDirection: 'column', gap: SP.md }}>
            <Block title="Indicators">
              {indicators.length > 0 && (
                <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 11, marginBottom: SP.sm }}>
                  <thead>
                    <tr style={{ background: C.surface2 }}>
                      {['Indicator', 'TF', 'Role', 'Base Params', 'Allowed Ranges', ''].map(h => (
                        <th key={h} style={thStyle}>{h}</th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {indicators.map(ind => (
                      <tr key={ind.id} style={{ borderBottom: `1px solid ${C.border2}` }}>
                        <td style={tdStyle}>{ind.type}</td>
                        <td style={{ ...tdStyle, color: C.textMuted }}>{ind.timeframe}</td>
                        <td style={{ ...tdStyle, color: C.textMuted }}>{ind.role}</td>
                        <td style={{ ...tdStyle, fontSize: 10, color: C.textDim }}>
                          {Object.entries(ind.baseParams).map(([k, v]) => `${k}:${v}`).join(', ')}
                        </td>
                        <td style={{ ...tdStyle, fontSize: 10, color: C.textDim }}>
                          {Object.entries(ind.allowedParamRanges).map(([k, r]) => {
                            if (Array.isArray(r)) return `${k}:[${r.join('|')}]`
                            return `${k}:${(r as { min: number; max: number }).min}–${(r as { min: number; max: number }).max}`
                          }).join(', ')}
                        </td>
                        <td style={tdStyle}>
                          <div style={{ display: 'flex', gap: 4 }}>
                            <button
                              onClick={() => { setEditingIndicatorId(ind.id); setIndicatorModalOpen(true) }}
                              style={smallBtnStyle}
                            >
                              Edit
                            </button>
                            <button
                              onClick={() => setIndicators(prev => prev.filter(i => i.id !== ind.id))}
                              style={{ ...smallBtnStyle, color: C.red }}
                            >
                              ×
                            </button>
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
              <button onClick={() => { setEditingIndicatorId(undefined); setIndicatorModalOpen(true) }} style={addBtnStyle}>
                + Add Indicator
              </button>
            </Block>

            <Block title="Risk Controls">
              <Field label="Max risk per trade % *" error={errors.maxRisk}>
                <input type="number" step={0.01} min={0.01} max={10}
                  value={riskControls.maxRiskPerTradePercent}
                  onChange={e => setRiskControls(p => ({ ...p, maxRiskPerTradePercent: Number(e.target.value) }))}
                  style={{ ...inputStyle, width: 120 }} />
              </Field>
              <Field label="Max trades per day *" error={errors.maxTrades}>
                <input type="number" min={1}
                  value={riskControls.maxTradesPerDay}
                  onChange={e => setRiskControls(p => ({ ...p, maxTradesPerDay: Number(e.target.value) }))}
                  style={{ ...inputStyle, width: 120 }} />
              </Field>
            </Block>

            <Block title="Kill Switch (optional)">
              <label style={checkLabel}>
                <input type="checkbox" checked={killSwitchEnabled} onChange={e => setKillSwitchEnabled(e.target.checked)} />
                Enable kill switch
              </label>
              {killSwitchEnabled && (
                <div style={{ display: 'flex', flexDirection: 'column', gap: SP.sm, marginTop: SP.sm }}>
                  <Field label="Daily loss limit (R)">
                    <input type="number" step={0.1}
                      value={killSwitch.dailyLossLimitR ?? ''}
                      onChange={e => setKillSwitch(p => ({ ...p, dailyLossLimitR: e.target.value ? Number(e.target.value) : undefined }))}
                      style={{ ...inputStyle, width: 120 }} />
                  </Field>
                  <Field label="Max intraday DD %">
                    <input type="number" step={0.1}
                      value={killSwitch.maxIntradayDrawdownPercent ?? ''}
                      onChange={e => setKillSwitch(p => ({ ...p, maxIntradayDrawdownPercent: e.target.value ? Number(e.target.value) : undefined }))}
                      style={{ ...inputStyle, width: 120 }} />
                  </Field>
                  <Field label="Cooldown after breach (bars)">
                    <input type="number" min={0}
                      value={killSwitch.cooldownBarsAfterHit ?? ''}
                      onChange={e => setKillSwitch(p => ({ ...p, cooldownBarsAfterHit: e.target.value ? Number(e.target.value) : undefined }))}
                      style={{ ...inputStyle, width: 120 }} />
                  </Field>
                </div>
              )}
            </Block>
          </div>
        </div>
      )}

      {/* ── Sub-tab 2: Rules ── */}
      {subTab === 'rules' && (
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: SP.xxl }}>
          {[
            { key: 'longEntry' as const, label: 'Long Entry', block: longEntry, set: setLongEntry },
            { key: 'shortEntry' as const, label: 'Short Entry', block: shortEntry, set: setShortEntry },
            { key: 'longExit' as const, label: 'Long Exit', block: longExit, set: setLongExit },
            { key: 'shortExit' as const, label: 'Short Exit', block: shortExit, set: setShortExit },
          ].map(({ key, label, block, set }) => (
            <div key={key}>
              <div style={{ display: 'flex', alignItems: 'center', gap: SP.sm, marginBottom: SP.sm }}>
                <label style={checkLabel}>
                  <input type="checkbox" checked={block.enabled}
                    onChange={e => set(prev => ({ ...prev, enabled: e.target.checked }))} />
                  <span style={{ fontSize: 13, fontWeight: 600 }}>{label}</span>
                </label>
                {block.enabled && (
                  <select
                    value={block.groupOperator}
                    onChange={e => set(prev => ({ ...prev, groupOperator: e.target.value as ExitCombineLogic }))}
                    style={{ background: C.surface2, border: `1px solid ${C.border}`, color: C.textSub, borderRadius: 4, padding: '2px 6px', fontSize: 11 }}
                  >
                    {logicOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
                  </select>
                )}
              </div>
              {block.enabled && block.groups.map((grp, i) => (
                <RuleGroupEditor
                  key={grp.id}
                  group={grp}
                  indicators={indicators}
                  onChange={updated => set(prev => ({ ...prev, groups: prev.groups.map((g, j) => j === i ? updated : g) }))}
                  onDelete={() => set(prev => ({ ...prev, groups: prev.groups.filter((_, j) => j !== i) }))}
                  onDuplicate={() => duplicateGroup(key, i)}
                />
              ))}
              {block.enabled && (
                <button
                  onClick={() => set(prev => ({
                    ...prev,
                    groups: [...prev.groups, {
                      id: newGroupId(), label: `Group ${prev.groups.length + 1}`,
                      logicalOperator: ExitCombineLogic.AND, conditions: [],
                    }],
                  }))}
                  style={addBtnStyle}
                >
                  + Add Group
                </button>
              )}
            </div>
          ))}
        </div>
      )}

      {/* ── Sub-tab 3: Risk & Regime ── */}
      {subTab === 'risk' && (
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: SP.xxl }}>
          {/* Stops & Targets */}
          <div>
            <Block title="Stop Loss">
              <Field label="Type">
                <select value={stopLoss.type}
                  onChange={e => setStopLoss(p => ({ ...p, type: e.target.value as StopType }))}
                  style={inputStyle}>
                  {stopOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
                </select>
              </Field>
              <Field label="Base value">
                <input type="number" step={0.01} value={stopLoss.baseValue}
                  onChange={e => setStopLoss(p => ({ ...p, baseValue: Number(e.target.value) }))}
                  style={{ ...inputStyle, width: 120 }} />
              </Field>
              <div style={{ display: 'flex', gap: SP.sm }}>
                <Field label="Min">
                  <input type="number" step={0.01} value={stopLoss.allowedRange.min}
                    onChange={e => setStopLoss(p => ({ ...p, allowedRange: { ...p.allowedRange, min: Number(e.target.value) } }))}
                    style={{ ...inputStyle, width: 80 }} />
                </Field>
                <Field label="Max">
                  <input type="number" step={0.01} value={stopLoss.allowedRange.max}
                    onChange={e => setStopLoss(p => ({ ...p, allowedRange: { ...p.allowedRange, max: Number(e.target.value) } }))}
                    style={{ ...inputStyle, width: 80 }} />
                </Field>
              </div>
            </Block>

            <Block title="Profit Target">
              <Field label="Type">
                <select value={profitTarget.type}
                  onChange={e => setProfitTarget(p => ({ ...p, type: e.target.value as StopType }))}
                  style={inputStyle}>
                  {stopOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
                </select>
              </Field>
              <label style={checkLabel}>
                <input type="checkbox" checked={rrConfig.deriveTargetFromSL}
                  onChange={e => setRrConfig(p => ({ ...p, deriveTargetFromSL: e.target.checked }))} />
                Derive from SL via R:R
              </label>
              {!rrConfig.deriveTargetFromSL && (
                <Field label="Base value">
                  <input type="number" step={0.01} value={profitTarget.baseValue}
                    onChange={e => setProfitTarget(p => ({ ...p, baseValue: Number(e.target.value) }))}
                    style={{ ...inputStyle, width: 120 }} />
                </Field>
              )}
              <div style={{ display: 'flex', gap: SP.sm }}>
                <Field label="rrMin">
                  <input type="number" step={0.1} value={rrConfig.rrMin}
                    onChange={e => setRrConfig(p => ({ ...p, rrMin: Number(e.target.value) }))}
                    style={{ ...inputStyle, width: 80 }} />
                </Field>
                <Field label="rrMax">
                  <input type="number" step={0.1} value={rrConfig.rrMax}
                    onChange={e => setRrConfig(p => ({ ...p, rrMax: Number(e.target.value) }))}
                    style={{ ...inputStyle, width: 80 }} />
                </Field>
              </div>
            </Block>
          </div>

          {/* Trailing & Scale-out */}
          <div>
            <Block title="Trailing & Scale-out">
              <label style={checkLabel}>
                <input type="checkbox" checked={trailing.enabled}
                  onChange={e => setTrailing(p => ({ ...p, enabled: e.target.checked }))} />
                Enable trailing stop
              </label>
              {trailing.enabled && (
                <div style={{ display: 'flex', flexDirection: 'column', gap: SP.sm, marginTop: SP.sm }}>
                  <Field label="Trailing type">
                    <select value={trailing.trailingType}
                      onChange={e => setTrailing(p => ({ ...p, trailingType: e.target.value as TrailingType }))}
                      style={inputStyle}>
                      {trailOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
                    </select>
                  </Field>
                  <Field label="Start condition">
                    <select value={trailing.startCondition}
                      onChange={e => setTrailing(p => ({ ...p, startCondition: e.target.value as StartCondition }))}
                      style={inputStyle}>
                      {scOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
                    </select>
                  </Field>
                  {trailing.startCondition !== StartCondition.Immediately && (
                    <Field label="Start condition value">
                      <input type="number" step={0.1}
                        value={trailing.startConditionValue ?? ''}
                        onChange={e => setTrailing(p => ({ ...p, startConditionValue: Number(e.target.value) }))}
                        style={{ ...inputStyle, width: 120 }} />
                    </Field>
                  )}

                  {/* Partial exits */}
                  <div>
                    <div style={{ fontSize: 11, color: C.textSub, fontWeight: 600, marginBottom: SP.xs }}>Partial Exits</div>
                    {trailing.partialExits.length > 0 && (
                      <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 11 }}>
                        <thead>
                          <tr style={{ background: C.surface2 }}>
                            {['Trigger R', '% Close', 'Move Stop To', ''].map(h => (
                              <th key={h} style={thStyle}>{h}</th>
                            ))}
                          </tr>
                        </thead>
                        <tbody>
                          {trailing.partialExits.map((pe, i) => (
                            <tr key={i} style={{ borderBottom: `1px solid ${C.border2}` }}>
                              <td style={tdStyle}>
                                <input type="number" step={0.1} value={pe.triggerR}
                                  onChange={e => {
                                    const exits = [...trailing.partialExits]
                                    exits[i] = { ...exits[i], triggerR: Number(e.target.value) }
                                    setTrailing(p => ({ ...p, partialExits: exits }))
                                  }}
                                  style={{ ...inputStyle, width: 60 }} />
                              </td>
                              <td style={tdStyle}>
                                <input type="number" step={1} min={1} max={100} value={pe.percentToClose}
                                  onChange={e => {
                                    const exits = [...trailing.partialExits]
                                    exits[i] = { ...exits[i], percentToClose: Number(e.target.value) }
                                    setTrailing(p => ({ ...p, partialExits: exits }))
                                  }}
                                  style={{ ...inputStyle, width: 60 }} />
                              </td>
                              <td style={tdStyle}>
                                <select value={pe.moveStopTo}
                                  onChange={e => {
                                    const exits = [...trailing.partialExits]
                                    exits[i] = { ...exits[i], moveStopTo: e.target.value as MoveStopTo }
                                    setTrailing(p => ({ ...p, partialExits: exits }))
                                  }}
                                  style={inputStyle}>
                                  {moveStopOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
                                </select>
                              </td>
                              <td style={tdStyle}>
                                <button onClick={() => setTrailing(p => ({ ...p, partialExits: p.partialExits.filter((_, j) => j !== i) }))}
                                  style={{ background: 'none', border: 'none', color: C.textMuted, cursor: 'pointer' }}>×</button>
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    )}
                    <button
                      onClick={() => setTrailing(p => ({
                        ...p,
                        partialExits: [...p.partialExits, { triggerR: 1, percentToClose: 50, moveStopTo: MoveStopTo.Breakeven }],
                      }))}
                      style={{ ...addBtnStyle, marginTop: SP.xs }}
                    >
                      + Add partial exit
                    </button>
                  </div>
                </div>
              )}
            </Block>
          </div>
        </div>
      )}

      {/* Page footer */}
      <div style={{
        display: 'flex', gap: SP.sm, justifyContent: 'flex-end',
        paddingTop: SP.lg, marginTop: SP.lg, borderTop: `1px solid ${C.border}`,
      }}>
        <button onClick={() => navigate('/strategies')} style={cancelBtnStyle}>Cancel</button>
        <button onClick={() => save(false)} disabled={isPending} style={secondaryBtnStyle}>
          {isPending ? '…' : 'Save Strategy'}
        </button>
        <button onClick={() => save(true)} disabled={isPending} style={primaryBtnStyle}>
          {isPending ? '…' : 'Save & Go to Scenarios'}
        </button>
      </div>

      {indicatorModalOpen && (
        <IndicatorModal
          indicatorId={editingIndicatorId}
          strategyIndicators={indicators}
          onSave={saveIndicator}
          onClose={() => { setIndicatorModalOpen(false); setEditingIndicatorId(undefined) }}
        />
      )}
    </div>
  )
}

function Block({ title, children, error }: { title: string; children: React.ReactNode; error?: string }) {
  return (
    <div style={{
      background: C.surface, border: `1px solid ${error ? C.red + '44' : C.border}`,
      borderRadius: 6, padding: '12px 14px', marginBottom: SP.sm,
    }}>
      <div style={{ fontSize: 12, fontWeight: 700, color: C.textSub, marginBottom: SP.sm }}>{title}</div>
      <div style={{ display: 'flex', flexDirection: 'column', gap: SP.sm }}>{children}</div>
      {error && <div style={{ fontSize: 10, color: C.red, marginTop: 4 }}>{error}</div>}
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
  color: C.text, borderRadius: 4, padding: '6px 8px', fontSize: 12, boxSizing: 'border-box',
}

const thStyle: React.CSSProperties = {
  padding: '5px 8px', textAlign: 'left', fontSize: 10, color: C.textMuted, fontWeight: 600,
}

const tdStyle: React.CSSProperties = { padding: '5px 8px', fontSize: 11 }

const checkLabel: React.CSSProperties = {
  display: 'flex', alignItems: 'center', gap: SP.xs, cursor: 'pointer',
  fontSize: 12, color: C.text,
}

const addBtnStyle: React.CSSProperties = {
  background: 'none', border: `1px dashed ${C.border3}`, color: C.textSub,
  borderRadius: 4, padding: '4px 12px', cursor: 'pointer', fontSize: 11,
}

const smallBtnStyle: React.CSSProperties = {
  background: C.surface2, border: `1px solid ${C.border}`, color: C.textSub,
  borderRadius: 3, padding: '2px 7px', cursor: 'pointer', fontSize: 10,
}

const primaryBtnStyle: React.CSSProperties = {
  background: '#1e3a5f', color: '#93c5fd', border: '1px solid #2563eb44',
  borderRadius: 5, padding: '7px 18px', cursor: 'pointer', fontSize: 12, fontWeight: 700,
}

const secondaryBtnStyle: React.CSSProperties = {
  background: C.surface2, color: C.textSub, border: `1px solid ${C.border}`,
  borderRadius: 5, padding: '7px 18px', cursor: 'pointer', fontSize: 12,
}

const cancelBtnStyle: React.CSSProperties = {
  background: 'none', color: C.textMuted, border: `1px solid ${C.border}`,
  borderRadius: 5, padding: '7px 18px', cursor: 'pointer', fontSize: 12,
}
