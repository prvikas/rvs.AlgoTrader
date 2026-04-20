import { C, SP } from '../../styles/tokens'
import { AggregationType, IndicatorConfig, WindowExpression } from '../../types/strategy'
import { useEnums } from '../../context/EnumsContext'

interface Props {
  value: WindowExpression
  indicators: IndicatorConfig[]
  onChange: (v: WindowExpression) => void
}

export function WindowExpressionEditor({ value, indicators, onChange }: Props) {
  const { enums } = useEnums()
  const aggOptions = enums.aggregationType ?? []

  return (
    <div style={{
      display: 'grid', gridTemplateColumns: '1fr 80px 1fr 80px',
      gap: SP.sm, background: C.surface2, borderRadius: 4, padding: SP.sm,
    }}>
      {/* Source indicator */}
      <div>
        <label style={{ fontSize: 10, color: C.textMuted, display: 'block', marginBottom: 2 }}>Source</label>
        <select
          value={value.sourceIndicatorId}
          onChange={e => onChange({ ...value, sourceIndicatorId: e.target.value })}
          style={selectStyle}
        >
          <option value="">— pick —</option>
          {indicators.map(i => (
            <option key={i.id} value={i.id}>
              {i.label?.trim() ? i.label.trim() : `${i.type} (${i.timeframe})`}
            </option>
          ))}
        </select>
      </div>

      {/* Lookback */}
      <div>
        <label style={{ fontSize: 10, color: C.textMuted, display: 'block', marginBottom: 2 }}>Bars</label>
        <input
          type="number" min={1}
          value={value.lookbackBars}
          onChange={e => onChange({ ...value, lookbackBars: Number(e.target.value) })}
          style={inputStyle}
        />
      </div>

      {/* Aggregation */}
      <div>
        <label style={{ fontSize: 10, color: C.textMuted, display: 'block', marginBottom: 2 }}>Agg</label>
        <select
          value={value.aggregationType}
          onChange={e => onChange({ ...value, aggregationType: e.target.value as AggregationType })}
          style={selectStyle}
        >
          {aggOptions.map(o => (
            <option key={o.value} value={o.value}>{o.label}</option>
          ))}
        </select>
      </div>

      {/* Multiplier */}
      <div>
        <label style={{ fontSize: 10, color: C.textMuted, display: 'block', marginBottom: 2 }}>×</label>
        <input
          type="number" step={0.01}
          value={value.multiplier ?? ''}
          placeholder="1"
          onChange={e => onChange({ ...value, multiplier: e.target.value ? Number(e.target.value) : undefined })}
          style={inputStyle}
        />
      </div>
    </div>
  )
}

const selectStyle: React.CSSProperties = {
  width: '100%', background: C.surface3, border: `1px solid ${C.border}`,
  color: C.text, borderRadius: 4, padding: '4px 6px', fontSize: 11,
}

const inputStyle: React.CSSProperties = {
  width: '100%', background: C.surface3, border: `1px solid ${C.border}`,
  color: C.text, borderRadius: 4, padding: '4px 6px', fontSize: 11,
  boxSizing: 'border-box',
}
