/**
 * PreFlightChecklistModal — shown before promoting a forward test to live.
 *
 * Calls the promote-to-live endpoint which runs 7 pre-flight checks server-side.
 * Displays each check result (pass/fail). If all pass, enables the "Go Live" button.
 * If any fail, shows failures with reasons and an override option.
 */

import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { forwardTestApi, ForwardTestSession, PreFlightCheck } from '../../api/client'
import { formatInr } from '../../utils/datetime'

const BROKERS = ['MStock', 'Zerodha', 'Upstox']

interface Props {
  session: ForwardTestSession
  onClose: () => void
}

type Step = 'config' | 'checks' | 'done'

export function PreFlightChecklistModal({ session, onClose }: Props) {
  const qc = useQueryClient()
  const [step, setStep] = useState<Step>('config')
  const [brokerName, setBrokerName] = useState(session.brokerName || localStorage.getItem('active_broker') || 'MStock')
  const [capital, setCapital] = useState(session.initialCapital)
  const [checks, setChecks] = useState<PreFlightCheck[]>([])
  const [newInstanceId, setNewInstanceId] = useState<string | null>(null)
  const [overrideMode, setOverrideMode] = useState(false)
  const [overrideConfirm, setOverrideConfirm] = useState(false)

  const promoteMutation = useMutation({
    mutationFn: () => forwardTestApi.promoteToLive(session.instanceId, {
      brokerName,
      allocatedCapital: capital,
    }),
    onSuccess: (res) => {
      const result = res.data.data
      if (!result) return
      setChecks(result.checks)
      if (result.success) {
        setNewInstanceId(result.newStrategyInstanceId ?? null)
        setStep('done')
        qc.invalidateQueries({ queryKey: ['strategies'] })
        qc.invalidateQueries({ queryKey: ['forward-tests'] })
      } else {
        setStep('checks')
      }
    },
  })

  const allPassed = checks.every(c => c.passed)
  const failedChecks = checks.filter(c => !c.passed)

  return (
    <div style={{
      position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.8)',
      display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000,
    }}>
      <div style={{
        background: '#1e1e2e', border: `2px solid ${step === 'done' ? '#16a34a' : '#f59e0b'}`,
        borderRadius: 10, padding: 28, maxWidth: 520, width: '90%',
      }}>

        {/* ── Step 1: Config ───────────────────────────────────────────── */}
        {step === 'config' && (
          <>
            <h3 style={{ margin: '0 0 4px 0', fontSize: 17, fontWeight: 700, color: '#fbbf24' }}>
              🚀 Pre-Flight Check — Going Live
            </h3>
            <p style={{ margin: '0 0 16px 0', fontSize: 12, color: '#64748b' }}>
              Confirm the live trading configuration. We'll run 7 checks before creating the live instance.
            </p>

            {/* Session summary */}
            <div style={{ background: '#13131f', borderRadius: 6, padding: '10px 14px', marginBottom: 16, display: 'flex', gap: 16, flexWrap: 'wrap' }}>
              <Summary label="Strategy" value={session.strategyType} />
              <Summary label="Symbol" value={session.internalSymbol} />
              <Summary label="Win Rate" value={`${(session.winRate * 100).toFixed(1)}%`} valueColor={session.winRate >= 0.4 ? '#10b981' : '#f59e0b'} />
              <Summary label="Max DD" value={`${(session.maxDrawdown * 100).toFixed(1)}%`} valueColor={session.maxDrawdown < 0.25 ? '#10b981' : '#ef4444'} />
              <Summary label="Trades" value={String(session.totalTrades)} />
              <Summary label="Net P&L" value={formatInr(session.totalPnl)} valueColor={session.totalPnl >= 0 ? '#10b981' : '#ef4444'} />
            </div>

            <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
              <Field label="Broker">
                <select value={brokerName} onChange={e => setBrokerName(e.target.value)} style={inputStyle}>
                  {BROKERS.map(b => <option key={b} value={b}>{b}</option>)}
                </select>
              </Field>
              <Field label="Capital to Allocate (₹)">
                <input type="number" min={1000} step={1000} value={capital}
                  onChange={e => setCapital(Number(e.target.value))} style={inputStyle} />
              </Field>
            </div>

            <div style={{ display: 'flex', gap: 10, justifyContent: 'flex-end', marginTop: 20 }}>
              <button onClick={onClose} style={cancelBtn}>Cancel</button>
              <button
                onClick={() => promoteMutation.mutate()}
                disabled={promoteMutation.isPending}
                style={{ ...actionBtn, background: '#d97706' }}
              >
                {promoteMutation.isPending ? 'Running checks…' : '⚡ Run Pre-Flight Checks'}
              </button>
            </div>
          </>
        )}

        {/* ── Step 2: Check results ────────────────────────────────────── */}
        {step === 'checks' && (
          <>
            <h3 style={{ margin: '0 0 4px 0', fontSize: 17, fontWeight: 700, color: allPassed ? '#4ade80' : '#f87171' }}>
              {allPassed ? '✅ All Checks Passed' : `❌ ${failedChecks.length} Check${failedChecks.length > 1 ? 's' : ''} Failed`}
            </h3>
            <p style={{ margin: '0 0 16px 0', fontSize: 12, color: '#64748b' }}>
              {allPassed
                ? 'The strategy is ready for live trading. Click "Go Live" to proceed.'
                : 'Resolve the issues below before going live. You may override at your own risk.'}
            </p>

            <div style={{ display: 'flex', flexDirection: 'column', gap: 8, marginBottom: 16 }}>
              {checks.map((c, i) => (
                <div key={i} style={{
                  display: 'flex', alignItems: 'flex-start', gap: 10,
                  background: c.passed ? '#0f2a1a' : '#2a0f0f',
                  border: `1px solid ${c.passed ? '#16a34a33' : '#dc262633'}`,
                  borderRadius: 6, padding: '9px 12px',
                }}>
                  <span style={{ fontSize: 14, flexShrink: 0, marginTop: 1 }}>{c.passed ? '✅' : '❌'}</span>
                  <div>
                    <div style={{ fontSize: 13, fontWeight: 600, color: c.passed ? '#86efac' : '#fca5a5' }}>{c.name}</div>
                    {c.reason && <div style={{ fontSize: 12, color: '#94a3b8', marginTop: 2 }}>{c.reason}</div>}
                  </div>
                </div>
              ))}
            </div>

            {allPassed ? (
              <div style={{ display: 'flex', gap: 10, justifyContent: 'flex-end' }}>
                <button onClick={onClose} style={cancelBtn}>Cancel</button>
                <button onClick={() => promoteMutation.mutate()} disabled={promoteMutation.isPending} style={{ ...actionBtn, background: '#16a34a' }}>
                  {promoteMutation.isPending ? 'Creating…' : '🚀 Go Live'}
                </button>
              </div>
            ) : (
              <div>
                {!overrideMode ? (
                  <div style={{ display: 'flex', gap: 10, justifyContent: 'flex-end' }}>
                    <button onClick={onClose} style={cancelBtn}>Cancel</button>
                    <button onClick={() => setOverrideMode(true)} style={{ ...actionBtn, background: '#7c3aed' }}>
                      ⚠️ Override Anyway
                    </button>
                  </div>
                ) : (
                  <div>
                    <div style={{ background: '#450a0a', border: '1px solid #dc2626', borderRadius: 6, padding: '10px 14px', marginBottom: 14, fontSize: 13, color: '#fca5a5' }}>
                      ⚠️ You are overriding failed pre-flight checks. This may result in suboptimal performance or losses. Only proceed if you understand the risks.
                    </div>
                    <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13, color: '#e2e8f0', cursor: 'pointer', marginBottom: 14 }}>
                      <input type="checkbox" checked={overrideConfirm} onChange={e => setOverrideConfirm(e.target.checked)} />
                      I understand the risks and want to proceed anyway.
                    </label>
                    <div style={{ display: 'flex', gap: 10, justifyContent: 'flex-end' }}>
                      <button onClick={() => setOverrideMode(false)} style={cancelBtn}>Back</button>
                      <button
                        onClick={() => promoteMutation.mutate()}
                        disabled={!overrideConfirm || promoteMutation.isPending}
                        style={{ ...actionBtn, background: overrideConfirm ? '#dc2626' : '#374151', cursor: overrideConfirm ? 'pointer' : 'not-allowed' }}
                      >
                        {promoteMutation.isPending ? 'Creating…' : '🚀 Go Live (Override)'}
                      </button>
                    </div>
                  </div>
                )}
              </div>
            )}
          </>
        )}

        {/* ── Step 3: Done ─────────────────────────────────────────────── */}
        {step === 'done' && (
          <>
            <div style={{ textAlign: 'center', padding: '12px 0' }}>
              <div style={{ fontSize: 40 }}>🚀</div>
              <h3 style={{ margin: '10px 0 6px 0', fontSize: 18, fontWeight: 700, color: '#4ade80' }}>
                Live Strategy Created!
              </h3>
              <p style={{ color: '#94a3b8', fontSize: 13, marginBottom: 4 }}>
                A new Live-mode strategy instance has been created.
                Go to the <strong>Strategies</strong> page and click <strong>▶ Start</strong> to begin trading.
              </p>
              {newInstanceId && (
                <p style={{ color: '#64748b', fontSize: 11, marginTop: 8 }}>ID: {newInstanceId}</p>
              )}
            </div>
            <div style={{ display: 'flex', justifyContent: 'center', marginTop: 16 }}>
              <button onClick={onClose} style={{ ...actionBtn, background: '#16a34a' }}>Done</button>
            </div>
          </>
        )}
      </div>
    </div>
  )
}

