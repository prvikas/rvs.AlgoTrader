import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAppStore } from '../stores/appStore'
import { useFeatures } from '../context/FeaturesContext'
import { C } from '../styles/tokens'

interface ProviderInfo {
  name: string
  displayName: string
  iconKey: string
}

// ── Provider brand colours ─────────────────────────────────────────────────────
const PROVIDER_STYLES: Record<string, { bg: string; text: string; border: string }> = {
  google:    { bg: '#fff',    text: '#3c4043', border: '#dadce0' },
  microsoft: { bg: '#2f2f2f', text: '#fff',    border: '#444'    },
  apple:     { bg: '#000',    text: '#fff',    border: '#333'    },
}

// ── Inline SVG icons ──────────────────────────────────────────────────────────
function GoogleIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 48 48">
      <path fill="#EA4335" d="M24 9.5c3.54 0 6.71 1.22 9.21 3.6l6.85-6.85C35.9 2.38 30.47 0 24 0 14.62 0 6.51 5.38 2.56 13.22l7.98 6.19C12.43 13.72 17.74 9.5 24 9.5z"/>
      <path fill="#4285F4" d="M46.98 24.55c0-1.57-.15-3.09-.38-4.55H24v9.02h12.94c-.58 2.96-2.26 5.48-4.78 7.18l7.73 6c4.51-4.18 7.09-10.36 7.09-17.65z"/>
      <path fill="#FBBC05" d="M10.53 28.59c-.48-1.45-.76-2.99-.76-4.59s.27-3.14.76-4.59l-7.98-6.19C.92 16.46 0 20.12 0 24c0 3.88.92 7.54 2.56 10.78l7.97-6.19z"/>
      <path fill="#34A853" d="M24 48c6.48 0 11.93-2.13 15.89-5.81l-7.73-6c-2.15 1.45-4.92 2.3-8.16 2.3-6.26 0-11.57-4.22-13.47-9.91l-7.98 6.19C6.51 42.62 14.62 48 24 48z"/>
    </svg>
  )
}

function MicrosoftIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 21 21">
      <rect x="1"  y="1"  width="9" height="9" fill="#f25022"/>
      <rect x="11" y="1"  width="9" height="9" fill="#7fba00"/>
      <rect x="1"  y="11" width="9" height="9" fill="#00a4ef"/>
      <rect x="11" y="11" width="9" height="9" fill="#ffb900"/>
    </svg>
  )
}

function AppleIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 814 1000">
      <path fill="currentColor" d="M788.1 340.9c-5.8 4.5-108.2 62.2-108.2 190.5 0 148.4 130.3 200.9 134.2 202.2-.6 3.2-20.7 71.9-68.7 141.9-42.8 61.6-87.5 123.1-155.5 123.1s-85.5-39.5-164-39.5c-76 0-103.7 40.8-165.9 40.8s-105-37.3-166.8-127.5C74 412.7 32 351.4 32 283c0-154.3 100.7-235.6 199.2-235.6 52.6 0 96.4 34.5 129.7 34.5 31.8 0 81.4-36.7 141-36.7C540.6 45.2 576 60.9 788.1 340.9z"/>
    </svg>
  )
}

function ProviderIcon({ iconKey }: { iconKey: string }) {
  if (iconKey === 'google')    return <GoogleIcon />
  if (iconKey === 'microsoft') return <MicrosoftIcon />
  if (iconKey === 'apple')     return <AppleIcon />
  return null
}

