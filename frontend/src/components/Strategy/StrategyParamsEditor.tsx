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
 * Params with no `section` are rendered in a flat 2-col grid ("General").
 * Params that share a `section` are grouped under a condition card:
 *   - A bool param at the section root acts as the card toggle (header pill).
 *   - Other params gated by that bool via `enabledBy` appear inside the card,
 *     dimmed when the toggle is off.
 *
 * Schema is fetched from GET /api/strategies/schema/{name} — the backend
 * Config class is the single source of truth.  No metadata is hardcoded here.
 */
export function StrategyParamsEditor({ strategyName, value, onChange }: Props) {
  const [params, setParams] = useState<Record<string, unknown>>(value ?? {})

  const { data: schemaResp, isLoading, isError } = useQuery({
    queryKey: ['strategy-schema', strategyName],
    queryFn: () => strategiesApi.getSchema(strategyName),
    staleTime: Infinity,
    enabled: !!strategyName,
  })

  const schema: StrategyParamDef[] = schemaResp?.data?.data ?? []

  useEffect(() => {
    if (!schema.length) return
    const defaults: Record<string, unknown> = {}
    schema.forEach(p => { defaults[p.key] = p.default })
    const next = value && Object.keys(value).length > 0 ? value : defaults
    setParams(next)
    onChange(next)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [strategyName, schema.length])

  useEffect(() => {
    if (value && Object.keys(value).length > 0) setParams(value)
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

  // ── Partition into flat (no section) and sectioned groups ─────────────────
  const flat    = schema.filter(p => !p.section)
  const grouped = groupBySection(schema.filter(p => !!p.section))

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>

      {/* Flat params — rendered first in a simple 2-col grid */}
      {flat.length > 0 && (
        <div style={{ background: C.surface, border: `1px solid ${C.border}`, borderRadius: 8, padding: 16 }}>
          <SectionHeader title={strategyName + ' — General'} />
          <ParamGrid params={flat} values={params} set={set} />
        </div>
      )}

      {/* Section cards */}
      {Object.entries(grouped).map(([section, sParams]) => (
        <SectionCard
          key={section}
          section={section}
          params={sParams}
          values={params}
          set={set}
        />
      ))}
    </div>
  )
}

// ─── Section card ─────────────────────────────────────────────────────────────

interface SectionCardProps {
  section: string
  params: StrategyParamDef[]
  values: Record<string, unknown>
  set: (key: string, val: unknown) => void
}

function SectionCard({ section, params, values, set }: SectionCardProps) {
  // The gating bool: a bool param whose key is referenced by other params' enabledBy,
  // OR the first bool param in the section (convention: leading toggle).
  const gatingKeys = new Set(params.map(p => p.enabledBy).filter(Boolean) as string[])
  const gate = params.find(p => p.type === 'bool' && gatingKeys.has(p.key))
              ?? params.find(p => p.type === 'bool')

  const isEnabled = gate ? !!values[gate.key] : true

  // Params that belong to the body of the card (everything except the gating bool itself)
  const bodyParams = params.filter(p => p !== gate)

  return (
    <div style={{
      background: C.surface,
      border: `1px solid ${isEnabled ? C.border : C.border}`,
      borderRadius: 8,
      overflow: 'hidden',
      opacity: gate && !isEnabled ? 0.65 : 1,
      transition: 'opacity 0.15s',
    }}>
      {/* Card header: section name + gating toggle */}
      <div style={{
        display: 'flex', alignItems: 'center', justifyContent: 'space-between',
        padding: '10px 16px',
        background: C.surface2,
        borderBottom: `1px solid ${C.border}`,
        cursor: gate ? 'pointer' : 'default',
      }}
        onClick={gate ? () => set(gate.key, !values[gate.key]) : undefined}
      >
        <div>
          <span style={{ fontSize: 12, fontWeight: 700, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.06em' }}>
            {section}
          </span>
          {gate?.hint && (
            <div style={{ fontSize: 11, color: C.textSub, marginTop: 1 }}>{gate.hint}</div>
          )}
        </div>
        {gate && <Pill on={!!values[gate.key]} />}
      </div>

      {/* Card body: dependent params */}
      {bodyParams.length > 0 && (
        <div style={{ padding: 16 }}>
          <ParamGrid
            params={bodyParams}
            values={values}
            set={set}
            parentEnabled={isEnabled}
          />
        </div>
      )}
    </div>
  )
}

// ─── 2-column param grid ──────────────────────────────────────────────────────

interface ParamGridProps {
  params: StrategyParamDef[]
  values: Record<string, unknown>
  set: (key: string, val: unknown) => void
  parentEnabled?: boolean
}

function ParamGrid({ params, values, set, parentEnabled = true }: ParamGridProps) {
  return (
    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
      {params.map(p => {
        // A param is active when its direct gate (enabledBy) is on AND the parent section is enabled
        const gateVal = p.enabledBy !== undefined ? !!values[p.enabledBy] : true
        const active  = parentEnabled && gateVal

        return (
          <div
            key={p.key}
            style={{
              ...(p.type === 'bool' ? { gridColumn: '1 / -1' } : {}),
              opacity: active ? 1 : 0.45,
              transition: 'opacity 0.15s',
              pointerEvents: active ? 'auto' : 'none',
            }}
          >
            <ParamInput p={p} values={values} set={set} />
          </div>
        )
      })}
    </div>
  )
}

// ─── Individual param input ───────────────────────────────────────────────────

function ParamInput({ p, values, set }: { p: StrategyParamDef; values: Record<string, unknown>; set: (k: string, v: unknown) => void }) {
  if (p.type === 'bool') {
    return (
      <div
        style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', cursor: 'pointer', padding: '6px 0', borderBottom: `1px solid ${C.border}` }}
        onClick={() => set(p.key, !values[p.key])}
      >
        <div>
          <span style={{ fontSize: 13, fontWeight: 600, color: C.text }}>{p.label}</span>
          {p.hint && <div style={{ fontSize: 11, color: C.textSub, marginTop: 2 }}>{p.hint}</div>}
        </div>
        <Pill on={!!values[p.key]} />
      </div>
    )
  }

  if (p.type === 'select') {
    return (
      <div>
        <label style={label12}>{p.label}</label>
        <select
          value={String(values[p.key] ?? p.default)}
          onChange={e => set(p.key, e.target.value)}
          style={inp}
        >
          {p.options?.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
        </select>
        {p.hint && <span style={{ fontSize: 10, color: C.textSub, marginTop: 3, display: 'block' }}>{p.hint}</span>}
      </div>
    )
  }

  return (
    <div>
      <label style={label12}>{p.label}</label>
      <input
        type="number"
        value={String(values[p.key] ?? p.default)}
        onChange={e => set(p.key, p.type === 'int' ? parseInt(e.target.value, 10) : parseFloat(e.target.value))}
        min={p.min}
        max={p.max}
        step={p.step ?? (p.type === 'int' ? 1 : 0.1)}
        style={inp}
      />
      {p.hint && <span style={{ fontSize: 10, color: C.textSub, marginTop: 3, display: 'block' }}>{p.hint}</span>}
    </div>
  )
}

// ─── Small helpers ────────────────────────────────────────────────────────────

function SectionHeader({ title }: { title: string }) {
  return (
    <div style={{ fontSize: 11, fontWeight: 700, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.06em', marginBottom: 12, paddingBottom: 6, borderBottom: `1px solid ${C.border}` }}>
      {title}
    </div>
  )
}

function Pill({ on }: { on: boolean }) {
  return (
    <div style={{ width: 40, height: 20, borderRadius: 10, background: on ? C.blue : C.border, position: 'relative', flexShrink: 0, transition: 'background 0.2s' }}>
      <div style={{ position: 'absolute', top: 2, left: on ? 22 : 2, width: 16, height: 16, borderRadius: '50%', background: '#fff', transition: 'left 0.2s' }} />
    </div>
  )
}

/** Group params by their section value, preserving insertion order */
function groupBySection(params: StrategyParamDef[]): Record<string, StrategyParamDef[]> {
  const result: Record<string, StrategyParamDef[]> = {}
  for (const p of params) {
    const s = p.section!
    if (!result[s]) result[s] = []
    result[s].push(p)
  }
  return result
}

/** Stringify params for the API — kept as a named export for backward compatibility */
export const paramsToJson = (params: Record<string, unknown>) => JSON.stringify(params)
