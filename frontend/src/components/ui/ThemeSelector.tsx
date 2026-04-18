/**
 * ThemeSelector — compact 3-pill switcher that lives in the top nav.
 * Shows the current theme highlighted; clicking switches instantly.
 */

import { useTheme } from '../../context/ThemeContext'
import { THEME_META, ThemeName } from '../../styles/themes'
import { C } from '../../styles/tokens'

const THEME_ORDER: ThemeName[] = ['dark', 'warm', 'cool']

// Small colored circle swatches to give instant visual meaning
const ICONS: Record<ThemeName, string> = {
  dark: '🌙',
  warm: '🌅',
  cool: '❄️',
}

export function ThemeSelector() {
  const { theme, setTheme } = useTheme()

  return (
    <div
      style={{
        display: 'flex', alignItems: 'center', gap: 2,
        background: C.surface2, borderRadius: 6,
        border: `1px solid ${C.border}`,
        padding: '2px 3px',
      }}
      title="Switch theme"
    >
      {THEME_ORDER.map(t => {
        const isActive = theme === t
        const meta = THEME_META[t]
        return (
          <button
            key={t}
            onClick={() => setTheme(t)}
            title={meta.description}
            style={{
              display: 'flex', alignItems: 'center', gap: 4,
              padding: '2px 8px', borderRadius: 4, border: 'none',
              cursor: 'pointer', fontSize: 10, fontWeight: isActive ? 700 : 400,
              background: isActive ? C.surface3 : 'transparent',
              color: isActive ? C.text : C.navMuted,
              transition: 'background 0.15s, color 0.15s',
              whiteSpace: 'nowrap',
            }}
            onMouseEnter={e => {
              if (!isActive) e.currentTarget.style.background = C.surface3
            }}
            onMouseLeave={e => {
              if (!isActive) e.currentTarget.style.background = 'transparent'
            }}
          >
            <span style={{ fontSize: 11 }}>{ICONS[t]}</span>
            <span>{meta.label}</span>
            {isActive && (
              <span style={{
                width: 5, height: 5, borderRadius: '50%',
                background: meta.swatch, flexShrink: 0,
              }} />
            )}
          </button>
        )
      })}
    </div>
  )
}
