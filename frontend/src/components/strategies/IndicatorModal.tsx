import { useState } from 'react'
import { C, SP } from '../../styles/tokens'
import {
  IndicatorConfig, IndicatorRole, IndicatorType,
  ParamRange, SignalLayer, Timeframe,
} from '../../types/strategy'
import { useEnums } from '../../context/EnumsContext'

interface Props {
  indicatorId?: string
  strategyIndicators: IndicatorConfig[]
  primaryTimeframe?: Timeframe   // inherit default from strategy Basic Details
  /** Pre-select this layer when opening the modal from a layer's own + button */
  defaultLayer?: SignalLayer
  onSave: (i: IndicatorConfig) => void
  onClose: () => void
}

let _indicatorId = 0
function newId() { return `ind-${Date.now()}-${_indicatorId++}` }

// Context-backed indicator types — values read from StrategyContext (option chain / IV rank).
// No user-configurable parameters; skip Step 2 entirely.
const CONTEXT_BACKED_TYPES = new Set<IndicatorType>([
  IndicatorType.PCR,
  IndicatorType.PCRChange,
  IndicatorType.AtmIV,
  IndicatorType.MaxPain,
  IndicatorType.IVRank,
  IndicatorType.IVPercentile,
])

const CONTEXT_BACKED_LABELS: Partial<Record<IndicatorType, string>> = {
  [IndicatorType.PCR]:          'Put/Call Ratio (OI) — reads live option chain',
  [IndicatorType.PCRChange]:    'PCR Change (OI delta) — reads live option chain',
  [IndicatorType.AtmIV]:        'ATM Implied Volatility — reads live option chain',
  [IndicatorType.MaxPain]:      'Max Pain Strike — reads live option chain',
  [IndicatorType.IVRank]:       'IV Rank (0–100) — reads IV history',
  [IndicatorType.IVPercentile]: 'IV Percentile (0–100) — reads IV history',
}

// Default parameters for every registered IndicatorType
const DEFAULT_PARAMS: Record<string, Record<string, number>> = {
  // Trend
  EMA:              { period: 20 },
  SMA:              { period: 20 },
  HullMA:           { period: 20 },
  DonchianChannel:  { period: 20 },
  SuperTrend:       { period: 10, multiplier: 3 },
  // Momentum
  RSI:              { period: 14 },
  CCI:              { period: 20 },
  Stochastics:      { kPeriod: 14, dPeriod: 3 },
  MACD:             { fast: 12, slow: 26, signal: 9 },
  // Volatility
  ATR:              { period: 14 },
  BollingerBands:   { period: 20, stdDev: 2 },
  RangePercentile:  { period: 20 },
  ATRPercentile:    { period: 20, lookback: 100 },
  // Volume
  Volume:           { maPeriod: 20 },
  VolumeSpike:      { threshold: 2, maPeriod: 20 },
  VWAP:             { bands: 1 },
  // Price Action
  SwingHighLow:     { lookback: 20, swing: 3 },
  InsideBar:        { lookback: 5 },
  Engulfing:        { lookback: 5 },
  PrevHighLowBreak: { lookback: 20 },
  // Session / Time
  SessionFilter:    { startHour: 9, startMinute: 15, endHour: 15, endMinute: 20 },
  DayOfWeekFilter:  { mon: 1, tue: 1, wed: 1, thu: 1, fri: 1 },
  // Regime
  ADX:              { period: 14, threshold: 20 },
}

