import { useState, useEffect, useCallback } from 'react'
import { useQuery } from '@tanstack/react-query'
import {
  strategiesApi,
  scenariosApi,
  StrategyScenario,
  StrategyParamDef,
} from '../../api/client'
import { C, SP } from '../../styles/tokens'

interface Props {
  open: boolean
  onClose: () => void
  instanceId: string
  strategyType: string
  /** Base parameters JSON from the strategy instance */
  baseParametersJson?: string
  /** If set, editing an existing scenario; otherwise creating new */
  scenario?: StrategyScenario
  onSaved: () => void
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
  fontSize: 12,
  fontWeight: 600,
  color: C.textMuted,
  display: 'block',
  marginBottom: 4,
}

export default function ScenarioEditorDrawer({
  open, onClose, instanceId, strategyType, baseParametersJson, scenario, onSaved,
}: Props) {
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  // Only the keys the user explicitly wants to override — shown as a partial editor
  const [overrideKeys, setOverrideKeys] = useState<Set<string>>(new Set())
  const [overrideValues, setOverrideValues] = useState<Record<string, unknown>>({})
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Fetch schema (cached — never re-requested for the same strategy)
  const { data: schemaResp } = useQuery({
    queryKey: ['strategy-schema', strategyType],
    queryFn: () => strategiesApi.getSchema(strategyType),
    enabled: open && !!strategyType,
    staleTime: Infinity,
  })
  const schema: StrategyParamDef[] = schemaResp?.data?.data ?? []

  // Parse base params for defaults
  const baseParams: Record<string, unknown> = (() => {
    try { return JSON.parse(baseParametersJson || '{}') }
    catch { return {} }
  })()

  // Populate form when editing an existing scenario
  useEffect(() => {
    if (!open) return
    if (scenario) {
      setName(scenario.name)
      setDescription(scenario.description ?? '')
      try {
        const override = JSON.parse(scenario.parametersJsonOverride || '{}')
        setOverrideKeys(new Set(Object.keys(override)))
        setOverrideValues(override)
      } catch {
        setOverrideKeys(new Set())
        setOverrideValues({})
      }
    } else {
      setName('')
      setDescription('')
      setOverrideKeys(new Set())
      setOverrideValues({})
    }
    setError(null)
  }, [open, scenario])

  const toggleKey = useCallback((key: string, def: StrategyParamDef) => {
    setOverrideKeys(prev => {
      const next = new Set(prev)
      if (next.has(key)) {
        next.delete(key)
        setOverrideValues(v => { const { [key]: _, ...rest } = v; return rest })
      } else {
        next.add(key)
        // Seed with base value or schema default
        const seed = (baseParams[key] !== undefined ? baseParams[key] : def.default) ?? ''
        setOverrideValues(v => ({ ...v, [key]: seed }))
      }
      return next
    })
  }, [baseParams])

  const setValue = useCallback((key: string, raw: string, type: string) => {
    const parsed = type === 'bool'
      ? raw === 'true'
      : type === 'int'
        ? parseInt(raw, 10)
        : parseFloat(raw)
    setOverrideValues(v => ({ ...v, [key]: isNaN(parsed as number) ? raw : parsed }))
  }, [])

  const handleSave = async () => {
    if (!name.trim()) { setError('Scenario name is required.'); return }
    setSaving(true)
    setError(null)
    try {
      const overrideJson = overrideKeys.size > 0
        ? JSON.stringify(Object.fromEntries(
            [...overrideKeys].map(k => [k, overrideValues[k]])
          ))
        : undefined

      if (scenario) {
        await scenariosApi.update(instanceId, scenario.id, {
          name: name.trim(),
          description: description.trim() || undefined,
          parametersJsonOverride: overrideJson,
        })
      } else {
        await scenariosApi.create(instanceId, {
          name: name.trim(),
          description: description.trim() || undefined,
          parametersJsonOverride: overrideJson,
        })
      }
      onSaved()
      onClose()
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : 'Save failed'
      setError(msg)
    } finally {
      setSaving(false)
    }
  }

  if (!open) return null

  return (
    <>
      {/* Backdrop */}
      <div
        onClick={onClose}
        style={{
          position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.5)', zIndex: 1000,
        }}
      />
      {/* Drawer */}
      <div style={{
        position: 'fixed', top: 0, right: 0, bottom: 0, width: 520,
        background: C.surface, borderLeft: `1px solid ${C.border}`,
        zIndex: 1001, display: 'flex', flexDirection: 'column',
        overflowY: 'auto',
      }}>
        {/* Header */}
        <div style={{
          padding: '14px 20px', borderBottom: `1px solid ${C.border}`,
          display: 'flex', alignItems: 'center', justifyContent: 'space-between',
        }}>
          <span style={{ fontWeight: 600, fontSize: 15, color: C.text }}>
            {scenario ? 'Edit Scenario' : 'New Scenario'}
          </span>
          <button onClick={onClose} style={{
            background: 'none', border: 'none', color: C.textMuted,
            cursor: 'pointer', fontSize: 18, lineHeight: 1,
          }}>✕</button>
        </div>

        {/* Body */}
        <div style={{ padding: '20px', flex: 1 }}>
          {/* Name */}
          <div style={{ marginBottom: SP.lg }}>
            <label style={label12}>SCENARIO NAME *</label>
            <input
              value={name}
              onChange={e => setName(e.target.value)}
              placeholder="e.g. Tight Stop + High Volume"
              style={inp}
            />
          </div>

          {/* Description */}
          <div style={{ marginBottom: SP.xl }}>
            <label style={label12}>DESCRIPTION</label>
            <textarea
              value={description}
              onChange={e => setDescription(e.target.value)}
              rows={2}
              placeholder="Optional notes on this parameter set"
              style={{ ...inp, resize: 'vertical' }}
            />
          </div>

          {/* Parameter Override Section */}
          <div style={{
            marginBottom: SP.md,
            display: 'flex', alignItems: 'center', justifyContent: 'space-between',
          }}>
            <span style={{ fontSize: 13, fontWeight: 600, color: C.text }}>
              Parameter Overrides
            </span>
            <span style={{ fontSize: 11, color: C.textMuted }}>
              Check a param to override it; leave unchecked to use instance default
            </span>
          </div>

          {schema.length === 0 ? (
            <div style={{ color: C.textMuted, fontSize: 12 }}>Loading schema…</div>
          ) : (
            schema.map(def => {
              const checked = overrideKeys.has(def.key)
              const baseVal = baseParams[def.key] !== undefined
                ? baseParams[def.key]
                : def.default
              const currentVal = overrideValues[def.key] ?? baseVal

              return (
                <div key={def.key} style={{
                  marginBottom: SP.md,
                  padding: '10px 12px',
                  background: checked ? C.surface2 : C.surface3,
                  borderRadius: 6,
                  border: `1px solid ${checked ? C.border3 : C.border}`,
                  opacity: checked ? 1 : 0.65,
                  transition: 'all 0.15s',
                }}>
                  <div style={{
                    display: 'flex', alignItems: 'center', gap: 10, marginBottom: checked ? 8 : 0,
                  }}>
                    <input
                      type="checkbox"
                      checked={checked}
                      onChange={() => toggleKey(def.key, def)}
                      style={{ cursor: 'pointer', accentColor: C.blue }}
                    />
                    <div style={{ flex: 1 }}>
                      <span style={{ fontSize: 13, color: C.text, fontWeight: 500 }}>{def.label}</span>
                      {!checked && (
                        <span style={{ marginLeft: 8, fontSize: 11, color: C.textMuted }}>
                          base: {String(baseVal)}
                        </span>
                      )}
                    </div>
                    <span style={{
                      fontSize: 11, color: C.textMuted,
                      background: C.bg, padding: '2px 6px', borderRadius: 4,
                    }}>
                      {def.type}
                    </span>
                  </div>

                  {checked && (
                    <div>
                      {def.type === 'bool' ? (
                        <select
                          value={String(currentVal)}
                          onChange={e => setValue(def.key, e.target.value, 'bool')}
                          style={{ ...inp, width: 'auto' }}
                        >
                          <option value="true">true</option>
                          <option value="false">false</option>
                        </select>
                      ) : (
                        <input
                          type="number"
                          value={String(currentVal)}
                          step={def.step ?? (def.type === 'int' ? 1 : 0.1)}
                          min={def.min}
                          max={def.max}
                          onChange={e => setValue(def.key, e.target.value, def.type)}
                          style={inp}
                        />
                      )}
                      {def.hint && (
                        <div style={{ fontSize: 11, color: C.textMuted, marginTop: 4 }}>
                          {def.hint}
                        </div>
                      )}
                    </div>
                  )}
                </div>
              )
            })
          )}

          {error && (
            <div style={{
              marginTop: SP.lg, padding: '8px 12px',
              background: C.redBg, border: `1px solid ${C.red}`,
              borderRadius: 6, color: C.red, fontSize: 12,
            }}>
              {error}
            </div>
          )}
        </div>

        {/* Footer */}
        <div style={{
          padding: '14px 20px', borderTop: `1px solid ${C.border}`,
          display: 'flex', gap: 10, justifyContent: 'flex-end',
        }}>
          <button onClick={onClose} disabled={saving} style={{
            padding: '7px 18px', background: C.surface2,
            border: `1px solid ${C.border}`, borderRadius: 6,
            color: C.textSub, cursor: 'pointer', fontSize: 13,
          }}>
            Cancel
          </button>
          <button onClick={handleSave} disabled={saving || !name.trim()} style={{
            padding: '7px 18px', background: C.blue,
            border: 'none', borderRadius: 6,
            color: '#fff', cursor: saving ? 'not-allowed' : 'pointer', fontSize: 13,
            opacity: saving ? 0.7 : 1,
          }}>
            {saving ? 'Saving…' : scenario ? 'Save Changes' : 'Create Scenario'}
          </button>
        </div>
      </div>
    </>
  )
}
