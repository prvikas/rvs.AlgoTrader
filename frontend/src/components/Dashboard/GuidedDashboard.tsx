/**
 * GuidedDashboard — simplified portfolio + recommendation view for novice users.
 *
 * Shows:
 *  - Market trend badge (from MarketBreadth API)
 *  - StrategyRecommendationPanel (driven by user-selected outlook + risk)
 *  - Today's P&L (single number, not a table)
 *  - CTA: "Start a new strategy" → StrategiesPage
 *
 * Visible when UserMode === 'guided' on the Portfolio page.
 */

import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { strategiesApi } from '../../api/client'
import { C, SP, F } from '../../styles/tokens'
import {
  StrategyRecommendationPanel,
  MarketOutlook,
  RiskTolerance,
  InstrumentType,
} from '../Strategy/StrategyRecommendationPanel'

// ── Helpers ───────────────────────────────────────────────────────────────────

function fmtInr(v: number): string {
  const abs = Math.abs(v)
  if (abs >= 100_000) return `₹${(v / 100_000).toFixed(2)}L`
  if (abs >= 1_000) return `₹${(v / 1_000).toFixed(1)}K`
  return `₹${v.toFixed(0)}`
}

// ── Market Trend Badge ────────────────────────────────────────────────────────

interface BreadthData {
  pct200Sma?: number
  advanceDeclineRatio?: number
  newHighs?: number
  newLows?: number
}

function deriveMarketTrend(breadth?: BreadthData): { trend: string; color: string; desc: string } {
  if (!breadth) return { trend: 'Unknown', color: C.textMuted, desc: 'Market breadth data unavailable.' }
  const { pct200Sma = 50, advanceDeclineRatio = 1 } = breadth
  if (pct200Sma >= 60 && advanceDeclineRatio >= 1.5) return { trend: 'Bullish', color: C.green, desc: 'Most stocks are above their 200-day average and advances outnumber declines. A healthy bull market.' }
  if (pct200Sma <= 35 || advanceDeclineRatio <= 0.6) return { trend: 'Bearish', color: C.red, desc: 'Many stocks are in downtrends. Proceed with caution.' }
  if (pct200Sma >= 45 && pct200Sma < 60) return { trend: 'Neutral', color: C.blue, desc: 'Markets are mixed — some sectors strong, others weak. Selective approach recommended.' }
  return { trend: 'Volatile', color: C.amber, desc: 'Market breadth is choppy. Higher volatility means higher risk.' }
}

function MarketTrendBadge({ trend, color, desc }: { trend: string; color: string; desc: string }) {
  return (
    <div style={{
      display: 'flex', alignItems: 'flex-start', gap: 12,
      padding: '12px 16px', background: color + '11',
      border: `1px solid ${color}33`, borderRadius: 10,
    }}>
      <div style={{
        width: 44, height: 44, flexShrink: 0,
        background: color + '22', borderRadius: '50%',
        display: 'flex', alignItems: 'center', justifyContent: 'center',
        fontSize: 20, fontWeight: 800, color,
      }}>
        {trend === 'Bullish' ? '↑' : trend === 'Bearish' ? '↓' : trend === 'Volatile' ? '↕' : '→'}
      </div>
      <div>
        <div style={{ fontSize: 11, color: C.textMuted, marginBottom: 2, textTransform: 'uppercase', letterSpacing: '0.06em' }}>
          Current Market Trend
        </div>
        <div style={{ fontSize: 18, fontWeight: 800, color }}>{trend}</div>
        <div style={{ fontSize: 12, color: C.textSub, marginTop: 4, lineHeight: 1.5 }}>{desc}</div>
      </div>
    </div>
  )
}

// ── Outlook selector ──────────────────────────────────────────────────────────

const OUTLOOK_OPTIONS: Array<{ value: MarketOutlook; label: string; icon: string; desc: string }> = [
  { value: 'bullish',  label: 'Bullish',  icon: '↑', desc: 'I think the market will go up' },
  { value: 'bearish',  label: 'Bearish',  icon: '↓', desc: 'I think the market will go down' },
  { value: 'neutral',  label: 'Neutral',  icon: '→', desc: 'Market will move sideways' },
  { value: 'volatile', label: 'Volatile', icon: '↕', desc: 'Big move expected — not sure which way' },
]

