import { C, SP, TABLE_CELL } from '../../styles/tokens'
import {
  Condition, ConditionOperand, ConditionOperator,
  IndicatorConfig, WindowExpression,
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

  function updateLeft(left: ConditionOperand) { onChange({ ...condition, left }) }
  function updateRight(right: ConditionOperand) { onChange({ ...condition, right }) }
  function updateOp(operator: ConditionOperator) { onChange({ ...condition, operator }) }

  return (
    <div style={{
      display: 'flex', alignItems: 'flex-start', gap: SP.sm,
      padding: TABLE_CELL, borderBottom: `1px solid ${C.border2}`,
    }}>
      <OperandEditor operand={condition.left} indicators={indicators} onChange={updateLeft} />

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

      <OperandEditor operand={condition.right} indicators={indicators} onChange={updateRight} />

      <button onClick={onDelete} style={deleteBtnStyle} title="Delete condition">×</button>
    </div>
  )
}

function OperandEditor({ operand, indicators, onChange }: {
  operand: ConditionOperand
  indicators: IndicatorConfig[]
  onChange: (o: ConditionOperand) => void
}) {
  const kindOpts: { value: ConditionOperand['kind']; label: string }[] = [
    { value: 'indicator', label: 'Indicator' },
    { value: 'window',    label: 'Window' },
    { value: 'value',     label: 'Value' },
  ]

  return (
    <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 4 }}>
      <select
        value={operand.kind}
        onChange={e => {
          const kind = e.target.value as ConditionOperand['kind']
          if (kind === 'indicator') onChange({ kind, indicatorId: indicators[0]?.id ?? '' })
          else if (kind === 'window') onChange({ kind, expr: defaultWindow(indicators) })
          else onChange({ kind, value: 0 })
        }}
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
