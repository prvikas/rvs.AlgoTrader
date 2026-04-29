import { useState } from 'react'
import { C } from '../../styles/tokens'
import { IndicatorIntelligenceDrawer } from './IndicatorIntelligenceCard'
import { GreeksIntelligenceDrawer } from './GreeksIntelligenceCard'

interface IndicatorInfoButtonProps {
  indicatorKey: string
  label?: string
}

/**
 * Small ⓘ trigger that opens an editable intelligence card for a technical indicator.
 * Drop this next to any indicator selector: <IntelligenceInfoButton indicatorKey="ADX" />
 */
export function IntelligenceInfoButton({ indicatorKey, label }: IndicatorInfoButtonProps) {
  const [open, setOpen] = useState(false)
  return (
    <>
      <button
        onClick={() => setOpen(true)}
        title={`${indicatorKey} intelligence card`}
        style={{
          display: 'inline-flex', alignItems: 'center', gap: 3,
          background: 'none', border: 'none', cursor: 'pointer',
          color: C.blue, fontSize: 12, padding: '0 4px',
          opacity: 0.8, lineHeight: 1,
        }}
        onMouseEnter={e => (e.currentTarget.style.opacity = '1')}
        onMouseLeave={e => (e.currentTarget.style.opacity = '0.8')}
      >
        <span style={{ fontSize: 13 }}>ⓘ</span>
        {label && <span style={{ fontSize: 11, color: C.textMuted }}>{label}</span>}
      </button>
      {open && (
        <IndicatorIntelligenceDrawer
          indicatorKey={indicatorKey}
          onClose={() => setOpen(false)}
        />
      )}
    </>
  )
}

interface GreeksInfoButtonProps {
  metricKey: string
  label?: string
}

/**
 * Small ⓘ trigger that opens an editable intelligence card for an options metric.
 * Drop this next to any Greek / IV / VIX display: <GreeksInfoButton metricKey="Theta" />
 */
export function GreeksInfoButton({ metricKey, label }: GreeksInfoButtonProps) {
  const [open, setOpen] = useState(false)
  return (
    <>
      <button
        onClick={() => setOpen(true)}
        title={`${metricKey} intelligence card`}
        style={{
          display: 'inline-flex', alignItems: 'center', gap: 3,
          background: 'none', border: 'none', cursor: 'pointer',
          color: C.amber, fontSize: 12, padding: '0 4px',
          opacity: 0.8, lineHeight: 1,
        }}
        onMouseEnter={e => (e.currentTarget.style.opacity = '1')}
        onMouseLeave={e => (e.currentTarget.style.opacity = '0.8')}
      >
        <span style={{ fontSize: 13 }}>ⓘ</span>
        {label && <span style={{ fontSize: 11, color: C.textMuted }}>{label}</span>}
      </button>
      {open && (
        <GreeksIntelligenceDrawer
          metricKey={metricKey}
          onClose={() => setOpen(false)}
        />
      )}
    </>
  )
}
