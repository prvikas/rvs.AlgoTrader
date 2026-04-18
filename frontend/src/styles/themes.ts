// Theme palettes — single source of truth for the 3 visual themes.
// Dark  = dark-mode navy-tinted terminal (original)
// Warm  = light-mode warm cream / rust brand   (CodePen xbEEExr §warm)
// Cool  = light-mode cool slate / blue brand   (CodePen xbEEExr §cool)

export type ThemeName = 'dark' | 'warm' | 'cool'

export interface ThemePalette {
  bg: string; surface: string; surface2: string; surface3: string
  border: string; border2: string; border3: string
  text: string; textSub: string; textMuted: string; textDim: string
  green: string; greenBg: string
  red: string; redBg: string
  blue: string; blueBg: string
  amber: string; amberBg: string
  navBg: string; navBorder: string; navActive: string; navText: string; navMuted: string
}

// ── Dark — navy-tinted dark terminal (unchanged) ──────────────────────────────
const dark: ThemePalette = {
  bg:       '#0F1117',
  surface:  '#161B27',
  surface2: '#1A2035',
  surface3: '#1E2540',
  border:   '#1E2738',
  border2:  '#141A28',
  border3:  '#283255',
  text:     '#E8EDF5',
  textSub:  '#8A9DBF',
  textMuted:'#445070',
  textDim:  '#253045',
  green:    '#00d07a',
  greenBg:  '#091E14',
  red:      '#ff4757',
  redBg:    '#1A0A0F',
  blue:     '#5B8DEF',
  blueBg:   '#1A2540',
  amber:    '#F59E0B',
  amberBg:  '#181008',
  navBg:    '#161B27',
  navBorder:'#1E2738',
  navActive:'#5B8DEF',
  navText:  '#E8EDF5',
  navMuted: '#445070',
}

// ── Warm — light cream / rust brand (CodePen §warm) ───────────────────────────
// bg #F7F3EE · surface #FDFAF7 · border #E8DDD2 · text #1A1410 · brand #C4502A
const warm: ThemePalette = {
  bg:       '#F7F3EE',
  surface:  '#FDFAF7',
  surface2: '#F0EAE0',
  surface3: '#E8DDD2',
  border:   '#E8DDD2',
  border2:  '#F5EFE8',
  border3:  '#D4C4B0',
  text:     '#1A1410',
  textSub:  '#4A3C28',
  textMuted:'#8A7860',
  textDim:  '#C4B8A8',
  green:    '#187040',   // darker green readable on cream
  greenBg:  '#E8F5ED',
  red:      '#B01E1E',   // darker red readable on cream
  redBg:    '#FBEBEB',
  blue:     '#C4502A',   // rust/copper brand
  blueBg:   '#F5EAE4',   // brand-subtle from CodePen
  amber:    '#A06010',
  amberBg:  '#FBF0DC',
  navBg:    '#F7F3EE',
  navBorder:'#E8DDD2',
  navActive:'#C4502A',
  navText:  '#1A1410',
  navMuted: '#8A7860',
}

// ── Cool — light slate / blue brand (CodePen §cool) ───────────────────────────
// bg #F4F6FA · surface #FAFBFD · border #DDE3EE · text #0F1520 · brand #2563EB
const cool: ThemePalette = {
  bg:       '#F4F6FA',
  surface:  '#FAFBFD',
  surface2: '#EBF0FA',
  surface3: '#DDE3EE',
  border:   '#DDE3EE',
  border2:  '#EEF2FB',
  border3:  '#C0CCE4',
  text:     '#0F1520',
  textSub:  '#2A3860',
  textMuted:'#607098',
  textDim:  '#B0BCDC',
  green:    '#0A7850',   // darker green readable on slate
  greenBg:  '#E0F5EC',
  red:      '#A01830',   // darker red readable on slate
  redBg:    '#FAE5EA',
  blue:     '#2563EB',   // cool brand
  blueBg:   '#EEF3FF',   // brand-subtle from CodePen
  amber:    '#A07010',
  amberBg:  '#FFF0D8',
  navBg:    '#F4F6FA',
  navBorder:'#DDE3EE',
  navActive:'#2563EB',
  navText:  '#0F1520',
  navMuted: '#607098',
}

export const THEMES: Record<ThemeName, ThemePalette> = { dark, warm, cool }

export const THEME_META: Record<ThemeName, { label: string; swatch: string; description: string }> = {
  dark:  { label: 'Dark',  swatch: '#5B8DEF', description: 'Navy-tinted dark — easy on the eyes during long sessions' },
  warm:  { label: 'Warm',  swatch: '#C4502A', description: 'Warm cream & rust — editorial light mode' },
  cool:  { label: 'Cool',  swatch: '#2563EB', description: 'Cool slate & blue — crisp light mode' },
}
