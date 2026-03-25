import { useState, useEffect } from 'react'

// ─── Strategy metadata registry ──────────────────────────────────────────────

export interface ParamDef {
  key: string
  label: string
  type: 'int' | 'decimal' | 'bool' | 'select'
  default: number | boolean | string
  min?: number
  max?: number
  step?: number
  options?: Array<{ value: string; label: string }>
  hint?: string
}

export interface StrategyMeta {
  name: string
  label: string
  description: string
  suitableFor: string     // e.g. "Index futures, BankNifty, Nifty"
  timeframes: string[]
  params: ParamDef[]
}

export const STRATEGY_REGISTRY: StrategyMeta[] = [
  {
    name: 'PriceActionBreakout',
    label: 'Price Action Breakout',
    description: 'Buys/sells on consolidation range breakouts confirmed by volume. '
      + 'Identifies a tight range over LookbackBars bars, waits for a close outside the range '
      + 'with volume ≥ VolumeMultiple × average.',
    suitableFor: 'Equities, Index ETFs, Futures (any liquid instrument)',
    timeframes: ['5m', '15m', '30m', '1h'],
    params: [
      { key: 'LookbackBars',     label: 'Lookback Bars',      type: 'int',     default: 20,  min: 5,   max: 100, hint: 'Bars to measure the consolidation range' },
      { key: 'VolumeMultiple',   label: 'Volume Multiple',    type: 'decimal', default: 1.5, min: 1.0, max: 5.0, step: 0.1, hint: 'Volume must be ≥ this × average volume for confirmation' },
      { key: 'RiskRewardRatio',  label: 'Risk:Reward Ratio',  type: 'decimal', default: 2.0, min: 1.0, max: 10.0, step: 0.5, hint: 'TP = entry ± risk × this value (e.g. 2.0 = 1:2)' },
      { key: 'AllowShort',       label: 'Allow Short Trades', type: 'bool',    default: true, hint: 'Enable SELL signals on downside breakouts' },
    ],
  },
  {
    name: 'EmaVwapMomentum',
    label: 'EMA + VWAP Momentum',
    description: 'Multi-indicator composite strategy. Enters on EMA golden/death cross, '
      + 'confirmed by price above/below VWAP, above/below Bollinger Band midline, and volume conviction. '
      + 'Optionally uses option chain PCR as a sentiment filter.',
    suitableFor: 'Index futures (BankNifty, Nifty), Liquid large-cap equities',
    timeframes: ['5m', '15m'],
    params: [
      { key: 'FastEmaPeriod',      label: 'Fast EMA Period',       type: 'int',     default: 9,    min: 3,   max: 50  },
      { key: 'SlowEmaPeriod',      label: 'Slow EMA Period',       type: 'int',     default: 21,   min: 5,   max: 200 },
      { key: 'BbPeriod',           label: 'Bollinger Band Period',  type: 'int',     default: 20,   min: 5,   max: 50  },
      { key: 'BbStdDev',           label: 'BB Std Deviation',       type: 'decimal', default: 2.0,  min: 1.0, max: 4.0, step: 0.5 },
      { key: 'AtrPeriod',          label: 'ATR Period',             type: 'int',     default: 14,   min: 3,   max: 50  },
      { key: 'AtrStopMultiple',    label: 'ATR Stop Multiple',      type: 'decimal', default: 1.5,  min: 0.5, max: 5.0, step: 0.5, hint: 'SL = entry ± ATR × this' },
      { key: 'VolumeMultiple',     label: 'Volume Filter Multiple', type: 'decimal', default: 1.5,  min: 1.0, max: 5.0, step: 0.5, hint: 'Volume must be ≥ this × avg to confirm signal' },
      { key: 'RiskRewardRatio',    label: 'Risk:Reward Ratio',      type: 'decimal', default: 2.0,  min: 1.0, max: 10.0, step: 0.5 },
      { key: 'MinAtrPct',          label: 'Min ATR % of Price',     type: 'decimal', default: 0.1,  min: 0.0, max: 5.0, step: 0.1, hint: 'Skips signal if market is too flat (ATR < this % of close)' },
      { key: 'AllowShort',         label: 'Allow Short Trades',     type: 'bool',    default: true  },
      { key: 'UseOptionChain',     label: 'Option Chain PCR Filter',type: 'bool',    default: false, hint: 'Requires an index instrument — uses PCR as directional bias filter' },
      { key: 'PcrBullishThreshold',label: 'PCR Bullish Threshold',  type: 'decimal', default: 0.8,  min: 0.3, max: 1.5, step: 0.1, hint: 'PCR < this → bullish bias → only allow BUY' },
      { key: 'PcrBearishThreshold',label: 'PCR Bearish Threshold',  type: 'decimal', default: 1.2,  min: 0.5, max: 3.0, step: 0.1, hint: 'PCR > this → bearish bias → only allow SELL' },
      { key: 'OiWallBufferPct',    label: 'OI Wall Buffer %',       type: 'decimal', default: 0.3,  min: 0.1, max: 5.0, step: 0.1, hint: 'Suppress signal if price within this % of a max-OI strike' },
    ],
  },
  {
    name: 'AlertCandleShort',
    label: 'Alert Candle Short',
    description: 'Identifies "Alert Candles" whose Low does not touch the 5-EMA at all. '
      + 'When the next candle breaks below the Alert Candle\'s Low, enters a SHORT at that level. '
      + 'Stop loss = Alert Candle High. Target = 1:3 RRR minimum. One trade per day only.',
    suitableFor: 'BankNifty, Nifty 50 index futures (5-minute chart)',
    timeframes: ['5m'],
    params: [
      { key: 'EmaPeriod',       label: 'EMA Period',          type: 'int',     default: 5,   min: 3, max: 20, hint: 'The EMA the Alert Candle must float ABOVE' },
      { key: 'RiskRewardRatio', label: 'Min Risk:Reward',     type: 'decimal', default: 3.0, min: 1.5, max: 10.0, step: 0.5, hint: 'TP = entry - (SL - entry) × this. Minimum 1:3 recommended.' },
      { key: 'MinRiskPoints',   label: 'Min Risk (points)',   type: 'decimal', default: 0,   min: 0, max: 500, step: 5, hint: 'Skip trade if SL-Entry spread < this many points (noise filter). 0 = disabled.' },
    ],
  },
]

