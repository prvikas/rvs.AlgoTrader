import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { brokerApi, BrokerStatus } from '../../api/client'
import { BrokerLoginModal } from './BrokerLoginModal'

const BROKER_API_KEYS: Record<string, string> = {
  MStock: import.meta.env.VITE_MSTOCK_API_KEY ?? '',
  Zerodha: import.meta.env.VITE_ZERODHA_API_KEY ?? '',
  Upstox: import.meta.env.VITE_UPSTOX_API_KEY ?? '',
}

// The broker the user logged into the app with (set at login time)
function getSessionBroker(): string | null {
  return localStorage.getItem('active_broker')
}

function handleBrokerLogout() {
  localStorage.removeItem('jwt_token')
  localStorage.removeItem('active_broker')
  window.location.href = '/login'
}

export function BrokerStatusBar() {
  const qc = useQueryClient()
  const [loginBroker, setLoginBroker] = useState<'MStock' | 'Zerodha' | 'Upstox' | null>(null)
  const sessionBroker = getSessionBroker()

  const { data: brokerStatus } = useQuery({
    queryKey: ['broker-status'],
    queryFn: () => brokerApi.status().then(r => r.data.data ?? []),
    refetchInterval: 15_000,
  })

  function handleLoginSuccess(broker: string) {
    setLoginBroker(null)
    qc.invalidateQueries({ queryKey: ['broker-status'] })
  }

  // Determine connection status: API response takes priority,
  // but if not yet returned, fall back to sessionBroker from localStorage
  function isConnected(name: string): boolean {
    const apiStatus = brokerStatus?.find(b => b.brokerName === name)
    if (apiStatus) return apiStatus.isConnected && apiStatus.isAuthenticated
    // If the API hasn't returned yet, trust localStorage (user just logged in)
    return name === sessionBroker
  }

  const allBrokers: Array<'MStock' | 'Zerodha' | 'Upstox'> = ['MStock', 'Zerodha', 'Upstox']

  return (
    <>
      <div style={{ display: 'flex', gap: 12, alignItems: 'center' }}>
        {allBrokers.map(name => {
          const connected = isConnected(name)
          const isSessionBroker = name === sessionBroker

          return (
            <div key={name} style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
              <span style={{
                width: 8, height: 8, borderRadius: '50%',
                background: connected ? '#16a34a' : '#dc2626',
                display: 'inline-block', flexShrink: 0,
              }} />
              <span style={{ fontSize: 13, color: connected ? '#e2e8f0' : '#94a3b8', fontWeight: connected ? 600 : 400 }}>
                {name}
              </span>
              {/* Session broker: show Logout. Other disconnected brokers: show Login */}
              {isSessionBroker ? (
                <button
                  onClick={handleBrokerLogout}
                  title="Logout and disconnect"
                  style={{
                    fontSize: 10, padding: '2px 7px', borderRadius: 4,
                    background: '#7f1d1d', border: '1px solid #991b1b',
                    color: '#fca5a5', cursor: 'pointer',
                  }}
                >
                  Logout
                </button>
              ) : !connected ? (
                <button
                  onClick={() => setLoginBroker(name)}
                  style={{
                    fontSize: 10, padding: '2px 7px', borderRadius: 4,
                    background: '#1e1e2e', border: '1px solid #3d3d5c',
                    color: '#94a3b8', cursor: 'pointer',
                  }}
                >
                  Login
                </button>
              ) : null}
            </div>
          )
        })}
      </div>

      {loginBroker && (
        <BrokerLoginModal
          broker={loginBroker}
          apiKey={BROKER_API_KEYS[loginBroker]}
          onSuccess={handleLoginSuccess}
          onClose={() => setLoginBroker(null)}
        />
      )}
    </>
  )
}