function Summary({ label, value, valueColor = '#e2e8f0' }: { label: string; value: string; valueColor?: string }) {
  return (
    <div>
      <div style={{ fontSize: 10, color: '#64748b', textTransform: 'uppercase', letterSpacing: '0.05em', marginBottom: 2 }}>{label}</div>
      <div style={{ fontSize: 13, fontWeight: 700, color: valueColor }}>{value}</div>
    </div>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <label style={{ display: 'block', fontSize: 12, fontWeight: 600, color: '#8b8b9f', marginBottom: 5 }}>{label}</label>
      {children}
    </div>
  )
}

const inputStyle: React.CSSProperties = {
  width: '100%', background: '#13131f', border: '1px solid #2d2d3f',
  borderRadius: 5, color: '#e2e8f0', padding: '8px 10px', fontSize: 13,
  boxSizing: 'border-box',
}

const cancelBtn: React.CSSProperties = {
  padding: '8px 16px', background: '#2d2d3f', color: '#e2e8f0',
  border: '1px solid #3b3b4f', borderRadius: 6, cursor: 'pointer', fontSize: 14,
}

const actionBtn: React.CSSProperties = {
  padding: '8px 18px', color: '#fff', border: 'none',
  borderRadius: 6, cursor: 'pointer', fontSize: 14, fontWeight: 700,
}
