import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAppStore } from '../stores/appStore'
import { useFeatures } from '../context/FeaturesContext'

export function Login() {
  const navigate = useNavigate()
  const setJwtToken = useAppStore(s => s.setJwtToken)
  const setActiveBroker = useAppStore(s => s.setActiveBroker)
  const { brokerRequired, loaded } = useFeatures()

  // ── Local (username/password) login state ─────────────────────────────────
  const [localUser, setLocalUser] = useState('')
  const [localPass, setLocalPass] = useState('')

  // ── MStock broker login state ─────────────────────────────────────────────
  const [apiKey, setApiKey] = useState('')
  const [clientCode, setClientCode] = useState('')
  const [password, setPassword] = useState('')
  const [totp, setTotp] = useState('')
  const [showTotpField, setShowTotpField] = useState(false)

  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  // ── Local login handler ───────────────────────────────────────────────────
  const handleLocalLogin = async (e: React.FormEvent) => {
    e.preventDefault()
    setError('')
    setLoading(true)
    try {
      const response = await fetch('/api/auth/local', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username: localUser, password: localPass }),
      })
      const data = await response.json()
      if (!response.ok) {
        setError(data.message || data.error || 'Invalid username or password')
        return
      }
      setJwtToken(data.data.token)
      setActiveBroker('None')
      navigate('/')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Connection error')
    } finally {
      setLoading(false)
    }
  }

  // ── MStock handlers ───────────────────────────────────────────────────────
  const handleProceedToTotp = (e: React.FormEvent) => {
    e.preventDefault()
    setError('')
    if (!apiKey || !clientCode || !password) {
      setError('Please fill in all fields before requesting TOTP')
      return
    }
    setShowTotpField(true)
  }

  const handleMStockLogin = async (e: React.FormEvent) => {
    e.preventDefault()
    setError('')
    setLoading(true)
    try {
      const response = await fetch('/api/auth/mstock/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ apiKey, clientCode, password, totp }),
      })
      const data = await response.json()
      if (!response.ok) {
        setError(data.message || data.error || 'Login failed')
        setShowTotpField(false)
        setTotp('')
        return
      }
      setJwtToken(data.data.token)
      setActiveBroker(data.data.brokerName)
      navigate('/')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Connection error')
    } finally {
      setLoading(false)
    }
  }

  // ── Shared styles ─────────────────────────────────────────────────────────
  const inputStyle: React.CSSProperties = {
    width: '100%',
    padding: '10px 12px',
    background: '#0f0f1a',
    border: '1px solid #2d2d3f',
    borderRadius: 6,
    color: '#e2e8f0',
    fontSize: 13,
    boxSizing: 'border-box',
  }

  const labelStyle: React.CSSProperties = {
    fontSize: 13,
    fontWeight: 600,
    color: '#cbd5e1',
    display: 'block',
    marginBottom: 6,
  }

  // Hold render until features are loaded (prevents flash of wrong form)
  if (!loaded) return null

  return (
    <div style={{
      minHeight: '100vh',
      background: 'linear-gradient(135deg, #0f0f1a 0%, #1a1a2e 100%)',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      fontFamily: 'Inter, sans-serif',
      color: '#e2e8f0',
    }}>
      <div style={{
        background: '#1e1e2e',
        borderRadius: 12,
        border: '1px solid #2d2d3f',
        padding: 40,
        width: '100%',
        maxWidth: 400,
        boxShadow: '0 20px 60px rgba(0,0,0,0.3)',
      }}>
        <h1 style={{ fontSize: 28, fontWeight: 700, marginBottom: 8, textAlign: 'center' }}>
          AlgoTrader
        </h1>
        <p style={{ fontSize: 14, color: '#94a3b8', textAlign: 'center', marginBottom: 32 }}>
          {brokerRequired ? 'MStock Login' : 'Sign in to continue'}
        </p>

        {error && (
          <div style={{
            background: '#7f1d1d',
            border: '1px solid #991b1b',
            color: '#fca5a5',
            borderRadius: 8,
            padding: 12,
            marginBottom: 24,
            fontSize: 13,
          }}>
            {error}
          </div>
        )}

        {/* ── Local username/password form (broker-free mode) ───────────── */}
        {!brokerRequired && (
          <form onSubmit={handleLocalLogin}>
            <div style={{ marginBottom: 16 }}>
              <label style={labelStyle}>Username</label>
              <input
                type="text"
                value={localUser}
                onChange={e => setLocalUser(e.target.value)}
                placeholder="admin"
                autoFocus
                autoComplete="username"
                style={inputStyle}
                required
              />
            </div>
            <div style={{ marginBottom: 24 }}>
              <label style={labelStyle}>Password</label>
              <input
                type="password"
                value={localPass}
                onChange={e => setLocalPass(e.target.value)}
                placeholder="••••••••"
                autoComplete="current-password"
                style={inputStyle}
                required
              />
            </div>
            <button
              type="submit"
              disabled={loading || !localUser || !localPass}
              style={{
                width: '100%',
                padding: '10px 16px',
                background: loading || !localUser || !localPass ? '#4b5563' : '#3b82f6',
                color: '#fff',
                border: 'none',
                borderRadius: 6,
                fontSize: 13,
                fontWeight: 600,
                cursor: loading || !localUser || !localPass ? 'not-allowed' : 'pointer',
                transition: 'background 0.2s',
              }}
            >
              {loading ? 'Signing in…' : 'Sign in'}
            </button>
            <p style={{ fontSize: 12, color: '#64748b', textAlign: 'center', marginTop: 20 }}>
              Backtest-only mode — broker connection not required
            </p>
          </form>
        )}

        {/* ── MStock broker login (broker-required mode) ────────────────── */}
        {brokerRequired && !showTotpField && (
          <form onSubmit={handleProceedToTotp}>
            <div style={{ marginBottom: 16 }}>
              <label style={labelStyle}>API Key</label>
              <input
                type="text"
                value={apiKey}
                onChange={e => setApiKey(e.target.value)}
                placeholder="Your MStock API Key"
                style={inputStyle}
                required
              />
            </div>
            <div style={{ marginBottom: 16 }}>
              <label style={labelStyle}>Client Code</label>
              <input
                type="text"
                value={clientCode}
                onChange={e => setClientCode(e.target.value)}
                placeholder="Your MStock Client Code"
                style={inputStyle}
                required
              />
            </div>
            <div style={{ marginBottom: 24 }}>
              <label style={labelStyle}>Password</label>
              <input
                type="password"
                value={password}
                onChange={e => setPassword(e.target.value)}
                placeholder="Your MStock Password"
                style={inputStyle}
                required
              />
            </div>
            <button
              type="submit"
              disabled={!apiKey || !clientCode || !password}
              style={{
                width: '100%',
                padding: '10px 16px',
                background: !apiKey || !clientCode || !password ? '#4b5563' : '#3b82f6',
                color: '#fff',
                border: 'none',
                borderRadius: 6,
                fontSize: 13,
                fontWeight: 600,
                cursor: !apiKey || !clientCode || !password ? 'not-allowed' : 'pointer',
                transition: 'background 0.2s',
              }}
            >
              Continue →
            </button>
            <p style={{ fontSize: 12, color: '#64748b', textAlign: 'center', marginTop: 20 }}>
              MStock Type B authentication
            </p>
          </form>
        )}

        {brokerRequired && showTotpField && (
          <form onSubmit={handleMStockLogin}>
            <div style={{
              background: '#0f0f1a',
              border: '1px solid #2d2d3f',
              borderRadius: 8,
              padding: 12,
              marginBottom: 24,
              fontSize: 12,
              color: '#cbd5e1',
            }}>
              <p style={{ margin: '0 0 8px 0', fontWeight: 600 }}>✓ Credentials confirmed</p>
              <p style={{ margin: 0, color: '#94a3b8' }}>
                Open your authenticator app and enter the 6-digit code below
              </p>
            </div>

            <div style={{
              background: '#16a34a22',
              border: '1px solid #16a34a44',
              borderRadius: 8,
              padding: 12,
              marginBottom: 24,
              fontSize: 12,
            }}>
              <p style={{ margin: 0, color: '#86efac', fontWeight: 600 }}>
                ⏱ TOTP changes every 30 seconds — enter quickly
              </p>
            </div>

            <div style={{ marginBottom: 24 }}>
              <label style={labelStyle}>TOTP (6 digits from authenticator app)</label>
              <input
                type="text"
                value={totp}
                onChange={e => setTotp(e.target.value.replace(/[^0-9]/g, '').slice(0, 6))}
                placeholder="000000"
                maxLength={6}
                autoFocus
                style={{
                  ...inputStyle,
                  border: totp.length === 6 ? '1px solid #16a34a' : '1px solid #2d2d3f',
                  fontSize: 18,
                  letterSpacing: 4,
                  textAlign: 'center',
                }}
                required
              />
            </div>

            <button
              type="submit"
              disabled={loading || totp.length !== 6}
              style={{
                width: '100%',
                padding: '10px 16px',
                background: totp.length !== 6 || loading ? '#4b5563' : '#16a34a',
                color: '#fff',
                border: 'none',
                borderRadius: 6,
                fontSize: 13,
                fontWeight: 600,
                cursor: totp.length !== 6 || loading ? 'not-allowed' : 'pointer',
                transition: 'background 0.2s',
              }}
            >
              {loading ? 'Authenticating...' : 'Complete Login'}
            </button>

            <button
              type="button"
              onClick={() => { setShowTotpField(false); setTotp(''); setError('') }}
              style={{
                width: '100%',
                padding: '8px 16px',
                background: 'transparent',
                color: '#64748b',
                border: '1px solid #2d2d3f',
                borderRadius: 6,
                fontSize: 12,
                fontWeight: 500,
                cursor: 'pointer',
                marginTop: 8,
                transition: 'all 0.2s',
              }}
            >
              Back to credentials
            </button>
            <p style={{ fontSize: 12, color: '#64748b', textAlign: 'center', marginTop: 20 }}>
              Enter your TOTP code
            </p>
          </form>
        )}
      </div>
    </div>
  )
}
