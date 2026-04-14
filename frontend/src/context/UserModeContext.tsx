/**
 * UserModeContext — single source of truth for Guided vs Pro mode.
 *
 * Guided mode: simplified views, plain-English explanations, wizard flows.
 * Pro mode:    full professional interface (default).
 *
 * Persisted in localStorage so the preference survives page refreshes.
 */

import React, { createContext, useContext, useState, useCallback } from 'react'

export type UserMode = 'guided' | 'pro'

interface UserModeContextValue {
  mode: UserMode
  isGuided: boolean
  isPro: boolean
  setMode: (mode: UserMode) => void
  toggle: () => void
}

const STORAGE_KEY = 'rvs_user_mode'

const UserModeContext = createContext<UserModeContextValue>({
  mode: 'pro',
  isGuided: false,
  isPro: true,
  setMode: () => {},
  toggle: () => {},
})

export function UserModeProvider({ children }: { children: React.ReactNode }) {
  const [mode, setModeState] = useState<UserMode>(() => {
    const stored = localStorage.getItem(STORAGE_KEY)
    return stored === 'guided' ? 'guided' : 'pro'
  })

  const setMode = useCallback((m: UserMode) => {
    setModeState(m)
    localStorage.setItem(STORAGE_KEY, m)
  }, [])

  const toggle = useCallback(() => {
    setModeState(prev => {
      const next: UserMode = prev === 'guided' ? 'pro' : 'guided'
      localStorage.setItem(STORAGE_KEY, next)
      return next
    })
  }, [])

  return (
    <UserModeContext.Provider value={{
      mode,
      isGuided: mode === 'guided',
      isPro: mode === 'pro',
      setMode,
      toggle,
    }}>
      {children}
    </UserModeContext.Provider>
  )
}

export function useUserMode(): UserModeContextValue {
  return useContext(UserModeContext)
}
