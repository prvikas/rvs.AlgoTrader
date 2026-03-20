import { useState } from 'react'
import { useNavigate } from 'react-router-dom'

export function Login() {
  const navigate = useNavigate()
  const [apiKey, setApiKey] = useState('')
  const [clientCode, setClientCode] = useState('')
  const [password, setPassword] = useState('')
  const [totp, setTotp] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [showTotpField, setShowTotpField] = useState(false)

  const handleProceedToTotp = (e: React.FormEvent) => {
    e.preventDefault()
    setError('')

    if (!apiKey || !clientCode || !password) {
      setError('Please fill in all fields before requesting TOTP')
      return
    }

    setShowTotpField(true)
  }

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault()
    setError('')
    setLoading(true)

    try {
      const response = await fetch('/api/auth/mstock/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ apiKey, clientCode, password, totp })
      })

      const data = await response.json()

      if (!response.ok) {
        setError(data.message || data.error || 'Login failed')
        setShowTotpField(false)
        setTotp('')
        return
      }

      // Store JWT token and active broker name
      localStorage.setItem('jwt_token', data.data.token)
      localStorage.setItem('active_broker', data.data.brokerName)
      navigate('/')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Connection error')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div style={{
      minHeight: '100vh',
      background: 'linear-gradient(135deg, #0f0f1a 0%, #1a1a2e 100%)',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      fontFamily: 'Inter, sans-serif',
      color: '#e2e8f0'
    }}>
      <div style={{
        background: '#1e1e2e',
        borderRadius: 12,
        border: '1px solid #2d2d3f',
        padding: 40,
        width: '100%',
        maxWidth: 400,
        boxShadow: '0 20px 60px rgba(0,0,0,0.3)'
      }}>
        <h1 style={{ fontSize: 28, fontWeight: 700, marginBottom: 8, textAlign: 'center' }}>
          AlgoTrader
        </h1>
        <p style={{ fontSize: 14, color: '#94a3b8', textAlign: 'center', marginBottom: 32 }}>
          MStock Login
        </p>

        {error && (
          <div style={{
            background: '#7f1d1d',
            border: '1px solid #991b1b',
            color: '#fca5a5',
            borderRadius: 8,
            padding: 12,
            marginBottom: 24,
            fontSize: 13
          }}>
            {error}
          </div>
        )}

        {!showTotpField ? (
          <form onSubmit={handleProceedToTotp}>
            <div style={{ marginBottom: 16 }}>
              <label style={{ fontSize: 13, fontWeight: 600, color: '#cbd5e1', display: 'block', marginBottom: 6 }}>
                API Key
              </label>
              <input
                type="text"
                value={apiKey}
                onChange={e => setApiKey(e.target.value)}
                placeholder="Your MStock API Key"
                style={{
                  width: '100%',
                  padding: '10px 12px',
                  background: '#0f0f1a',
                  border: '1px solid #2d2d3f',
                  borderRadius: 6,
                  color: '#e2e8f0',
                  fontSize: 13,
                  boxSizing: 'border-box'
                }}
                required
              />
            </div>

            <div style={{ marginBottom: 16 }}>
              <label style={{ fontSize: 13, fontWeight: 600, color: '#cbd5e1', display: 'block', marginBottom: 6 }}>
                Client Code
              </label>
              <input
                type="text"
                value={clientCode}
                onChange={e => setClientCode(e.target.value)}
                placeholder="Your MStock Client Code"
                style={{
                  width: '100%',
                  padding: '10px 12px',
                  background: '#0f0f1a',
                  border: '1px solid #2d2d3f',
                  borderRadius: 6,
                  color: '#e2e8f0',
                  fontSize: 13,
                  boxSizing: 'border-box'
                }}
                required
              />
            </div>

            <div style={{ marginBottom: 24 }}>
              <label style={{ fontSize: 13, fontWeight: 600, color: '#cbd5e1', display: 'block', marginBottom: 6 }}>
                Password
              </label>
              <input
                type="password"
                value={password}
                onChange={e => setPassword(e.target.value)}
                placeholder="Your MStock Password"
                style={{
                  width: '100%',
                  padding: '10px 12px',
                  background: '#0f0f1a',
                  border: '1px solid #2d2d3f',
                  borderRadius: 6,
                  color: '#e2e8f0',
                  fontSize: 13,
                  boxSizing: 'border-box'
                }}
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
                transition: 'background 0.2s'
              }}
            >
              Continue →
            </button>
          </form>
        ) : (
          <form onSubmit={handleLogin}>
            <div style={{
              background: '#0f0f1a',
              border: '1px solid #2d2d3f',
              borderRadius: 8,
              padding: 12,
              marginBottom: 24,
              fontSize: 12,
              color: '#cbd5e1'
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
              fontSize: 12
            }}>
              <p style={{ margin: 0, color: '#86efac', fontWeight: 600 }}>
                ⏱ TOTP changes every 30 seconds — enter quickly
              </p>
            </div>

            <div style={{ marginBottom: 24 }}>
              <label style={{ fontSize: 13, fontWeight: 600, color: '#cbd5e1', display: 'block', marginBottom: 6 }}>
                TOTP (6 digits from authenticator app)
              </label>
              <input
                type="text"
                value={totp}
                onChange={e => setTotp(e.target.value.replace(/[^0-9]/g, '').slice(0, 6))}
                placeholder="000000"
                maxLength={6}
                autoFocus
                style={{
                  width: '100%',
                  padding: '12px 12px',
                  background: '#0f0f1a',
                  border: totp.length === 6 ? '1px solid #16a34a' : '1px solid #2d2d3f',
                  borderRadius: 6,
                  color: '#e2e8f0',
                  fontSize: 18,
                  letterSpacing: 4,
                  textAlign: 'center',
                  boxSizing: 'border-box'
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
                transition: 'background 0.2s'
              }}
            >
              {loading ? 'Authenticating...' : 'Complete Login'}
            </button>

            <button
              type="button"
              onClick={() => {
                setShowTotpField(false)
                setTotp('')
                setError('')
              }}
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
                transition: 'all 0.2s'
              }}
            >
              Back to credentials
            </button>
          </form>
        )}

        <p style={{ fontSize: 12, color: '#64748b', textAlign: 'center', marginTop: 20 }}>
          {!showTotpField ? 'MStock Type B authentication' : 'Enter your TOTP code'}
        </p>
      </div>
    </div>
  )
}
