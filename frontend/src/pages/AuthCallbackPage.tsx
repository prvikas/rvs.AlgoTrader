import { useEffect, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useAppStore } from '../stores/appStore'
import { C } from '../styles/tokens'

/**
 * Landing page after an OAuth provider redirects back to us.
 * URL shape: /auth/callback?provider=Google&exchange=<token>
 *             /auth/callback?provider=Google&error=<message>
 *
 * Redeems the exchange token for the real JWT — the JWT itself never appears in a URL.
 */
export function AuthCallbackPage() {
  const [params]       = useSearchParams()
  const navigate       = useNavigate()
  const setJwtToken    = useAppStore(s => s.setJwtToken)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const provider     = params.get('provider') ?? 'OAuth'
    const exchange     = params.get('exchange')
    const errorMsg     = params.get('error')

    if (errorMsg) {
      setError(decodeURIComponent(errorMsg))
      return
    }

    if (!exchange) {
      setError('No exchange token received from provider.')
      return
    }

    // Redeem the short-lived exchange token for the real JWT
    fetch(`/api/auth/oauth/${encodeURIComponent(provider)}/redeem`, {
      method:  'POST',
      headers: { 'Content-Type': 'application/json' },
      body:    JSON.stringify({ exchangeToken: exchange }),
    })
      .then(async r => {
        const data = await r.json()
        if (!r.ok) throw new Error(data?.message ?? data?.error ?? 'Token exchange failed')
        setJwtToken(data.data.token)
        navigate('/', { replace: true })
      })
      .catch(e => setError(e instanceof Error ? e.message : 'Unexpected error during sign-in'))
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  if (error) {
    return (
      <div style={{
        minHeight: '100vh', background: C.bg, display: 'flex', alignItems: 'center',
        justifyContent: 'center', fontFamily: 'Inter, sans-serif', color: C.text,
      }}>
        <div style={{
          background: C.surface, border: `1px solid ${C.border}`, borderRadius: 12,
          padding: 40, maxWidth: 440, width: '100%', textAlign: 'center',
        }}>
          <div style={{ fontSize: 36, marginBottom: 16 }}>⚠️</div>
          <h2 style={{ margin: '0 0 12px', fontSize: 18, color: C.red }}>Sign-in failed</h2>
          <p style={{ fontSize: 13, color: C.textSub, margin: '0 0 24px' }}>{error}</p>
          <button
            onClick={() => navigate('/login', { replace: true })}
            style={{
              padding: '10px 24px', background: C.blue, color: 'white',
              border: 'none', borderRadius: 6, fontSize: 13, fontWeight: 600, cursor: 'pointer',
            }}
          >
            Back to sign-in
          </button>
        </div>
      </div>
    )
  }

  // Loading state while exchanging token
  return (
    <div style={{
      minHeight: '100vh', background: C.bg, display: 'flex', alignItems: 'center',
      justifyContent: 'center', flexDirection: 'column', gap: 16,
      fontFamily: 'Inter, sans-serif', color: C.textSub, fontSize: 14,
    }}>
      <div style={{
        width: 36, height: 36, border: `3px solid ${C.border}`,
        borderTopColor: C.blue, borderRadius: '50%',
        animation: 'spin 0.8s linear infinite',
      }} />
      Completing sign-in…
      <style>{`@keyframes spin { to { transform: rotate(360deg) } }`}</style>
    </div>
  )
}
