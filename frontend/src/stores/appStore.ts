import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import { isTokenValid } from '../utils/auth'

interface AppState {
  // Auth
  jwtToken: string | null
  setJwtToken: (token: string | null) => void
  isAuthenticated: () => boolean

  // Kill switch
  killSwitchActive: boolean
  setKillSwitchActive: (active: boolean) => void

  // Active broker
  activeBroker: string
  setActiveBroker: (broker: string) => void

  // User preferences
  timezone: string  // always 'Asia/Kolkata' for trading, user can switch display
  setTimezone: (tz: string) => void

  // UI state
  sidebarCollapsed: boolean
  toggleSidebar: () => void
}

export const useAppStore = create<AppState>()(
  persist(
    (set, get) => ({
      jwtToken: null,
      setJwtToken: (token) => set({ jwtToken: token }),
      isAuthenticated: () => isTokenValid(get().jwtToken),

      killSwitchActive: false,
      setKillSwitchActive: (active) => set({ killSwitchActive: active }),

      activeBroker: 'Zerodha',
      setActiveBroker: (broker) => set({ activeBroker: broker }),

      timezone: 'Asia/Kolkata',
      setTimezone: (tz) => set({ timezone: tz }),

      sidebarCollapsed: false,
      toggleSidebar: () => set(s => ({ sidebarCollapsed: !s.sidebarCollapsed })),
    }),
    {
      name: 'algotrader-app',
      partialize: (state) => ({
        jwtToken: state.jwtToken,          // persist so session survives page reload
        activeBroker: state.activeBroker,
        timezone: state.timezone,
        sidebarCollapsed: state.sidebarCollapsed,
      })
    }
  )
)

interface StrategyState {
  instances: Map<string, import('../api/client').StrategyInstance>
  setInstance: (instance: import('../api/client').StrategyInstance) => void
  removeInstance: (id: string) => void
  updateStatus: (id: string, status: string) => void
}

export const useStrategyStore = create<StrategyState>((set) => ({
  instances: new Map(),
  setInstance: (instance) => set(s => {
    const next = new Map(s.instances)
    next.set(instance.id, instance)
    return { instances: next }
  }),
  removeInstance: (id) => set(s => {
    const next = new Map(s.instances)
    next.delete(id)
    return { instances: next }
  }),
  updateStatus: (id, status) => set(s => {
    const next = new Map(s.instances)
    const existing = next.get(id)
    if (existing) next.set(id, { ...existing, status })
    return { instances: next }
  })
}))
