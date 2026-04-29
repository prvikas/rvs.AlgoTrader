import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  quantLabApi, QuantCondition, QuantConditionEntry,
  QUANT_CONDITION_STATUSES,
} from '../api/client'
import { C, CONTENT_PAD, F } from '../styles/tokens'

// ── Status colour map ─────────────────────────────────────────────────────────

const STATUS_COLOR: Record<string, string> = {
  Hypothesis:   C.textMuted,
  Backtesting:  C.blue,
  PaperTrading: C.amber,
  LiveSmall:    '#a78bfa',   // purple-ish
  LiveFull:     C.green,
  Retired:      C.textDim,
}

const STATUS_LABEL: Record<string, string> = {
  Hypothesis:   'Hypothesis',
  Backtesting:  'Backtesting',
  PaperTrading: 'Paper Trading',
  LiveSmall:    'Live Small',
  LiveFull:     'Live Full',
  Retired:      'Retired',
}

function StatusBadge({ status }: { status: string }) {
  const color = STATUS_COLOR[status] ?? C.textMuted
  return (
    <span style={{
      fontSize: 10, fontWeight: 700, color,
      background: `${color}18`, borderRadius: 3,
      padding: '2px 8px', textTransform: 'uppercase', letterSpacing: '0.05em',
    }}>
      {STATUS_LABEL[status] ?? status}
    </span>
  )
}

// ── Condition entry row ───────────────────────────────────────────────────────

function ConditionEntryRow({ entry }: { entry: QuantConditionEntry }) {
  return (
    <div style={{
      display: 'grid', gridTemplateColumns: '130px 50px 80px 1fr',
      gap: 8, padding: '5px 0', borderBottom: `1px solid ${C.border}`, fontSize: 12,
    }}>
      <span style={{ color: C.blue, fontWeight: 600 }}>{entry.indicator}</span>
      <span style={{ color: C.text, fontFamily: F.mono }}>{entry.operator}</span>
      <span style={{ color: C.text, fontFamily: F.mono, fontWeight: 700 }}>{entry.value}</span>
      <span style={{ color: C.textMuted }}>{entry.description}</span>
    </div>
  )
}

// ── Condition card (full expanded view) ───────────────────────────────────────

