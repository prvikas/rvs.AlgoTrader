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

export function ConditionRow({ condition, indicators, onChange, onDelete }: Props) {
  const { enums } = useEnums()
  const opOptions = enums.conditionOp ?? []
  const sessionStateOptions = enums.sessionStateProperty ?? []

  function updateLeft(left: ConditionOperand) { onChange({ ...condition, left }) }
  function updateRight(right: ConditionOperand) { onChange({ ...condition, right }) }
  function updateOp(operator: ConditionOperator) { onChange({ ...condition, operator }) }

  // sessionState boolean properties need no right operand
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
            style={{ ...selectStyle, width: 110, flexShrink: 0 }}
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
          />
        </>
      )}

      <button onClick={onDelete} style={deleteBtnStyle} title="Delete condition">×</button>
    </div>
  )
}

function OperandEditor({ operand, indicators, onChange, sessionStateOptions, isLeft }: {
  operand: ConditionOperand
  indicators: IndicatorConfig[]
  onChange: (o: ConditionOperand) => void
  sessionStateOptions: { value: string; label: string }[]
  isLeft?: boolean
}) {
  // Left operand gets all 7 kinds; right operand only gets indicator/window/value
  const kindOpts: { value: ConditionOperand['kind']; label: string }[] = isLeft
    ? [
        { value: 'indicator',    label: 'Indicator' },
        { value: 'window',       label: 'Window' },
        { value: 'value',        label: 'Value' },
        { value: 'absence',      label: 'Absence (N bars)' },
        { value: 'percentile',   label: 'Percentile Rank' },
        { value: 'slope',        label: 'Slope Direction' },
        { value: 'sessionState', label: 'Session State' },
      ]
    : [
        { value: 'indicator', label: 'Indicator' },
        { value: 'window',    label: 'Window' },
        { value: 'value',     label: 'Value' },
      ]

  function handleKindChange(kind: ConditionOperand['kind']) {
    switch (kind) {
      case 'indicator':    onChange({ kind, indicatorId: indicators[0]?.id ?? '' }); break
      case 'window':       onChange({ kind, expr: defaultWindow(indicators) }); break
      case 'value':        onChange({ kind, value: 0 }); break
      case 'absence':      onChange({ kind, indicatorId: indicators[0]?.id ?? '', lookbackBars: 10 }); break
      case 'percentile':   onChange({ kind, indicatorId: indicators[0]?.id ?? '', lookbackBars: 50, pct: 70 }); break
      case 'slope':        onChange({ kind, indicatorId: indicators[0]?.id ?? '', lookbackBars: 3 }); break
      case 'sessionState': onChange({ kind, property: SessionStateProperty.IsFirstSignalOfSession }); break
    }
  }

  return (
    <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 4 }}>
      <select
        value={operand.kind}
        onChange={e => handleKindChange(e.target.value as ConditionOperand['kind'])}
        style={{ ...selectStyle, fontSize: 10 }}
      >
        {kindOpts.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
      </select>

      {operand.kind === 'indicator' && (
        <div style={{ display: 'flex', gap: 4 }}>
          <select
            value={operand.indicatorId}
            onChange={e => onChange({ ...operand, indicatorId: e.target.value })}
            style={selectStyle}
          >
            <option value="">— pick —</option>
            {indicators.map(i => (
              <option key={i.id} value={i.id}>{i.type} ({i.timeframe})</option>
            ))}
          </select>
          <input
            placeholder="field"
            value={operand.field ?? ''}
            onChange={e => onChange({ ...operand, field: e.target.value || undefined })}
            style={{ ...inputStyle, width: 60 }}
          />
        </div>
      )}

      {operand.kind === 'window' && (
        <WindowExpressionEditor
          value={operand.expr}
          indicators={indicators}
          onChange={expr => onChange({ kind: 'window', expr })}
        />
      )}

      {operand.kind === 'value' && (
        <input
          type="number" step={0.01}
          value={operand.value}
          onChange={e => onChange({ kind: 'value', value: Number(e.target.value) })}
          style={inputStyle}
        />
      )}

      {operand.kind === 'absence' && (
        <div style={{ display: 'flex', gap: 4, alignItems: 'center' }}>
          <select
            value={operand.indicatorId}
            onChange={e => onChange({ ...operand, indicatorId: e.target.value })}
            style={{ ...selectStyle, flex: 1 }}
          >
            <option value="">— pick —</option>
            {indicators.map(i => (
              <option key={i.id} value={i.id}>{i.type} ({i.timeframe})</option>
            ))}
          </select>
          <span style={{ fontSize: 10, color: C.textMuted, whiteSpace: 'nowrap' }}>not in last</span>
          <input
            type="number" min={1}
            value={operand.lookbackBars}
            onChange={e => onChange({ ...operand, lookbackBars: Number(e.target.value) })}
            style={{ ...inputStyle, width: 45 }}
          />
          <span style={{ fontSize: 10, color: C.textMuted }}>bars</span>
        </div>
      )}

      {operand.kind === 'percentile' && (
        <div style={{ display: 'flex', gap: 4, alignItems: 'center', flexWrap: 'wrap' }}>
          <select
            value={operand.indicatorId}
            onChange={e => onChange({ ...operand, indicatorId: e.target.value })}
            style={{ ...selectStyle, flex: 1 }}
          >
            <option value="">— pick —</option>
            {indicators.map(i => (
              <option key={i.id} value={i.id}>{i.type} ({i.timeframe})</option>
            ))}
          </select>
          <span style={{ fontSize: 10, color: C.textMuted }}>≥</span>
          <input
            type="number" min={0} max={100}
            value={operand.pct}
            onChange={e => onChange({ ...operand, pct: Number(e.target.value) })}
            style={{ ...inputStyle, width: 45 }}
          />
          <span style={{ fontSize: 10, color: C.textMuted }}>pct of last</span>
          <input
            type="number" min={1}
            value={operand.lookbackBars}
            onChange={e => onChange({ ...operand, lookbackBars: Number(e.target.value) })}
            style={{ ...inputStyle, width: 45 }}
          />
          <span style={{ fontSize: 10, color: C.textMuted }}>bars</span>
        </div>
      )}

      {operand.kind === 'slope' && (
        <div style={{ display: 'flex', gap: 4, alignItems: 'center' }}>
          <select
            value={operand.indicatorId}
            onChange={e => onChange({ ...operand, indicatorId: e.target.value })}
            style={{ ...selectStyle, flex: 1 }}
          >
            <option value="">— pick —</option>
            {indicators.map(i => (
              <option key={i.id} value={i.id}>{i.type} ({i.timeframe})</option>
            ))}
          </select>
          <span style={{ fontSize: 10, color: C.textMuted }}>slope over</span>
          <input
            type="number" min={1}
            value={operand.lookbackBars}
            onChange={e => onChange({ ...operand, lookbackBars: Number(e.target.value) })}
            style={{ ...inputStyle, width: 45 }}
          />
          <span style={{ fontSize: 10, color: C.textMuted }}>bars</span>
        </div>
      )}

      {operand.kind === 'sessionState' && (
        <select
          value={operand.property}
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
