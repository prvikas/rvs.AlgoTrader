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

// ── Brand / vendor colors (AP-020 exempt) ────────────────────────────────────
// These hex values intentionally stay as literals — they represent external
// brand identities (OAuth providers, brokers) that must not shift with theme.
export const BRAND = {
  // OAuth provider styles (must match official brand guidelines)
  google:    { bg: '#fff',    text: '#3c4043', border: '#dadce0' },
  microsoft: { bg: '#2f2f2f', text: '#fff',    border: '#444'    },
  apple:     { bg: '#000',    text: '#fff',    border: '#333'    },
  // Broker accent colors (login modal borders / glows)
  mstock:  '#f59e0b',  // amber — intentionally brand-specific
  zerodha: '#e11d48',  // rose-red (Zerodha brand)
  upstox:  '#7c3aed',  // violet  (Upstox brand)
} as const

// ── Chart color palette (AP-020) ──────────────────────────────────────────────
// All chart lines/fills reference these tokens so ThemeContext can adapt them.
// CSS custom properties work in SVG (Recharts, Recharts SVG, lightweight-charts).
export const CHART = {
  // EMA / SMA indicator lines
  ema5:        'var(--c-amber)',      // short EMAs → amber
  ema9:        'var(--c-amber)',
  ema20:       'var(--c-blue)',       // medium EMAs → blue
  ema21:       'var(--c-blue)',
  ema50:       '#8b5cf6',             // mid-term → purple (no theme token yet)
  ema200:      '#ec4899',             // long-term → pink (no theme token yet)
  sma20:       'var(--c-blue)',
  sma50:       '#8b5cf6',
  // P&L / equity
  equityLine:  'var(--c-blue)',
  profit:      'var(--c-green)',
  loss:        'var(--c-red)',
  // Chart chrome
  grid:        'var(--c-border)',
  axisText:    'var(--c-textMuted)',
  refLine:     'var(--c-border3)',
  tooltip:     { bg: 'var(--c-surface)', border: 'var(--c-border)' },
  // Candlestick
  bullCandle:  'var(--c-green)',
  bearCandle:  'var(--c-red)',
  // Misc indicator lines
  bbUpper:     'var(--c-blue)',
  bbLower:     'var(--c-blue)',
  bbMid:       'var(--c-blue)',
  vwap:        'var(--c-amber)',
  volume:      'var(--c-border3)',
  // Chart-specific extra colors (no semantic CSS var yet — chart-only use)
  violet:    '#8b5cf6',   // purple/violet series
  violetBg:  '#8b5cf611', // 7% opacity violet background
  pink:      '#f472b6',   // pink series
  // Fallback palette for dynamic indicator series (up to 5)
  palette: ['#e879f9', '#38bdf8', '#fb923c', '#a3e635', '#f472b6'] as string[],
  // Comparison series colors: first 3 semantic, then chart-specific extras
  series: ['var(--c-blue)', 'var(--c-green)', 'var(--c-amber)', '#8b5cf6', '#f97316'] as string[],
} as const