// ─── Component ────────────────────────────────────────────────────────────────

interface Props {
  strategyName: string
  value?: Record<string, unknown>
  onChange: (params: Record<string, unknown>) => void
}

const inp: React.CSSProperties = {
  padding: '6px 10px',
  background: '#0f0f1a',
  border: '1px solid #2d2d3f',
  borderRadius: 6,
  color: '#e2e8f0',
  fontSize: 13,
  width: '100%',
  boxSizing: 'border-box',
}

const label12: React.CSSProperties = {
  fontSize: 12, fontWeight: 600, color: '#94a3b8', display: 'block', marginBottom: 4,
}

/**
 * Dynamic parameter editor for a chosen strategy.
 * Renders a form section from the STRATEGY_REGISTRY metadata.
 * Output: Record<string, unknown> to be JSON.stringify'd as parametersJson.
 */
export function StrategyParamsEditor({ strategyName, value, onChange }: Props) {
  const meta = STRATEGY_REGISTRY.find(s => s.name === strategyName)
  const [params, setParams] = useState<Record<string, unknown>>(() => {
    const defaults: Record<string, unknown> = {}
    meta?.params.forEach(p => { defaults[p.key] = p.default })
    return value ?? defaults
  })

  // Reset to defaults when strategy changes
  useEffect(() => {
    const m = STRATEGY_REGISTRY.find(s => s.name === strategyName)
    if (!m) return
    const defaults: Record<string, unknown> = {}
    m.params.forEach(p => { defaults[p.key] = p.default })
    const next = value ?? defaults
    setParams(next)
    onChange(next)
  }, [strategyName])

  const set = (key: string, val: unknown) => {
    const next = { ...params, [key]: val }
    setParams(next)
    onChange(next)
  }

  if (!meta) {
    return (
      <div style={{ padding: 12, background: '#161628', borderRadius: 8, border: '1px solid #2d2d3f', color: '#64748b', fontSize: 12 }}>
        No parameters defined for strategy "{strategyName}".
        The strategy will use its built-in defaults.
      </div>
    )
  }

  return (
    <div style={{ background: '#161628', border: '1px solid #2d2d3f', borderRadius: 8, padding: 16 }}>
      {/* Strategy info header */}
      <div style={{ marginBottom: 16 }}>
        <div style={{ fontSize: 13, fontWeight: 700, color: '#93c5fd', marginBottom: 4 }}>
          ⚙ {meta.label} — Parameters
        </div>
        <div style={{ fontSize: 11, color: '#64748b', lineHeight: 1.5 }}>{meta.description}</div>
        <div style={{ marginTop: 6, display: 'flex', gap: 8, flexWrap: 'wrap' }}>
          <Tag label={`Suitable: ${meta.suitableFor}`} color="#818cf8" />
          <Tag label={`Timeframes: ${meta.timeframes.join(', ')}`} color="#60a5fa" />
        </div>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
        {meta.params.map(p => (
          <div key={p.key} style={p.type === 'bool' ? { gridColumn: '1 / -1' } : {}}>
            {p.type === 'bool' ? (
              /* Toggle */
              <div
                style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', cursor: 'pointer', padding: '6px 0', borderBottom: '1px solid #2d2d3f' }}
                onClick={() => set(p.key, !params[p.key])}
              >
                <div>
                  <span style={{ fontSize: 13, fontWeight: 600, color: '#e2e8f0' }}>{p.label}</span>
                  {p.hint && <div style={{ fontSize: 11, color: '#64748b', marginTop: 2 }}>{p.hint}</div>}
                </div>
                <Pill on={!!params[p.key]} />
              </div>
            ) : p.type === 'select' ? (
              /* Select */
              <div>
                <label style={label12}>{p.label}</label>
                <select value={String(params[p.key] ?? p.default)} onChange={e => set(p.key, e.target.value)} style={inp}>
                  {p.options?.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
                </select>
                {p.hint && <span style={{ fontSize: 10, color: '#64748b', marginTop: 3, display: 'block' }}>{p.hint}</span>}
              </div>
            ) : (
              /* Number input */
              <div>
                <label style={label12}>{p.label}</label>
                <input
                  type="number"
                  value={String(params[p.key] ?? p.default)}
                  onChange={e => set(p.key, p.type === 'int' ? parseInt(e.target.value, 10) : parseFloat(e.target.value))}
                  min={p.min}
                  max={p.max}
                  step={p.step ?? (p.type === 'int' ? 1 : 0.1)}
                  style={inp}
                />
                {p.hint && <span style={{ fontSize: 10, color: '#64748b', marginTop: 3, display: 'block' }}>{p.hint}</span>}
              </div>
            )}
          </div>
        ))}
      </div>
    </div>
  )
}

// ─── Small helpers ────────────────────────────────────────────────────────────

function Pill({ on }: { on: boolean }) {
  return (
    <div style={{ width: 40, height: 20, borderRadius: 10, background: on ? '#3b82f6' : '#2d2d3f', position: 'relative', flexShrink: 0 }}>
      <div style={{ position: 'absolute', top: 2, left: on ? 22 : 2, width: 16, height: 16, borderRadius: '50%', background: '#fff', transition: 'left 0.2s' }} />
    </div>
  )
}

function Tag({ label, color }: { label: string; color: string }) {
  return (
    <span style={{ fontSize: 10, padding: '2px 7px', borderRadius: 4, background: `${color}22`, color, border: `1px solid ${color}44` }}>
      {label}
    </span>
  )
}

/** Helper to stringify params for the API */
export const paramsToJson = (params: Record<string, unknown>) => JSON.stringify(params)

/** Helper to get default params for a strategy */
export const defaultParams = (strategyName: string): Record<string, unknown> => {
  const meta = STRATEGY_REGISTRY.find(s => s.name === strategyName)
  if (!meta) return {}
  const out: Record<string, unknown> = {}
  meta.params.forEach(p => { out[p.key] = p.default })
  return out
}
