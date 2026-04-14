/**
 * PayoffChart — options/spread payoff diagram at expiry.
 *
 * Accepts an array of { price, pnl } data points and renders:
 *  - Area chart with green profit zone / red loss zone
 *  - Breakeven vertical dashed line(s) + label
 *  - Stop-loss horizontal dashed line
 *  - Profit target horizontal dashed line
 *  - Guided mode: plain-English summary below the chart
 *  - Pro mode: numeric Greeks + scenario table
 */

import {
  ResponsiveContainer,
  AreaChart,
  Area,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ReferenceLine,
  TooltipProps,
} from 'recharts'
import { C, F } from '../../styles/tokens'
import { useUserMode } from '../../context/UserModeContext'

// ── Types ─────────────────────────────────────────────────────────────────────

export interface PayoffPoint {
  price: number
  pnl: number
}

export interface GreeksSnapshot {
  delta?: number
  gamma?: number
  theta?: number
  vega?: number
  iv?: number
}

export interface PayoffChartProps {
  /** Array of (underlyingPrice, PnL) points spanning the range of interest */
  data: PayoffPoint[]
  /** Price(s) where PnL = 0; draws vertical dashed line + "BE" label */
  breakeven?: number | number[]
  /** Price level for stop-loss horizontal line */
  stopLoss?: number
  /** Price level for profit-target horizontal line */
  targetPrice?: number
  /** Max P&L value (for annotation; auto-computed if omitted) */
  maxProfit?: number
  /** Min P&L value — should be negative (auto-computed if omitted) */
  maxLoss?: number
  /** Optional expiry label, e.g. "25 Apr 2026" */
  expiry?: string
  /** Override guided mode (uses UserModeContext when omitted) */
  showGuided?: boolean
  /** Optional Greeks for pro-mode table */
  greeks?: GreeksSnapshot
  /** Chart height in px (default 260) */
  height?: number
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function fmtInr(v: number): string {
  const abs = Math.abs(v)
  if (abs >= 100_000) return `₹${(v / 100_000).toFixed(1)}L`
  if (abs >= 1_000) return `₹${(v / 1_000).toFixed(1)}K`
  return `₹${v.toFixed(0)}`
}

function fmtPrice(v: number): string {
  return v >= 1000 ? v.toLocaleString('en-IN', { maximumFractionDigits: 0 }) : v.toFixed(1)
}

// ── Custom tooltip ────────────────────────────────────────────────────────────

function CustomTooltip({ active, payload }: TooltipProps<number, string>) {
  if (!active || !payload?.length) return null
  const d = payload[0].payload as PayoffPoint
  const positive = d.pnl >= 0
  return (
    <div style={{
      background: C.surface2, border: `1px solid ${C.border3}`,
      borderRadius: 6, padding: '8px 12px', fontSize: 12,
    }}>
      <div style={{ color: C.textSub, marginBottom: 2 }}>Price: <strong style={{ color: C.text }}>₹{fmtPrice(d.price)}</strong></div>
      <div style={{ color: positive ? C.green : C.red, fontWeight: 700 }}>
        P&L: {positive ? '+' : ''}{fmtInr(d.pnl)}
      </div>
    </div>
  )
}

// ── Guided summary ────────────────────────────────────────────────────────────

function GuidedSummary({
  maxProfit, maxLoss, breakeven, stopLoss, targetPrice, expiry,
}: {
  maxProfit: number; maxLoss: number; breakeven?: number | number[]
  stopLoss?: number; targetPrice?: number; expiry?: string
}) {
  const beList = breakeven === undefined ? []
    : Array.isArray(breakeven) ? breakeven : [breakeven]

  return (
    <div style={{
      marginTop: 16, padding: '12px 16px',
      background: C.surface2, borderRadius: 8,
      border: `1px solid ${C.border}`, fontSize: 13,
    }}>
      <div style={{ fontWeight: 700, color: C.text, marginBottom: 8, fontSize: 12, textTransform: 'uppercase', letterSpacing: '0.06em' }}>
        What this means for you
      </div>

      <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
        {maxProfit > 0 && (
          <SummaryRow icon="✅" color={C.green}>
            <strong>Max profit: {fmtInr(maxProfit)}</strong>
            {beList.length > 0 && (
              <> — if the price stays {beList.length === 1 ? `above ₹${fmtPrice(beList[0])}` : `between ₹${fmtPrice(beList[0])} and ₹${fmtPrice(beList[1])}`}{expiry ? ` by ${expiry}` : ''}</>
            )}
          </SummaryRow>
        )}

        {maxLoss < 0 && (
          <SummaryRow icon="🛡️" color={C.amber}>
            <strong>Max loss: {fmtInr(maxLoss)}</strong> — your worst-case downside is capped at this amount
          </SummaryRow>
        )}

        {beList.length > 0 && (
          <SummaryRow icon="⚖️" color={C.textSub}>
            <strong>Breakeven{beList.length > 1 ? 's' : ''}: ₹{beList.map(fmtPrice).join(' and ₹')}</strong> — you start making money once price crosses this level
          </SummaryRow>
        )}

        {stopLoss !== undefined && (
          <SummaryRow icon="🚪" color={C.red}>
            <strong>Stop loss: ₹{fmtPrice(stopLoss)}</strong> — trade exits automatically if price falls to this level
          </SummaryRow>
        )}

        {targetPrice !== undefined && (
          <SummaryRow icon="🎯" color={C.blue}>
            <strong>Profit target: ₹{fmtPrice(targetPrice)}</strong> — if price reaches here, consider taking profits
          </SummaryRow>
        )}
      </div>
    </div>
  )
}

function SummaryRow({ icon, color, children }: { icon: string; color: string; children: React.ReactNode }) {
  return (
    <div style={{ display: 'flex', gap: 8, alignItems: 'flex-start', color }}>
      <span style={{ flexShrink: 0 }}>{icon}</span>
      <span style={{ fontSize: 13, lineHeight: 1.5 }}>{children}</span>
    </div>
  )
}

// ── Pro Greeks table ──────────────────────────────────────────────────────────

function GreeksTable({ greeks }: { greeks: GreeksSnapshot }) {
  const items = [
    { label: 'Delta', value: greeks.delta?.toFixed(2) },
    { label: 'Gamma', value: greeks.gamma?.toFixed(4) },
    { label: 'Theta', value: greeks.theta !== undefined ? fmtInr(greeks.theta) + '/day' : undefined },
    { label: 'Vega', value: greeks.vega?.toFixed(2) },
    { label: 'IV', value: greeks.iv !== undefined ? `${(greeks.iv * 100).toFixed(1)}%` : undefined },
  ].filter(i => i.value !== undefined)

  if (items.length === 0) return null

  return (
    <div style={{ marginTop: 12, display: 'flex', gap: 20, flexWrap: 'wrap' }}>
      {items.map(({ label, value }) => (
        <div key={label}>
          <div style={{ fontSize: 9, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.07em' }}>{label}</div>
          <div style={{ fontSize: 13, fontWeight: 700, color: C.text, fontFamily: F.mono }}>{value}</div>
        </div>
      ))}
    </div>
  )
}

// ── Main component ────────────────────────────────────────────────────────────

export function PayoffChart({
  data,
  breakeven,
  stopLoss,
  targetPrice,
  maxProfit: maxProfitProp,
  maxLoss: maxLossProp,
  expiry,
  showGuided,
  greeks,
  height = 260,
}: PayoffChartProps) {
  const { isGuided } = useUserMode()
  const guided = showGuided ?? isGuided

  if (!data.length) {
    return (
      <div style={{ height, display: 'flex', alignItems: 'center', justifyContent: 'center', color: C.textMuted, fontSize: 12 }}>
        No payoff data available
      </div>
    )
  }

  const pnlValues = data.map(d => d.pnl)
  const computedMax = Math.max(...pnlValues)
  const computedMin = Math.min(...pnlValues)
  const maxProfit = maxProfitProp ?? computedMax
  const maxLoss = maxLossProp ?? computedMin

  // Pad Y-axis so reference lines don't clip
  const yPad = Math.max(Math.abs(maxProfit), Math.abs(maxLoss)) * 0.15
  const yMin = Math.min(0, computedMin) - yPad
  const yMax = Math.max(0, computedMax) + yPad

  const beList: number[] = breakeven === undefined ? []
    : Array.isArray(breakeven) ? breakeven : [breakeven]

  return (
    <div>
      {/* Chart header */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8 }}>
        <div style={{ fontSize: 12, fontWeight: 600, color: C.textSub }}>
          Payoff at Expiry{expiry ? ` · ${expiry}` : ''}
        </div>
        <div style={{ display: 'flex', gap: 16, fontSize: 10 }}>
          <LegendDot color={C.green} label="Profit zone" />
          <LegendDot color={C.red} label="Loss zone" />
          {beList.length > 0 && <LegendDot color={C.amber} label="Breakeven" dashed />}
          {stopLoss !== undefined && <LegendDot color={C.red} label="Stop loss" dashed />}
          {targetPrice !== undefined && <LegendDot color={C.green} label="Target" dashed />}
        </div>
      </div>

      {/* Recharts area chart */}
      <ResponsiveContainer width="100%" height={height}>
        <AreaChart data={data} margin={{ top: 4, right: 8, bottom: 4, left: 8 }}>
          <defs>
            {/* Profit gradient — green above zero */}
            <linearGradient id="payoffProfit" x1="0" y1="0" x2="0" y2="1">
              <stop offset="5%" stopColor={C.green} stopOpacity={0.25} />
              <stop offset="95%" stopColor={C.green} stopOpacity={0.0} />
            </linearGradient>
            {/* Loss gradient — red below zero */}
            <linearGradient id="payoffLoss" x1="0" y1="0" x2="0" y2="1">
              <stop offset="5%" stopColor={C.red} stopOpacity={0.0} />
              <stop offset="95%" stopColor={C.red} stopOpacity={0.25} />
            </linearGradient>
          </defs>

          <CartesianGrid strokeDasharray="3 3" stroke={C.border2} vertical={false} />

          <XAxis
            dataKey="price"
            tickFormatter={fmtPrice}
            tick={{ fill: C.textMuted, fontSize: 10 }}
            axisLine={{ stroke: C.border }}
            tickLine={false}
          />
          <YAxis
            tickFormatter={v => fmtInr(v as number)}
            tick={{ fill: C.textMuted, fontSize: 10 }}
            axisLine={false}
            tickLine={false}
            domain={[yMin, yMax]}
            width={60}
          />

          <Tooltip content={<CustomTooltip />} />

          {/* Zero line */}
          <ReferenceLine y={0} stroke={C.border3} strokeWidth={1} />

          {/* Profit area (PnL above 0) */}
          <Area
            type="monotone"
            dataKey="pnl"
            stroke={C.green}
            strokeWidth={2}
            fill="url(#payoffProfit)"
            fillOpacity={1}
            dot={false}
            activeDot={{ r: 4, fill: C.green, stroke: C.surface }}
            // The dual-color effect: positive values get green, negative get red
            // Recharts doesn't natively split by value, so we layer two Areas:
            // this one shows the full line; the red fill area below handles the loss zone
          />

          {/* Loss fill overlay — same data, red fill, clipped below 0 */}
          <Area
            type="monotone"
            dataKey="pnl"
            stroke="none"
            fill="url(#payoffLoss)"
            fillOpacity={1}
            dot={false}
            activeDot={false}
          />

          {/* Breakeven vertical lines */}
          {beList.map((be, i) => (
            <ReferenceLine
              key={`be-${i}`}
              x={be}
              stroke={C.amber}
              strokeDasharray="5 3"
              strokeWidth={1.5}
              label={{
                value: `BE ₹${fmtPrice(be)}`,
                fill: C.amber,
                fontSize: 10,
                position: 'top',
              }}
            />
          ))}

          {/* Stop loss horizontal line */}
          {stopLoss !== undefined && (
            <ReferenceLine
              x={stopLoss}
              stroke={C.red}
              strokeDasharray="4 3"
              strokeWidth={1.5}
              label={{
                value: `SL ₹${fmtPrice(stopLoss)}`,
                fill: C.red,
                fontSize: 10,
                position: 'insideTopRight',
              }}
            />
          )}

          {/* Target price vertical line */}
          {targetPrice !== undefined && (
            <ReferenceLine
              x={targetPrice}
              stroke={C.green}
              strokeDasharray="4 3"
              strokeWidth={1.5}
              label={{
                value: `TGT ₹${fmtPrice(targetPrice)}`,
                fill: C.green,
                fontSize: 10,
                position: 'insideTopRight',
              }}
            />
          )}
        </AreaChart>
      </ResponsiveContainer>

      {/* Pro mode: Greeks */}
      {!guided && greeks && <GreeksTable greeks={greeks} />}

      {/* Guided mode: plain-English summary */}
      {guided && (
        <GuidedSummary
          maxProfit={maxProfit}
          maxLoss={maxLoss}
          breakeven={breakeven}
          stopLoss={stopLoss}
          targetPrice={targetPrice}
          expiry={expiry}
        />
      )}
    </div>
  )
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function LegendDot({ color, label, dashed }: { color: string; label: string; dashed?: boolean }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 4, color: C.textMuted }}>
      <div style={{
        width: 20, height: 2,
        background: dashed ? 'none' : color,
        borderTop: dashed ? `2px dashed ${color}` : 'none',
      }} />
      {label}
    </div>
  )
}

