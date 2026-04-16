import { createContext, useContext, useEffect, useState } from 'react'
import { apiClient, ApiResponse } from '../api/client'

interface FeaturesState {
  /** True when broker login + Forward/Live modes are available. False = backtest-only mode. */
  brokerRequired: boolean
  /** False until the initial /api/config/features fetch resolves. */
  loaded: boolean
}

const FeaturesContext = createContext<FeaturesState>({
  brokerRequired: true,  // safe default — show everything until we know otherwise
  loaded: false,
})

export function FeaturesProvider({ children }: { children: React.ReactNode }) {
  const [state, setState] = useState<FeaturesState>({ brokerRequired: true, loaded: false })

  useEffect(() => {
    apiClient
      .get<ApiResponse<{ brokerRequired: boolean }>>('/config/features')
      .then(res => {
        const brokerRequired = res.data.data?.brokerRequired ?? true
        setState({ brokerRequired, loaded: true })
      })
      .catch(() => {
        // On failure keep the safe default (show everything) so we don't silently block broker login
        setState({ brokerRequired: true, loaded: true })
      })
  }, [])

  return <FeaturesContext.Provider value={state}>{children}</FeaturesContext.Provider>
}

export function useFeatures(): FeaturesState {
  return useContext(FeaturesContext)
}