export function IndicatorModal({ indicatorId, strategyIndicators, primaryTimeframe, defaultLayer, onSave, onClose }: Props) {
  const existing = strategyIndicators.find(i => i.id === indicatorId)
  const { enums } = useEnums()

  const [step, setStep] = useState(1)
  const [type, setType] = useState<IndicatorType>(existing?.type ?? IndicatorType.EMA)
  // Default: inherit strategy primary timeframe (user can override)
  const [timeframe, setTimeframe] = useState<Timeframe>(
    existing?.timeframe ?? primaryTimeframe ?? Timeframe.D1
  )
  const [inheritTf, setInheritTf] = useState<boolean>(
    !existing && !!primaryTimeframe   // auto-inherit when adding new indicator
  )
  const [role, setRole] = useState<IndicatorRole>(existing?.role ?? IndicatorRole.EntryTrigger)
  const [baseParams, setBaseParams] = useState<Record<string, number | string | boolean>>(
    existing?.baseParams ?? (DEFAULT_PARAMS[type] ?? { period: 14 })
  )
  const [allowedRanges, setAllowedRanges] = useState<Record<string, ParamRange | string[]>>(
    existing?.allowedParamRanges ?? {}
  )
  // Pre-select layer from: (1) existing indicator's layer, (2) defaultLayer prop (from layer's + button), (3) PrimarySignal
  const [signalLayer, setSignalLayer] = useState<SignalLayer>(
    existing?.signalLayer ?? defaultLayer ?? SignalLayer.PrimarySignal
  )
  const [layerOrder, setLayerOrder] = useState<number>(existing?.layerOrder ?? 0)

  const tfOptions = enums.timeframe ?? []
  const typeOptions = enums.indicatorType ?? []
  const roleOptions = enums.indicatorRole ?? []

  const isContextBacked = CONTEXT_BACKED_TYPES.has(type)

  function handleTypeChange(t: IndicatorType) {
    setType(t)
    setBaseParams(CONTEXT_BACKED_TYPES.has(t) ? {} : (DEFAULT_PARAMS[t] ?? { period: 14 }))
    setAllowedRanges({})
  }

  function save() {
    onSave({
      id: existing?.id ?? newId(),
      type,
      timeframe,
      role,
      baseParams,
      allowedParamRanges: allowedRanges,
      signalLayer,
      layerOrder,
    })
    onClose()
  }

  const numericParamKeys = Object.entries(baseParams)
    .filter(([, v]) => typeof v === 'number')
    .map(([k]) => k)

  return (
    <div style={{
      position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.65)', zIndex: 200,
      display: 'flex', alignItems: 'center', justifyContent: 'center',
    }}>
      <div style={{
        background: C.surface, border: `1px solid ${C.border}`, borderRadius: 8,
        width: 480, maxHeight: '80vh', overflow: 'auto', padding: 20,
      }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: SP.md }}>
          <span style={{ fontWeight: 700, fontSize: 14 }}>
            {existing ? 'Edit' : 'Add'} Indicator — Step {step} of {isContextBacked ? 1 : 2}
          </span>
          <button onClick={onClose} style={{ background: 'none', border: 'none', color: C.textMuted, cursor: 'pointer', fontSize: 18 }}>×</button>
        </div>

        {step === 1 && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: SP.md }}>
            <Field label="Type *">
              <select value={type} onChange={e => handleTypeChange(e.target.value as IndicatorType)} style={selectStyle}>
                {typeOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
              </select>
            </Field>
            <Field label="Timeframe *">
              {primaryTimeframe && (
                <label style={{ display: 'flex', alignItems: 'center', gap: 6, marginBottom: 6, cursor: 'pointer', fontSize: 11, color: C.textSub }}>
                  <input
                    type="checkbox"
                    checked={inheritTf}
                    onChange={e => {
                      setInheritTf(e.target.checked)
                      if (e.target.checked) setTimeframe(primaryTimeframe)
                    }}
                  />
                  Inherit from strategy ({primaryTimeframe})
                </label>
              )}
              <select
                value={timeframe}
                disabled={inheritTf}
                onChange={e => { setTimeframe(e.target.value as Timeframe); setInheritTf(false) }}
                style={{ ...selectStyle, opacity: inheritTf ? 0.5 : 1 }}
              >
                {tfOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
              </select>
            </Field>
            <Field label="Role *">
              <select value={role} onChange={e => setRole(e.target.value as IndicatorRole)} style={selectStyle}>
                {roleOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
              </select>
            </Field>
            <Field label="Signal layer">
              <select value={signalLayer} onChange={e => setSignalLayer(e.target.value as SignalLayer)} style={selectStyle}>
                {Object.values(SignalLayer).map(v => <option key={v} value={v}>{v}</option>)}
              </select>
            </Field>
            <Field label="Layer order (within layer)">
              <input type="number" min={0}
                value={layerOrder}
                onChange={e => setLayerOrder(Number(e.target.value))}
                style={{ ...inputStyle, width: 80 }} />
            </Field>
            {isContextBacked && (
              <div style={{
                background: '#0d1f3c', border: '1px solid #2563eb44',
                borderRadius: 5, padding: '8px 12px', fontSize: 11, color: '#93c5fd',
                lineHeight: 1.5,
              }}>
                <strong>Context-backed indicator</strong><br />
                {CONTEXT_BACKED_LABELS[type]}<br />
                <span style={{ color: C.textMuted }}>
                  No parameters needed — data is pre-fetched per bar from the live/historical
                  option chain snapshot before strategy evaluation.
                </span>
              </div>
            )}
            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: SP.sm, marginTop: SP.sm }}>
              <button onClick={onClose} style={cancelBtnStyle}>Cancel</button>
              {isContextBacked
                ? <button onClick={save} style={primaryBtnStyle}>Save Indicator</button>
                : <button onClick={() => setStep(2)} style={primaryBtnStyle}>Next →</button>
              }
            </div>
          </div>
        )}

        {step === 2 && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: SP.md }}>
            <div>
              <div style={{ fontSize: 12, fontWeight: 600, color: C.textSub, marginBottom: SP.sm }}>Base Parameters</div>
              {Object.entries(baseParams).map(([key, val]) => (
                <Field key={key} label={key}>
                  <input
                    type="number"
                    step={0.01}
                    value={typeof val === 'number' ? val : 0}
                    onChange={e => setBaseParams(p => ({ ...p, [key]: Number(e.target.value) }))}
                    style={inputStyle}
                  />
                </Field>
              ))}
            </div>

            {numericParamKeys.length > 0 && (
              <div>
                <div style={{ fontSize: 12, fontWeight: 600, color: C.textSub, marginBottom: SP.sm }}>Allowed Ranges (for Scenario overrides)</div>
                {numericParamKeys.map(key => {
                  const r = (allowedRanges[key] as ParamRange | undefined) ?? { min: 1, max: 200 }
                  return (
                    <div key={key} style={{ display: 'flex', gap: SP.sm, alignItems: 'center', marginBottom: 6 }}>
                      <span style={{ fontSize: 11, color: C.textMuted, width: 80 }}>{key}</span>
                      <input
                        type="number" step={0.01} placeholder="min"
                        value={r.min}
                        onChange={e => setAllowedRanges(prev => ({ ...prev, [key]: { ...(prev[key] as ParamRange ?? { min: 1, max: 200 }), min: Number(e.target.value) } }))}
                        style={{ ...inputStyle, width: 70 }}
                      />
                      <span style={{ fontSize: 11, color: C.textMuted }}>–</span>
                      <input
                        type="number" step={0.01} placeholder="max"
                        value={r.max}
                        onChange={e => setAllowedRanges(prev => ({ ...prev, [key]: { ...(prev[key] as ParamRange ?? { min: 1, max: 200 }), max: Number(e.target.value) } }))}
                        style={{ ...inputStyle, width: 70 }}
                      />
                    </div>
                  )
                })}
              </div>
            )}

            <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: SP.sm }}>
              <button onClick={() => setStep(1)} style={cancelBtnStyle}>← Back</button>
              <div style={{ display: 'flex', gap: SP.sm }}>
                <button onClick={onClose} style={cancelBtnStyle}>Cancel</button>
                <button onClick={save} style={primaryBtnStyle}>Save Indicator</button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <label style={{ fontSize: 11, color: C.textMuted, display: 'block', marginBottom: 3 }}>{label}</label>
      {children}
    </div>
  )
}

const selectStyle: React.CSSProperties = {
  width: '100%', background: C.surface3, border: `1px solid ${C.border}`,
  color: C.text, borderRadius: 4, padding: '6px 8px', fontSize: 12,
}

const inputStyle: React.CSSProperties = {
  background: C.surface3, border: `1px solid ${C.border}`,
  color: C.text, borderRadius: 4, padding: '6px 8px', fontSize: 12,
  width: '100%', boxSizing: 'border-box',
}

const primaryBtnStyle: React.CSSProperties = {
  background: '#1e3a5f', color: '#93c5fd', border: '1px solid #2563eb44',
  borderRadius: 5, padding: '6px 16px', cursor: 'pointer', fontSize: 12, fontWeight: 700,
}

const cancelBtnStyle: React.CSSProperties = {
  background: C.surface2, color: C.textSub, border: `1px solid ${C.border}`,
  borderRadius: 5, padding: '6px 16px', cursor: 'pointer', fontSize: 12,
}
