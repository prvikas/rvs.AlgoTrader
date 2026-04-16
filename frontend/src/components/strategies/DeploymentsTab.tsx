import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { strategyDomainApi } from '../../api/client'
import {
  Broker, DayOfWeek, Deployment, DeploymentMode, DeploymentStatus,
  Scenario, Strategy, Timeframe,
} from '../../types/strategy'
import { C, F, SP, TABLE_CELL } from '../../styles/tokens'
import { RightDrawer } from '../ui/RightDrawer'
import { useEnums } from '../../context/EnumsContext'
import { useFeatures } from '../../context/FeaturesContext'

interface Props {
  strategy: Strategy
}

function StatusChip({ status }: { status: DeploymentStatus }) {
  const colorMap: Record<string, string> = {
    [DeploymentStatus.Draft]: C.textMuted,
    [DeploymentStatus.Running]: C.amber,
    [DeploymentStatus.Backtested]: C.blue,
    [DeploymentStatus.FwdTesting]: C.blue,
    [DeploymentStatus.LiveCandidate]: C.green,
    [DeploymentStatus.Live]: C.green,
    [DeploymentStatus.Archived]: C.textDim,
  }
  const color = colorMap[status] ?? C.textMuted
  return (
    <span style={{
      fontSize: 10, fontWeight: 700, color, padding: '2px 6px',
      borderRadius: 3, border: `1px solid ${color}44`, background: `${color}11`,
    }}>
      {status}
    </span>
  )
}

