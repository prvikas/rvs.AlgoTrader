/**
 * StrategyRecommendationPanel — recommends strategies based on market outlook,
 * risk tolerance, and instrument type.
 *
 * Shown prominently in Guided mode on the Dashboard.
 * Collapsed/hidden in Pro mode.
 *
 * Clicking "Use this strategy" navigates to StrategyDefinitionPage with
 * the strategy name pre-filled (via hash params or a parent callback).
 */

import { useState } from 'react'
import { C, SP } from '../../styles/tokens'

// ── Types ─────────────────────────────────────────────────────────────────────

export type MarketOutlook = 'bullish' | 'bearish' | 'neutral' | 'volatile'
export type RiskTolerance = 'low' | 'medium' | 'high'
export type InstrumentType = 'equity' | 'options'

export interface StrategyRecommendation {
  name: string
  displayName: string
  tagline: string
  reason: string
  riskReward: 'low' | 'medium' | 'high'
  expiryNote?: string
  /** Link to the strategy definition page; e.g. "?strategy=BullCallSpread" */
  strategyKey?: string
}

export interface StrategyRecommendationPanelProps {
  marketOutlook: MarketOutlook
  riskTolerance: RiskTolerance
  instrumentType: InstrumentType
  onSelectStrategy?: (strategyKey: string) => void
}

// ── Recommendation matrix ─────────────────────────────────────────────────────

