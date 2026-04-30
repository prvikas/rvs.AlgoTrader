import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  indicatorIntelligenceApi,
  IndicatorIntelligenceCard,
  UpdateIndicatorIntelligenceRequest,
} from '../../api/client'
import { C, F } from '../../styles/tokens'

// ── Section definitions ────────────────────────────────────────────────────────

const SECTIONS: { key: keyof UpdateIndicatorIntelligenceRequest; label: string; color: string }[] = [
  { key: 'whatItMeasures',       label: 'What it measures',                color: C.blue },
  { key: 'commonMistake',        label: 'Common mistake',                  color: C.red },
  { key: 'positiveEvConditions', label: 'Positive expected value (EV)',    color: C.green },
  { key: 'ignoreConditions',     label: 'Ignore / avoid conditions',       color: C.amber },
  { key: 'bestPairedWith',       label: 'Best paired with',                color: C.textSub },
  { key: 'sizingImplications',   label: 'Sizing implications',             color: C.textSub },
  { key: 'userNotes',            label: 'Your research notes',             color: C.textMuted },
]

// ── Full card (standalone, rendered in the Quant Intelligence page) ────────────

interface CardProps { card: IndicatorIntelligenceCard; onSaved?: () => void }

export function IndicatorIntelligenceCardView({ card, onSaved }: CardProps) {
  const qc = useQueryClient()
  const [editing, setEditing] = useState(false)
  const [draft, setDraft] = useState<UpdateIndicatorIntelligenceRequest>({
    whatItMeasures:       card.whatItMeasures,
    commonMistake:        card.commonMistake,
    positiveEvConditions: card.positiveEvConditions,
    ignoreConditions:     card.ignoreConditions,
    bestPairedWith:       card.bestPairedWith,
    sizingImplications:   card.sizingImplications,
    userNotes:            card.userNotes,
  })

  const save = useMutation({
    mutationFn: () => indicatorIntelligenceApi.update(card.indicatorKey, draft),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['indicator-intelligence'] })
      setEditing(false)
      onSaved?.()
    },
  })

  const updatedLabel = card.updatedAt
    ? new Date(card.updatedAt).toLocaleDateString('en-IN', { day: '2-digit', month: 'short', year: 'numeric' })
    : ''

  return (
    <div style={{
      background: C.surface, border: `1px solid ${C.border}`, borderRadius: 8,
      padding: '16px 20px', display: 'flex', flexDirection: 'column', gap: 12,
    }}>
      {/* Header */}
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <div>
          <span style={{ fontSize: 16, fontWeight: 700, color: C.text }}>{card.displayName}</span>
          {updatedLabel && (
            <span style={{ marginLeft: 10, fontSize: 11, color: C.textDim }}>updated {updatedLabel}</span>
          )}
        </div>
        <button
          onClick={() => setEditing(e => !e)}
          style={{
            background: editing ? C.surface2 : 'none',
            border: `1px solid ${C.border}`, borderRadius: 4,
            color: C.textSub, fontSize: 11, padding: '3px 10px', cursor: 'pointer',
          }}
        >
          {editing ? 'Cancel' : 'Edit'}
        </button>
      </div>

      {/* Sections */}
      {SECTIONS.map(({ key, label, color }) => (
        <div key={key}>
          <div style={{ fontSize: 10, fontWeight: 700, color, textTransform: 'uppercase', letterSpacing: '0.06em', marginBottom: 4 }}>
            {label}
          </div>
          {editing ? (
            <textarea
              value={draft[key]}
              onChange={e => setDraft(d => ({ ...d, [key]: e.target.value }))}
              rows={key === 'userNotes' ? 4 : 3}
              style={{
                width: '100%', background: C.surface2, border: `1px solid ${C.border2}`,
                borderRadius: 4, color: C.text, fontSize: 13, padding: '6px 8px',
                fontFamily: 'inherit', resize: 'vertical', boxSizing: 'border-box',
              }}
            />
          ) : (
            <p style={{ margin: 0, fontSize: 13, color: C.textSub, lineHeight: 1.55 }}>
              {card[key as keyof IndicatorIntelligenceCard] as string || <em style={{ color: C.textDim }}>—</em>}
            </p>
          )}
        </div>
      ))}

      {/* Save button */}
      {editing && (
        <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end', marginTop: 4 }}>
          <button
            onClick={() => save.mutate()}
            disabled={save.isPending}
            style={{
              background: C.blue, color: 'white', border: 'none', borderRadius: 5,
              padding: '6px 18px', fontWeight: 700, fontSize: 13, cursor: 'pointer',
            }}
          >
            {save.isPending ? 'Saving…' : 'Save'}
          </button>
          {save.isError && (
            <span style={{ fontSize: 12, color: C.red, alignSelf: 'center' }}>Save failed</span>
          )}
        </div>
      )}
    </div>
  )
}

// ── Slide-in drawer (opened by IntelligenceInfoButton) ─────────────────────────

interface DrawerProps { indicatorKey: string; onClose: () => void }

export function IndicatorIntelligenceDrawer({ indicatorKey, onClose }: DrawerProps) {
  const { data, isLoading } = useQuery({
    queryKey: ['indicator-intelligence', indicatorKey],
    queryFn: () => indicatorIntelligenceApi.getByKey(indicatorKey).then(r => r.data.data),
  })

  return (
    <div
      style={{
        position: 'fixed', inset: 0, zIndex: 9000,
        display: 'flex', alignItems: 'flex-start', justifyContent: 'flex-end',
      }}
    >
      {/* Backdrop */}
      <div
        onClick={onClose}
        style={{ position: 'absolute', inset: 0, background: 'rgba(0,0,0,0.55)' }}
      />
      {/* Panel */}
      <div style={{
        position: 'relative', zIndex: 1,
        width: 520, maxWidth: '95vw', height: '100vh',
        background: C.surface, borderLeft: `1px solid ${C.border}`,
        display: 'flex', flexDirection: 'column', overflowY: 'auto',
        padding: '20px 24px',
      }}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 16 }}>
          <span style={{ fontSize: 13, color: C.textMuted, fontFamily: F.mono }}>
            Indicator Intelligence
          </span>
          <button
            onClick={onClose}
            style={{ background: 'none', border: 'none', color: C.textMuted, fontSize: 20, cursor: 'pointer', lineHeight: 1 }}
          >
            ×
          </button>
        </div>

        {isLoading && (
          <div style={{ color: C.textMuted, fontSize: 13 }}>Loading…</div>
        )}
        {!isLoading && !data && (
          <div style={{ color: C.textDim, fontSize: 13 }}>
            No intelligence card found for <strong>{indicatorKey}</strong>.
          </div>
        )}
        {data && (
          <IndicatorIntelligenceCardView card={data} onSaved={onClose} />
        )}
      </div>
    </div>
  )
}
