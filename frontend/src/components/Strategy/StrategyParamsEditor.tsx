import { useState, useEffect } from 'react'
import { useQuery } from '@tanstack/react-query'
import { strategiesApi, StrategyParamDef } from '../../api/client'
import { C } from '../../styles/tokens'

// Re-export type so existing imports like `import { ParamDef } from './StrategyParamsEditor'`
// keep working without changes.
export type { StrategyParamDef as ParamDef }

// ─── Component ────────────────────────────────────────────────────────────────

interface Props {
  strategyName: string
  value?: Record<string, unknown>
  onChange: (params: Record<string, unknown>) => void
}

const inp: React.CSSProperties = {
  padding: '6px 10px',
  background: C.surface2,
  border: `1px solid ${C.border}`,
  borderRadius: 6,
  color: C.text,
  fontSize: 13,
  width: '100%',
  boxSizing: 'border-box',
}

const label12: React.CSSProperties = {
  fontSize: 12, fontWeight: 600, color: C.textMuted, display: 'block', marginBottom: 4,
}

/**
 * Dynamic parameter editor for a chosen strategy.
 *
 * Schema is fetched from GET /api/strategies/schema/{name} — the backend
 * Config class is the single source of truth for parameter definitions.
 * No parameter metadata is hardcoded on the frontend.
 *
 * Output: Record<string, unknown> — caller JSON.stringify's it as parametersJson.
 */
export function StrategyParamsEditor({ strategyName, value, onChange }: Props) {
  const [params, setParams] = useState<Record<string, unknown>>(value ?? {})

  // Fetch schema from backend — cached forever since schemas never change at runtime
  const { data: schemaResp, isLoading, isError } = useQuery({
    queryKey: ['strategy-schema', strategyName],
    queryFn: () => strategiesApi.getSchema(strategyName),
    staleTime: Infinity,
    enabled: !!strategyName,
  })

  const schema: StrategyParamDef[] = schemaResp?.data?.data ?? []

  // When strategy changes or schema loads: initialize params with defaults,
  // merging any pre-existing values that were passed in via `value`.
  useEffect(() => {
    if (!schema.length) return
    const defaults: Record<string, unknown> = {}
    schema.forEach(p => { defaults[p.key] = p.default })
    const next = value && Object.keys(value).length > 0 ? value : defaults
    setParams(next)
    onChange(next)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [strategyName, schema.length])

  // Sync external `value` changes (e.g. pre-fill from a saved instance)
  useEffect(() => {
    if (value && Object.keys(value).length > 0) {
      setParams(value)
    }
  }, [value])

  const set = (key: string, val: unknown) => {
    const next = { ...params, [key]: val }
    setParams(next)
    onChange(next)
  }

  if (isLoading) {
    return (
      <div style={{ padding: 12, background: C.surface, borderRadius: 8, border: `1px solid ${C.border}`, color: C.textMuted, fontSize: 12 }}>
        Loading parameters…
      </div>
    )
  }

  if (isError || !schema.length) {
    return (
      <div style={{ padding: 12, background: C.surface, borderRadius: 8, border: `1px solid ${C.border}`, color: C.textSub, fontSize: 12 }}>
        No parameters defined for "{strategyName}". Strategy will use built-in defaults.
      </div>
    )
  }

  return (
    <div style={{ background: C.surface, border: `1px solid ${C.border}`, borderRadius: 8, padding: 16 }}>
      <div style={{ fontSize: 11, fontWeight: 700, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.06em', marginBottom: 12, paddingBottom: 6, borderBottom: `1px solid ${C.border}` }}>
        {strategyName} — Parameters
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
        {schema.map(p => (
          <div key={p.key} style={p.type === 'bool' ? { gridColumn: '1 / -1' } : {}}>
            {p.type === 'bool' ? (
              /* Toggle */
              <div
                style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', cursor: 'pointer', padding: '6px 0', borderBottom: `1px solid ${C.border}` }}
                onClick={() => set(p.key, !params[p.key])}
              >
                <div>
                  <span style={{ fontSize: 13, fontWeight: 600, color: C.text }}>{p.label}</span>
                  {p.hint && <div style={{ fontSize: 11, color: C.textSub, marginTop: 2 }}>{p.hint}</div>}
                </div>
                <Pill on={!!params[p.key]} />
              </div>
            ) : p.type === 'select' ? (
              /* Select */
              <div>
                <label style={label12}>{p.label}</label>
                <select
                  value={String(params[p.key] ?? p.default)}
                  onChange={e => set(p.key, e.target.value)}
                  style={inp}
                >
                  {p.options?.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
                </select>
                {p.hint && <span style={{ fontSize: 10, color: C.textSub, marginTop: 3, display: 'block' }}>{p.hint}</span>}
              </div>
            ) : (
              /* Number input */
              <div>
                <label style={label12}>{p.label}</label>
                <input
                  type="number"
                  value={String(params[p.key] ?? p.default)}
                  onChange={e => set(p.key, p.type === 'int' ? parseInt(e.target.value, 10) : parseFloat(e.target.value))}
                  min={p.min}
                  max={p.max}
                  step={p.step ?? (p.type === 'int' ? 1 : 0.1)}
                  style={inp}
                />
                {p.hint && <span style={{ fontSize: 10, color: C.textSub, marginTop: 3, display: 'block' }}>{p.hint}</span>}
              </div>
            )}
          </div>
        ))}
      </div>
    </div>
  )
}

// ─── Small helpers ────────────────────────────────────────────────────────────

function Pill({ on }: { on: boolean }) {
  return (
    <div style={{ width: 40, height: 20, borderRadius: 10, background: on ? C.blue : C.border, position: 'relative', flexShrink: 0 }}>
      <div style={{ position: 'absolute', top: 2, left: on ? 22 : 2, width: 16, height: 16, borderRadius: '50%', background: '#fff', transition: 'left 0.2s' }} />
    </div>
  )
}

/** Stringify params for the API — kept as a named export for backward compatibility */
export const paramsToJson = (params: Record<string, unknown>) => JSON.stringify(params)