export function DeploymentsTab({ strategy }: Props) {
  const qc = useQueryClient()
  const [drawerOpen, setDrawerOpen] = useState(false)

  const { data: deployments = [], isLoading, error, refetch } = useQuery({
    queryKey: ['deployments', strategy.id],
    queryFn: () => strategyDomainApi.listDeployments(strategy.id),
  })

  const { data: scenarios = [] } = useQuery({
    queryKey: ['scenarios', strategy.id],
    queryFn: () => strategyDomainApi.listScenarios(strategy.id),
  })

  const deleteMut = useMutation({
    mutationFn: (did: string) => strategyDomainApi.deleteDeployment(strategy.id, did),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['deployments', strategy.id] }),
  })

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: SP.md }}>
        <button onClick={() => setDrawerOpen(true)} style={primaryBtnStyle}>+ New Deployment</button>
      </div>

      {isLoading && <SkeletonRows />}

      {!isLoading && error && (
        <div style={{ color: C.red, fontSize: 12 }}>
          Failed to load deployments.{' '}
          <button onClick={refetch as () => void} style={{ color: C.blue, background: 'none', border: 'none', cursor: 'pointer', fontSize: 12 }}>Retry?</button>
        </div>
      )}

      {!isLoading && !error && deployments.length === 0 && (
        <div style={{ textAlign: 'center', padding: 40, color: C.textMuted }}>
          <div style={{ marginBottom: SP.sm }}>No deployments yet.</div>
          <button onClick={() => setDrawerOpen(true)} style={primaryBtnStyle}>+ New Deployment</button>
        </div>
      )}

      {!isLoading && !error && deployments.length > 0 && (
        <div style={{ overflowX: 'auto' }}>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
            <thead>
              <tr style={{ background: C.surface2 }}>
                {['Name', 'Scenario', 'Symbol', 'TF', 'Mode', 'Broker', 'Capital', 'Status', 'Actions'].map(h => (
                  <th key={h} style={thStyle}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {deployments.map(d => {
                const scenarioName = scenarios.find(s => s.id === d.scenarioId)?.name ?? d.scenarioId
                return (
                  <tr key={d.id} style={{ borderBottom: `1px solid ${C.border2}` }}>
                    <td style={tdStyle}>{d.name}</td>
                    <td style={{ ...tdStyle, color: C.textSub }}>{scenarioName}</td>
                    <td style={{ ...tdStyle, fontFamily: F.mono }}>{d.symbol}</td>
                    <td style={{ ...tdStyle, color: C.textMuted }}>{d.timeframe}</td>
                    <td style={tdStyle}>{d.mode}</td>
                    <td style={tdStyle}>{d.broker}</td>
                    <td style={{ ...tdStyle, fontFamily: F.mono, textAlign: 'right' }}>
                      ₹{(d.allocatedCapital / 1000).toFixed(0)}K
                    </td>
                    <td style={tdStyle}><StatusChip status={d.status} /></td>
                    <td style={tdStyle}>
                      <button
                        onClick={() => deleteMut.mutate(d.id)}
                        disabled={deleteMut.isPending}
                        style={{
                          background: C.redBg, color: C.red, border: `1px solid ${C.red}44`,
                          borderRadius: 4, padding: '3px 8px', cursor: 'pointer', fontSize: 11,
                        }}
                      >
                        Delete
                      </button>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}

      <RightDrawer
        isOpen={drawerOpen}
        title="New Deployment"
        onClose={() => setDrawerOpen(false)}
      >
        <DeploymentDrawerForm
          strategy={strategy}
          scenarios={scenarios}
          onClose={() => setDrawerOpen(false)}
          onSaved={() => {
            qc.invalidateQueries({ queryKey: ['deployments', strategy.id] })
            setDrawerOpen(false)
          }}
        />
      </RightDrawer>
    </div>
  )
}

function DeploymentDrawerForm({ strategy, scenarios, onClose, onSaved }: {
  strategy: Strategy
  scenarios: Scenario[]
  onClose: () => void
  onSaved: () => void
}) {
  const { enums } = useEnums()
  const { brokerRequired } = useFeatures()
  const tfOptions = enums.timeframe ?? []
  // In backtest-only mode, hide Forward and Live deployment modes
  const modeOptions = (enums.deploymentMode ?? []).filter(o =>
    brokerRequired || (o.value !== DeploymentMode.ForwardTest && o.value !== DeploymentMode.Live)
  )
  const brokerOptions = enums.broker ?? []
  const dowOptions = enums.dayOfWeek ?? []

  const [name, setName] = useState('')
  const [scenarioId, setScenarioId] = useState(scenarios[0]?.id ?? '')
  const [symbol, setSymbol] = useState('')
  const [timeframe, setTimeframe] = useState<Timeframe>(strategy.primaryTimeframe)
  const [mode, setMode] = useState<DeploymentMode>(DeploymentMode.Backtest)
  const [broker, setBroker] = useState<Broker>(Broker.MStock)
  const [capital, setCapital] = useState(100000)
  const [days, setDays] = useState<DayOfWeek[]>([DayOfWeek.Mon, DayOfWeek.Tue, DayOfWeek.Wed, DayOfWeek.Thu, DayOfWeek.Fri])
  const [startTime, setStartTime] = useState('09:15')
  const [endTime, setEndTime] = useState('15:20')
  const [errors, setErrors] = useState<Record<string, string>>({})

  function toggleDay(d: DayOfWeek) {
    setDays(prev => prev.includes(d) ? prev.filter(x => x !== d) : [...prev, d])
  }

  const createMut = useMutation({
    mutationFn: (d: Omit<Deployment, 'id' | 'strategyId'>) =>
      strategyDomainApi.createDeployment(strategy.id, d),
    onSuccess: onSaved,
  })

  function validate(): boolean {
    const e: Record<string, string> = {}
    if (!name.trim()) e.name = 'Required'
    if (!scenarioId) e.scenario = 'Required'
    if (!symbol.trim()) e.symbol = 'Required'
    if (capital <= 0) e.capital = 'Must be > 0'
    setErrors(e)
    return Object.keys(e).length === 0
  }

  function save() {
    if (!validate()) return
    createMut.mutate({
      scenarioId, name: name.trim(), symbol: symbol.trim(),
      timeframe, mode, broker, allocatedCapital: capital,
      schedule: { days, startTime, endTime },
      status: DeploymentStatus.Draft,
    })
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: SP.md }}>
      <Field label="Deployment Name *" error={errors.name}>
        <input value={name} onChange={e => setName(e.target.value)} style={inputStyle} />
      </Field>
      <Field label="Scenario *" error={errors.scenario}>
        <select value={scenarioId} onChange={e => setScenarioId(e.target.value)} style={inputStyle}>
          <option value="">— select —</option>
          {scenarios.map(s => <option key={s.id} value={s.id}>{s.name}</option>)}
        </select>
      </Field>
      <Field label="Symbol *" error={errors.symbol}>
        <input value={symbol} onChange={e => setSymbol(e.target.value)} placeholder="NSE:AXISBANK" style={inputStyle} />
      </Field>
      <Field label="Timeframe *">
        <select value={timeframe} onChange={e => setTimeframe(e.target.value as Timeframe)} style={inputStyle}>
          {tfOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
        </select>
      </Field>
      <Field label="Mode *">
        <select value={mode} onChange={e => setMode(e.target.value as DeploymentMode)} style={inputStyle}>
          {modeOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
        </select>
      </Field>
      <Field label="Broker *">
        <select value={broker} onChange={e => setBroker(e.target.value as Broker)} style={inputStyle}>
          {brokerOptions.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
        </select>
      </Field>
      <Field label="Allocated Capital (₹) *" error={errors.capital}>
        <input type="number" min={1} value={capital} onChange={e => setCapital(Number(e.target.value))} style={inputStyle} />
      </Field>

      <div>
        <label style={{ fontSize: 11, color: C.textMuted, display: 'block', marginBottom: 6 }}>Schedule Days</label>
        <div style={{ display: 'flex', gap: SP.xs, flexWrap: 'wrap' }}>
          {dowOptions.map(o => (
            <button
              key={o.value}
              onClick={() => toggleDay(o.value as DayOfWeek)}
              style={{
                padding: '3px 8px', borderRadius: 4, fontSize: 11, cursor: 'pointer',
                background: days.includes(o.value as DayOfWeek) ? C.blueBg : C.surface2,
                color: days.includes(o.value as DayOfWeek) ? C.blue : C.textMuted,
                border: `1px solid ${days.includes(o.value as DayOfWeek) ? C.blue + '66' : C.border}`,
              }}
            >
              {o.label}
            </button>
          ))}
        </div>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: SP.sm }}>
        <Field label="Start Time">
          <input type="time" value={startTime} onChange={e => setStartTime(e.target.value)} style={inputStyle} />
        </Field>
        <Field label="End Time">
          <input type="time" value={endTime} onChange={e => setEndTime(e.target.value)} style={inputStyle} />
        </Field>
      </div>

      <div style={{ display: 'flex', gap: SP.sm, paddingTop: SP.sm, borderTop: `1px solid ${C.border}` }}>
        <button onClick={onClose} style={cancelBtnStyle}>Cancel</button>
        <button onClick={save} disabled={createMut.isPending} style={primaryBtnStyle}>
          {createMut.isPending ? '…' : 'Create Deployment'}
        </button>
      </div>
    </div>
  )
}

function SkeletonRows() {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
      {[1, 2, 3].map(i => (
        <div key={i} style={{ height: 32, background: C.surface2, borderRadius: 4, opacity: 0.5 }} />
      ))}
    </div>
  )
}

function Field({ label, error, children }: { label: string; error?: string; children: React.ReactNode }) {
  return (
    <div>
      <label style={{ fontSize: 11, color: C.textMuted, display: 'block', marginBottom: 3 }}>{label}</label>
      {children}
      {error && <div style={{ fontSize: 10, color: C.red, marginTop: 2 }}>{error}</div>}
    </div>
  )
}

const inputStyle: React.CSSProperties = {
  width: '100%', background: C.surface3, border: `1px solid ${C.border}`,
  color: C.text, borderRadius: 4, padding: '6px 8px', fontSize: 12,
  boxSizing: 'border-box',
}

const thStyle: React.CSSProperties = {
  padding: TABLE_CELL, textAlign: 'left', fontSize: 11, color: C.textMuted, fontWeight: 600,
  borderBottom: `1px solid ${C.border}`,
}

const tdStyle: React.CSSProperties = { padding: TABLE_CELL, fontSize: 12, color: C.text }

const primaryBtnStyle: React.CSSProperties = {
  flex: 1, background: '#1e3a5f', color: '#93c5fd', border: '1px solid #2563eb44',
  borderRadius: 5, padding: '7px 14px', cursor: 'pointer', fontSize: 12, fontWeight: 700,
}

const cancelBtnStyle: React.CSSProperties = {
  background: 'none', color: C.textMuted, border: `1px solid ${C.border}`,
  borderRadius: 5, padding: '7px 14px', cursor: 'pointer', fontSize: 12,
}
