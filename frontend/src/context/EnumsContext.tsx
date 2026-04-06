import React, { createContext, useContext, useEffect, useState } from 'react'
import { ENUM_VALUES, validateEnumResponse } from '../types/strategy'

export interface EnumOption {
  value: string
  label: string
}

export type EnumsMap = Record<string, EnumOption[]>

interface EnumsContextValue {
  enums: EnumsMap
  loading: boolean
  error: string | null
}

// Module-level cache: survives hot-reload without re-fetching
let _cachedEnums: EnumsMap | null = null

function buildFallbackEnums(): EnumsMap {
  const result: EnumsMap = {}
  for (const [domain, values] of Object.entries(ENUM_VALUES)) {
    result[domain] = (values as readonly string[]).map(v => ({ value: v, label: v }))
  }
  return result
}

const EnumsContext = createContext<EnumsContextValue>({
  enums: buildFallbackEnums(),
  loading: false,
  error: null,
})

export function EnumsProvider({ children }: { children: React.ReactNode }) {
  const [enums, setEnums] = useState<EnumsMap>(_cachedEnums ?? buildFallbackEnums())
  const [loading, setLoading] = useState(_cachedEnums === null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (_cachedEnums !== null) return

    fetch('/api/enums', {
      headers: {
        Authorization: `Bearer ${localStorage.getItem('jwt_token') ?? ''}`,
      },
    })
      .then(res => {
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        return res.json()
      })
      .then((body: { data?: Record<string, EnumOption[]> }) => {
        const data = body.data ?? (body as Record<string, EnumOption[]>)
        validateEnumResponse(data)
        _cachedEnums = data
        setEnums(data)
        setError(null)
      })
      .catch(err => {
        console.warn('[EnumsContext] Failed to fetch /api/enums, falling back to compile-time values:', err)
        setError('Failed to load enum values from server')
        // enums already initialised with fallback — nothing to do
      })
      .finally(() => setLoading(false))
  }, [])

  return (
    <EnumsContext.Provider value={{ enums, loading, error }}>
      {children}
    </EnumsContext.Provider>
  )
}

export function useEnums(): EnumsContextValue {
  return useContext(EnumsContext)
}
