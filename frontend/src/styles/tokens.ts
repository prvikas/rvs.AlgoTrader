// Design tokens — single source of truth for all colors, spacing, typography.
// Import this wherever inline styles reference raw hex values.

export const C = {
  // Backgrounds
  bg:       '#090910',   // page background — deepest
  surface:  '#0d0d17',   // cards, panels, drawers
  surface2: '#111120',   // table headers, nested panels
  surface3: '#13131f',   // hover states, subtle sections

  // Borders
  border:   '#1a1a2e',   // panel/card borders
  border2:  '#0f0f18',   // table row separators (near-invisible)
  border3:  '#252538',   // prominent dividers

  // Text
  text:     '#e2e8f0',   // primary
  textSub:  '#94a3b8',   // secondary
  textMuted:'#4a5568',   // labels, metadata
  textDim:  '#374151',   // hints, placeholders

  // Semantic
  green:  '#00d07a',     // profit, running, success
  greenBg:'#0a2218',
  red:    '#ff4757',     // loss, stopped, error
  redBg:  '#1f0a0a',
  blue:   '#3b82f6',     // forward test, info, accent
  blueBg: '#0a1829',
  amber:  '#f59e0b',     // warnings, paused
  amberBg:'#1a1209',

  // Nav
  navBg:    '#0d0d17',
  navBorder:'#1a1a2e',
  navActive:'#3b82f6',
  navText:  '#e2e8f0',
  navMuted: '#4a5568',
} as const

export const F = {
  mono: '"JetBrains Mono", "Cascadia Code", "Fira Code", "Courier New", monospace',
  sans: '"Inter", system-ui, -apple-system, sans-serif',
} as const

export const SP = {
  xs:  4,
  sm:  8,
  md:  12,
  lg:  16,
  xl:  20,
  xxl: 24,
} as const

// Table row padding (professional density)
export const TABLE_CELL = '5px 10px'
export const TABLE_HEADER_CELL = '6px 10px'

// Compact card padding
export const CARD_PAD = '10px 14px'

// Content area
export const CONTENT_PAD = '12px 16px'

// Nav height
export const NAV_HEIGHT = 36