function ConditionCard({
  condition,
  onEdit,
  onDeleted,
}: {
  condition: QuantCondition
  onEdit: (c: QuantCondition) => void
  onDeleted: () => void
}) {
  const qc = useQueryClient()
  const [noteText, setNoteText] = useState('')
  const [expanded, setExpanded] = useState(false)

  const addNote = useMutation({
    mutationFn: () => quantLabApi.addNote(condition.id, noteText.trim()),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ['quant-conditions'] }); setNoteText('') },
  })

  const changeStatus = useMutation({
    mutationFn: (status: string) => quantLabApi.changeStatus(condition.id, status),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['quant-conditions'] }),
  })

  const clone = useMutation({
    mutationFn: () => quantLabApi.clone(condition.id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['quant-conditions'] }),
  })

  const del = useMutation({
    mutationFn: () => quantLabApi.delete(condition.id),
    onSuccess: onDeleted,
  })

  const updatedLabel = new Date(condition.updatedAt).toLocaleDateString('en-IN', { day:'2-digit', month:'short', year:'numeric' })

  return (
    <div style={{
      background: C.surface, border: `1px solid ${C.border}`, borderRadius: 8,
      padding: '14px 18px', display: 'flex', flexDirection: 'column', gap: 10,
    }}>
      {/* Header row */}
      <div style={{ display: 'flex', alignItems: 'flex-start', gap: 10 }}>
        <div style={{ flex: 1 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
            <span style={{ fontSize: 15, fontWeight: 700, color: C.text }}>{condition.name}</span>
            <StatusBadge status={condition.status} />
            {condition.isTemplate && (
              <span style={{ fontSize: 10, color: C.amber, background: `${C.amber}18`, borderRadius: 3, padding: '2px 7px' }}>
                Template
              </span>
            )}
          </div>
          <div style={{ display: 'flex', gap: 6, marginTop: 4, flexWrap: 'wrap' }}>
            {condition.tags.map(t => (
              <span key={t} style={{ fontSize: 10, color: C.textDim, background: C.surface2, borderRadius: 3, padding: '1px 6px' }}>
                {t}
              </span>
            ))}
          </div>
        </div>
        <span style={{ fontSize: 10, color: C.textDim, flexShrink: 0 }}>{updatedLabel}</span>
      </div>

      {/* Hypothesis */}
      <p style={{ margin: 0, fontSize: 13, color: C.textSub, lineHeight: 1.55 }}>
        {condition.hypothesis || <em style={{ color: C.textDim }}>No hypothesis defined</em>}
      </p>

      {/* Summary stats */}
      <div style={{ display: 'flex', gap: 16, fontSize: 11, color: C.textDim }}>
        <span>{condition.conditions.length} conditions</span>
        <span>{condition.notes.length} notes</span>
        <button
          onClick={() => setExpanded(e => !e)}
          style={{ background: 'none', border: 'none', color: C.blue, fontSize: 11, cursor: 'pointer', padding: 0 }}
        >
          {expanded ? 'Collapse' : 'Expand'}
        </button>
      </div>

      {/* Expanded detail */}
      {expanded && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 14, marginTop: 4 }}>
          {/* Conditions */}
          {condition.conditions.length > 0 && (
            <div>
              <Label color={C.blue}>Conditions</Label>
              <div style={{ display: 'grid', gridTemplateColumns: '130px 50px 80px 1fr', gap: 8, padding: '3px 0 6px', fontSize: 10, color: C.textDim, fontWeight: 700, borderBottom: `1px solid ${C.border2}`, textTransform: 'uppercase' }}>
                <span>Indicator</span><span>Op</span><span>Value</span><span>Description</span>
              </div>
              {condition.conditions.map((e, i) => <ConditionEntryRow key={i} entry={e} />)}
            </div>
          )}

          {/* Sizing rules */}
          {condition.sizingRules && (
            <div>
              <Label color={C.green}>Sizing rules</Label>
              <p style={{ margin: 0, fontSize: 13, color: C.textSub, lineHeight: 1.55 }}>{condition.sizingRules}</p>
            </div>
          )}

          {/* Invalidation */}
          {condition.invalidationConditions && (
            <div>
              <Label color={C.red}>Invalidation / ignore</Label>
              <p style={{ margin: 0, fontSize: 13, color: C.textSub, lineHeight: 1.55 }}>{condition.invalidationConditions}</p>
            </div>
          )}

          {/* Notes */}
          <div>
            <Label color={C.textMuted}>Research notes</Label>
            {condition.notes.length === 0 && (
              <p style={{ margin: 0, fontSize: 12, color: C.textDim }}>No notes yet.</p>
            )}
            {condition.notes.slice().reverse().map(n => (
              <div key={n.id} style={{
                background: C.surface2, borderRadius: 5, padding: '7px 10px',
                marginBottom: 6, fontSize: 12,
              }}>
                <div style={{ fontSize: 10, color: C.textDim, marginBottom: 3 }}>{n.date}</div>
                <div style={{ color: C.textSub, lineHeight: 1.5 }}>{n.text}</div>
              </div>
            ))}

            {!condition.isTemplate && (
              <div style={{ display: 'flex', gap: 6, marginTop: 8 }}>
                <textarea
                  value={noteText}
                  onChange={e => setNoteText(e.target.value)}
                  placeholder="Add a dated research note…"
                  rows={2}
                  style={{
                    flex: 1, background: C.surface2, border: `1px solid ${C.border2}`,
                    borderRadius: 4, color: C.text, fontSize: 12, padding: '5px 8px',
                    fontFamily: 'inherit', resize: 'vertical',
                  }}
                />
                <button
                  onClick={() => addNote.mutate()}
                  disabled={!noteText.trim() || addNote.isPending}
                  style={{
                    background: C.blue, color: '#fff', border: 'none', borderRadius: 4,
                    padding: '0 14px', fontWeight: 700, fontSize: 12, cursor: 'pointer',
                    alignSelf: 'flex-end', height: 32,
                  }}
                >
                  Add
                </button>
              </div>
            )}
          </div>
        </div>
      )}

      {/* Actions */}
      <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', paddingTop: 4, borderTop: `1px solid ${C.border}` }}>
        {!condition.isTemplate && (
          <>
            <ActionBtn onClick={() => onEdit(condition)}>Edit</ActionBtn>
            <select
              value={condition.status}
              onChange={e => changeStatus.mutate(e.target.value)}
              style={{
                background: C.surface2, border: `1px solid ${C.border}`, borderRadius: 4,
                color: C.textSub, fontSize: 11, padding: '3px 6px', cursor: 'pointer',
              }}
            >
              {QUANT_CONDITION_STATUSES.map(s => (
                <option key={s} value={s}>{STATUS_LABEL[s]}</option>
              ))}
            </select>
          </>
        )}
        <ActionBtn onClick={() => clone.mutate()} disabled={clone.isPending}>
          {clone.isPending ? 'Cloning…' : 'Clone'}
        </ActionBtn>
        {!condition.isTemplate && (
          <ActionBtn
            onClick={() => { if (window.confirm('Delete this condition?')) del.mutate() }}
            disabled={del.isPending}
            danger
          >
            Delete
          </ActionBtn>
        )}
      </div>
    </div>
  )
}

