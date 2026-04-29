import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { indicatorIntelligenceApi, greeksIntelligenceApi } from '../api/client'
import { IndicatorIntelligenceCardView } from '../components/quantIntelligence/IndicatorIntelligenceCard'
import { GreeksIntelligenceCardView } from '../components/quantIntelligence/GreeksIntelligenceCard'
import { RegimePanel } from '../components/quantIntelligence/RegimePanel'
import { C, CONTENT_PAD } from '../styles/tokens'

type Tab = 'regime' | 'indicators' | 'greeks'

// ── Section header ─────────────────────────────────────────────────────────────

function SectionHeader({ title, sub }: { title: string; sub: string }) {
  return (
    <div style={{ marginBottom: 20 }}>
      <h2 style={{ margin: 0, fontSize: 18, fontWeight: 700, color: C.text }}>{title}</h2>
      <p style={{ margin: '4px 0 0', fontSize: 13, color: C.textMuted }}>{sub}</p>
    </div>
  )
}

// ── Page ──────────────────────────────────────────────────────────────────────

export function QuantIntelligencePage() {
  const [activeTab, setActiveTab] = useState<Tab>('regime')

  const { data: indicators, isLoading: indLoading } = useQuery({
    queryKey: ['indicator-intelligence'],
    queryFn: () => indicatorIntelligenceApi.getAll().then(r => r.data.data ?? []),
    staleTime: 60_000,
    enabled: activeTab === 'indicators',
  })

  const { data: greeks, isLoading: grkLoading } = useQuery({
    queryKey: ['greeks-intelligence'],
    queryFn: () => greeksIntelligenceApi.getAll().then(r => r.data.data ?? []),
    staleTime: 60_000,
    enabled: activeTab === 'greeks',
  })

  const isLoading = activeTab === 'indicators' ? indLoading : grkLoading

  const TAB_ITEMS: { id: Tab; label: string }[] = [
    { id: 'regime',     label: 'Market Regime' },
    { id: 'indicators', label: 'Indicator Intelligence' },
    { id: 'greeks',     label: 'Options / Greeks Intelligence' },
  ]

  return (
    <div style={{ padding: CONTENT_PAD, maxWidth: 960, margin: '0 auto' }}>
      {/* Page title */}
      <div style={{ marginBottom: 24 }}>
        <h1 style={{ margin: 0, fontSize: 22, fontWeight: 800, color: C.text }}>
          Quant Intelligence
        </h1>
        <p style={{ margin: '6px 0 0', fontSize: 13, color: C.textMuted, maxWidth: 680 }}>
          Editable knowledge cards for every indicator and options metric. Click{' '}
          <strong style={{ color: C.text }}>Edit</strong> on any card to refine the
          conditions, misuse warnings, and sizing rules based on your own research.
          Seed content is version 1 — your real edge compounds as you keep updating these.
        </p>
      </div>

      {/* Tabs */}
      <div style={{ display: 'flex', gap: 2, marginBottom: 24, borderBottom: `1px solid ${C.border}` }}>
        {TAB_ITEMS.map(t => (
          <button
            key={t.id}
            onClick={() => setActiveTab(t.id)}
            style={{
              background: 'none', border: 'none', cursor: 'pointer',
              padding: '8px 18px', fontSize: 13, fontWeight: activeTab === t.id ? 700 : 400,
              color: activeTab === t.id ? C.blue : C.textMuted,
              borderBottom: activeTab === t.id ? `2px solid ${C.blue}` : '2px solid transparent',
              marginBottom: -1,
            }}
          >
            {t.label}
          </button>
        ))}
      </div>

      {/* Regime panel */}
      {activeTab === 'regime' && (
        <>
          <SectionHeader
            title="Market Regime Engine"
            sub="Rules-based regime classifier using ADX, ATR expansion, and IV Percentile. Select a symbol and timeframe to see the current regime, traffic-light signal quality, and which factors contributed."
          />
          <RegimePanel />
        </>
      )}

      {/* Indicator cards */}
      {activeTab === 'indicators' && (
        <>
          <SectionHeader
            title="Technical Indicator Cards"
            sub="What each indicator measures, when it has positive EV, when to ignore it, and how it should affect sizing."
          />
          {isLoading && <div style={{ color: C.textMuted, fontSize: 13 }}>Loading…</div>}
          <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
            {(indicators ?? []).map(card => (
              <IndicatorIntelligenceCardView key={card.id} card={card} />
            ))}
            {!isLoading && (indicators ?? []).length === 0 && (
              <div style={{ color: C.textDim, fontSize: 13 }}>
                No indicator cards found. Run migration 045 to seed the default cards.
              </div>
            )}
          </div>
        </>
      )}

      {/* Greeks cards */}
      {activeTab === 'greeks' && (
        <>
          <SectionHeader
            title="Options Metrics Cards"
            sub="Delta, Theta, Vega, Gamma, IV, IV Percentile, and India VIX — what each measures, regime context, and P&L implications."
          />
          {isLoading && <div style={{ color: C.textMuted, fontSize: 13 }}>Loading…</div>}
          <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
            {(greeks ?? []).map(card => (
              <GreeksIntelligenceCardView key={card.id} card={card} />
            ))}
            {!isLoading && (greeks ?? []).length === 0 && (
              <div style={{ color: C.textDim, fontSize: 13 }}>
                No Greeks cards found. Run migration 046 to seed the default cards.
              </div>
            )}
          </div>
        </>
      )}
    </div>
  )
}
