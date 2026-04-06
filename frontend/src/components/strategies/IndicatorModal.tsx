import { useState } from 'react'
import { C, SP } from '../../styles/tokens'
import {
  IndicatorConfig, IndicatorRole, IndicatorType,
  ParamRange, Timeframe,
} from '../../types/strategy'
import { useEnums } from '../../context/EnumsContext'

interface Props {
  indicatorId?: string
  strategyIndicators: IndicatorConfig[]
  onSave: (i: IndicatorConfig) => void
  onClose: () => void
}

let _indicatorId = 0
function newId() { return `ind-${Date.now()}-${_indicatorId++}` }

const DEFAULT_PARAMS: Record<string, Record<string, number>> = {
  EMA: { period: 20 },
  SMA: { period: 20 },
  HullMA: { period: 20 },
  RSI: { period: 14 },
  MACD: { fast: 12, slow: 26, signal: 9 },
  ATR: { period: 14 },
  BollingerBands: { period: 20, stdDev: 2 },
  CCI: { period: 20 },
  Stochastics: { kPeriod: 14, dPeriod: 3 },
  ADX: { period: 14 },
  DonchianChannel: { period: 20 },
  SuperTrend: { period: 10, multiplier: 3 },
  VolumeSpike: { threshold: 2 },
  RangePercentile: { period: 20 },
  ATRPercentile: { period: 20, lookback: 100 },
}

export function IndicatorModal({ indicatorId, strategyIndicators, onSave, onClose }: Props) {
  const existing = strategyIndicators.find(i => i.id === indicatorId)
  const { enums } = useEnums()

  const [step, setStep] = useState(1)
  const [type, setType] = useState<IndicatorType>(existing?.type ?? IndicatorType.EMA)
  const [timeframe, setTimeframe] = useState<Timeframe>(existing?.timeframe ?? Timeframe.D1)
  const [role, setRole] = useState<IndicatorRole>(existing?.role ?? IndicatorRole.EntryTrigger)
  const [baseParams, setBaseParams] = useState<Record<string, number | string | boolean>>(
    existing?.baseParams ?? (DEFAULT_PARAMS[type] ?? { period: 14 })
  )
  const [allowedRanges, setAllowedRanges] = useState<Record<string, ParamRange | string[]>>(
    existing?.allowedParamRanges ?? {}
  )

  const tfOptions = enums.timeframe ?? []
  const typeOptions = enums.indicatorType ?? []
  const roleOptions = enums.indicatorRole ?? []

  function handleTypeChange(t: IndicatorType) {
    setType(t)
    setBaseParams(DEFAULT_PARAMS[t] ?? { period: 14 })
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
            {existing ? 'Edit' : 'Add'} Indicator — Step {step} of 2
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
              <select value={timeframe} onChange={e => setTimeframe(e.target.value as Timeframe)} style={selectStyle}>
                {tfOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
              </select>
            </Field>
            <Field label="Role *">
              <select value={role} onChange={e => setRole(e.target.value as IndicatorRole)} style={selectStyle}>
                {roleOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
              </select>
            </Field>
            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: SP.sm, marginTop: SP.sm }}>
              <button onClick={onClose} style={cancelBtnStyle}>Cancel</button>
              <button onClick={() => setStep(2)} style={primaryBtnStyle}>Next →</button>
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
