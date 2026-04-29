import { C } from '../../styles/tokens'

/** Maps regime label to a short display string. */
export function regimeLabel(regime: string): string {
  const MAP: Record<string, string> = {
    TrendingUp:        'Trending Up',
    TrendingDown:      'Trending Down',
    Choppy:            'Choppy',
    ElevatedVolatility:'Elevated Vol',
    PanicShock:        'Panic / Shock',
    CompressionWatch:  'Compression',
  }
  return MAP[regime] ?? regime
}

function trafficColor(light: string): string {
  if (light === 'Green') return C.green
  if (light === 'Red')   return C.red
  return C.amber
}

interface RegimeBadgeProps {
  regime: string
  trafficLight: string
  confidence?: number
  /** Show compact inline version (dot + label only, no confidence) */
  compact?: boolean
}

/**
 * Traffic-light badge for the current market regime.
 * Compact mode: colored dot + short label — drop into any header/card.
 * Full mode: dot + label + confidence bar.
 */
export function RegimeBadge({ regime, trafficLight, confidence, compact = false }: RegimeBadgeProps) {
  const color = trafficColor(trafficLight)
  const label = regimeLabel(regime)

  if (compact) {
    return (
      <span style={{ display: 'inline-flex', alignItems: 'center', gap: 5 }}>
        <span style={{
          width: 8, height: 8, borderRadius: '50%',
          background: color, flexShrink: 0,
          boxShadow: `0 0 6px ${color}88`,
        }} />
        <span style={{ fontSize: 11, color: C.textSub, fontWeight: 600 }}>{label}</span>
      </span>
    )
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 5 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
        <span style={{
          width: 10, height: 10, borderRadius: '50%',
          background: color, flexShrink: 0,
          boxShadow: `0 0 8px ${color}99`,
        }} />
        <span style={{ fontSize: 13, fontWeight: 700, color: C.text }}>{label}</span>
        <span style={{
          fontSize: 11, color, fontWeight: 600,
          background: `${color}18`, borderRadius: 3, padding: '1px 7px',
        }}>
          {trafficLight}
        </span>
      </div>
      {confidence !== undefined && (
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <div style={{
            flex: 1, maxWidth: 120, height: 3, background: C.surface2, borderRadius: 2,
          }}>
            <div style={{
              width: `${confidence}%`, height: '100%',
              background: color, borderRadius: 2,
              transition: 'width 0.3s',
            }} />
          </div>
          <span style={{ fontSize: 10, color: C.textDim }}>{confidence}% confidence</span>
        </div>
      )}
    </div>
  )
}