export function Login() {
  const navigate       = useNavigate()
  const setJwtToken    = useAppStore(s => s.setJwtToken)
  const setActiveBroker = useAppStore(s => s.setActiveBroker)
  const { loaded }     = useFeatures()

  const [providers, setProviders]   = useState<ProviderInfo[]>([])
  const [localUser,  setLocalUser]  = useState('')
  const [localPass,  setLocalPass]  = useState('')
  const [loading,    setLoading]    = useState(false)
  const [oauthLoading, setOauthLoading] = useState<string | null>(null)
  const [error,      setError]      = useState('')

  // Fetch enabled providers on mount
  useEffect(() => {
    fetch('/api/auth/providers')
      .then(r => r.json())
      .then(d => setProviders(d?.data ?? []))
      .catch(() => {/* non-fatal — fall back to local login only */})
  }, [])

  // ── Local login ───────────────────────────────────────────────────────────
  const handleLocalLogin = async (e: React.FormEvent) => {
    e.preventDefault()
    setError('')
    setLoading(true)
    try {
      const r    = await fetch('/api/auth/local', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username: localUser, password: localPass }),
      })
      const data = await r.json()
      if (!r.ok) { setError(data.message || data.error || 'Invalid username or password'); return }
      setJwtToken(data.data.token)
      setActiveBroker('None')
      navigate('/')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Connection error')
    } finally {
      setLoading(false)
    }
  }

  // ── OAuth provider login ──────────────────────────────────────────────────
  const handleOAuthLogin = async (providerName: string) => {
    setError('')
    setOauthLoading(providerName)
    try {
      const r    = await fetch(`/api/auth/oauth/${encodeURIComponent(providerName)}/login-url`)
      const data = await r.json()
      if (!r.ok || !data?.data) throw new Error(data?.message ?? 'Could not get login URL')
      // Redirect the browser to the provider's authorization page
      window.location.href = data.data
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not initiate sign-in')
      setOauthLoading(null)
    }
  }

  if (!loaded) return null

  const hasOAuthProviders = providers.length > 0

  return (
    <div style={{
      minHeight: '100vh',
      background: 'linear-gradient(135deg, #0f0f1a 0%, #1a1a2e 100%)',
      display: 'flex', alignItems: 'center', justifyContent: 'center',
      fontFamily: 'Inter, sans-serif', color: C.text,
    }}>
      <div style={{
        background: C.surface, borderRadius: 12, border: `1px solid ${C.border}`,
        padding: 40, width: '100%', maxWidth: 400,
        boxShadow: '0 20px 60px rgba(0,0,0,0.3)',
      }}>
        {/* ── Header ─────────────────────────────────────────────────────── */}
        <h1 style={{ fontSize: 28, fontWeight: 700, marginBottom: 8, textAlign: 'center' }}>
          AlgoTrader
        </h1>
        <p style={{ fontSize: 14, color: C.textSub, textAlign: 'center', marginBottom: 32 }}>
          Sign in to continue
        </p>

        {/* ── Error banner ───────────────────────────────────────────────── */}
        {error && (
          <div style={{
            background: '#7f1d1d', border: '1px solid #991b1b', color: '#fca5a5',
            borderRadius: 8, padding: 12, marginBottom: 24, fontSize: 13,
          }}>
            {error}
          </div>
        )}

        {/* ── OAuth provider buttons ─────────────────────────────────────── */}
        {hasOAuthProviders && (
          <>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
              {providers.map(p => {
                const style = PROVIDER_STYLES[p.iconKey] ?? { bg: C.surface2, text: C.text, border: C.border }
                const busy  = oauthLoading === p.name
                return (
                  <button
                    key={p.name}
                    onClick={() => handleOAuthLogin(p.name)}
                    disabled={!!oauthLoading}
                    style={{
                      display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 10,
                      padding: '10px 16px', background: style.bg, color: style.text,
                      border: `1px solid ${style.border}`, borderRadius: 6,
                      fontSize: 13, fontWeight: 600,
                      cursor: oauthLoading ? 'not-allowed' : 'pointer',
                      opacity: oauthLoading && !busy ? 0.6 : 1,
                      transition: 'opacity 0.15s',
                    }}
                  >
                    {busy
                      ? <span style={{ fontSize: 13 }}>Redirecting…</span>
                      : <>
                          <ProviderIcon iconKey={p.iconKey} />
                          Continue with {p.displayName}
                        </>
                    }
                  </button>
                )
              })}
            </div>

            {/* Divider */}
            <div style={{ display: 'flex', alignItems: 'center', margin: '24px 0', gap: 12 }}>
              <div style={{ flex: 1, height: 1, background: C.border }} />
              <span style={{ fontSize: 12, color: C.textMuted }}>or</span>
              <div style={{ flex: 1, height: 1, background: C.border }} />
            </div>
          </>
        )}

        {/* ── Local username / password form ─────────────────────────────── */}
        <form onSubmit={handleLocalLogin}>
          <div style={{ marginBottom: 16 }}>
            <label style={{ fontSize: 13, fontWeight: 600, color: C.textSub, display: 'block', marginBottom: 6 }}>
              Username
            </label>
            <input
              type="text"
              value={localUser}
              onChange={e => setLocalUser(e.target.value)}
              placeholder="admin"
              autoFocus={!hasOAuthProviders}
              autoComplete="username"
              style={inputStyle}
              required
            />
          </div>
          <div style={{ marginBottom: 24 }}>
            <label style={{ fontSize: 13, fontWeight: 600, color: C.textSub, display: 'block', marginBottom: 6 }}>
              Password
            </label>
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
              width: '100%', padding: '10px 16px',
              background: loading || !localUser || !localPass ? '#4b5563' : C.blue,
              color: '#fff', border: 'none', borderRadius: 6,
              fontSize: 13, fontWeight: 600,
              cursor: loading || !localUser || !localPass ? 'not-allowed' : 'pointer',
              transition: 'background 0.2s',
            }}
          >
            {loading ? 'Signing in…' : 'Sign in'}
          </button>
        </form>
      </div>
    </div>
  )
}

const inputStyle: React.CSSProperties = {
  width: '100%', padding: '10px 12px',
  background: '#0f0f1a', border: '1px solid #2d2d3f',
  borderRadius: 6, color: '#e2e8f0', fontSize: 13, boxSizing: 'border-box',
}