const RISK_OPTIONS: Array<{ value: RiskTolerance; label: string; desc: string }> = [
  { value: 'low',    label: 'Conservative', desc: 'Preserve capital, accept lower returns' },
  { value: 'medium', label: 'Moderate',     desc: 'Balanced risk/reward approach' },
  { value: 'high',   label: 'Aggressive',   desc: 'Higher risk for higher potential return' },
]

const INSTR_OPTIONS: Array<{ value: InstrumentType; label: string; icon: string }> = [
  { value: 'options', label: 'Options',   icon: '📊' },
  { value: 'equity',  label: 'Equities',  icon: '📈' },
]

function SelectorCard({
  selected, onClick, children, accent,
}: { selected: boolean; onClick: () => void; children: React.ReactNode; accent?: string }) {
  return (
    <button
      onClick={onClick}
      style={{
        padding: '10px 14px', background: selected ? (accent ? accent + '22' : C.blue + '22') : C.surface2,
        border: `1px solid ${selected ? (accent ?? C.blue) : C.border}`,
        borderRadius: 8, cursor: 'pointer', textAlign: 'left',
        transition: 'all 0.15s',
      }}
    >
      {children}
    </button>
  )
}

// ── Today's P&L summary ───────────────────────────────────────────────────────

function TodayPnlCard({ pnl }: { pnl: number }) {
  const positive = pnl >= 0
  return (
    <div style={{
      padding: '16px 20px',
      background: positive ? C.greenBg : C.redBg,
      border: `1px solid ${positive ? C.green : C.red}44`,
      borderRadius: 10,
    }}>
      <div style={{ fontSize: 11, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.07em', marginBottom: 4 }}>
        Today's P&L
      </div>
      <div style={{
        fontSize: 32, fontWeight: 800,
        color: positive ? C.green : C.red,
        fontFamily: F.mono,
        lineHeight: 1,
      }}>
        {positive ? '+' : ''}{fmtInr(pnl)}
      </div>
      <div style={{ fontSize: 12, color: C.textSub, marginTop: 6 }}>
        {positive
          ? 'Profitable day — keep your risk controls active.'
          : 'Down day — review if positions are within your planned stop levels.'}
      </div>
    </div>
  )
}

// ── Main component ────────────────────────────────────────────────────────────

interface GuidedDashboardProps {
  onNavigate: (page: string) => void
}

export function GuidedDashboard({ onNavigate }: GuidedDashboardProps) {
  const [outlook, setOutlook] = useState<MarketOutlook>('bullish')
  const [risk, setRisk] = useState<RiskTolerance>('low')
  const [instr, setInstr] = useState<InstrumentType>('options')

  // Fetch live strategies for today's P&L
  const { data: strategies = [] } = useQuery({
    queryKey: ['strategies'],
    queryFn: () => strategiesApi.list().then(r => r.data.data ?? []),
    refetchInterval: 30_000,
  })

  // Fetch market breadth
  const { data: breadthData } = useQuery({
    queryKey: ['market-breadth-latest'],
    queryFn: async () => {
      const res = await fetch('/api/market-breadth/latest', {
        headers: { Authorization: `Bearer ${localStorage.getItem('jwt_token') ?? ''}` },
      })
      if (!res.ok) return undefined
      const body = await res.json()
      return body.data as BreadthData | undefined
    },
    staleTime: 5 * 60 * 1000,
  })

  const todayPnl = strategies.reduce((sum, s) => {
    return sum + (s.todayRealizedPnl ?? 0) + (s.todayUnrealizedPnl ?? 0)
  }, 0)

  const marketTrend = deriveMarketTrend(breadthData)

  const OUTLOOK_COLORS: Record<MarketOutlook, string> = {
    bullish: C.green, bearish: C.red, neutral: C.blue, volatile: C.amber,
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: SP.xl, maxWidth: 900 }}>

      {/* Welcome header */}
      <div>
        <div style={{ fontSize: 22, fontWeight: 800, color: C.text, marginBottom: 4 }}>
          Welcome to AlgoTrader
        </div>
        <div style={{ fontSize: 14, color: C.textSub }}>
          Here's a simple overview to get you started. Switch to Pro mode for full controls.
        </div>
      </div>

      {/* Two-column layout: Trend + P&L */}
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 220px', gap: SP.lg }}>
        <MarketTrendBadge {...marketTrend} />
        <TodayPnlCard pnl={todayPnl} />
      </div>

      {/* CTA */}
      <div style={{
        padding: '14px 18px',
        background: C.surface,
        border: `1px solid ${C.border}`,
        borderRadius: 10,
        display: 'flex', alignItems: 'center', justifyContent: 'space-between',
      }}>
        <div>
          <div style={{ fontSize: 14, fontWeight: 700, color: C.text, marginBottom: 2 }}>
            Ready to build your first strategy?
          </div>
          <div style={{ fontSize: 12, color: C.textSub }}>
            Create a strategy, backtest it on historical data, and deploy when you're confident.
          </div>
        </div>
        <button
          onClick={() => onNavigate('strategies')}
          style={{
            padding: '9px 20px', background: C.blue, color: '#fff',
            border: 'none', borderRadius: 7, cursor: 'pointer',
            fontSize: 13, fontWeight: 700, whiteSpace: 'nowrap', flexShrink: 0,
          }}
        >
          Start a strategy →
        </button>
      </div>

      {/* Divider */}
      <div style={{ borderTop: `1px solid ${C.border}` }} />

      {/* Strategy finder */}
      <div>
        <div style={{ fontSize: 14, fontWeight: 700, color: C.text, marginBottom: SP.md }}>
          Find the right strategy for today
        </div>

        {/* Step 1: Outlook */}
        <div style={{ marginBottom: SP.md }}>
          <div style={{ fontSize: 11, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.06em', marginBottom: SP.sm }}>
            1. What's your market view?
          </div>
          <div style={{ display: 'flex', gap: SP.sm, flexWrap: 'wrap' }}>
            {OUTLOOK_OPTIONS.map(o => (
              <SelectorCard
                key={o.value}
                selected={outlook === o.value}
                onClick={() => setOutlook(o.value)}
                accent={OUTLOOK_COLORS[o.value]}
              >
                <div style={{ fontSize: 18, marginBottom: 2 }}>{o.icon}</div>
                <div style={{ fontSize: 12, fontWeight: 700, color: C.text }}>{o.label}</div>
                <div style={{ fontSize: 11, color: C.textSub, marginTop: 2, maxWidth: 90 }}>{o.desc}</div>
              </SelectorCard>
            ))}
          </div>
        </div>

        {/* Step 2: Risk */}
        <div style={{ marginBottom: SP.md }}>
          <div style={{ fontSize: 11, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.06em', marginBottom: SP.sm }}>
            2. How much risk can you take?
          </div>
          <div style={{ display: 'flex', gap: SP.sm, flexWrap: 'wrap' }}>
            {RISK_OPTIONS.map(r => (
              <SelectorCard key={r.value} selected={risk === r.value} onClick={() => setRisk(r.value)}>
                <div style={{ fontSize: 12, fontWeight: 700, color: C.text }}>{r.label}</div>
                <div style={{ fontSize: 11, color: C.textSub, marginTop: 2, maxWidth: 120 }}>{r.desc}</div>
              </SelectorCard>
            ))}
          </div>
        </div>

        {/* Step 3: Instrument */}
        <div style={{ marginBottom: SP.lg }}>
          <div style={{ fontSize: 11, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.06em', marginBottom: SP.sm }}>
            3. What do you want to trade?
          </div>
          <div style={{ display: 'flex', gap: SP.sm }}>
            {INSTR_OPTIONS.map(o => (
              <SelectorCard key={o.value} selected={instr === o.value} onClick={() => setInstr(o.value)}>
                <span style={{ fontSize: 16, marginRight: 6 }}>{o.icon}</span>
                <span style={{ fontSize: 12, fontWeight: 700, color: C.text }}>{o.label}</span>
              </SelectorCard>
            ))}
          </div>
        </div>

        {/* Recommendations */}
        <StrategyRecommendationPanel
          marketOutlook={outlook}
          riskTolerance={risk}
          instrumentType={instr}
          onSelectStrategy={(_key) => onNavigate('strategies')}
        />
      </div>
    </div>
  )
}
