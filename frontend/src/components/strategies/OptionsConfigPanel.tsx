import { useState } from 'react'
import {
  OptionsConfig, SpreadLegDef, SpreadType,
  OptionLegType, LegDirection, StrikeSelection,
} from '../../types/strategy'
import { C, SP } from '../../styles/tokens'

interface Props {
  value: OptionsConfig
  onChange: (cfg: OptionsConfig) => void
}

const SPREAD_LABELS: Record<SpreadType, string> = {
  [SpreadType.BullCallSpread]: 'Bull Call Spread',
  [SpreadType.BearPutSpread]:  'Bear Put Spread',
  [SpreadType.BullPutSpread]:  'Bull Put Spread',
  [SpreadType.BearCallSpread]: 'Bear Call Spread',
  [SpreadType.IronCondor]:     'Iron Condor',
  [SpreadType.ShortStraddle]:  'Short Straddle',
  [SpreadType.ShortStrangle]:  'Short Strangle',
  [SpreadType.CalendarSpread]: 'Calendar Spread',
  [SpreadType.LongCall]:       'Long Call',
  [SpreadType.LongPut]:        'Long Put',
}

const SPREAD_HINTS: Record<SpreadType, string> = {
  [SpreadType.BullCallSpread]: 'Buy ATM CE + Sell OTM CE. Defined-risk bullish. Best when moderately bullish.',
  [SpreadType.BearPutSpread]:  'Buy ATM PE + Sell OTM PE. Defined-risk bearish. Best when moderately bearish.',
  [SpreadType.BullPutSpread]:  'Sell ATM PE + Buy OTM PE. Credit spread. Profits if price stays above short strike.',
  [SpreadType.BearCallSpread]: 'Sell ATM CE + Buy OTM CE. Credit spread. Profits if price stays below short strike.',
  [SpreadType.IronCondor]:     'Sell OTM CE + PE, buy further OTM wings. Profits from range-bound, low-IV sessions.',
  [SpreadType.ShortStraddle]:  'Sell ATM CE + ATM PE. High premium; profits from IV crush. Requires defined stops.',
  [SpreadType.ShortStrangle]:  'Sell OTM CE + OTM PE. Wider profit zone than straddle. IV crush play.',
  [SpreadType.CalendarSpread]: 'Sell near-expiry + Buy far-expiry at same strike. Theta differential harvest.',
  [SpreadType.LongCall]:       'Buy ATM CE. Unlimited upside, limited risk. Directional bullish play.',
  [SpreadType.LongPut]:        'Buy ATM PE. Profits on downside move. Directional bearish or hedge play.',
}

