import { useState } from 'react'
import { C, SP } from '../../styles/tokens'
import {
  Condition, ConditionOperator, ExitCombineLogic,
  IndicatorConfig, RuleGroup,
} from '../../types/strategy'
import { useEnums } from '../../context/EnumsContext'
import { ConditionRow } from './ConditionRow'

interface Props {
  group: RuleGroup
  indicators: IndicatorConfig[]
  onChange: (g: RuleGroup) => void
  onDelete: () => void
  onDuplicate: () => void
}

let _conditionId = 0
function newConditionId() { return `cond-${Date.now()}-${_conditionId++}` }

export function RuleGroupEditor({ group, indicators, onChange, onDelete, onDuplicate }: Props) {
  const { enums } = useEnums()
  const logicOptions = enums.exitCombineLogic ?? []
  const [expanded, setExpanded] = useState(true)

  function updateCondition(idx: number, c: Condition) {
    const conditions = [...group.conditions]
    conditions[idx] = c
    onChange({ ...group, conditions })
  }

  function deleteCondition(idx: number) {
    onChange({ ...group, conditions: group.conditions.filter((_, i) => i !== idx) })
  }

  function addCondition() {
    const blank: Condition = {
      id: newConditionId(),
      left: { kind: 'indicator', indicatorId: indicators[0]?.id ?? '' },
      operator: ConditionOperator.Gt,
      right: { kind: 'value', value: 0 },
    }
    onChange({ ...group, conditions: [...group.conditions, blank] })
  }

  return (
    <div style={{
      border: `1px solid ${C.border}`, borderRadius: 6, marginBottom: SP.sm,
      background: C.surface,
    }}>
      {/* Header */}
      <div style={{
        display: 'flex', alignItems: 'center', gap: SP.sm,
        padding: '6px 10px', background: C.surface2, borderRadius: '6px 6px 0 0',
        borderBottom: expanded ? `1px solid ${C.border}` : 'none',
      }}>
        <button
          onClick={() => setExpanded(x => !x)}
          style={{ background: 'none', border: 'none', color: C.textSub, cursor: 'pointer', fontSize: 11 }}
        >
          {expanded ? '▼' : '▶'}
        </button>
        <input
          value={group.label}
          onChange={e => onChange({ ...group, label: e.target.value })}
          style={{
            flex: 1, background: 'none', border: 'none', color: C.text, fontSize: 12,
            fontWeight: 600, outline: 'none',
          }}
        />
        <select
          value={group.logicalOperator}
          onChange={e => onChange({ ...group, logicalOperator: e.target.value as ExitCombineLogic })}
          style={{ background: C.surface3, border: `1px solid ${C.border}`, color: C.textSub, borderRadius: 4, padding: '2px 6px', fontSize: 10 }}
        >
          {logicOptions.map(o => (
            <option key={o.value} value={o.value}>{o.label}</option>
          ))}
        </select>
        <span style={{ fontSize: 10, color: C.textMuted }}>{group.conditions.length} cond.</span>
        <button onClick={onDuplicate} style={actionBtnStyle} title="Duplicate group">⧉</button>
        <button onClick={onDelete} style={{ ...actionBtnStyle, color: C.red }} title="Delete group">×</button>
      </div>

      {/* Conditions */}
      {expanded && (
        <div>
          {group.conditions.length === 0 && (
            <div style={{ padding: '10px 12px', fontSize: 11, color: C.textMuted }}>
              No conditions. Add one below.
            </div>
          )}
          {group.conditions.map((c, i) => (
            <ConditionRow
              key={c.id}
              condition={c}
              indicators={indicators}
              onChange={updated => updateCondition(i, updated)}
              onDelete={() => deleteCondition(i)}
            />
          ))}
          <div style={{ padding: '6px 10px' }}>
            <button onClick={addCondition} style={addBtnStyle}>+ Condition</button>
          </div>
        </div>
      )}
    </div>
  )
}

const actionBtnStyle: React.CSSProperties = {
  background: 'none', border: 'none', color: C.textMuted, cursor: 'pointer', fontSize: 13,
  padding: '2px 4px',
}

const addBtnStyle: React.CSSProperties = {
  background: 'none', border: `1px dashed ${C.border3}`, color: C.textSub,
  borderRadius: 4, padding: '4px 10px', cursor: 'pointer', fontSize: 11,
}
