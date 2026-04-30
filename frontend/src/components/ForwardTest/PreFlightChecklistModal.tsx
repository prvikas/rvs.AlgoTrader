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
import { C } from '../../styles/tokens'
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
        background: C.surface, border: `2px solid ${step === 'done' ? C.green : C.amber}`,
        borderRadius: 10, padding: 28, maxWidth: 520, width: '90%',
      }}>

        {/* ── Step 1: Config ───────────────────────────────────────────── */}
        {step === 'config' && (
          <>
            <h3 style={{ margin: '0 0 4px 0', fontSize: 17, fontWeight: 700, color: C.amber }}>
              🚀 Pre-Flight Check — Going Live
            </h3>
            <p style={{ margin: '0 0 16px 0', fontSize: 12, color: C.textMuted }}>
              Confirm the live trading configuration. We'll run 7 checks before creating the live instance.
            </p>

            {/* Session summary */}
            <div style={{ background: C.bg, borderRadius: 6, padding: '10px 14px', marginBottom: 16, display: 'flex', gap: 16, flexWrap: 'wrap' }}>
              <Summary label="Strategy" value={session.strategyType} />
              <Summary label="Symbol" value={session.internalSymbol} />
              <Summary label="Win Rate" value={`${(session.winRate * 100).toFixed(1)}%`} valueColor={session.winRate >= 0.4 ? C.green : C.amber} />
              <Summary label="Max DD" value={`${(session.maxDrawdown * 100).toFixed(1)}%`} valueColor={session.maxDrawdown < 0.25 ? C.green : C.red} />
              <Summary label="Trades" value={String(session.totalTrades)} />
              <Summary label="Net P&L" value={formatInr(session.totalPnl)} valueColor={session.totalPnl >= 0 ? C.green : C.red} />
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
                style={{ ...actionBtn, background: C.amber }}
              >
                {promoteMutation.isPending ? 'Running checks…' : '⚡ Run Pre-Flight Checks'}
              </button>
            </div>
          </>
        )}

        {/* ── Step 2: Check results ────────────────────────────────────── */}
        {step === 'checks' && (
          <>
            <h3 style={{ margin: '0 0 4px 0', fontSize: 17, fontWeight: 700, color: allPassed ? C.green : C.red }}>
              {allPassed ? '✅ All Checks Passed' : `❌ ${failedChecks.length} Check${failedChecks.length > 1 ? 's' : ''} Failed`}
            </h3>
            <p style={{ margin: '0 0 16px 0', fontSize: 12, color: C.textMuted }}>
              {allPassed
                ? 'The strategy is ready for live trading. Click "Go Live" to proceed.'
                : 'Resolve the issues below before going live. You may override at your own risk.'}
            </p>

            <div style={{ display: 'flex', flexDirection: 'column', gap: 8, marginBottom: 16 }}>
              {checks.map((c, i) => (
                <div key={i} style={{
                  display: 'flex', alignItems: 'flex-start', gap: 10,
                  background: c.passed ? C.greenBg : C.redBg,
                  border: `1px solid ${c.passed ? C.green33 : C.red30}`,
                  borderRadius: 6, padding: '9px 12px',
                }}>
                  <span style={{ fontSize: 14, flexShrink: 0, marginTop: 1 }}>{c.passed ? '✅' : '❌'}</span>
                  <div>
                    <div style={{ fontSize: 13, fontWeight: 600, color: c.passed ? C.green : C.red }}>{c.name}</div>
                    {c.reason && <div style={{ fontSize: 12, color: C.textSub, marginTop: 2 }}>{c.reason}</div>}
                  </div>
                </div>
              ))}
            </div>

            {allPassed ? (
              <div style={{ display: 'flex', gap: 10, justifyContent: 'flex-end' }}>
                <button onClick={onClose} style={cancelBtn}>Cancel</button>
                <button onClick={() => promoteMutation.mutate()} disabled={promoteMutation.isPending} style={{ ...actionBtn, background: C.green }}>
                  {promoteMutation.isPending ? 'Creating…' : '🚀 Go Live'}
                </button>
              </div>
            ) : (
              <div>
                {!overrideMode ? (
                  <div style={{ display: 'flex', gap: 10, justifyContent: 'flex-end' }}>
                    <button onClick={onClose} style={cancelBtn}>Cancel</button>
                    <button onClick={() => setOverrideMode(true)} style={{ ...actionBtn, background: C.surface3 }}>
                      ⚠️ Override Anyway
                    </button>
                  </div>
                ) : (
                  <div>
                    <div style={{ background: C.redBg, border: `1px solid ${C.red}`, borderRadius: 6, padding: '10px 14px', marginBottom: 14, fontSize: 13, color: C.red }}>
                      ⚠️ You are overriding failed pre-flight checks. This may result in suboptimal performance or losses. Only proceed if you understand the risks.
                    </div>
                    <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 13, color: C.text, cursor: 'pointer', marginBottom: 14 }}>
                      <input type="checkbox" checked={overrideConfirm} onChange={e => setOverrideConfirm(e.target.checked)} />
                      I understand the risks and want to proceed anyway.
                    </label>
                    <div style={{ display: 'flex', gap: 10, justifyContent: 'flex-end' }}>
                      <button onClick={() => setOverrideMode(false)} style={cancelBtn}>Back</button>
                      <button
                        onClick={() => promoteMutation.mutate()}
                        disabled={!overrideConfirm || promoteMutation.isPending}
                        style={{ ...actionBtn, background: overrideConfirm ? C.red : C.surface3, cursor: overrideConfirm ? 'pointer' : 'not-allowed' }}
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
              <h3 style={{ margin: '10px 0 6px 0', fontSize: 18, fontWeight: 700, color: C.green }}>
                Live Strategy Created!
              </h3>
              <p style={{ color: C.textSub, fontSize: 13, marginBottom: 4 }}>
                A new Live-mode strategy instance has been created.
                Go to the <strong>Strategies</strong> page and click <strong>▶ Start</strong> to begin trading.
              </p>
              {newInstanceId && (
                <p style={{ color: C.textMuted, fontSize: 11, marginTop: 8 }}>ID: {newInstanceId}</p>
              )}
            </div>
            <div style={{ display: 'flex', justifyContent: 'center', marginTop: 16 }}>
              <button onClick={onClose} style={{ ...actionBtn, background: C.green }}>Done</button>
            </div>
          </>
        )}
      </div>
    </div>
  )
}

function Summary({ label, value, valueColor = C.text }: { label: string; value: string; valueColor?: string }) {
  return (
    <div>
      <div style={{ fontSize: 10, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.05em', marginBottom: 2 }}>{label}</div>
      <div style={{ fontSize: 13, fontWeight: 700, color: valueColor }}>{value}</div>
    </div>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
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

const cancelBtn: React.CSSProperties = {
  padding: '8px 16px', background: C.surface3, color: C.text,
  border: `1px solid ${C.border2}`, borderRadius: 6, cursor: 'pointer', fontSize: 14,
}

const actionBtn: React.CSSProperties = {
  padding: '8px 18px', color: 'white', border: 'none',
  borderRadius: 6, cursor: 'pointer', fontSize: 14, fontWeight: 700,
}
