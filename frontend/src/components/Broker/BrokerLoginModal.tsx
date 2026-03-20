import { useState } from 'react'
import { apiClient, ApiResponse } from '../../api/client'

interface BrokerAuthResult {
  success: boolean
  brokerName: string
  message?: string
  expiresAt?: string
}

interface Props {
  broker: 'MStock' | 'Zerodha' | 'Upstox'
  apiKey: string
  onSuccess: (broker: string) => void
  onClose: () => void
}

// Shared input style
const inputStyle: React.CSSProperties = {
  width: '100%', padding: '10px 12px', background: '#1e1e2e',
  border: '1px solid #3d3d5c', borderRadius: 6, color: '#e2e8f0',
  fontSize: 14, outline: 'none', boxSizing: 'border-box',
}
const labelStyle: React.CSSProperties = { fontSize: 12, color: '#94a3b8', marginBottom: 4, display: 'block' }
const errStyle: React.CSSProperties = { color: '#f87171', fontSize: 13, marginTop: 8 }
const btnPrimary: React.CSSProperties = {
  background: '#3b82f6', color: '#fff', border: 'none', borderRadius: 6,
  padding: '10px 20px', fontSize: 14, fontWeight: 600, cursor: 'pointer', width: '100%',
}
const btnSecondary: React.CSSProperties = {
  ...btnPrimary, background: '#2d2d3f', color: '#94a3b8',
}

// ── mStock Form ───────────────────────────────────────────────────────────────
function MStockForm({ apiKey, onSuccess, onError }: {
  apiKey: string
  onSuccess: (broker: string) => void
  onError: (msg: string) => void
}) {
  const [clientCode, setClientCode] = useState('')
  const [password, setPassword] = useState('')
  const [totp, setTotp] = useState('')
  const [loading, setLoading] = useState(false)

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    if (totp.length !== 6 || !/^\d{6}$/.test(totp)) {
      onError('TOTP must be exactly 6 digits'); return
    }
    setLoading(true)
    try {
      const res = await apiClient.post<ApiResponse<BrokerAuthResult>>(
        '/broker/mstock/login',
        { apiKey, clientCode, password, totp }   // backend: MStockLoginRequest.ClientCode
      )
      if (res.data.success) onSuccess('MStock')
      else onError(res.data.error ?? 'Login failed')
    } catch (err: any) {
      const msg = err.response?.data?.error ?? err.message ?? 'Network error'
      onError(msg)
    } finally {
      setLoading(false)
      setTotp('') // Always clear TOTP after attempt
    }
  }

  return (
    <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
      <div>
        <label style={labelStyle}>User ID (Client Code)</label>
        <input style={inputStyle} value={clientCode} onChange={e => setClientCode(e.target.value)}
          placeholder="e.g. MS12345" autoComplete="username" required />
      </div>
      <div>
        <label style={labelStyle}>Password</label>
        <input style={inputStyle} type="password" value={password} onChange={e => setPassword(e.target.value)}
          placeholder="mStock login password" autoComplete="current-password" required />
      </div>
      <div>
        <label style={labelStyle}>TOTP (6-digit code from authenticator app)</label>
        <input style={{ ...inputStyle, letterSpacing: 4, fontWeight: 700, fontSize: 18, textAlign: 'center' }}
          value={totp} onChange={e => setTotp(e.target.value.replace(/\D/g, '').slice(0, 6))}
          placeholder="000000" maxLength={6} inputMode="numeric" pattern="\d{6}"
          autoComplete="one-time-code" required />
        <div style={{ fontSize: 11, color: '#6b7280', marginTop: 4 }}>
          Expires every 30 seconds — enter immediately after clicking Login
        </div>
      </div>
      <button style={btnPrimary} type="submit" disabled={loading}>
        {loading ? 'Authenticating…' : 'Login to mStock'}
      </button>
    </form>
  )
}