const RECOMMENDATIONS: Record<MarketOutlook, Record<RiskTolerance, Record<InstrumentType, StrategyRecommendation[]>>> = {
  bullish: {
    low: {
      options: [
        {
          name: 'BullCallSpread', displayName: 'Bull Call Spread',
          tagline: 'Defined-risk bullish bet with limited capital',
          reason: 'Limits your maximum loss to the premium paid. Good when you expect moderate upside.',
          riskReward: 'medium', expiryNote: 'Best with 20–45 days to expiry',
          strategyKey: 'VerticalSpread',
        },
        {
          name: 'BullPutSpread', displayName: 'Bull Put Spread (Credit)',
          tagline: 'Get paid upfront while staying bullish',
          reason: 'You receive a credit immediately. Profitable as long as the market doesn\'t fall sharply.',
          riskReward: 'medium', expiryNote: 'Best with 15–30 days to expiry',
          strategyKey: 'VerticalSpread',
        },
      ],
      equity: [
        {
          name: 'VcpSwing', displayName: 'VCP Swing Strategy',
          tagline: 'Buy breakouts from tight consolidations',
          reason: 'Enters when a stock breaks out of a volatility contraction pattern — high-probability swing setups.',
          riskReward: 'medium', strategyKey: 'VcpSwing',
        },
      ],
    },
    medium: {
      options: [
        {
          name: 'LongCall', displayName: 'Long Call',
          tagline: 'Direct bullish exposure with limited downside',
          reason: 'Simple and powerful. You profit from upward moves while risking only the premium paid.',
          riskReward: 'high', expiryNote: 'Use monthly/quarterly expiry for more time',
          strategyKey: 'VerticalSpread',
        },
        {
          name: 'BullCallSpread', displayName: 'Bull Call Spread',
          tagline: 'Reduce cost of a long call with a cap on profits',
          reason: 'Lower cost than a naked long call. Ideal if you have a specific price target in mind.',
          riskReward: 'medium', expiryNote: 'Best with 20–45 days to expiry',
          strategyKey: 'VerticalSpread',
        },
      ],
      equity: [
        {
          name: 'VcpSwing', displayName: 'VCP Swing Strategy',
          tagline: 'Ride momentum breakouts',
          reason: 'Systematic swing trades in trending markets. Well-tested with breadth and volume filters.',
          riskReward: 'medium', strategyKey: 'VcpSwing',
        },
      ],
    },
    high: {
      options: [
        {
          name: 'LongCall', displayName: 'Long Call (Aggressive)',
          tagline: 'Maximum upside leverage on bullish conviction',
          reason: 'High leverage but your loss is capped at the premium. Use only when highly confident.',
          riskReward: 'high', expiryNote: 'Consider deep ITM for less time-decay risk',
          strategyKey: 'VerticalSpread',
        },
      ],
      equity: [
        {
          name: 'VcpSwing', displayName: 'VCP Swing + Pyramiding',
          tagline: 'Scale into winning positions',
          reason: 'Add to positions as they move in your favour using the scaling manager.',
          riskReward: 'high', strategyKey: 'VcpSwing',
        },
      ],
    },
  },

  bearish: {
    low: {
      options: [
        {
          name: 'BearPutSpread', displayName: 'Bear Put Spread',
          tagline: 'Defined-risk bet on downside',
          reason: 'Capped downside exposure. You profit if the market falls, with limited risk.',
          riskReward: 'medium', expiryNote: 'Best with 20–45 days to expiry',
          strategyKey: 'VerticalSpread',
        },
        {
          name: 'BearCallSpread', displayName: 'Bear Call Spread (Credit)',
          tagline: 'Get paid to bet on sideways-to-down movement',
          reason: 'Receive a credit upfront. Profitable as long as the market doesn\'t rally sharply.',
          riskReward: 'low', expiryNote: 'Best with 15–30 days to expiry',
          strategyKey: 'VerticalSpread',
        },
      ],
      equity: [
        {
          name: 'CashOrDefensive', displayName: 'Move to Cash / Defensive',
          tagline: 'Protect capital in bearish conditions',
          reason: 'In bearish equity environments, reducing exposure is often the highest-return action.',
          riskReward: 'low',
        },
      ],
    },
    medium: {
      options: [
        {
          name: 'LongPut', displayName: 'Long Put',
          tagline: 'Direct bearish exposure with limited risk',
          reason: 'Simple downside hedge. Profit from market falls while capping your maximum loss.',
          riskReward: 'high', expiryNote: 'Use 30–60 days to expiry for balance of cost and time',
          strategyKey: 'VerticalSpread',
        },
        {
          name: 'BearPutSpread', displayName: 'Bear Put Spread',
          tagline: 'Lower cost bearish position',
          reason: 'Reduces the cost of a long put by selling a further OTM put. Good if you have a downside target.',
          riskReward: 'medium', strategyKey: 'VerticalSpread',
        },
      ],
      equity: [
        {
          name: 'CashOrHedge', displayName: 'Reduce Equity Exposure',
          tagline: 'Trim positions in declining markets',
          reason: 'Cut equity exposure and wait for the trend to stabilise before re-entering.',
          riskReward: 'low',
        },
      ],
    },
    high: {
      options: [
        {
          name: 'LongPut', displayName: 'Long Put (Aggressive)',
          tagline: 'High-leverage bearish bet',
          reason: 'Significant downside leverage. Your loss is capped at the premium paid.',
          riskReward: 'high', expiryNote: 'Shorter expiry = more leverage, more time-decay risk',
          strategyKey: 'VerticalSpread',
        },
      ],
      equity: [
        {
          name: 'PcrOptions', displayName: 'PCR/OI/VWAP Options (STRAT-003)',
          tagline: 'Intraday options strategy for directional plays',
          reason: 'Uses Put-Call Ratio, Open Interest, and VWAP signals to time short-term directional entries.',
          riskReward: 'high', strategyKey: 'IntradayPcrOptions',
        },
      ],
    },
  },

  neutral: {
    low: {
      options: [
        {
          name: 'IronCondor', displayName: 'Iron Condor',
          tagline: 'Profit from a market that stays range-bound',
          reason: 'Collect premium from both sides. Profits as long as the market stays between two levels.',
          riskReward: 'low', expiryNote: 'Best with 20–40 days to expiry, sell after IV spike',
          strategyKey: 'IronCondor',
        },
        {
          name: 'CalendarSpread', displayName: 'Calendar Spread',
          tagline: 'Profit from time decay near ATM',
          reason: 'Sell short-expiry, buy long-expiry at the same strike. Theta works in your favour.',
          riskReward: 'low', expiryNote: 'Near-expiry option decays faster — roll before last week',
          strategyKey: 'CalendarSpread',
        },
      ],
      equity: [
        {
          name: 'MeanReversion', displayName: 'Mean Reversion',
          tagline: 'Buy dips in a range-bound market',
          reason: 'When markets consolidate, buying on dips near support can generate consistent returns.',
          riskReward: 'low',
        },
      ],
    },
    medium: {
      options: [
        {
          name: 'IronCondor', displayName: 'Iron Condor',
          tagline: 'Classic range-bound options strategy',
          reason: 'Four-leg defined-risk structure. Profits from theta decay as long as price stays in range.',
          riskReward: 'low', strategyKey: 'IronCondor',
        },
        {
          name: 'ShortStrangle', displayName: 'Short Strangle',
          tagline: 'Wide-range premium collection',
          reason: 'Sell OTM call and put. Higher credit than iron condor but unbounded risk — use with caution.',
          riskReward: 'medium', expiryNote: 'Best with high IV (IVP > 60%) and 20–30 DTE',
          strategyKey: 'ShortStraddle',
        },
      ],
      equity: [
        {
          name: 'VcpSwing', displayName: 'VCP Swing — Selective',
          tagline: 'Wait for breakouts in neutral market',
          reason: 'In neutral markets, only take the highest-quality VCP setups with strong breadth confirmation.',
          riskReward: 'medium', strategyKey: 'VcpSwing',
        },
      ],
    },
    high: {
      options: [
        {
          name: 'ShortStraddle', displayName: 'Short Straddle',
          tagline: 'Maximum theta decay play at ATM',
          reason: 'Sell ATM call and put. High credit, but risk increases sharply if market moves. Use hedges.',
          riskReward: 'high', expiryNote: 'Sell after IV spike; exit well before expiry',
          strategyKey: 'ShortStraddle',
        },
      ],
      equity: [
        {
          name: 'VcpSwing', displayName: 'VCP Swing',
          tagline: 'Systematic breakout trading',
          reason: 'Consistent edge across market cycles using volume contraction + breadth confirmation.',
          riskReward: 'medium', strategyKey: 'VcpSwing',
        },
      ],
    },
  },

  volatile: {
    low: {
      options: [
        {
          name: 'LongStrangle', displayName: 'Long Strangle',
          tagline: 'Profit from big moves in either direction',
          reason: 'Buy OTM call + OTM put. Cheaper than straddle. Profits if market moves significantly either way.',
          riskReward: 'medium', expiryNote: 'Use before expected events: budget, earnings, RBI policy',
          strategyKey: 'VerticalSpread',
        },
        {
          name: 'FibSpread', displayName: 'Fibonacci Hedged Spread (STRAT-002)',
          tagline: 'Directional option spread with Fibonacci timing',
          reason: 'Enters near Fibonacci retracement levels with a hedged spread to limit drawdown.',
          riskReward: 'medium', strategyKey: 'FibOptionSpread',
        },
      ],
      equity: [
        {
          name: 'ReduceSize', displayName: 'Reduce Position Size',
          tagline: 'Survive volatile markets by trading smaller',
          reason: 'In volatile markets, capital preservation is the priority. Smaller size = smaller drawdowns.',
          riskReward: 'low',
        },
      ],
    },
    medium: {
      options: [
        {
          name: 'LongStraddle', displayName: 'Long Straddle',
          tagline: 'Pure volatility play — direction doesn\'t matter',
          reason: 'Buy ATM call + put. Profits if market makes a large move in either direction.',
          riskReward: 'high', expiryNote: 'Buy before IV rises; target events with expected volatility',
          strategyKey: 'VerticalSpread',
        },
        {
          name: 'PcrOptions', displayName: 'PCR/OI/VWAP Strategy (STRAT-003)',
          tagline: 'Capture intraday directional swings',
          reason: 'Volatile sessions often generate strong PCR and OI signals — ideal for STRAT-003.',
          riskReward: 'medium', strategyKey: 'IntradayPcrOptions',
        },
      ],
      equity: [
        {
          name: 'VcpSwing', displayName: 'VCP with Tight Stops',
          tagline: 'Trade breakouts but cut losses quickly',
          reason: 'VCP setups still work in volatile markets — just use tighter stops and smaller size.',
          riskReward: 'medium', strategyKey: 'VcpSwing',
        },
      ],
    },
    high: {
      options: [
        {
          name: 'LongStraddle', displayName: 'Long Straddle (Aggressive)',
          tagline: 'Maximum leverage on a big expected move',
          reason: 'High leverage play on volatility. Ideal before major market-moving events.',
          riskReward: 'high', expiryNote: 'Time carefully — too early loses to time decay',
          strategyKey: 'VerticalSpread',
        },
      ],
      equity: [
        {
          name: 'PcrOptions', displayName: 'Intraday PCR Momentum',
          tagline: 'Ride intraday volatility waves',
          reason: 'Volatile markets produce strong intraday PCR divergences — ideal for STRAT-003.',
          riskReward: 'high', strategyKey: 'IntradayPcrOptions',
        },
      ],
    },
  },
}

