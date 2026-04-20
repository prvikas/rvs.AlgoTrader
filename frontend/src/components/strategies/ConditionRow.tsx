import { C, SP, TABLE_CELL } from '../../styles/tokens'
import {
  Condition, ConditionOperand, ConditionOperator,
  IndicatorConfig, SessionStateProperty, WindowExpression,
} from '../../types/strategy'
import { useEnums } from '../../context/EnumsContext'
import { WindowExpressionEditor } from './WindowExpressionEditor'

interface Props {
  condition: Condition
  indicators: IndicatorConfig[]
  onChange: (c: Condition) => void
  onDelete: () => void
}

// ─── Sub-field definitions per multi-output indicator ─────────────────────────
// Indicators NOT listed here have a single unnamed output → no field selector shown.
const INDICATOR_FIELDS: Record<string, { value: string; label: string }[]> = {
  SuperTrend:      [
    { value: '',          label: 'Band value (price level)' },
    { value: 'direction', label: 'Direction — Bullish / Bearish' },
  ],
  MACD:            [
    { value: 'macd',      label: 'MACD line' },
    { value: 'signal',    label: 'Signal line' },
    { value: 'histogram', label: 'Histogram' },
  ],
  BollingerBands:  [
    { value: 'upper',  label: 'Upper band' },
    { value: 'middle', label: 'Middle band' },
    { value: 'lower',  label: 'Lower band' },
  ],
  Stochastics:     [
    { value: 'k', label: '%K line' },
    { value: 'd', label: '%D line' },
  ],
  DonchianChannel: [
    { value: 'upper',  label: 'Upper channel' },
    { value: 'middle', label: 'Midline' },
    { value: 'lower',  label: 'Lower channel' },
  ],
  SwingHighLow:    [
    { value: 'high', label: 'Swing High' },
    { value: 'low',  label: 'Swing Low' },
  ],
}

// When the left operand is SuperTrend with field = direction,
// the right side offers this named dropdown instead of a raw number.
const DIRECTION_OPTIONS = [
  { value:  1, label: 'Bullish  (+1)' },
  { value: -1, label: 'Bearish  (−1)' },
]

/** Display name for an indicator in dropdowns. */
function indLabel(i: IndicatorConfig): string {
  return i.label?.trim() ? i.label.trim() : `${i.type} (${i.timeframe})`
}

export function ConditionRow({ condition, indicators, onChange, onDelete }: Props) {
  const { enums } = useEnums()
  const opOptions = enums.conditionOp ?? []
  const sessionStateOptions = enums.sessionStateProperty ?? []

  // Detect whether the right side should show a Direction picker
  const leftInd = condition.left.kind === 'indicator'
    ? indicators.find(i => i.id === (condition.left as { indicatorId: string }).indicatorId)
    : undefined
  const leftField = condition.left.kind === 'indicator'
    ? ((condition.left as { field?: string }).field ?? '')
    : ''
  const isDirectionLeft = leftInd?.type === 'SuperTrend' && leftField === 'direction'

  function updateLeft(left: ConditionOperand) {
    // When switching to SuperTrend direction on the left, auto-reset right to Bullish
    const newLeftInd = left.kind === 'indicator'
      ? indicators.find(i => i.id === (left as { indicatorId: string }).indicatorId)
      : undefined
    const newLeftField = left.kind === 'indicator' ? ((left as { field?: string }).field ?? '') : ''
    const newIsDirection = newLeftInd?.type === 'SuperTrend' && newLeftField === 'direction'

    if (newIsDirection && !isDirectionLeft) {
      onChange({ ...condition, left, right: { kind: 'value', value: 1 } })
    } else {
      onChange({ ...condition, left })
    }
  }

  function updateRight(right: ConditionOperand) { onChange({ ...condition, right }) }
  function updateOp(operator: ConditionOperator) { onChange({ ...condition, operator }) }

  const hideRight = condition.left.kind === 'sessionState'

  return (
    <div style={{
      display: 'flex', alignItems: 'flex-start', gap: SP.sm,
      padding: TABLE_CELL, borderBottom: `1px solid ${C.border2}`,
    }}>
      <OperandEditor
        operand={condition.left}
        indicators={indicators}
        onChange={updateLeft}
        sessionStateOptions={sessionStateOptions}
        isLeft
      />

      {!hideRight && (
        <>
          {/* Operator */}
          <select
            value={condition.operator}
            onChange={e => updateOp(e.target.value as ConditionOperator)}
            style={{ ...selectStyle, width: 120, flexShrink: 0 }}
          >
            {opOptions.map(o => (
              <option key={o.value} value={o.value}>{o.label}</option>
            ))}
          </select>

          <OperandEditor
            operand={condition.right}
            indicators={indicators}
            onChange={updateRight}
            sessionStateOptions={sessionStateOptions}
            directionMode={isDirectionLeft}
          />
        </>
      )}

      <button onClick={onDelete} style={deleteBtnStyle} title="Delete condition">×</button>
    </div>
  )
}