// ── Spread presets — pre-filled legs for each built-in spread type ─────────────
// Auto-populated when the user selects a spread type and the legs table is empty.
const SPREAD_PRESETS: Record<SpreadType, SpreadLegDef[]> = {
  [SpreadType.BullCallSpread]: [
    { optionType: OptionLegType.CE, direction: LegDirection.Buy,  selectionMode: StrikeSelection.Atm,         otmStrikes: 0, otmPct: 0, targetDelta: 0.50, quantity: 1, nearestWeekly: true },
    { optionType: OptionLegType.CE, direction: LegDirection.Sell, selectionMode: StrikeSelection.OtmByStrike, otmStrikes: 2, otmPct: 0, targetDelta: 0.30, quantity: 1, nearestWeekly: true },
  ],
  [SpreadType.BearPutSpread]: [
    { optionType: OptionLegType.PE, direction: LegDirection.Buy,  selectionMode: StrikeSelection.Atm,         otmStrikes: 0, otmPct: 0, targetDelta: 0.50, quantity: 1, nearestWeekly: true },
    { optionType: OptionLegType.PE, direction: LegDirection.Sell, selectionMode: StrikeSelection.OtmByStrike, otmStrikes: 2, otmPct: 0, targetDelta: 0.30, quantity: 1, nearestWeekly: true },
  ],
  [SpreadType.BullPutSpread]: [
    { optionType: OptionLegType.PE, direction: LegDirection.Sell, selectionMode: StrikeSelection.Atm,         otmStrikes: 0, otmPct: 0, targetDelta: 0.50, quantity: 1, nearestWeekly: true },
    { optionType: OptionLegType.PE, direction: LegDirection.Buy,  selectionMode: StrikeSelection.OtmByStrike, otmStrikes: 2, otmPct: 0, targetDelta: 0.30, quantity: 1, nearestWeekly: true },
  ],
  [SpreadType.BearCallSpread]: [
    { optionType: OptionLegType.CE, direction: LegDirection.Sell, selectionMode: StrikeSelection.Atm,         otmStrikes: 0, otmPct: 0, targetDelta: 0.50, quantity: 1, nearestWeekly: true },
    { optionType: OptionLegType.CE, direction: LegDirection.Buy,  selectionMode: StrikeSelection.OtmByStrike, otmStrikes: 2, otmPct: 0, targetDelta: 0.30, quantity: 1, nearestWeekly: true },
  ],
  [SpreadType.IronCondor]: [
    { optionType: OptionLegType.PE, direction: LegDirection.Sell, selectionMode: StrikeSelection.OtmByStrike, otmStrikes: 1, otmPct: 0, targetDelta: 0.30, quantity: 1, nearestWeekly: true },
    { optionType: OptionLegType.PE, direction: LegDirection.Buy,  selectionMode: StrikeSelection.OtmByStrike, otmStrikes: 3, otmPct: 0, targetDelta: 0.15, quantity: 1, nearestWeekly: true },
    { optionType: OptionLegType.CE, direction: LegDirection.Sell, selectionMode: StrikeSelection.OtmByStrike, otmStrikes: 1, otmPct: 0, targetDelta: 0.30, quantity: 1, nearestWeekly: true },
    { optionType: OptionLegType.CE, direction: LegDirection.Buy,  selectionMode: StrikeSelection.OtmByStrike, otmStrikes: 3, otmPct: 0, targetDelta: 0.15, quantity: 1, nearestWeekly: true },
  ],
  [SpreadType.ShortStraddle]: [
    { optionType: OptionLegType.CE, direction: LegDirection.Sell, selectionMode: StrikeSelection.Atm, otmStrikes: 0, otmPct: 0, targetDelta: 0.50, quantity: 1, nearestWeekly: true },
    { optionType: OptionLegType.PE, direction: LegDirection.Sell, selectionMode: StrikeSelection.Atm, otmStrikes: 0, otmPct: 0, targetDelta: 0.50, quantity: 1, nearestWeekly: true },
  ],
  [SpreadType.ShortStrangle]: [
    { optionType: OptionLegType.CE, direction: LegDirection.Sell, selectionMode: StrikeSelection.OtmByStrike, otmStrikes: 1, otmPct: 0, targetDelta: 0.30, quantity: 1, nearestWeekly: true },
    { optionType: OptionLegType.PE, direction: LegDirection.Sell, selectionMode: StrikeSelection.OtmByStrike, otmStrikes: 1, otmPct: 0, targetDelta: 0.30, quantity: 1, nearestWeekly: true },
  ],
  [SpreadType.CalendarSpread]: [
    { optionType: OptionLegType.CE, direction: LegDirection.Sell, selectionMode: StrikeSelection.Atm, otmStrikes: 0, otmPct: 0, targetDelta: 0.50, quantity: 1, nearestWeekly: true },
    { optionType: OptionLegType.CE, direction: LegDirection.Buy,  selectionMode: StrikeSelection.Atm, otmStrikes: 0, otmPct: 0, targetDelta: 0.50, quantity: 1, nearestWeekly: false },
  ],
  [SpreadType.LongCall]: [
    { optionType: OptionLegType.CE, direction: LegDirection.Buy, selectionMode: StrikeSelection.Atm, otmStrikes: 0, otmPct: 0, targetDelta: 0.50, quantity: 1, nearestWeekly: true },
  ],
  [SpreadType.LongPut]: [
    { optionType: OptionLegType.PE, direction: LegDirection.Buy, selectionMode: StrikeSelection.Atm, otmStrikes: 0, otmPct: 0, targetDelta: 0.50, quantity: 1, nearestWeekly: true },
  ],
}

const DEFAULT_LEG: SpreadLegDef = {
  optionType: OptionLegType.CE, direction: LegDirection.Buy,
  selectionMode: StrikeSelection.Atm, otmStrikes: 0, otmPct: 0,
  targetDelta: 0.30, quantity: 1, nearestWeekly: true,
}