// ── Zerodha Form ──────────────────────────────────────────────────────────────
function ZerodhaForm({ onSuccess, onError }: {
  onSuccess: (broker: string) => void
  onError: (msg: string) => void
}) {
  const [loginUrl, setLoginUrl] = useState<string | null>(null)
  const [requestToken, setRequestToken] = useState('')
  const [loading, setLoading] = useState(false)
  const [fetching, setFetching] = useState(false)

  async function fetchLoginUrl() {
    setFetching(true)
    try {
      const res = await apiClient.get<ApiResponse<string>>('/broker/zerodha/login-url')
      setLoginUrl(res.data.data ?? null)
    } catch {
      onError('Could not fetch Zerodha login URL')
    } finally { setFetching(false) }
  }

  async function handleCallback(e: React.FormEvent) {
    e.preventDefault()
    if (!requestToken.trim()) { onError('Request token is required'); return }
    setLoading(true)
    try {
      const res = await apiClient.post<ApiResponse<BrokerAuthResult>>(
        '/broker/zerodha/callback', { requestToken: requestToken.trim() }
      )
      if (res.data.success) onSuccess('Zerodha')
      else onError(res.data.error ?? 'Login failed')
    } catch (err: any) {
      onError(err.response?.data?.error ?? err.message ?? 'Network error')
    } finally { setLoading(false) }
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <div style={{ background: '#1a2744', border: '1px solid #1e40af', borderRadius: 6, padding: 12, fontSize: 13, color: '#93c5fd' }}>
        Zerodha uses OAuth. Click below to open Kite Login in a new tab. After successful login,
        Zerodha will redirect you to a URL containing <code>?request_token=xxxxx</code>. Paste that token below.
      </div>
      {!loginUrl ? (
        <button style={btnSecondary} onClick={fetchLoginUrl} disabled={fetching}>
          {fetching ? 'Loading…' : 'Get Kite Login URL'}
        </button>
      ) : (
        <a href={loginUrl} target="_blank" rel="noreferrer"
          style={{ ...btnPrimary, display: 'block', textDecoration: 'none', textAlign: 'center' }}>
          ↗ Open Kite Login
        </a>
      )}
      <form onSubmit={handleCallback} style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
        <label style={labelStyle}>Request Token (from redirect URL)</label>
        <input style={{ ...inputStyle, fontFamily: 'monospace', fontSize: 12 }}
          value={requestToken} onChange={e => setRequestToken(e.target.value)}
          placeholder="Paste request_token here…" />
        <button style={btnPrimary} type="submit" disabled={loading || !requestToken.trim()}>
          {loading ? 'Exchanging token…' : 'Complete Login'}
        </button>
      </form>
    </div>
  )
}

// ── Upstox Form ───────────────────────────────────────────────────────────────
function UpstoxForm({ onSuccess, onError }: {
  onSuccess: (broker: string) => void
  onError: (msg: string) => void
}) {
  const [loginUrl, setLoginUrl] = useState<string | null>(null)
  const [fetching, setFetching] = useState(false)

  async function fetchLoginUrl() {
    setFetching(true)
    try {
      const res = await apiClient.get<ApiResponse<string>>('/broker/upstox/login-url')
      setLoginUrl(res.data.data ?? null)
    } catch {
      onError('Could not fetch Upstox login URL')
    } finally { setFetching(false) }
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <div style={{ background: '#1a2744', border: '1px solid #1e40af', borderRadius: 6, padding: 12, fontSize: 13, color: '#93c5fd' }}>
        Upstox uses OAuth2. Click below to open the authorization page.
        After you approve, Upstox will automatically redirect back to this app with your access token.
        Token expires at 3:30 AM IST daily.
      </div>
      {!loginUrl ? (
        <button style={btnSecondary} onClick={fetchLoginUrl} disabled={fetching}>
          {fetching ? 'Loading…' : 'Get Upstox Login URL'}
        </button>
      ) : (
        <a href={loginUrl} target="_blank" rel="noreferrer"
          style={{ ...btnPrimary, display: 'block', textDecoration: 'none', textAlign: 'center' }}
          onClick={() => setTimeout(() => onSuccess('Upstox'), 2000)}>
          ↗ Open Upstox Authorization
        </a>
      )}
    </div>
  )
}

// ── Main Modal ────────────────────────────────────────────────────────────────
export function BrokerLoginModal({ broker, apiKey, onSuccess, onClose }: Props) {
  const [error, setError] = useState<string | null>(null)

  const brokerColors: Record<string, string> = {
    MStock: '#f59e0b', Zerodha: '#e11d48', Upstox: '#7c3aed',
  }
  const color = brokerColors[broker] ?? '#3b82f6'

  return (
    <div style={{
      position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.7)',
      display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000,
    }}>
      <div style={{
        background: '#14141f', border: `1px solid ${color}44`, borderRadius: 12,
        width: 420, maxWidth: '95vw', padding: 24, boxShadow: `0 0 40px ${color}22`,
      }}>
        {/* Header */}
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 20 }}>
          <div>
            <div style={{ fontSize: 18, fontWeight: 700, color: '#e2e8f0' }}>
              <span style={{ color }}>{broker}</span> Login
            </div>
            <div style={{ fontSize: 12, color: '#6b7280', marginTop: 2 }}>
              {broker === 'MStock' && 'Type B API — direct credentials'}
              {broker === 'Zerodha' && 'Kite Connect — OAuth'}
              {broker === 'Upstox' && 'Upstox API v2 — OAuth2'}
            </div>
          </div>
          <button onClick={onClose} style={{ background: 'none', border: 'none', color: '#6b7280', fontSize: 20, cursor: 'pointer' }}>×</button>
        </div>

        {/* Error banner */}
        {error && (
          <div style={{ background: '#3f1111', border: '1px solid #f87171', borderRadius: 6, padding: '10px 14px', marginBottom: 16 }}>
            <span style={errStyle}>{error}</span>
          </div>
        )}

        {/* Broker-specific form */}
        {broker === 'MStock' && (
          <MStockForm apiKey={apiKey} onSuccess={onSuccess} onError={setError} />
        )}
        {broker === 'Zerodha' && (
          <ZerodhaForm onSuccess={onSuccess} onError={setError} />
        )}
        {broker === 'Upstox' && (
          <UpstoxForm onSuccess={onSuccess} onError={setError} />
        )}

        <button onClick={onClose} style={{ ...btnSecondary, marginTop: 12 }}>Cancel</button>
      </div>
    </div>
  )
}
