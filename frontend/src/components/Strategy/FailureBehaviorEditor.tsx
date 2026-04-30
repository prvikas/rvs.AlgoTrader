import { C } from '../../styles/tokens'

/**
 * FailureBehaviorEditor — inline editor for the failure_behavior_json column.
 *
 * Shape (matches CLAUDE.md §failure_behavior_json):
 * {
 *   OnBrokerCircuitOpen: "PAUSE_INSTANCE" | "STOP_INSTANCE" | "LOG_AND_SKIP"
 *   OnStreamDisconnect:  "PAUSE_NEW_SIGNALS" | "PAUSE_INSTANCE" | "STOP_INSTANCE"
 *   OnDataStale:         "PAUSE_INSTANCE" | "STOP_INSTANCE" | "LOG_AND_SKIP"
 *   DataStalenessThresholdMinutes: number
 *   OnRiskLimitBreached: "STOP_INSTANCE" | "PAUSE_INSTANCE"
 *   OnEvaluationTimeout: "LOG_AND_SKIP" | "PAUSE_INSTANCE" | "STOP_INSTANCE"
 * }
 */

export interface FailureBehaviorConfig {
  OnBrokerCircuitOpen: string
  OnStreamDisconnect: string
  OnDataStale: string
  DataStalenessThresholdMinutes: number
  OnRiskLimitBreached: string
  OnEvaluationTimeout: string
}

export const defaultFailureBehavior = (): FailureBehaviorConfig => ({
  OnBrokerCircuitOpen: 'PAUSE_INSTANCE',
  OnStreamDisconnect: 'PAUSE_NEW_SIGNALS',
  OnDataStale: 'PAUSE_INSTANCE',
  DataStalenessThresholdMinutes: 5,
  OnRiskLimitBreached: 'STOP_INSTANCE',
  OnEvaluationTimeout: 'LOG_AND_SKIP',
})

export function failureBehaviorToJson(cfg: FailureBehaviorConfig): string {
  return JSON.stringify(cfg)
}

interface Props {
  value: FailureBehaviorConfig
  onChange: (cfg: FailureBehaviorConfig) => void
}

const ACTIONS_CIRCUIT = ['PAUSE_INSTANCE', 'STOP_INSTANCE', 'LOG_AND_SKIP']
const ACTIONS_STREAM = ['PAUSE_NEW_SIGNALS', 'PAUSE_INSTANCE', 'STOP_INSTANCE']
const ACTIONS_RISK = ['STOP_INSTANCE', 'PAUSE_INSTANCE']
const ACTIONS_TIMEOUT = ['LOG_AND_SKIP', 'PAUSE_INSTANCE', 'STOP_INSTANCE']

// Color-coded severity of each action choice
function actionColor(action: string): string {
  if (action === 'STOP_INSTANCE') return C.red
  if (action === 'PAUSE_INSTANCE') return C.amber
  if (action === 'PAUSE_NEW_SIGNALS') return C.amber
  return C.textMuted
}

const selectStyle: React.CSSProperties = {
  background: C.bg,
  border: `1px solid ${C.border2}`,
  borderRadius: 4,
  color: C.text,
  padding: '6px 10px',
  fontSize: 13,
  width: '100%',
}

const labelStyle: React.CSSProperties = {
  display: 'block',
  fontSize: 12,
  color: C.textMuted,
  marginBottom: 4,
  fontWeight: 600,
}

function FieldSelect({
  label,
  value,
  options,
  onChange,
  hint,
}: {
  label: string
  value: string
  options: string[]
  onChange: (v: string) => void
  hint?: string
}) {
  return (
    <div style={{ marginBottom: 14 }}>
      <label style={labelStyle}>{label}</label>
      <select
        value={value}
        onChange={e => onChange(e.target.value)}
        style={{ ...selectStyle, color: actionColor(value) }}
      >
        {options.map(opt => (
          <option key={opt} value={opt} style={{ color: actionColor(opt) }}>{opt}</option>
        ))}
      </select>
      {hint && <p style={{ color: C.textMuted, fontSize: 11, marginTop: 3 }}>{hint}</p>}
    </div>
  )
}

export function FailureBehaviorEditor({ value, onChange }: Props) {
  const set = (key: keyof FailureBehaviorConfig) => (v: string | number) =>
    onChange({ ...value, [key]: v })

  return (
    <div style={{
      background: C.bg,
      border: `1px solid ${C.border2}`,
      borderRadius: 6,
      padding: '14px 16px',
    }}>
      <p style={{ color: C.textMuted, fontSize: 12, marginTop: 0, marginBottom: 12 }}>
        Controls what happens when each type of fault occurs. Conservative defaults are pre-filled.
        <br />
        <strong style={{ color: C.red }}>STOP_INSTANCE</strong> = full stop + requires manual restart.{' '}
        <strong style={{ color: C.amber }}>PAUSE_INSTANCE</strong> = signals paused, positions still managed.{' '}
        <strong style={{ color: C.textMuted }}>LOG_AND_SKIP</strong> = warn + skip this cycle.
      </p>

      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0 20px' }}>
        <FieldSelect
          label="On Broker Circuit Open"
          value={value.OnBrokerCircuitOpen}
          options={ACTIONS_CIRCUIT}
          onChange={set('OnBrokerCircuitOpen')}
          hint="Broker order API unavailable (circuit breaker tripped)"
        />
        <FieldSelect
          label="On Stream Disconnect"
          value={value.OnStreamDisconnect}
          options={ACTIONS_STREAM}
          onChange={set('OnStreamDisconnect')}
          hint="WebSocket market data feed disconnected"
        />
        <FieldSelect
          label="On Data Stale"
          value={value.OnDataStale}
          options={ACTIONS_CIRCUIT}
          onChange={set('OnDataStale')}
          hint="No new candle received within the staleness threshold"
        />
        <div style={{ marginBottom: 14 }}>
          <label style={labelStyle}>Data Staleness Threshold (min)</label>
          <input
            type="number"
            min={1}
            max={60}
            value={value.DataStalenessThresholdMinutes}
            onChange={e => set('DataStalenessThresholdMinutes')(parseInt(e.target.value) || 5)}
            style={{ ...selectStyle, color: C.text }}
          />
          <p style={{ color: C.textMuted, fontSize: 11, marginTop: 3 }}>Minutes without a new bar before triggering OnDataStale</p>
        </div>
        <FieldSelect
          label="On Risk Limit Breached"
          value={value.OnRiskLimitBreached}
          options={ACTIONS_RISK}
          onChange={set('OnRiskLimitBreached')}
          hint="Daily drawdown or max-trades-per-day limit exceeded"
        />
        <FieldSelect
          label="On Evaluation Timeout"
          value={value.OnEvaluationTimeout}
          options={ACTIONS_TIMEOUT}
          onChange={set('OnEvaluationTimeout')}
          hint="Strategy EvaluateAsync exceeded the time budget"
        />
      </div>
    </div>
  )
}