// ── Utility: build payoff data for common spread types ────────────────────────

export type SpreadType = 'bull_call' | 'bear_put' | 'bull_put' | 'bear_call' | 'iron_condor' | 'straddle' | 'strangle'

export interface SpreadParams {
  type: SpreadType
  spot: number
  lowerStrike: number
  upperStrike: number
  /** Net debit paid (positive) or credit received (negative) */
  netPremium: number
  lotSize?: number
  /** For iron condor: second pair of strikes */
  innerLowerStrike?: number
  innerUpperStrike?: number
}

/**
 * buildPayoffData — generates { price, pnl } points for standard spread structures.
 * Price range: spot ± 15%, 80 evenly-spaced steps.
 */
export function buildPayoffData(params: SpreadParams): { data: PayoffPoint[]; breakeven: number[] } {
  const { type, spot, lowerStrike, upperStrike, netPremium, lotSize = 1 } = params
  const lo = spot * 0.85
  const hi = spot * 1.15
  const steps = 80
  const step = (hi - lo) / steps

  const points: PayoffPoint[] = []
  const breakevenSet = new Set<number>()

  for (let i = 0; i <= steps; i++) {
    const price = lo + i * step
    let intrinsic = 0

    switch (type) {
      case 'bull_call':
        // Long lower call + Short upper call
        intrinsic = Math.min(Math.max(price - lowerStrike, 0), upperStrike - lowerStrike)
        break
      case 'bear_put':
        // Long upper put + Short lower put
        intrinsic = Math.min(Math.max(upperStrike - price, 0), upperStrike - lowerStrike)
        break
      case 'bull_put':
        // Short upper put + Long lower put (credit spread)
        intrinsic = -(Math.min(Math.max(upperStrike - price, 0), upperStrike - lowerStrike))
        break
      case 'bear_call':
        // Short lower call + Long upper call (credit spread)
        intrinsic = -(Math.min(Math.max(price - lowerStrike, 0), upperStrike - lowerStrike))
        break
      case 'iron_condor': {
        const il = params.innerLowerStrike ?? lowerStrike + (upperStrike - lowerStrike) * 0.25
        const iu = params.innerUpperStrike ?? upperStrike - (upperStrike - lowerStrike) * 0.25
        const putLoss = Math.min(Math.max(il - price, 0), il - lowerStrike)
        const callLoss = Math.min(Math.max(price - iu, 0), upperStrike - iu)
        intrinsic = -(putLoss + callLoss)
        break
      }
      case 'straddle':
        intrinsic = Math.abs(price - lowerStrike)  // lowerStrike = ATM
        break
      case 'strangle':
        intrinsic = Math.max(lowerStrike - price, 0) + Math.max(price - upperStrike, 0)
        break
    }

    const pnl = (intrinsic - netPremium) * lotSize
    points.push({ price: Math.round(price), pnl: Math.round(pnl) })

    // Detect zero crossing for breakeven
    if (i > 0) {
      const prev = points[i - 1].pnl
      const curr = pnl
      if ((prev < 0 && curr >= 0) || (prev >= 0 && curr < 0)) {
        // Linear interpolation
        const be = points[i - 1].price + (price - points[i - 1].price) * Math.abs(prev) / (Math.abs(prev) + Math.abs(curr))
        breakevenSet.add(Math.round(be))
      }
    }
  }

  return { data: points, breakeven: Array.from(breakevenSet) }
}
