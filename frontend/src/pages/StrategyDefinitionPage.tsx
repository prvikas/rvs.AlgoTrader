import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { strategyDomainApi } from '../api/client'
import {
  DayOfWeek, ExitCombineLogic, IndicatorConfig,
  KillSwitch, MoveStopTo, RRConfig, RiskControls,
  StartCondition, StopTargetConfig, StopType, Strategy, StrategyStatus,
  Timeframe, TrailingConfig, TrailingType, TradingStyle,
  ExitBehaviour, ProfitBookingRule,
  // PROMPT-002
  SignalLayer, EntryExecutionModel, EntryOrderType, EntryTiming, ScalingModel,
  SizingMethod, PositionSizingModel,
  StopState, StopStateMachine,
} from '../types/strategy'
import { C, SP } from '../styles/tokens'
import { useEnums } from '../context/EnumsContext'
import { IndicatorModal } from '../components/strategies/IndicatorModal'
import { RuleGroupEditor } from '../components/strategies/RuleGroupEditor'
import { SymbolSearchInput } from '../components/Strategy/SymbolSearchInput'
import { HelpTooltip } from '../components/ui/HelpTooltip'

interface Props {
  strategyId?: string
  initialData?: Strategy
  onSaved?: (s: Strategy) => void
}

type SubTab = 'core' | 'rules' | 'risk'

let _groupId = 0
function newGroupId() { return `grp-${Date.now()}-${_groupId++}` }