function OperandEditor({
  operand, indicators, onChange, sessionStateOptions, isLeft, directionMode,
}: {
  operand: ConditionOperand
  indicators: IndicatorConfig[]
  onChange: (o: ConditionOperand) => void
  sessionStateOptions: { value: string; label: string }[]
  isLeft?: boolean
  directionMode?: boolean   // right side: show Bullish/Bearish picker instead of raw number
}) {
  const kindOpts: { value: ConditionOperand['kind']; label: string }[] = isLeft
    ? [
        { value: 'indicator',    label: 'Indicator' },
        { value: 'window',       label: 'Window (lookback)' },
        { value: 'value',        label: 'Fixed value' },
        { value: 'absence',      label: 'Absence (N bars)' },
        { value: 'percentile',   label: 'Percentile rank' },
        { value: 'slope',        label: 'Slope direction' },
        { value: 'sessionState', label: 'Session state' },
      ]
    : [
        { value: 'indicator', label: 'Indicator' },
        { value: 'window',    label: 'Window (lookback)' },
        { value: 'value',     label: directionMode ? 'Direction' : 'Fixed value' },
      ]

  function handleKindChange(kind: ConditionOperand['kind']) {
    switch (kind) {
      case 'indicator':    onChange({ kind, indicatorId: indicators[0]?.id ?? '' }); break
      case 'window':       onChange({ kind, expr: defaultWindow(indicators) }); break
      case 'value':        onChange({ kind, value: directionMode ? 1 : 0 }); break
      case 'absence':      onChange({ kind, indicatorId: indicators[0]?.id ?? '', lookbackBars: 10 }); break
      case 'percentile':   onChange({ kind, indicatorId: indicators[0]?.id ?? '', lookbackBars: 50, pct: 70 }); break
      case 'slope':        onChange({ kind, indicatorId: indicators[0]?.id ?? '', lookbackBars: 3 }); break
      case 'sessionState': onChange({ kind, property: SessionStateProperty.IsFirstSignalOfSession }); break
    }
  }

  // For indicator kind: look up the indicator to show its sub-fields
  const selectedInd = operand.kind === 'indicator'
    ? indicators.find(i => i.id === (operand as { indicatorId: string }).indicatorId)
    : undefined
  const fieldOptions = selectedInd ? (INDICATOR_FIELDS[selectedInd.type] ?? null) : null

  return (
    <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 4 }}>
      {/* Kind selector — hidden on right when directionMode (always shows "Direction") */}
      {!(directionMode && !isLeft) && (
        <select
          value={operand.kind}
          onChange={e => handleKindChange(e.target.value as ConditionOperand['kind'])}
          style={{ ...selectStyle, fontSize: 10 }}
        >
          {kindOpts.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
        </select>
      )}

      {/* ── Indicator ── */}
      {operand.kind === 'indicator' && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
          <select
            value={(operand as { indicatorId: string }).indicatorId}
            onChange={e => onChange({ ...operand, indicatorId: e.target.value })}
            style={selectStyle}
          >
            <option value="">— pick indicator —</option>
            {indicators.map(i => (
              <option key={i.id} value={i.id}>{indLabel(i)}</option>
            ))}
          </select>
          {/* Sub-field selector — only shown for multi-output indicators */}
          {fieldOptions && (
            <select
              value={(operand as { field?: string }).field ?? ''}
              onChange={e => onChange({ ...operand, field: e.target.value || undefined })}
              style={{ ...selectStyle, fontSize: 10, color: C.textSub }}
            >
              {fieldOptions.map(f => (
                <option key={f.value} value={f.value}>{f.label}</option>
              ))}
            </select>
          )}
        </div>
      )}

      {/* ── Window (lookback aggregate) ── */}
      {operand.kind === 'window' && (
        <WindowExpressionEditor
          value={(operand as { expr: WindowExpression }).expr}
          indicators={indicators}
          onChange={expr => onChange({ kind: 'window', expr })}
        />
      )}

      {/* ── Fixed value / Direction ── */}
      {operand.kind === 'value' && (
        directionMode
          ? (
            <select
              value={(operand as { value: number }).value}
              onChange={e => onChange({ kind: 'value', value: Number(e.target.value) })}
              style={selectStyle}
            >
              {DIRECTION_OPTIONS.map(d => (
                <option key={d.value} value={d.value}>{d.label}</option>
              ))}
            </select>
          ) : (
            <input
              type="number" step={0.01}
              value={(operand as { value: number }).value}
              onChange={e => onChange({ kind: 'value', value: Number(e.target.value) })}
              style={inputStyle}
            />
          )
      )}

      {/* ── Absence ── */}
      {operand.kind === 'absence' && (
        <div style={{ display: 'flex', gap: 4, alignItems: 'center' }}>
          <select
            value={(operand as { indicatorId: string }).indicatorId}
            onChange={e => onChange({ ...operand, indicatorId: e.target.value })}
            style={{ ...selectStyle, flex: 1 }}
          >
            <option value="">— pick —</option>
            {indicators.map(i => (
              <option key={i.id} value={i.id}>{indLabel(i)}</option>
            ))}
          </select>
          <span style={{ fontSize: 10, color: C.textMuted, whiteSpace: 'nowrap' }}>not in last</span>
          <input
            type="number" min={1}
            value={(operand as { lookbackBars: number }).lookbackBars}
            onChange={e => onChange({ ...operand, lookbackBars: Number(e.target.value) })}
            style={{ ...inputStyle, width: 45 }}
          />
          <span style={{ fontSize: 10, color: C.textMuted }}>bars</span>
        </div>
      )}

      {/* ── Percentile rank ── */}
      {operand.kind === 'percentile' && (
        <div style={{ display: 'flex', gap: 4, alignItems: 'center', flexWrap: 'wrap' }}>
          <select
            value={(operand as { indicatorId: string }).indicatorId}
            onChange={e => onChange({ ...operand, indicatorId: e.target.value })}
            style={{ ...selectStyle, flex: 1 }}
          >
            <option value="">— pick —</option>
            {indicators.map(i => (
              <option key={i.id} value={i.id}>{indLabel(i)}</option>
            ))}
          </select>
          <span style={{ fontSize: 10, color: C.textMuted }}>≥</span>
          <input
            type="number" min={0} max={100}
            value={(operand as { pct: number }).pct}
            onChange={e => onChange({ ...operand, pct: Number(e.target.value) })}
            style={{ ...inputStyle, width: 45 }}
          />
          <span style={{ fontSize: 10, color: C.textMuted }}>pct of last</span>
          <input
            type="number" min={1}
            value={(operand as { lookbackBars: number }).lookbackBars}
            onChange={e => onChange({ ...operand, lookbackBars: Number(e.target.value) })}
            style={{ ...inputStyle, width: 45 }}
          />
          <span style={{ fontSize: 10, color: C.textMuted }}>bars</span>
        </div>
      )}

      {/* ── Slope direction ── */}
      {operand.kind === 'slope' && (
        <div style={{ display: 'flex', gap: 4, alignItems: 'center' }}>
          <select
            value={(operand as { indicatorId: string }).indicatorId}
            onChange={e => onChange({ ...operand, indicatorId: e.target.value })}
            style={{ ...selectStyle, flex: 1 }}
          >
            <option value="">— pick —</option>
            {indicators.map(i => (
              <option key={i.id} value={i.id}>{indLabel(i)}</option>
            ))}
          </select>
          <span style={{ fontSize: 10, color: C.textMuted }}>slope over</span>
          <input
            type="number" min={1}
            value={(operand as { lookbackBars: number }).lookbackBars}
            onChange={e => onChange({ ...operand, lookbackBars: Number(e.target.value) })}
            style={{ ...inputStyle, width: 45 }}
          />
          <span style={{ fontSize: 10, color: C.textMuted }}>bars</span>
        </div>
      )}

      {/* ── Session state ── */}
      {operand.kind === 'sessionState' && (
        <select
          value={(operand as { property: SessionStateProperty }).property}
          onChange={e => onChange({ ...operand, property: e.target.value as SessionStateProperty })}
          style={selectStyle}
        >
          {sessionStateOptions.length > 0
            ? sessionStateOptions.map(o => (
                <option key={o.value} value={o.value}>{o.label}</option>
              ))
            : Object.values(SessionStateProperty).map(v => (
                <option key={v} value={v}>{v}</option>
              ))
          }
        </select>
      )}
    </div>
  )
}

function defaultWindow(indicators: IndicatorConfig[]): WindowExpression {
  return {
    sourceIndicatorId: indicators[0]?.id ?? '',
    lookbackBars: 5,
    aggregationType: 'avg' as import('../../types/strategy').AggregationType,
  }
}

const selectStyle: React.CSSProperties = {
  background: C.surface3, border: `1px solid ${C.border}`, color: C.text,
  borderRadius: 4, padding: '4px 6px', fontSize: 11, width: '100%',
}

const inputStyle: React.CSSProperties = {
  background: C.surface3, border: `1px solid ${C.border}`, color: C.text,
  borderRadius: 4, padding: '4px 6px', fontSize: 11, width: '100%',
  boxSizing: 'border-box',
}

const deleteBtnStyle: React.CSSProperties = {
  background: 'none', border: 'none', color: C.textMuted, cursor: 'pointer',
  fontSize: 16, lineHeight: 1, padding: '2px 4px', flexShrink: 0,
}
