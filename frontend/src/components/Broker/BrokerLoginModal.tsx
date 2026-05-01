import { useState } from 'react'
import { brokerApi } from '../../api/client'
import { C, BRAND } from '../../styles/tokens'


interface Props {
  broker: 'MStock' | 'Zerodha' | 'Upstox'
  apiKey: string
  onSuccess: (broker: string) => void
  onClose: () => void
}

// Shared input style
const inputStyle: React.CSSProperties = {
  width: '100%', padding: '10px 12px', background: 'var(--c-surface)',
  border: `1px solid var(--c-border2)`, borderRadius: 6, color: 'var(--c-text)',
  fontSize: 14, outline: 'none', boxSizing: 'border-box',
}
const labelStyle: React.CSSProperties = { fontSize: 12, color: 'var(--c-textSub)', marginBottom: 4, display: 'block' }
const errStyle: React.CSSProperties = { color: 'var(--c-red)', fontSize: 13, marginTop: 8 }
const btnPrimary: React.CSSProperties = {
  background: 'var(--c-blue)', color: 'white', border: 'none', borderRadius: 6,
  padding: '10px 20px', fontSize: 14, fontWeight: 600, cursor: 'pointer', width: '100%',
}
const btnSecondary: React.CSSProperties = {
  ...btnPrimary, background: 'var(--c-surface3)', color: 'var(--c-textSub)',
}

// ── mStock Form ───────────────────────────────────────────────────────────────
function MStockForm({ apiKey: apiKeyProp, onSuccess, onError }: {
  apiKey: string
  onSuccess: (broker: string) => void
  onError: (msg: string) => void
}) {
  const [apiKey, setApiKey]       = useState(apiKeyProp)
  const [clientCode, setClientCode] = useState('')
  const [password, setPassword]   = useState('')
  const [totp, setTotp]           = useState('')
  const [loading, setLoading]     = useState(false)

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    if (!apiKey.trim()) { onError('API Key is required'); return }
    if (totp.length !== 6 || !/^\d{6}$/.test(totp)) {
      onError('TOTP must be exactly 6 digits'); return
    }
    setLoading(true)
    try {
      const res = await brokerApi.connect('MStock', { apiKey: apiKey.trim(), clientCode, password, totp })
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
        <label style={labelStyle}>API Key</label>
        <input style={{ ...inputStyle, fontFamily: 'monospace', fontSize: 13 }}
          value={apiKey} onChange={e => setApiKey(e.target.value)}
          placeholder="mStock Type B API key" autoComplete="off" required />
        <div style={{ fontSize: 11, color: C.textMuted, marginTop: 4 }}>
          Obtain from mStock developer portal. Stored only for this session.
        </div>
      </div>
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
        <div style={{ fontSize: 11, color: C.textMuted, marginTop: 4 }}>
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
      const res = await brokerApi.loginUrl('Zerodha')
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
      const res = await brokerApi.connect('Zerodha', { requestToken: requestToken.trim() })
      if (res.data.success) onSuccess('Zerodha')
      else onError(res.data.error ?? 'Login failed')
    } catch (err: any) {
      onError(err.response?.data?.error ?? err.message ?? 'Network error')
    } finally { setLoading(false) }
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <div style={{ background: C.blueBg, border: `1px solid ${C.blue}`, borderRadius: 6, padding: 12, fontSize: 13, color: C.textSub }}>
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
      const res = await brokerApi.loginUrl('Upstox')
      setLoginUrl(res.data.data ?? null)
    } catch {
      onError('Could not fetch Upstox login URL')
    } finally { setFetching(false) }
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <div style={{ background: C.blueBg, border: `1px solid ${C.blue}`, borderRadius: 6, padding: 12, fontSize: 13, color: C.textSub }}>
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

  // Broker accent colors from BRAND tokens (AP-020)
  const brokerColors: Record<string, string> = {
    MStock: BRAND.mstock, Zerodha: BRAND.zerodha, Upstox: BRAND.upstox,
  }
  const color = brokerColors[broker] ?? C.blue

  return (
    <div style={{
      position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.7)',
      display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 1000,
    }}>
      <div style={{
        background: C.surface, border: `1px solid ${color}44`, borderRadius: 12,
        width: 420, maxWidth: '95vw', padding: 24, boxShadow: `0 0 40px ${color}22`,
      }}>
        {/* Header */}
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 20 }}>
          <div>
            <div style={{ fontSize: 18, fontWeight: 700, color: C.text }}>
              <span style={{ color }}>{broker}</span> Login
            </div>
            <div style={{ fontSize: 12, color: C.textMuted, marginTop: 2 }}>
              {broker === 'MStock' && 'Type B API — direct credentials'}
              {broker === 'Zerodha' && 'Kite Connect — OAuth'}
              {broker === 'Upstox' && 'Upstox API v2 — OAuth2'}
            </div>
          </div>
          <button onClick={onClose} style={{ background: 'none', border: 'none', color: C.textMuted, fontSize: 20, cursor: 'pointer' }}>×</button>
        </div>

        {/* Error banner */}
        {error && (
          <div style={{ background: C.redBg, border: `1px solid ${C.red}`, borderRadius: 6, padding: '10px 14px', marginBottom: 16 }}>
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
