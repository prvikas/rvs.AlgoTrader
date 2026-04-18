import { createContext, useContext, useEffect, useState } from 'react'
import { apiClient, ApiResponse } from '../api/client'

interface FeaturesState {
  /** True when broker login + Forward/Live modes are available. False = backtest-only mode. */
  brokerRequired: boolean
  /**
   * False until the initial /api/config/features fetch completes.
   * ProtectedRoute must NOT redirect to /login while this is false.
   */
  loaded: boolean
}

const FeaturesContext = createContext<FeaturesState>({
  brokerRequired: true,  // safe default — show everything until we know otherwise
  loaded: false,
})

export function FeaturesProvider({ children }: { children: React.ReactNode }) {
  const [state, setState] = useState<FeaturesState>({ brokerRequired: true, loaded: false })

  useEffect(() => {
    let cancelled = false

    async function init() {
      let brokerRequired = true

      try {
        const res = await apiClient.get<ApiResponse<{ brokerRequired: boolean }>>('/config/features')
        brokerRequired = res.data.data?.brokerRequired ?? true
      } catch {
        // Network/server error — fall back to safe default (broker required)
        brokerRequired = true
      }

      // Persist mode flag to localStorage — used by the 401 interceptor to decide
      // whether to redirect to /login or attempt silent re-auth.
      if (!brokerRequired) {
        localStorage.setItem('rvs_offline_mode', 'true')
      } else {
        localStorage.removeItem('rvs_offline_mode')
      }

      if (!cancelled) setState({ brokerRequired, loaded: true })
    }

    init()
    return () => { cancelled = true }
  }, [])

  return <FeaturesContext.Provider value={state}>{children}</FeaturesContext.Provider>
}

export function useFeatures(): FeaturesState {
  return useContext(FeaturesContext)
}