function Label({ children, color }: { children: React.ReactNode; color: string }) {
  return (
    <div style={{ fontSize: 10, fontWeight: 700, color, textTransform: 'uppercase', letterSpacing: '0.06em', marginBottom: 6 }}>
      {children}
    </div>
  )
}

function ActionBtn({ children, onClick, disabled, danger }: {
  children: React.ReactNode
  onClick: () => void
  disabled?: boolean
  danger?: boolean
}) {
  return (
    <button
      onClick={onClick}
      disabled={disabled}
      style={{
        background: 'none', border: `1px solid ${danger ? C.red : C.border}`,
        borderRadius: 4, color: danger ? C.red : C.textSub,
        fontSize: 11, padding: '3px 10px', cursor: 'pointer',
      }}
    >
      {children}
    </button>
  )
}

// ── Condition editor (right-side drawer) ──────────────────────────────────────

function ConditionEditor({
  initial,
  onClose,
}: {
  initial?: QuantCondition
  onClose: () => void
}) {
  const qc = useQueryClient()
  const isEdit = !!initial

  const blank = { indicator: '', operator: '>', value: '', description: '' }
  const [name, setName] = useState(initial?.name ?? '')
  const [hypothesis, setHypothesis] = useState(initial?.hypothesis ?? '')
  const [sizingRules, setSizingRules] = useState(initial?.sizingRules ?? '')
  const [invalidation, setInvalidation] = useState(initial?.invalidationConditions ?? '')
  const [tagsRaw, setTagsRaw] = useState(initial?.tags.join(', ') ?? '')
  const [entries, setEntries] = useState<QuantConditionEntry[]>(
    initial?.conditions.length ? initial.conditions : [{ ...blank }]
  )

  const addEntry = () => setEntries(e => [...e, { ...blank }])
  const removeEntry = (i: number) => setEntries(e => e.filter((_, idx) => idx !== i))
  const updateEntry = (i: number, field: keyof QuantConditionEntry, val: string) =>
    setEntries(e => e.map((entry, idx) => idx === i ? { ...entry, [field]: val } : entry))

  const save = useMutation({
    mutationFn: () => {
      const tags = tagsRaw.split(',').map(t => t.trim()).filter(Boolean)
      const conditions = entries.filter(e => e.indicator.trim())
      if (isEdit) {
        return quantLabApi.update(initial!.id, { name, hypothesis, conditions, sizingRules, invalidationConditions: invalidation, tags })
      } else {
        return quantLabApi.create({ name, hypothesis, conditions, sizingRules, invalidationConditions: invalidation, tags })
      }
    },
    onSuccess: () => { qc.invalidateQueries({ queryKey: ['quant-conditions'] }); onClose() },
  })

  const inputStyle: React.CSSProperties = {
    width: '100%', background: C.surface2, border: `1px solid ${C.border2}`,
    borderRadius: 4, color: C.text, fontSize: 13, padding: '6px 8px',
    fontFamily: 'inherit', boxSizing: 'border-box',
  }
  const taStyle: React.CSSProperties = { ...inputStyle, resize: 'vertical' }

  return (
    <div style={{ position: 'fixed', inset: 0, zIndex: 9000, display: 'flex', justifyContent: 'flex-end' }}>
      <div onClick={onClose} style={{ position: 'absolute', inset: 0, background: 'rgba(0,0,0,0.55)' }} />
      <div style={{
        position: 'relative', zIndex: 1,
        width: 560, maxWidth: '95vw', height: '100vh',
        background: C.surface, borderLeft: `1px solid ${C.border}`,
        display: 'flex', flexDirection: 'column', overflowY: 'auto',
        padding: '20px 24px', gap: 14,
      }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <span style={{ fontSize: 15, fontWeight: 700, color: C.text }}>
            {isEdit ? 'Edit condition' : 'New condition'}
          </span>
          <button onClick={onClose} style={{ background: 'none', border: 'none', color: C.textMuted, fontSize: 20, cursor: 'pointer' }}>×</button>
        </div>

        <Field label="Name">
          <input value={name} onChange={e => setName(e.target.value)} style={inputStyle} placeholder="Short descriptive name" />
        </Field>
        <Field label="Hypothesis">
          <textarea value={hypothesis} onChange={e => setHypothesis(e.target.value)} rows={4} style={taStyle}
            placeholder="When X happens, Y strategy has positive EV because…" />
        </Field>

        {/* Conditions table */}
        <div>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8 }}>
            <FieldLabel>Conditions</FieldLabel>
            <button onClick={addEntry} style={{ background: 'none', border: `1px solid ${C.border}`, borderRadius: 3, color: C.blue, fontSize: 11, padding: '2px 8px', cursor: 'pointer' }}>
              + Add
            </button>
          </div>
          {entries.map((e, i) => (
            <div key={i} style={{ display: 'grid', gridTemplateColumns: '1fr 50px 70px 1fr 24px', gap: 4, marginBottom: 6 }}>
              <input value={e.indicator} onChange={v => updateEntry(i, 'indicator', v.target.value)}
                placeholder="Indicator" style={{ ...inputStyle, fontSize: 12 }} />
              <input value={e.operator} onChange={v => updateEntry(i, 'operator', v.target.value)}
                placeholder="Op" style={{ ...inputStyle, fontSize: 12 }} />
              <input value={e.value} onChange={v => updateEntry(i, 'value', v.target.value)}
                placeholder="Value" style={{ ...inputStyle, fontSize: 12 }} />
              <input value={e.description} onChange={v => updateEntry(i, 'description', v.target.value)}
                placeholder="Description" style={{ ...inputStyle, fontSize: 12 }} />
              <button onClick={() => removeEntry(i)} style={{ background: 'none', border: 'none', color: C.red, cursor: 'pointer', fontSize: 14 }}>×</button>
            </div>
          ))}
        </div>

        <Field label="Sizing rules">
          <textarea value={sizingRules} onChange={e => setSizingRules(e.target.value)} rows={3} style={taStyle}
            placeholder="Risk %, vega cap, lot size logic, scaling rules…" />
        </Field>
        <Field label="Invalidation / ignore conditions">
          <textarea value={invalidation} onChange={e => setInvalidation(e.target.value)} rows={3} style={taStyle}
            placeholder="When to exit early or avoid entry…" />
        </Field>
        <Field label="Tags (comma-separated)">
          <input value={tagsRaw} onChange={e => setTagsRaw(e.target.value)} style={inputStyle}
            placeholder="options, nifty, vix, straddle" />
        </Field>

        <div style={{ display: 'flex', gap: 8, marginTop: 4 }}>
          <button
            onClick={() => save.mutate()}
            disabled={!name.trim() || save.isPending}
            style={{
              background: C.blue, color: '#fff', border: 'none', borderRadius: 5,
              padding: '8px 22px', fontWeight: 700, fontSize: 13, cursor: 'pointer',
            }}
          >
            {save.isPending ? 'Saving…' : isEdit ? 'Update' : 'Create'}
          </button>
          {save.isError && <span style={{ fontSize: 12, color: C.red, alignSelf: 'center' }}>Save failed</span>}
        </div>
      </div>
    </div>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
      <FieldLabel>{label}</FieldLabel>
      {children}
    </div>
  )
}
function FieldLabel({ children }: { children: React.ReactNode }) {
  return <div style={{ fontSize: 10, fontWeight: 700, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.06em' }}>{children}</div>
}

// ── Main page ─────────────────────────────────────────────────────────────────

type LabTab = 'my' | 'templates'

export function QuantLabPage() {
  const qc = useQueryClient()
  const [activeTab, setActiveTab] = useState<LabTab>('my')
  const [statusFilter, setStatusFilter] = useState<string>('All')
  const [editing, setEditing] = useState<QuantCondition | null | undefined>(undefined) // undefined=closed, null=new

  const { data: myConditions, isLoading: myLoading } = useQuery({
    queryKey: ['quant-conditions'],
    queryFn: () => quantLabApi.getAll().then(r => r.data.data ?? []),
    staleTime: 30_000,
    enabled: activeTab === 'my',
  })

  const { data: templates, isLoading: tplLoading } = useQuery({
    queryKey: ['quant-conditions-templates'],
    queryFn: () => quantLabApi.getTemplates().then(r => r.data.data ?? []),
    staleTime: 5 * 60_000,
    enabled: activeTab === 'templates',
  })

  const list = activeTab === 'my' ? (myConditions ?? []) : (templates ?? [])
  const isLoading = activeTab === 'my' ? myLoading : tplLoading

  const filtered = statusFilter === 'All'
    ? list
    : list.filter(c => c.status === statusFilter)

  const TAB_ITEMS: { id: LabTab; label: string }[] = [
    { id: 'my',        label: 'My Conditions' },
    { id: 'templates', label: 'Template Library' },
  ]

  const tabStyle = (id: LabTab): React.CSSProperties => ({
    background: 'none', border: 'none', cursor: 'pointer',
    padding: '8px 18px', fontSize: 13,
    fontWeight: activeTab === id ? 700 : 400,
    color: activeTab === id ? C.blue : C.textMuted,
    borderBottom: activeTab === id ? `2px solid ${C.blue}` : '2px solid transparent',
    marginBottom: -1,
  })

  return (
    <div style={{ padding: CONTENT_PAD, maxWidth: 960, margin: '0 auto' }}>
      {/* Header */}
      <div style={{ marginBottom: 20 }}>
        <h1 style={{ margin: 0, fontSize: 22, fontWeight: 800, color: C.text }}>Quant Lab</h1>
        <p style={{ margin: '6px 0 0', fontSize: 13, color: C.textMuted, maxWidth: 680 }}>
          Build research conditions with structured hypothesis, entry filters, sizing rules, and invalidation logic.
          Track each condition through its lifecycle — from hypothesis to live trading — with dated research notes.
        </p>
      </div>

      {/* Tabs + New button */}
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', borderBottom: `1px solid ${C.border}`, marginBottom: 20 }}>
        <div style={{ display: 'flex', gap: 2 }}>
          {TAB_ITEMS.map(t => (
            <button key={t.id} onClick={() => setActiveTab(t.id)} style={tabStyle(t.id)}>
              {t.label}
            </button>
          ))}
        </div>
        {activeTab === 'my' && (
          <button
            onClick={() => setEditing(null)}
            style={{
              background: C.blue, color: '#fff', border: 'none', borderRadius: 5,
              padding: '6px 16px', fontWeight: 700, fontSize: 12, cursor: 'pointer', marginBottom: 2,
            }}
          >
            + New Condition
          </button>
        )}
      </div>

      {/* Status filter — only for My Conditions */}
      {activeTab === 'my' && (
        <div style={{ display: 'flex', gap: 6, marginBottom: 16, flexWrap: 'wrap' }}>
          {['All', ...QUANT_CONDITION_STATUSES].map(s => (
            <button
              key={s}
              onClick={() => setStatusFilter(s)}
              style={{
                background: statusFilter === s ? `${STATUS_COLOR[s] ?? C.blue}22` : 'none',
                border: `1px solid ${statusFilter === s ? (STATUS_COLOR[s] ?? C.blue) : C.border}`,
                borderRadius: 4, color: statusFilter === s ? (STATUS_COLOR[s] ?? C.blue) : C.textMuted,
                fontSize: 11, padding: '3px 10px', cursor: 'pointer',
              }}
            >
              {s === 'All' ? 'All' : (STATUS_LABEL[s] ?? s)}
            </button>
          ))}
        </div>
      )}

      {/* Condition list */}
      {isLoading && <div style={{ color: C.textMuted, fontSize: 13 }}>Loading…</div>}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
        {filtered.map(c => (
          <ConditionCard
            key={c.id}
            condition={c}
            onEdit={cond => setEditing(cond)}
            onDeleted={() => qc.invalidateQueries({ queryKey: ['quant-conditions'] })}
          />
        ))}
        {!isLoading && filtered.length === 0 && (
          <div style={{ color: C.textDim, fontSize: 13, padding: '20px 0' }}>
            {activeTab === 'my'
              ? 'No conditions yet. Click "New Condition" to create your first research condition.'
              : 'No templates found.'}
          </div>
        )}
      </div>

      {/* Editor drawer */}
      {editing !== undefined && (
        <ConditionEditor
          initial={editing ?? undefined}
          onClose={() => setEditing(undefined)}
        />
      )}
    </div>
  )
}
