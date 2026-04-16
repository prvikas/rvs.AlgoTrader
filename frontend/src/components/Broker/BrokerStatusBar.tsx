import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { brokerApi } from '../../api/client'
import { useFeatures } from '../../context/FeaturesContext'
import { BrokerLoginModal } from './BrokerLoginModal'

const BROKER_API_KEYS: Record<string, string> = {
  MStock: import.meta.env.VITE_MSTOCK_API_KEY ?? '',
  Zerodha: import.meta.env.VITE_ZERODHA_API_KEY ?? '',
  Upstox: import.meta.env.VITE_UPSTOX_API_KEY ?? '',
}

function getSessionBroker(): string | null {
  return localStorage.getItem('active_broker')
}

function handleBrokerLogout() {
  localStorage.removeItem('jwt_token')
  localStorage.removeItem('active_broker')
  window.location.href = '/login'
}

// Shared button base style for Login / Logout — consistent appearance
const btnBase: React.CSSProperties = {
  fontSize: 11,
  fontWeight: 600,
  padding: '3px 9px',
  borderRadius: 4,
  cursor: 'pointer',
  border: '1px solid',
  transition: 'opacity 0.15s',
}

export function BrokerStatusBar() {
  const { brokerRequired, loaded } = useFeatures()
  const qc = useQueryClient()
  const [loginBroker, setLoginBroker] = useState<'MStock' | 'Zerodha' | 'Upstox' | null>(null)
  const sessionBroker = getSessionBroker()

  const { data: brokerStatus } = useQuery({
    queryKey: ['broker-status'],
    queryFn: () => brokerApi.status().then(r => r.data.data ?? []),
    refetchInterval: 15_000,
    // Skip polling entirely in backtest-only mode — no broker to check
    enabled: loaded && brokerRequired,
  })

  // In backtest-only mode the entire broker section is hidden
  if (loaded && !brokerRequired) return null

  function handleLoginSuccess(_broker: string) {
    setLoginBroker(null)
    qc.invalidateQueries({ queryKey: ['broker-status'] })
  }

  function isConnected(name: string): boolean {
    const apiStatus = brokerStatus?.find(b => b.brokerName === name)
    if (apiStatus) return apiStatus.isConnected && apiStatus.isAuthenticated
    return name === sessionBroker
  }

  const allBrokers: Array<'MStock' | 'Zerodha' | 'Upstox'> = ['MStock', 'Zerodha', 'Upstox']

  return (
    <>
      <div
        style={{ display: 'flex', gap: 14, alignItems: 'center', flexWrap: 'wrap' }}
        role="list"
        aria-label="Broker connection status"
      >
        {allBrokers.map(name => {
          const connected = isConnected(name)
          const isSessionBroker = name === sessionBroker

          return (
            <div
              key={name}
              role="listitem"
              style={{ display: 'flex', alignItems: 'center', gap: 7 }}
            >
              {/* Status indicator dot — 10×10 for better visibility */}
              <span
                style={{
                  width: 10,
                  height: 10,
                  borderRadius: '50%',
                  background: connected ? '#16a34a' : '#6b7280',
                  display: 'inline-block',
                  flexShrink: 0,
                  boxShadow: connected ? '0 0 0 2px #16a34a30' : 'none',
                }}
                aria-hidden="true"
                title={connected ? `${name}: connected` : `${name}: not connected`}
              />
              <span style={{
                fontSize: 13,
                fontWeight: 600,
                color: connected ? '#e2e8f0' : '#64748b',
              }}>
                {name}
              </span>

              {isSessionBroker ? (
                <button
                  onClick={handleBrokerLogout}
                  title={`Logout from ${name}`}
                  aria-label={`Logout from ${name}`}
                  style={{
                    ...btnBase,
                    background: '#27272a',
                    borderColor: '#3f3f46',
                    color: '#a1a1aa',
                  }}
                  onMouseEnter={e => (e.currentTarget.style.opacity = '0.75')}
                  onMouseLeave={e => (e.currentTarget.style.opacity = '1')}
                >
                  Logout
                </button>
              ) : !connected ? (
                <button
                  onClick={() => setLoginBroker(name)}
                  title={`Login to ${name}`}
                  aria-label={`Login to ${name}`}
                  style={{
                    ...btnBase,
                    background: '#1e3a5f',
                    borderColor: '#1d4ed8',
                    color: '#93c5fd',
                  }}
                  onMouseEnter={e => (e.currentTarget.style.opacity = '0.75')}
                  onMouseLeave={e => (e.currentTarget.style.opacity = '1')}
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