// PROMPT-002: human-readable labels for SignalLayer enum values
const SIGNAL_LAYER_LABELS: Record<SignalLayer, string> = {
  [SignalLayer.HTFBias]:            'HTF Bias',
  [SignalLayer.MTFContext]:         'MTF Context',
  [SignalLayer.PrimarySignal]:      'Primary Signal',
  [SignalLayer.ConfirmationFilter]: 'Confirmation Filter',
  [SignalLayer.EntryTrigger]:       'Entry Trigger',
  [SignalLayer.Invalidation]:       'Invalidation',
}

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
  // PROMPT-002
  const entryOrderOptions = enums.entryOrderType ?? []
  const entryTimingOptions = enums.entryTiming ?? []
  const scalingModelOptions = enums.scalingModel ?? []
  const sizingMethodOptions = enums.sizingMethod ?? []

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

  const [stopLoss] = useState<StopTargetConfig>(initialData?.stopLoss ?? {
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

  // PROMPT-002 state
  const [entryExecution, setEntryExecution] = useState<EntryExecutionModel>(
    initialData?.entryExecution ?? {
      orderType: EntryOrderType.MarketNextOpen,
      timing: EntryTiming.WaitForCandleClose,
      scalingModel: ScalingModel.FullImmediately,
    }
  )
  const [positionSizing, setPositionSizing] = useState<PositionSizingModel>(
    initialData?.positionSizing ?? {
      method: SizingMethod.FixedRiskPct,
      baseRiskPct: 1,
      maxPositionPct: 10,
      drawdownScaling: { enabled: false, reduceSizeAtDrawdownPct: 10, minSizeMultiplier: 0.5 },
    }
  )
  const [stopStateMachine, setStopStateMachine] = useState<StopStateMachine>(
    initialData?.stopStateMachine ?? {
      states: [{
        id: 'state-initial', label: 'Initial',
        stopType: StopType.ATRMultiple, value: 2,
      }],
      allowedRanges: {},
    }
  )
  const [profitBooking, setProfitBooking] = useState<ProfitBookingRule>(
    initialData?.profitBooking ?? {
      enabled: false, triggerR: 1, bookPct: 50,
      continueTrailing: true, trailType: TrailingType.ATRMultiple, trailMultiplier: 1.5,
    }
  )

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
      entryExecution,
      positionSizing,
      stopStateMachine,
      profitBooking: profitBooking.enabled ? profitBooking : undefined,
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
              <Field label="Strategy Name *" error={errors.name} help="A unique name for this strategy. Used in reports, logs, and comparison views.">
                <input value={name} onChange={e => setName(e.target.value)} style={inputStyle} />
              </Field>
              <Field label="Primary Timeframe *" help="The main candle resolution this strategy runs on. Indicators inherit this by default but can be overridden per indicator.">
                <select value={primaryTf} onChange={e => setPrimaryTf(e.target.value as Timeframe)} style={inputStyle}>
                  {tfOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
                </select>
              </Field>
              <Field label="Instruments *" help="Tradable symbols this strategy applies to. Search by name or trading symbol (e.g. BANKNIFTY). You can add multiple.">
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
                {/* Live search using SymbolSearchInput — clears itself after selection */}
                <SymbolSearchInput
                  value={instrumentInput}
                  onChange={sym => {
                    if (sym && !instruments.includes(sym)) {
                      setInstruments(prev => [...prev, sym])
                      setInstrumentInput('')
                    }
                  }}
                  placeholder="Search symbol e.g. BANKNIFTY…"
                />
                <div style={{ fontSize: 10, color: C.textDim, marginTop: 4 }}>
                  Select from search results to add. Manual entry: type exact symbol and press Enter.
                </div>
                {/* Fallback manual add for exact symbols */}
                <div style={{ display: 'flex', gap: SP.xs, marginTop: 4 }}>
                  <input
                    value={instrumentInput}
                    onChange={e => setInstrumentInput(e.target.value)}
                    onKeyDown={e => e.key === 'Enter' && addInstrument()}
                    placeholder="or type exact symbol + Enter"
                    style={{ ...inputStyle, flex: 1, fontSize: 11 }}
                  />
                  <button onClick={addInstrument} style={addBtnStyle}>Add</button>
                </div>
              </Field>
              <Field label="Trading Style *" error={errors.tradingStyle} help="Determines default session windows and holding period. Scalping: seconds–minutes. Intraday: within session. Swing: multi-day. Positional: weeks+.">
                <select value={tradingStyle} onChange={e => setTradingStyle(e.target.value as TradingStyle)} style={inputStyle}>
                  {styleOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
                </select>
              </Field>
              <Field label="Description" help="Optional free-text notes about this strategy's thesis, market context, or design rationale.">
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
              <Field label="Combine rules with" help="AND: all exit conditions must be true simultaneously. OR: any single condition triggers exit.">
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
            {/* Signal Layer Card Groups (PROMPT-002) */}
            <Block title="Signal Layer Chain">
              <div style={{ fontSize: 10, color: C.textMuted, marginBottom: SP.sm }}>
                Layers evaluate top → bottom. If a higher layer fails, downstream layers are not evaluated.
              </div>
              {Object.values(SignalLayer).map(layer => {
                const layerInds = indicators
                  .filter(i => i.signalLayer === layer)
                  .sort((a, b) => a.layerOrder - b.layerOrder)
                return (
                  <div key={layer} style={{
                    marginBottom: 8, borderRadius: 5,
                    border: `1px solid ${C.border}`, overflow: 'hidden',
                  }}>
                    {/* Layer header */}
                    <div style={{
                      padding: '5px 10px',
                      background: C.surface2,
                      display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                    }}>
                      <span style={{ fontSize: 11, fontWeight: 700, color: C.textSub }}>
                        {SIGNAL_LAYER_LABELS[layer]}
                      </span>
                      <span style={{ fontSize: 10, color: C.textDim }}>
                        {layerInds.length} indicator{layerInds.length !== 1 ? 's' : ''}
                      </span>
                    </div>
                    {/* Layer rows */}
                    {layerInds.length > 0 && (
                      <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 11 }}>
                        <tbody>
                          {layerInds.map(ind => (
                            <tr key={ind.id} style={{ borderBottom: `1px solid ${C.border2}` }}>
                              <td style={{ ...tdStyle, fontWeight: 600 }}>{ind.type}</td>
                              <td style={{ ...tdStyle, color: C.textMuted }}>{ind.timeframe}</td>
                              <td style={{ ...tdStyle, color: C.textMuted }}>{ind.role}</td>
                              <td style={{ ...tdStyle, fontSize: 10, color: C.textDim }}>
                                {Object.entries(ind.baseParams).map(([k, v]) => `${k}:${v}`).join(', ')}
                              </td>
                              <td style={{ ...tdStyle, width: 60 }}>
                                <div style={{ display: 'flex', gap: 4 }}>
                                  <button onClick={() => { setEditingIndicatorId(ind.id); setIndicatorModalOpen(true) }} style={smallBtnStyle}>Edit</button>
                                  <button onClick={() => setIndicators(prev => prev.filter(i => i.id !== ind.id))} style={{ ...smallBtnStyle, color: C.red }}>×</button>
                                </div>
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    )}
                    {layerInds.length === 0 && (
                      <div style={{ padding: '6px 10px', fontSize: 10, color: C.textDim, fontStyle: 'italic' }}>
                        No indicators — drag here or use + Add Indicator
                      </div>
                    )}
                  </div>
                )
              })}
              <button onClick={() => { setEditingIndicatorId(undefined); setIndicatorModalOpen(true) }} style={addBtnStyle}>
                + Add Indicator
              </button>
            </Block>

            <Block title="Risk Controls">
              <Field label="Max risk per trade % *" error={errors.maxRisk} help="Maximum capital at risk on a single trade, expressed as % of total capital. E.g. 1% means you risk ₹1,000 on a ₹1,00,000 account. Determines position size when using FixedRiskPct sizing.">
                <input type="number" step={0.01} min={0.01} max={10}
                  value={riskControls.maxRiskPerTradePercent}
                  onChange={e => setRiskControls(p => ({ ...p, maxRiskPerTradePercent: Number(e.target.value) }))}
                  style={{ ...inputStyle, width: 120 }} />
              </Field>
              <Field label="Max trades per day *" error={errors.maxTrades} help="Kill-switch guard: stops new entries once this count is reached for the day, regardless of signals. Resets at midnight.">
                <input type="number" min={1}
                  value={riskControls.maxTradesPerDay}
                  onChange={e => setRiskControls(p => ({ ...p, maxTradesPerDay: Number(e.target.value) }))}
                  style={{ ...inputStyle, width: 120 }} />
              </Field>
            </Block>

            {/* Entry Execution (PROMPT-002) */}
            <Block title="Entry Execution">
              <Field label="Order type" help="MarketNextOpen: enter at next bar's open — zero look-ahead. LimitAtClose: place limit at current bar's close price. LimitAtRetest: wait for price to retest a key level. StopEntryOffset: enter above/below current price with an offset — used for breakouts.">
                <select
                  value={entryExecution.orderType}
                  onChange={e => setEntryExecution(p => ({ ...p, orderType: e.target.value as EntryOrderType }))}
                  style={inputStyle}
                >
                  {entryOrderOptions.length > 0
                    ? entryOrderOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)
                    : Object.values(EntryOrderType).map(v => <option key={v} value={v}>{v}</option>)
                  }
                </select>
              </Field>
              <Field label="Timing" help="Immediately: enter as soon as signal fires. WaitForCandleClose: only enter once the signal bar is fully closed (avoids false signals mid-bar). FirstNBarsOnly: restrict entries to the opening N bars of the session.">
                <select
                  value={entryExecution.timing}
                  onChange={e => setEntryExecution(p => ({ ...p, timing: e.target.value as EntryTiming }))}
                  style={inputStyle}
                >
                  {entryTimingOptions.length > 0
                    ? entryTimingOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)
                    : Object.values(EntryTiming).map(v => <option key={v} value={v}>{v}</option>)
                  }
                </select>
              </Field>
              {entryExecution.timing === EntryTiming.FirstNBarsOnly && (
                <Field label="First N bars of session" help="Only allow entries in the first N candles after session open. Useful for opening-range strategies.">
                  <input type="number" min={1}
                    value={entryExecution.firstNBars ?? 1}
                    onChange={e => setEntryExecution(p => ({ ...p, firstNBars: Number(e.target.value) }))}
                    style={{ ...inputStyle, width: 100 }} />
                </Field>
              )}
              <Field label="Max slippage points (optional)" help="If actual fill price deviates more than this from the signal price, the order is cancelled. Leave blank to allow any slippage.">
                <input type="number" step={0.01} min={0}
                  value={entryExecution.maxSlippagePoints ?? ''}
                  onChange={e => setEntryExecution(p => ({
                    ...p, maxSlippagePoints: e.target.value ? Number(e.target.value) : undefined,
                  }))}
                  style={{ ...inputStyle, width: 120 }} />
              </Field>
              <Field label="Scaling model" help="FullImmediately: entire position at once. ScaleIn2: split into 2 equal tranches. Pyramid: add size as price moves in your favour — each add requires a new signal.">
                <select
                  value={entryExecution.scalingModel}
                  onChange={e => setEntryExecution(p => ({ ...p, scalingModel: e.target.value as ScalingModel }))}
                  style={inputStyle}
                >
                  {scalingModelOptions.length > 0
                    ? scalingModelOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)
                    : Object.values(ScalingModel).map(v => <option key={v} value={v}>{v}</option>)
                  }
                </select>
              </Field>
              {entryExecution.scalingModel === ScalingModel.Pyramid && (
                <Field label="Pyramid interval (R)">
                  <input type="number" step={0.1} min={0.1}
                    value={entryExecution.pyramidIntervalR ?? 1}
                    onChange={e => setEntryExecution(p => ({ ...p, pyramidIntervalR: Number(e.target.value) }))}
                    style={{ ...inputStyle, width: 100 }} />
                </Field>
              )}
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
            { key: 'longEntry' as const, label: 'Long Entry', block: longEntry, set: setLongEntry, isEntry: true },
            { key: 'shortEntry' as const, label: 'Short Entry', block: shortEntry, set: setShortEntry, isEntry: true },
            { key: 'longExit' as const, label: 'Long Exit', block: longExit, set: setLongExit, isEntry: false },
            { key: 'shortExit' as const, label: 'Short Exit', block: shortExit, set: setShortExit, isEntry: false },
          ].map(({ key, label, block, set, isEntry }) => (
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

              {/* Signal Waterfall Panel (PROMPT-002) — entry blocks only */}
              {isEntry && indicators.length > 0 && (
                <SignalWaterfallPanel indicators={indicators} />
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
            {/* Position Sizing (PROMPT-002) */}
            <Block title="Position Sizing">
              <Field label="Sizing method" help="FixedRiskPct: position sized so stop loss = baseRiskPct of capital. KellyFraction: optimal fraction from win-rate and R:R. VolatilityNorm: adjusts size inversely to recent ATR. FixedLots / FixedCapital: constant size regardless of stop distance.">
                <select value={positionSizing.method}
                  onChange={e => setPositionSizing(p => ({ ...p, method: e.target.value as SizingMethod }))}
                  style={inputStyle}>
                  {sizingMethodOptions.length > 0
                    ? sizingMethodOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)
                    : Object.values(SizingMethod).map(v => <option key={v} value={v}>{v}</option>)
                  }
                </select>
              </Field>
              {positionSizing.method === SizingMethod.FixedRiskPct && (
                <Field label="Base risk % per trade" help="The percentage of total account capital risked on each trade. Position size = (capital × riskPct) / (entry − stop).">
                  <input type="number" step={0.1} min={0.1}
                    value={positionSizing.baseRiskPct ?? 1}
                    onChange={e => setPositionSizing(p => ({ ...p, baseRiskPct: Number(e.target.value) }))}
                    style={{ ...inputStyle, width: 120 }} />
                </Field>
              )}
              {positionSizing.method === SizingMethod.KellyFraction && (
                <Field label="Kelly fraction (0–1)" help="Fraction of full Kelly to bet. Full Kelly (1.0) maximises long-run growth but causes extreme drawdowns. Quarter Kelly (0.25) is standard for systematic strategies.">
                  <input type="number" step={0.05} min={0.05} max={1}
                    value={positionSizing.kellyFraction ?? 0.25}
                    onChange={e => setPositionSizing(p => ({ ...p, kellyFraction: Number(e.target.value) }))}
                    style={{ ...inputStyle, width: 120 }} />
                </Field>
              )}
              {positionSizing.method === SizingMethod.FixedLots && (
                <Field label="Fixed lots">
                  <input type="number" min={1}
                    value={positionSizing.fixedLots ?? 1}
                    onChange={e => setPositionSizing(p => ({ ...p, fixedLots: Number(e.target.value) }))}
                    style={{ ...inputStyle, width: 120 }} />
                </Field>
              )}
              {positionSizing.method === SizingMethod.FixedCapital && (
                <Field label="Fixed capital (₹)">
                  <input type="number" min={1000}
                    value={positionSizing.fixedCapital ?? 50000}
                    onChange={e => setPositionSizing(p => ({ ...p, fixedCapital: Number(e.target.value) }))}
                    style={{ ...inputStyle, width: 120 }} />
                </Field>
              )}
              <Field label="Max position size % of capital">
                <input type="number" step={1} min={1} max={100}
                  value={positionSizing.maxPositionPct}
                  onChange={e => setPositionSizing(p => ({ ...p, maxPositionPct: Number(e.target.value) }))}
                  style={{ ...inputStyle, width: 120 }} />
              </Field>
              <Field label="Liquidity cap % ADV (optional)">
                <input type="number" step={0.5} min={0}
                  value={positionSizing.liquidityCapPctADV ?? ''}
                  onChange={e => setPositionSizing(p => ({
                    ...p, liquidityCapPctADV: e.target.value ? Number(e.target.value) : undefined,
                  }))}
                  style={{ ...inputStyle, width: 120 }} />
              </Field>
              <label style={checkLabel}>
                <input type="checkbox"
                  checked={positionSizing.drawdownScaling.enabled}
                  onChange={e => setPositionSizing(p => ({ ...p, drawdownScaling: { ...p.drawdownScaling, enabled: e.target.checked } }))} />
                Drawdown scaling
              </label>
              {positionSizing.drawdownScaling.enabled && (
                <div style={{ paddingLeft: 16, display: 'flex', flexDirection: 'column', gap: SP.xs }}>
                  <Field label="Reduce size when DD >%" help="When the account drawdown from peak exceeds this %, position sizes are multiplied by minSizeMultiplier. Automatically recovers as drawdown shrinks.">
                    <input type="number" step={1} min={1}
                      value={positionSizing.drawdownScaling.reduceSizeAtDrawdownPct}
                      onChange={e => setPositionSizing(p => ({ ...p, drawdownScaling: { ...p.drawdownScaling, reduceSizeAtDrawdownPct: Number(e.target.value) } }))}
                      style={{ ...inputStyle, width: 100 }} />
                  </Field>
                  <Field label="Min size multiplier (0–1)">
                    <input type="number" step={0.05} min={0.1} max={1}
                      value={positionSizing.drawdownScaling.minSizeMultiplier}
                      onChange={e => setPositionSizing(p => ({ ...p, drawdownScaling: { ...p.drawdownScaling, minSizeMultiplier: Number(e.target.value) } }))}
                      style={{ ...inputStyle, width: 100 }} />
                  </Field>
                </div>
              )}
            </Block>

            {/* Stop State Machine (PROMPT-002) — replaces simple Stop Loss block */}
            <Block title="Stop State Machine">
              <div style={{ fontSize: 10, color: C.textMuted, marginBottom: SP.sm }}>
                States activate in sequence. State 1 (Initial) is always active from entry.
                Each subsequent state fires when its activation condition is met.
              </div>
              {stopStateMachine.states.map((state, idx) => (
                <div key={state.id} style={{
                  border: `1px solid ${C.border}`, borderRadius: 5, padding: '8px 10px',
                  marginBottom: 6,
                  background: idx === 0 ? C.surface2 : C.surface,
                  position: 'relative',
                }}>
                  {/* State header */}
                  <div style={{ display: 'flex', gap: SP.sm, alignItems: 'center', marginBottom: 6 }}>
                    <span style={{
                      fontSize: 10, fontWeight: 700, color: idx === 0 ? C.blue : C.textDim,
                      background: idx === 0 ? '#1e3a5f' : C.surface3,
                      borderRadius: 3, padding: '2px 6px', minWidth: 24, textAlign: 'center',
                    }}>#{idx + 1}</span>
                    <input
                      value={state.label}
                      readOnly={idx === 0}
                      onChange={e => {
                        const next = [...stopStateMachine.states]
                        next[idx] = { ...next[idx], label: e.target.value }
                        setStopStateMachine(p => ({ ...p, states: next }))
                      }}
                      style={{ ...inputStyle, flex: 1, fontWeight: 600, background: idx === 0 ? 'transparent' : undefined }}
                    />
                    {idx > 0 && (
                      <button
                        onClick={() => setStopStateMachine(p => ({ ...p, states: p.states.filter((_, i) => i !== idx) }))}
                        style={{ background: 'none', border: 'none', color: C.textMuted, cursor: 'pointer', fontSize: 14, lineHeight: 1 }}>
                        ×
                      </button>
                    )}
                  </div>
                  {/* Stop type + value */}
                  <div style={{ display: 'grid', gridTemplateColumns: '1fr 80px', gap: SP.xs }}>
                    <Field label="Stop type">
                      <select value={state.stopType}
                        onChange={e => {
                          const next = [...stopStateMachine.states]
                          next[idx] = { ...next[idx], stopType: e.target.value as StopType }
                          setStopStateMachine(p => ({ ...p, states: next }))
                        }}
                        style={inputStyle}>
                        {stopOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
                      </select>
                    </Field>
                    <Field label="Value">
                      <input type="number" step={0.1}
                        value={state.value}
                        onChange={e => {
                          const next = [...stopStateMachine.states]
                          next[idx] = { ...next[idx], value: Number(e.target.value) }
                          setStopStateMachine(p => ({ ...p, states: next }))
                        }}
                        style={inputStyle} />
                    </Field>
                  </div>
                  {/* Activation condition (only for non-initial states) */}
                  {idx > 0 && (
                    <div style={{ marginTop: SP.xs }}>
                      <div style={{ fontSize: 10, color: C.textMuted, marginBottom: 4 }}>
                        Activation condition (first met triggers this state)
                      </div>
                      <div style={{ display: 'flex', gap: SP.xs, flexWrap: 'wrap', alignItems: 'center' }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
                          <span style={{ fontSize: 10, color: C.textDim }}>R≥</span>
                          <input type="number" step={0.1} min={0}
                            placeholder="e.g. 1"
                            value={state.activationCondition?.triggerR ?? ''}
                            onChange={e => {
                              const next = [...stopStateMachine.states]
                              next[idx] = { ...next[idx], activationCondition: { ...next[idx].activationCondition, triggerR: e.target.value ? Number(e.target.value) : undefined } }
                              setStopStateMachine(p => ({ ...p, states: next }))
                            }}
                            style={{ ...inputStyle, width: 65 }} />
                        </div>
                        <span style={{ fontSize: 10, color: C.textDim }}>or</span>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
                          <span style={{ fontSize: 10, color: C.textDim }}>bars≥</span>
                          <input type="number" min={0}
                            placeholder="e.g. 5"
                            value={state.activationCondition?.triggerBars ?? ''}
                            onChange={e => {
                              const next = [...stopStateMachine.states]
                              next[idx] = { ...next[idx], activationCondition: { ...next[idx].activationCondition, triggerBars: e.target.value ? Number(e.target.value) : undefined } }
                              setStopStateMachine(p => ({ ...p, states: next }))
                            }}
                            style={{ ...inputStyle, width: 65 }} />
                        </div>
                      </div>
                      <Field label="On activation: move stop to">
                        <select value={state.moveStopTo ?? ''}
                          onChange={e => {
                            const next = [...stopStateMachine.states]
                            next[idx] = { ...next[idx], moveStopTo: e.target.value ? e.target.value as MoveStopTo : undefined }
                            setStopStateMachine(p => ({ ...p, states: next }))
                          }}
                          style={inputStyle}>
                          <option value="">— keep new stop type —</option>
                          {moveStopOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
                        </select>
                      </Field>
                    </div>
                  )}
                </div>
              ))}
              <button
                onClick={() => {
                  const newState: StopState = {
                    id: `state-${Date.now()}`,
                    label: `State ${stopStateMachine.states.length + 1}`,
                    stopType: StopType.ATRMultiple, value: 1.5,
                    activationCondition: { triggerR: 1 },
                  }
                  setStopStateMachine(p => ({ ...p, states: [...p.states, newState] }))
                }}
                style={addBtnStyle}
              >
                + Add Stop State
              </button>
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
                <Field label="rrMin" help="Minimum acceptable risk:reward ratio. Trades are skipped if the target implied by the stop gives less than this R.">
                  <input type="number" step={0.1} value={rrConfig.rrMin}
                    onChange={e => setRrConfig(p => ({ ...p, rrMin: Number(e.target.value) }))}
                    style={{ ...inputStyle, width: 80 }} />
                </Field>
                <Field label="rrMax" help="Maximum target R:R. The system will not set a profit target more than this multiple beyond the stop — prevents unrealistically wide targets.">
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

            {/* Trailing Profit Booking */}
            <Block title="Trailing Profit Booking">
              <div style={{ fontSize: 10, color: C.textMuted, marginBottom: SP.sm }}>
                Book a portion of the position at a profit target, then trail the remainder. Separates profit-taking from stop-loss management.
              </div>
              <label style={checkLabel}>
                <input type="checkbox"
                  checked={profitBooking.enabled}
                  onChange={e => setProfitBooking(p => ({ ...p, enabled: e.target.checked }))} />
                Enable profit booking
              </label>
              {profitBooking.enabled && (
                <div style={{ display: 'flex', flexDirection: 'column', gap: SP.sm, marginTop: SP.sm }}>
                  <div style={{ display: 'flex', gap: SP.sm }}>
                    <Field label="Book at profit (R)" help="Close the booking tranche when unrealised profit reaches this R multiple. E.g. 1 = close when profit equals initial risk.">
                      <input type="number" step={0.1} min={0.1}
                        value={profitBooking.triggerR}
                        onChange={e => setProfitBooking(p => ({ ...p, triggerR: Number(e.target.value) }))}
                        style={{ ...inputStyle, width: 90 }} />
                    </Field>
                    <Field label="% to close" help="What percentage of the open position to close at the trigger R level. Remaining position continues.">
                      <input type="number" step={5} min={5} max={95}
                        value={profitBooking.bookPct}
                        onChange={e => setProfitBooking(p => ({ ...p, bookPct: Number(e.target.value) }))}
                        style={{ ...inputStyle, width: 90 }} />
                    </Field>
                  </div>
                  <label style={checkLabel}>
                    <input type="checkbox"
                      checked={profitBooking.continueTrailing}
                      onChange={e => setProfitBooking(p => ({ ...p, continueTrailing: e.target.checked }))} />
                    Trail remainder after booking
                    <HelpTooltip text="After booking the specified %, the remaining position switches to a trailing stop. If disabled, the existing stop/target applies to the remainder." />
                  </label>
                  {profitBooking.continueTrailing && (
                    <>
                      <Field label="Trail type" help="Trailing method for the remainder of the position after profit booking.">
                        <select value={profitBooking.trailType ?? TrailingType.ATRMultiple}
                          onChange={e => setProfitBooking(p => ({ ...p, trailType: e.target.value as TrailingType }))}
                          style={inputStyle}>
                          {trailOptions.length > 0
                            ? trailOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)
                            : Object.values(TrailingType).map(v => <option key={v} value={v}>{v}</option>)
                          }
                        </select>
                      </Field>
                      <Field label="Trail multiplier / points" help="ATR multiple or fixed points for the trailing stop on the remaining position.">
                        <input type="number" step={0.1} min={0.1}
                          value={profitBooking.trailMultiplier ?? 1.5}
                          onChange={e => setProfitBooking(p => ({ ...p, trailMultiplier: Number(e.target.value) }))}
                          style={{ ...inputStyle, width: 100 }} />
                      </Field>
                    </>
                  )}
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
          primaryTimeframe={primaryTf}
          onSave={saveIndicator}
          onClose={() => { setIndicatorModalOpen(false); setEditingIndicatorId(undefined) }}
        />
      )}
    </div>
  )
}

// PROMPT-002: Signal Waterfall Panel — shows indicator layer chain for entry blocks
function SignalWaterfallPanel({ indicators }: { indicators: IndicatorConfig[] }) {
  const [open, setOpen] = useState(false)
  const hasAnyLayer = indicators.some(i => i.signalLayer)
  if (!hasAnyLayer) return null
  return (
    <div style={{ marginTop: SP.sm }}>
      <button
        onClick={() => setOpen(o => !o)}
        style={{
          width: '100%', background: C.surface2, border: `1px solid ${C.border}`,
          color: C.textSub, borderRadius: 4, padding: '5px 10px', cursor: 'pointer',
          fontSize: 11, textAlign: 'left', display: 'flex', alignItems: 'center', justifyContent: 'space-between',
        }}
      >
        <span>Signal Waterfall</span>
        <span style={{ fontSize: 10, color: C.textDim }}>{open ? '▲ hide' : '▼ show'}</span>
      </button>
      {open && (
        <div style={{
          border: `1px solid ${C.border}`, borderTop: 'none', borderRadius: '0 0 4px 4px',
          background: C.surface, padding: '8px 10px',
        }}>
          <div style={{ fontSize: 10, color: C.textMuted, marginBottom: 6 }}>
            Layers evaluate top → bottom. A failed layer blocks all layers below it.
          </div>
          {Object.values(SignalLayer).map((layer, layerIdx) => {
            const layerInds = indicators
              .filter(i => i.signalLayer === layer)
              .sort((a, b) => (a.layerOrder ?? 0) - (b.layerOrder ?? 0))
            const isBlocked = layerIdx > 0
            return (
              <div key={layer} style={{
                marginBottom: 4, borderLeft: `3px solid ${layerInds.length > 0 ? C.blue : C.border2}`,
                paddingLeft: 8,
              }}>
                <div style={{ fontSize: 10, fontWeight: 700, color: C.textMuted, marginBottom: 2 }}>
                  {layerIdx + 1}. {SIGNAL_LAYER_LABELS[layer]}
                </div>
                {layerInds.length === 0 ? (
                  <div style={{ fontSize: 10, color: C.textDim, fontStyle: 'italic', paddingLeft: 4 }}>
                    No indicators — layer skipped
                  </div>
                ) : layerInds.map(ind => (
                  <div key={ind.id} style={{
                    display: 'flex', alignItems: 'center', gap: 6,
                    padding: '2px 4px', fontSize: 11,
                  }}>
                    <span style={{
                      fontSize: 9, fontWeight: 700, color: isBlocked ? C.textDim : '#93c5fd',
                      background: isBlocked ? C.surface3 : '#1e3a5f',
                      borderRadius: 3, padding: '1px 5px', minWidth: 14, textAlign: 'center',
                    }}>—</span>
                    <span style={{ color: C.text }}>{ind.type}</span>
                    <span style={{ fontSize: 10, color: C.textMuted }}>{ind.timeframe}</span>
                    <span style={{ fontSize: 10, color: C.textDim }}>{ind.role}</span>
                  </div>
                ))}
              </div>
            )
          })}
        </div>
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

function Field({ label, error, help, children }: { label: string; error?: string; help?: string; children: React.ReactNode }) {
  return (
    <div>
      <label style={{ fontSize: 11, color: C.textMuted, display: 'flex', alignItems: 'center', marginBottom: 3 }}>
        {label}
        {help && <HelpTooltip text={help} />}
      </label>
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
