/**
 * PromoteToForwardTestModal — shown when user clicks "▶ Forward Test" on a backtest result.
 *
 * Pre-fills: strategy, symbol, timeframe from the source backtest (read-only display).
 * User fills: instance name, broker, initial capital, schedule (optional).
 */

import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { forwardTestApi, BacktestResult } from '../../api/client'
import { C } from '../../styles/tokens'
import { formatInr } from '../../utils/datetime'

const BROKERS = ['MStock', 'Zerodha', 'Upstox']

interface Props {
  backtest: BacktestResult
  onClose: () => void
}

export function PromoteToForwardTestModal({ backtest, onClose }: Props) {
  const qc = useQueryClient()
  const [name, setName] = useState(`${backtest.strategyName} - ${backtest.symbol} FwdTest`)
  const [brokerName, setBrokerName] = useState(localStorage.getItem('active_broker') || 'MStock')
  const [capital, setCapital] = useState(backtest.initialCapital ?? 100000)
  const [error, setError] = useState('')

  const mutation = useMutation({
    mutationFn: () => forwardTestApi.promoteFromBacktest({
      backtestId: backtest.id!,
      instanceName: name.trim(),
      brokerName,
      initialCapital: capital,
    }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['forward-tests'] })
      qc.invalidateQueries({ queryKey: ['strategies'] })
      onClose()
    },
    onError: (err: any) => {
      setError(err.response?.data?.error || 'Failed to create forward test instance.')
    },
  })

  const canSubmit = name.trim().length > 0 && capital > 0 && backtest.id

  return (
    <div style={{
      position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.75)',
      display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000,
    }}>
      <div style={{
        background: C.surface, border: `2px solid ${C.blue}`,
        borderRadius: 10, padding: 28, maxWidth: 500, width: '90%',
      }}>
        <h3 style={{ margin: '0 0 4px 0', fontSize: 17, fontWeight: 700, color: C.textSub }}>
          🧪 Promote to Forward Test
        </h3>
        <p style={{ margin: '0 0 18px 0', fontSize: 12, color: C.textMuted }}>
          Creates a new Forward Test strategy instance pre-filled from this backtest run.
          No real orders will be placed.
        </p>

        {/* Source backtest summary */}
        <div style={{
          background: C.bg, border: `1px solid ${C.border2}`,
          borderRadius: 6, padding: '10px 14px', marginBottom: 18,
          display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: 10,
        }}>
          <InfoField label="Strategy" value={backtest.strategyName} />
          <InfoField label="Symbol" value={backtest.symbol} />
          <InfoField label="Timeframe" value={backtest.timeframe} />
          <InfoField label="Win Rate" value={`${(backtest.winRate * 100).toFixed(1)}%`} valueColor={backtest.winRate >= 0.5 ? C.green : C.amber} />
          <InfoField label="Net P&L" value={formatInr(backtest.totalPnl)} valueColor={backtest.totalPnl >= 0 ? C.green : C.red} />
          <InfoField label="Sharpe" value={backtest.sharpeRatio.toFixed(2)} />
        </div>

        {/* User inputs */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
          <FormRow label="Instance Name *">
            <input
              value={name}
              onChange={e => setName(e.target.value)}
              style={inputStyle}
              placeholder="e.g., RELIANCE FwdTest #1"
            />
          </FormRow>

          <FormRow label="Broker">
            <select value={brokerName} onChange={e => setBrokerName(e.target.value)} style={inputStyle}>
              {BROKERS.map(b => <option key={b} value={b}>{b}</option>)}
            </select>
          </FormRow>

          <FormRow label="Initial Capital (₹)">
            <input
              type="number"
              min={1000}
              step={1000}
              value={capital}
              onChange={e => setCapital(Number(e.target.value) || 100000)}
              style={inputStyle}
            />
          </FormRow>
        </div>

        {error && (
          <div style={{ background: C.redBg, color: C.red, padding: '8px 12px', borderRadius: 6, fontSize: 13, marginTop: 14 }}>
            {error}
          </div>
        )}

        <div style={{ display: 'flex', gap: 10, justifyContent: 'flex-end', marginTop: 20 }}>
          <button
            onClick={onClose}
            style={{ padding: '8px 16px', background: C.surface3, color: C.text, border: `1px solid ${C.border2}`, borderRadius: 6, cursor: 'pointer', fontSize: 14 }}
          >
            Cancel
          </button>
          <button
            onClick={() => mutation.mutate()}
            disabled={!canSubmit || mutation.isPending}
            style={{
              padding: '8px 18px', background: canSubmit ? C.blue : C.surface3,
              color: 'white', border: 'none', borderRadius: 6,
              cursor: canSubmit ? 'pointer' : 'not-allowed',
              fontSize: 14, fontWeight: 700, opacity: mutation.isPending ? 0.7 : 1,
            }}
          >
            {mutation.isPending ? 'Creating…' : '🧪 Start Forward Test'}
          </button>
        </div>
      </div>
    </div>
  )
}

function InfoField({ label, value, valueColor = C.text }: { label: string; value: string; valueColor?: string }) {
  return (
    <div>
      <div style={{ fontSize: 10, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.05em', marginBottom: 2 }}>{label}</div>
      <div style={{ fontSize: 13, fontWeight: 700, color: valueColor }}>{value}</div>
    </div>
  )
}

function FormRow({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <label style={{ display: 'block', fontSize: 12, fontWeight: 600, color: C.textSub, marginBottom: 5 }}>{label}</label>
      {children}
    </div>
  )
}

const inputStyle: React.CSSProperties = {
  width: '100%', background: C.bg, border: `1px solid ${C.border2}`,
  borderRadius: 5, color: C.text, padding: '8px 10px', fontSize: 13,
  boxSizing: 'border-box',
}
