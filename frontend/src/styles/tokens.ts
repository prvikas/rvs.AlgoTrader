// Design tokens — single source of truth for all colors, spacing, typography.
// ALL color values are CSS custom property references so ThemeContext can rewire
// the entire UI by writing to :root at runtime. Never use raw hex inline (AP-020).

// ── Hex-alpha helper ──────────────────────────────────────────────────────────
// Used by ThemeContext to compute alpha variants for each theme.
// alphaHex: two hex digits, e.g. '44' = 26.7%, '88' = 53.3%
export function hexAlpha(hex: string, alphaHex: string): string {
  const r = parseInt(hex.slice(1, 3), 16)
  const g = parseInt(hex.slice(3, 5), 16)
  const b = parseInt(hex.slice(5, 7), 16)
  const a = (parseInt(alphaHex, 16) / 255).toFixed(3)
  return `rgba(${r},${g},${b},${a})`
}

export const C = {
  // ── Backgrounds ──────────────────────────────────────────────────────────────
  bg:       'var(--c-bg)',
  surface:  'var(--c-surface)',
  surface2: 'var(--c-surface2)',
  surface3: 'var(--c-surface3)',

  // ── Borders ───────────────────────────────────────────────────────────────────
  border:   'var(--c-border)',
  border2:  'var(--c-border2)',
  border3:  'var(--c-border3)',

  // ── Text ─────────────────────────────────────────────────────────────────────
  text:      'var(--c-text)',
  textSub:   'var(--c-textSub)',
  textMuted: 'var(--c-textMuted)',
  textDim:   'var(--c-textDim)',

  // ── Semantic base ─────────────────────────────────────────────────────────────
  green:   'var(--c-green)',
  greenBg: 'var(--c-greenBg)',
  red:     'var(--c-red)',
  redBg:   'var(--c-redBg)',
  blue:    'var(--c-blue)',
  blueBg:  'var(--c-blueBg)',
  amber:   'var(--c-amber)',
  amberBg: 'var(--c-amberBg)',

  // ── Nav ───────────────────────────────────────────────────────────────────────
  navBg:    'var(--c-navBg)',
  navBorder:'var(--c-navBorder)',
  navActive:'var(--c-navActive)',
  navText:  'var(--c-navText)',
  navMuted: 'var(--c-navMuted)',

  // ── Alpha variants — pre-computed for each theme by ThemeContext ───────────────
  // red
  red18: 'var(--c-red18)', red30: 'var(--c-red30)', red44: 'var(--c-red44)',
  red55: 'var(--c-red55)', red66: 'var(--c-red66)', red88: 'var(--c-red88)',
  // green
  green18: 'var(--c-green18)', green30: 'var(--c-green30)', green33: 'var(--c-green33)',
  green44: 'var(--c-green44)', green55: 'var(--c-green55)', green88: 'var(--c-green88)',
  // blue
  blue04: 'var(--c-blue04)', blue06: 'var(--c-blue06)', blue08: 'var(--c-blue08)',
  blue0a: 'var(--c-blue0a)', blue11: 'var(--c-blue11)', blue22: 'var(--c-blue22)',
  blue30: 'var(--c-blue30)', blue33: 'var(--c-blue33)', blue44: 'var(--c-blue44)',
  blue55: 'var(--c-blue55)', blue66: 'var(--c-blue66)', blue88: 'var(--c-blue88)',
  blue99: 'var(--c-blue99)',
  // amber
  amber11: 'var(--c-amber11)', amber22: 'var(--c-amber22)',
  amber33: 'var(--c-amber33)', amber44: 'var(--c-amber44)',
  // bg alphas
  greenBg88: 'var(--c-greenBg88)',
  redBg88:   'var(--c-redBg88)',
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

export const TABLE_CELL        = '5px 10px'
export const TABLE_HEADER_CELL = '6px 10px'
export const CARD_PAD          = '10px 14px'
export const CONTENT_PAD       = '12px 16px'
export const NAV_HEIGHT        = 36
