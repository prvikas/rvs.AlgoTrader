import { useState, useRef } from 'react'
import { C } from '../../styles/tokens'

interface Props {
  text: string
}

/**
 * Small inline help icon — shows a tooltip on hover.
 * Usage: <HelpTooltip text="Explanation here" />
 */
export function HelpTooltip({ text }: Props) {
  const [visible, setVisible] = useState(false)
  const ref = useRef<HTMLSpanElement>(null)

  return (
    <span
      ref={ref}
      onMouseEnter={() => setVisible(true)}
      onMouseLeave={() => setVisible(false)}
      style={{ position: 'relative', display: 'inline-flex', alignItems: 'center', marginLeft: 4, verticalAlign: 'middle' }}
    >
      <span style={{
        width: 14, height: 14, borderRadius: '50%',
        background: C.surface3, border: `1px solid ${C.border3}`,
        color: C.textMuted, fontSize: 9, fontWeight: 700,
        display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
        cursor: 'help', userSelect: 'none', lineHeight: 1,
      }}>
        ?
      </span>
      {visible && (
        <span style={{
          position: 'absolute',
          bottom: 'calc(100% + 6px)',
          left: '50%',
          transform: 'translateX(-50%)',
          background: C.surface2,
          border: `1px solid ${C.border3}`,
          borderRadius: 5,
          padding: '7px 10px',
          fontSize: 11,
          color: C.text,
          whiteSpace: 'pre-wrap',
          maxWidth: 260,
          lineHeight: 1.5,
          boxShadow: '0 4px 16px rgba(0,0,0,0.5)',
          zIndex: 1000,
          pointerEvents: 'none',
        }}>
          {text}
          {/* Arrow */}
          <span style={{
            position: 'absolute', bottom: -5, left: '50%', transform: 'translateX(-50%)',
            width: 8, height: 8, background: C.surface2, border: `1px solid ${C.border3}`,
            borderTop: 'none', borderLeft: 'none',
            rotate: '45deg',
          }} />
        </span>
      )}
    </span>
  )
}
