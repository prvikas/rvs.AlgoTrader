import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import { isTokenValid } from '../utils/auth'

// Token key in localStorage — read/written directly so init is synchronous.
// Zustand persist hydrates asynchronously, which causes a render-before-hydration
// race that logs the user out on every page refresh. Using localStorage directly
// and initialising synchronously in the store default avoids that entirely.
const JWT_KEY = 'jwt_token'

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
      // Initialise synchronously from localStorage so the correct value is available
      // on the very first render — no async hydration gap, no redirect to /login.
      jwtToken: localStorage.getItem(JWT_KEY),

      setJwtToken: (token) => {
        // Keep localStorage['jwt_token'] as the single source of truth.
        // Writing here ensures the next synchronous init picks it up immediately.
        if (token) localStorage.setItem(JWT_KEY, token)
        else localStorage.removeItem(JWT_KEY)
        set({ jwtToken: token })
      },

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
      // jwtToken excluded from Zustand persist — managed via localStorage[JWT_KEY] directly.
      // Including it in persist would cause the async-hydration race we just eliminated.
      partialize: (state) => ({
        activeBroker: state.activeBroker,
        timezone: state.timezone,
        sidebarCollapsed: state.sidebarCollapsed,
      }),
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
