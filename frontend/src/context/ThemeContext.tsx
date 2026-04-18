/**
 * ThemeContext — switches between dark / warm / cool themes.
 *
 * On mount (and on every switch) it writes all CSS custom properties to
 * document.documentElement so that inline styles using `var(--c-xxx)` from
 * tokens.ts are rewired instantly. Preference persists in localStorage.
 */

import React, { createContext, useContext, useLayoutEffect, useState } from 'react'
import { ThemeName, THEMES, ThemePalette } from '../styles/themes'
import { hexAlpha } from '../styles/tokens'

const STORAGE_KEY = 'rvs_theme'

interface ThemeContextValue {
  theme: ThemeName
  setTheme: (t: ThemeName) => void
}

const ThemeContext = createContext<ThemeContextValue>({
  theme: 'dark',
  setTheme: () => {},
})

// ── CSS variable injection ────────────────────────────────────────────────────

function applyTheme(palette: ThemePalette): void {
  const root = document.documentElement
  const s = root.style

  // Base tokens
  s.setProperty('--c-bg',       palette.bg)
  s.setProperty('--c-surface',  palette.surface)
  s.setProperty('--c-surface2', palette.surface2)
  s.setProperty('--c-surface3', palette.surface3)
  s.setProperty('--c-border',   palette.border)
  s.setProperty('--c-border2',  palette.border2)
  s.setProperty('--c-border3',  palette.border3)
  s.setProperty('--c-text',     palette.text)
  s.setProperty('--c-textSub',  palette.textSub)
  s.setProperty('--c-textMuted',palette.textMuted)
  s.setProperty('--c-textDim',  palette.textDim)
  s.setProperty('--c-green',    palette.green)
  s.setProperty('--c-greenBg',  palette.greenBg)
  s.setProperty('--c-red',      palette.red)
  s.setProperty('--c-redBg',    palette.redBg)
  s.setProperty('--c-blue',     palette.blue)
  s.setProperty('--c-blueBg',   palette.blueBg)
  s.setProperty('--c-amber',    palette.amber)
  s.setProperty('--c-amberBg',  palette.amberBg)
  s.setProperty('--c-navBg',    palette.navBg)
  s.setProperty('--c-navBorder',palette.navBorder)
  s.setProperty('--c-navActive',palette.navActive)
  s.setProperty('--c-navText',  palette.navText)
  s.setProperty('--c-navMuted', palette.navMuted)

  // Red alpha variants
  s.setProperty('--c-red18', hexAlpha(palette.red, '18'))
  s.setProperty('--c-red30', hexAlpha(palette.red, '30'))
  s.setProperty('--c-red44', hexAlpha(palette.red, '44'))
  s.setProperty('--c-red55', hexAlpha(palette.red, '55'))
  s.setProperty('--c-red66', hexAlpha(palette.red, '66'))
  s.setProperty('--c-red88', hexAlpha(palette.red, '88'))

  // Green alpha variants
  s.setProperty('--c-green18', hexAlpha(palette.green, '18'))
  s.setProperty('--c-green30', hexAlpha(palette.green, '30'))
  s.setProperty('--c-green33', hexAlpha(palette.green, '33'))
  s.setProperty('--c-green44', hexAlpha(palette.green, '44'))
  s.setProperty('--c-green55', hexAlpha(palette.green, '55'))
  s.setProperty('--c-green88', hexAlpha(palette.green, '88'))

  // Blue alpha variants
  s.setProperty('--c-blue04', hexAlpha(palette.blue, '04'))
  s.setProperty('--c-blue06', hexAlpha(palette.blue, '06'))
  s.setProperty('--c-blue08', hexAlpha(palette.blue, '08'))
  s.setProperty('--c-blue0a', hexAlpha(palette.blue, '0a'))
  s.setProperty('--c-blue11', hexAlpha(palette.blue, '11'))
  s.setProperty('--c-blue22', hexAlpha(palette.blue, '22'))
  s.setProperty('--c-blue30', hexAlpha(palette.blue, '30'))
  s.setProperty('--c-blue33', hexAlpha(palette.blue, '33'))
  s.setProperty('--c-blue44', hexAlpha(palette.blue, '44'))
  s.setProperty('--c-blue55', hexAlpha(palette.blue, '55'))
  s.setProperty('--c-blue66', hexAlpha(palette.blue, '66'))
  s.setProperty('--c-blue88', hexAlpha(palette.blue, '88'))
  s.setProperty('--c-blue99', hexAlpha(palette.blue, '99'))

  // Amber alpha variants
  s.setProperty('--c-amber11', hexAlpha(palette.amber, '11'))
  s.setProperty('--c-amber22', hexAlpha(palette.amber, '22'))
  s.setProperty('--c-amber33', hexAlpha(palette.amber, '33'))
  s.setProperty('--c-amber44', hexAlpha(palette.amber, '44'))

  // Background alpha variants
  s.setProperty('--c-greenBg88', hexAlpha(palette.greenBg, '88'))
  s.setProperty('--c-redBg88',   hexAlpha(palette.redBg,   '88'))

  // Also update the body background so it matches immediately (belt-and-suspenders
  // alongside the index.html init script)
  document.body.style.background = palette.bg
  document.body.style.color      = palette.text
}

// ── Provider ──────────────────────────────────────────────────────────────────

export function ThemeProvider({ children }: { children: React.ReactNode }) {
  const [theme, setThemeState] = useState<ThemeName>(() => {
    const stored = localStorage.getItem(STORAGE_KEY) as ThemeName | null
    return stored && stored in THEMES ? stored : 'dark'
  })

  // useLayoutEffect runs synchronously before the browser paints — no flash
  useLayoutEffect(() => {
    applyTheme(THEMES[theme])
    localStorage.setItem(STORAGE_KEY, theme)
  }, [theme])

  return (
    <ThemeContext.Provider value={{ theme, setTheme: setThemeState }}>
      {children}
    </ThemeContext.Provider>
  )
}

export function useTheme(): ThemeContextValue {
  return useContext(ThemeContext)
}