// ── Lookups ───────────────────────────────────────────────────────────────────

const OUTLOOK_LABELS: Record<MarketOutlook, { label: string; icon: string; color: string }> = {
  bullish:  { label: 'Bullish',  icon: '↑', color: C.green },
  bearish:  { label: 'Bearish',  icon: '↓', color: C.red },
  neutral:  { label: 'Neutral',  icon: '→', color: C.blue },
  volatile: { label: 'Volatile', icon: '↕', color: C.amber },
}

const RISK_LABELS: Record<RiskTolerance, { label: string; color: string }> = {
  low:    { label: 'Low risk',    color: C.green },
  medium: { label: 'Moderate risk', color: C.amber },
  high:   { label: 'High risk',   color: C.red },
}

const RR_BADGE: Record<string, { label: string; bg: string; color: string }> = {
  low:    { label: 'Conservative', bg: C.greenBg, color: C.green },
  medium: { label: 'Moderate',     bg: C.amberBg, color: C.amber },
  high:   { label: 'Aggressive',   bg: C.redBg,   color: C.red },
}

// ── Component ─────────────────────────────────────────────────────────────────

export function StrategyRecommendationPanel({
  marketOutlook,
  riskTolerance,
  instrumentType,
  onSelectStrategy,
}: StrategyRecommendationPanelProps) {
  const [expanded, setExpanded] = useState<string | null>(null)

  const recs = RECOMMENDATIONS[marketOutlook]?.[riskTolerance]?.[instrumentType] ?? []
  const outlookInfo = OUTLOOK_LABELS[marketOutlook]
  const riskInfo = RISK_LABELS[riskTolerance]

  if (recs.length === 0) return null

  return (
    <div style={{
      background: C.surface,
      border: `1px solid ${C.border}`,
      borderRadius: 10,
      overflow: 'hidden',
    }}>
      {/* Header */}
      <div style={{
        padding: '10px 16px',
        background: C.surface2,
        borderBottom: `1px solid ${C.border}`,
        display: 'flex', alignItems: 'center', gap: 12,
      }}>
        <div style={{ fontSize: 11, fontWeight: 700, color: C.textSub, textTransform: 'uppercase', letterSpacing: '0.07em' }}>
          Recommended for you
        </div>
        <div style={{
          fontSize: 11, padding: '2px 8px', borderRadius: 20,
          background: outlookInfo.color + '22', color: outlookInfo.color, fontWeight: 700,
        }}>
          {outlookInfo.icon} {outlookInfo.label}
        </div>
        <div style={{
          fontSize: 11, padding: '2px 8px', borderRadius: 20,
          background: riskInfo.color + '22', color: riskInfo.color, fontWeight: 700,
        }}>
          {riskInfo.label}
        </div>
        <div style={{ marginLeft: 'auto', fontSize: 11, color: C.textMuted }}>
          {recs.length} suggestion{recs.length !== 1 ? 's' : ''}
        </div>
      </div>

      {/* Cards */}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 0 }}>
        {recs.map((rec, i) => {
          const isExpanded = expanded === rec.name
          const badge = RR_BADGE[rec.riskReward]
          return (
            <div
              key={rec.name}
              style={{
                borderBottom: i < recs.length - 1 ? `1px solid ${C.border2}` : 'none',
                padding: '12px 16px',
                transition: 'background 0.15s',
                background: isExpanded ? C.surface2 : 'transparent',
              }}
            >
              <div style={{ display: 'flex', alignItems: 'flex-start', gap: 12 }}>
                {/* Icon */}
                <div style={{
                  width: 36, height: 36, flexShrink: 0,
                  background: C.surface3, borderRadius: 8,
                  display: 'flex', alignItems: 'center', justifyContent: 'center',
                  fontSize: 16,
                }}>
                  {instrumentType === 'options' ? '📊' : '📈'}
                </div>

                {/* Main content */}
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap', marginBottom: 2 }}>
                    <span style={{ fontSize: 14, fontWeight: 700, color: C.text }}>
                      {rec.displayName}
                    </span>
                    <span style={{
                      fontSize: 10, padding: '2px 7px', borderRadius: 12,
                      background: badge.bg, color: badge.color, fontWeight: 700,
                    }}>
                      {badge.label}
                    </span>
                  </div>
                  <div style={{ fontSize: 12, color: C.textSub, marginBottom: isExpanded ? 8 : 0 }}>
                    {rec.tagline}
                  </div>

                  {isExpanded && (
                    <div style={{ fontSize: 12, color: C.text, lineHeight: 1.6 }}>
                      <p style={{ margin: '0 0 6px 0' }}>{rec.reason}</p>
                      {rec.expiryNote && (
                        <div style={{
                          fontSize: 11, color: C.amber,
                          display: 'flex', alignItems: 'center', gap: 6,
                        }}>
                          ⏱ {rec.expiryNote}
                        </div>
                      )}
                    </div>
                  )}
                </div>

                {/* Actions */}
                <div style={{ display: 'flex', flexDirection: 'column', gap: SP.xs, flexShrink: 0 }}>
                  <button
                    onClick={() => setExpanded(isExpanded ? null : rec.name)}
                    style={{
                      padding: '4px 10px', fontSize: 11, fontWeight: 600,
                      background: 'transparent', border: `1px solid ${C.border3}`,
                      borderRadius: 5, color: C.textSub, cursor: 'pointer',
                    }}
                  >
                    {isExpanded ? 'Less' : 'Learn more'}
                  </button>
                  {rec.strategyKey && onSelectStrategy && (
                    <button
                      onClick={() => onSelectStrategy(rec.strategyKey!)}
                      style={{
                        padding: '4px 10px', fontSize: 11, fontWeight: 700,
                        background: C.blue + '22', border: `1px solid ${C.blue}44`,
                        borderRadius: 5, color: C.blue, cursor: 'pointer',
                        whiteSpace: 'nowrap',
                      }}
                    >
                      Use this →
                    </button>
                  )}
                </div>
              </div>
            </div>
          )
        })}
      </div>
    </div>
  )
}