export function OptionsConfigPanel({ value, onChange }: Props) {
  const [editingLeg, setEditingLeg] = useState<number | null>(null)

  function set<K extends keyof OptionsConfig>(key: K, val: OptionsConfig[K]) {
    onChange({ ...value, [key]: val })
  }

  function setLeg(idx: number, leg: SpreadLegDef) {
    const legs = [...value.legs]
    legs[idx] = leg
    onChange({ ...value, legs })
  }

  function addLeg() {
    onChange({ ...value, legs: [...value.legs, { ...DEFAULT_LEG }] })
    setEditingLeg(value.legs.length)
  }

  function removeLeg(idx: number) {
    const legs = value.legs.filter((_, i) => i !== idx)
    onChange({ ...value, legs })
    if (editingLeg === idx) setEditingLeg(null)
  }

  const inputStyle: React.CSSProperties = {
    background: C.surface2, border: `1px solid ${C.border}`,
    color: C.text, borderRadius: 4, padding: '4px 8px', fontSize: 12,
    outline: 'none',
  }

  const labelStyle: React.CSSProperties = {
    fontSize: 11, color: C.textMuted, marginBottom: 3, display: 'block',
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: SP.lg }}>

      {/* Enable toggle */}
      <div style={{
        display: 'flex', alignItems: 'center', gap: SP.sm,
        padding: '10px 12px', background: value.enabled ? C.blueBg : C.surface2,
        border: `1px solid ${value.enabled ? C.blue44 : C.border}`,
        borderRadius: 6,
      }}>
        <input
          type="checkbox"
          id="opts-enabled"
          checked={value.enabled}
          onChange={e => set('enabled', e.target.checked)}
          style={{ accentColor: C.blue, width: 14, height: 14 }}
        />
        <label htmlFor="opts-enabled" style={{ fontSize: 13, color: C.text, cursor: 'pointer', fontWeight: 600 }}>
          Enable Options Spread Mode
        </label>
        <span style={{ fontSize: 11, color: C.textMuted, marginLeft: 4 }}>
          When enabled, entry signals fire spread orders instead of plain equity Buy/Sell.
        </span>
      </div>

      {!value.enabled && (
        <div style={{ fontSize: 12, color: C.textMuted, padding: '8px 12px', background: C.surface2, borderRadius: 6 }}>
          Enable Options Spread Mode above to configure spread type, legs, and IV filters.
          Your existing indicator rules still control <em>when</em> to enter — this section defines <em>what</em> to trade.
        </div>
      )}

      {value.enabled && (
        <>
          {/* Spread type selector */}
          <section>
            <div style={{ fontSize: 12, fontWeight: 700, color: C.textSub, marginBottom: SP.sm }}>
              Spread Type
            </div>
            <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
              {Object.values(SpreadType).map(st => (
                <button
                  key={st}
                  onClick={() => {
                    const preset = SPREAD_PRESETS[st]
                    onChange({
                      ...value,
                      spreadType: st,
                      // Always reset legs to preset when spread type changes so the
                      // payoff diagram updates immediately. Re-selecting the same type
                      // keeps existing legs (avoids clobbering manual edits).
                      legs: st !== value.spreadType
                        ? preset.map(l => ({ ...l }))
                        : value.legs,
                    })
                  }}
                  title={SPREAD_HINTS[st]}
                  style={{
                    padding: '5px 11px', borderRadius: 5, fontSize: 11, cursor: 'pointer',
                    fontWeight: value.spreadType === st ? 700 : 400,
                    background: value.spreadType === st ? C.blueBg : C.surface2,
                    color: value.spreadType === st ? C.blue : C.textSub,
                    border: `1px solid ${value.spreadType === st ? C.blue55 : C.border}`,
                  }}
                >
                  {SPREAD_LABELS[st]}
                </button>
              ))}
            </div>
            <div style={{
              marginTop: 8, padding: '7px 10px', background: C.surface2,
              border: `1px solid ${C.border}`, borderRadius: 5,
              fontSize: 11, color: C.textSub,
            }}>
              {SPREAD_HINTS[value.spreadType]}
            </div>
          </section>

          {/* Expiry + SL/TP */}
          <section>
            <div style={{ fontSize: 12, fontWeight: 700, color: C.textSub, marginBottom: SP.sm }}>
              Execution Settings
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: SP.md }}>
              <div>
                <label style={labelStyle}>Expiry Preference</label>
                <select
                  value={value.expiryPreference}
                  onChange={e => set('expiryPreference', e.target.value as 'Weekly' | 'Monthly')}
                  style={{ ...inputStyle, width: '100%' }}
                >
                  <option value="Weekly">Weekly</option>
                  <option value="Monthly">Monthly</option>
                </select>
              </div>
              <div>
                <label style={labelStyle}>Stop Loss % of Premium</label>
                <input
                  type="number" min={10} max={200} step={5}
                  value={value.stopLossPct}
                  onChange={e => set('stopLossPct', Number(e.target.value))}
                  style={{ ...inputStyle, width: '100%' }}
                />
              </div>
              <div>
                <label style={labelStyle}>Profit Target % of Premium</label>
                <input
                  type="number" min={10} max={100} step={5}
                  value={value.profitTargetPct}
                  onChange={e => set('profitTargetPct', Number(e.target.value))}
                  style={{ ...inputStyle, width: '100%' }}
                />
              </div>
            </div>
          </section>

          {/* IV / PCR filters */}
          <section>
            <div style={{ fontSize: 12, fontWeight: 700, color: C.textSub, marginBottom: 4 }}>
              IV / PCR Filters
              <span style={{ fontSize: 10, fontWeight: 400, color: C.textMuted, marginLeft: 6 }}>
                Optional — entry blocked when outside these bounds.
              </span>
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: SP.md }}>
              <div>
                <label style={labelStyle}>Min IV Rank (0–100)</label>
                <input
                  type="number" min={0} max={100} placeholder="—"
                  value={value.minIvRank ?? ''}
                  onChange={e => set('minIvRank', e.target.value === '' ? undefined : Number(e.target.value))}
                  style={{ ...inputStyle, width: '100%' }}
                />
              </div>
              <div>
                <label style={labelStyle}>Max IV Rank (0–100)</label>
                <input
                  type="number" min={0} max={100} placeholder="—"
                  value={value.maxIvRank ?? ''}
                  onChange={e => set('maxIvRank', e.target.value === '' ? undefined : Number(e.target.value))}
                  style={{ ...inputStyle, width: '100%' }}
                />
              </div>
              <div>
                <label style={labelStyle}>Min ATM IV %</label>
                <input
                  type="number" min={0} max={200} placeholder="—"
                  value={value.minAtmIv ?? ''}
                  onChange={e => set('minAtmIv', e.target.value === '' ? undefined : Number(e.target.value))}
                  style={{ ...inputStyle, width: '100%' }}
                />
              </div>
              <div>
                <label style={labelStyle}>Max ATM IV %</label>
                <input
                  type="number" min={0} max={200} placeholder="—"
                  value={value.maxAtmIv ?? ''}
                  onChange={e => set('maxAtmIv', e.target.value === '' ? undefined : Number(e.target.value))}
                  style={{ ...inputStyle, width: '100%' }}
                />
              </div>
              <div>
                <label style={labelStyle}>Min PCR (OI ratio)</label>
                <input
                  type="number" min={0} max={5} step={0.1} placeholder="—"
                  value={value.minPcr ?? ''}
                  onChange={e => set('minPcr', e.target.value === '' ? undefined : Number(e.target.value))}
                  style={{ ...inputStyle, width: '100%' }}
                />
              </div>
              <div>
                <label style={labelStyle}>Max PCR (OI ratio)</label>
                <input
                  type="number" min={0} max={5} step={0.1} placeholder="—"
                  value={value.maxPcr ?? ''}
                  onChange={e => set('maxPcr', e.target.value === '' ? undefined : Number(e.target.value))}
                  style={{ ...inputStyle, width: '100%' }}
                />
              </div>
            </div>
          </section>

          {/* Custom legs */}
          <section>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: SP.sm }}>
              <div>
                <span style={{ fontSize: 12, fontWeight: 700, color: C.textSub }}>Legs</span>
                <span style={{ fontSize: 10, color: C.textMuted, marginLeft: 6 }}>
                  Modify the preset legs or add your own. Payoff chart updates in real time.
                </span>
              </div>
              <div style={{ display: 'flex', gap: 6 }}>
                {value.legs.length > 0 && (
                  <button
                    onClick={() => onChange({ ...value, legs: SPREAD_PRESETS[value.spreadType].map(l => ({ ...l })) })}
                    style={{
                      padding: '4px 10px', borderRadius: 4, fontSize: 11, cursor: 'pointer',
                      background: 'none', border: `1px solid ${C.border}`, color: C.textMuted,
                    }}
                    title={`Reset to standard ${SPREAD_LABELS[value.spreadType]} legs`}
                  >
                    Reset to preset
                  </button>
                )}
                <button
                  onClick={addLeg}
                  style={{
                    padding: '4px 10px', borderRadius: 4, fontSize: 11, cursor: 'pointer',
                    background: C.surface2, border: `1px solid ${C.border}`, color: C.blue,
                  }}
                >
                  + Add Leg
                </button>
              </div>
            </div>

            {value.legs.length === 0 && (
              <div style={{
                padding: '8px 12px', background: C.surface2,
                border: `1px dashed ${C.border}`, borderRadius: 5,
                fontSize: 11, color: C.textMuted, textAlign: 'center',
              }}>
                No legs defined. Select a spread type above to load a preset, or click "+ Add Leg" to build from scratch.
              </div>
            )}

            {value.legs.length > 0 && (
              <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 11 }}>
                <thead>
                  <tr style={{ background: C.surface2 }}>
                    {['#', 'Type', 'Direction', 'Strike Selection', 'Strikes (+ OTM / − ITM)', 'Target Delta', 'Qty', 'Expiry', ''].map(h => (
                      <th key={h} style={{ padding: '5px 8px', textAlign: 'left', color: C.textMuted, fontWeight: 600 }}>{h}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {value.legs.map((leg, idx) => (
                    <tr
                      key={idx}
                      style={{ borderTop: `1px solid ${C.border}`, background: editingLeg === idx ? C.surface3 : 'transparent' }}
                    >
                      <td style={{ padding: '5px 8px', color: C.textMuted }}>{idx + 1}</td>
                      <td style={{ padding: '3px 4px' }}>
                        <select
                          value={leg.optionType}
                          onChange={e => setLeg(idx, { ...leg, optionType: e.target.value as OptionLegType })}
                          style={{ ...inputStyle, padding: '2px 4px' }}
                        >
                          <option value={OptionLegType.CE}>CE (Call)</option>
                          <option value={OptionLegType.PE}>PE (Put)</option>
                        </select>
                      </td>
                      <td style={{ padding: '3px 4px' }}>
                        <select
                          value={leg.direction}
                          onChange={e => setLeg(idx, { ...leg, direction: e.target.value as LegDirection })}
                          style={{ ...inputStyle, padding: '2px 4px' }}
                        >
                          <option value={LegDirection.Buy}>Buy</option>
                          <option value={LegDirection.Sell}>Sell</option>
                        </select>
                      </td>
                      <td style={{ padding: '3px 4px' }}>
                        <select
                          value={leg.selectionMode}
                          onChange={e => setLeg(idx, { ...leg, selectionMode: e.target.value as StrikeSelection })}
                          style={{ ...inputStyle, padding: '2px 4px' }}
                        >
                          <option value={StrikeSelection.Atm}>ATM</option>
                          <option value={StrikeSelection.OtmByStrike}>OTM by Strikes</option>
                          <option value={StrikeSelection.OtmByPct}>OTM by %</option>
                          <option value={StrikeSelection.ByDelta}>By Delta</option>
                        </select>
                      </td>
                      <td style={{ padding: '3px 4px' }}>
                        <input
                          type="number" min={-20} max={20}
                          value={leg.otmStrikes}
                          onChange={e => setLeg(idx, { ...leg, otmStrikes: Number(e.target.value) })}
                          style={{ ...inputStyle, width: 56, padding: '2px 4px' }}
                          disabled={leg.selectionMode !== StrikeSelection.OtmByStrike}
                          title="Positive = OTM strikes from ATM, Negative = ITM strikes from ATM"
                        />
                      </td>
                      <td style={{ padding: '3px 4px' }}>
                        <input
                          type="number" min={0.01} max={0.99} step={0.01}
                          value={leg.targetDelta}
                          onChange={e => setLeg(idx, { ...leg, targetDelta: Number(e.target.value) })}
                          style={{ ...inputStyle, width: 52, padding: '2px 4px' }}
                          disabled={leg.selectionMode !== StrikeSelection.ByDelta}
                        />
                      </td>
                      <td style={{ padding: '3px 4px' }}>
                        <input
                          type="number" min={1} max={50}
                          value={leg.quantity}
                          onChange={e => setLeg(idx, { ...leg, quantity: Number(e.target.value) })}
                          style={{ ...inputStyle, width: 44, padding: '2px 4px' }}
                        />
                      </td>
                      <td style={{ padding: '3px 4px' }}>
                        <select
                          value={leg.nearestWeekly ? 'Weekly' : 'Monthly'}
                          onChange={e => setLeg(idx, { ...leg, nearestWeekly: e.target.value === 'Weekly' })}
                          style={{ ...inputStyle, padding: '2px 4px' }}
                        >
                          <option value="Weekly">Weekly</option>
                          <option value="Monthly">Monthly</option>
                        </select>
                      </td>
                      <td style={{ padding: '3px 4px' }}>
                        <button
                          onClick={() => removeLeg(idx)}
                          style={{
                            background: 'none', border: 'none', color: C.textMuted,
                            cursor: 'pointer', fontSize: 13, padding: '2px 4px',
                          }}
                          title="Remove leg"
                        >
                          ×
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </section>
        </>
      )}
    </div>
  )
}

export function defaultOptionsConfig(): OptionsConfig {
  return {
    enabled: false,
    spreadType: SpreadType.BullCallSpread,
    legs: [],
    expiryPreference: 'Weekly',
    stopLossPct: 50,
    profitTargetPct: 50,
  }
}
