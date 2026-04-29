import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { regimeApi, QuantRegimeResult } from '../../api/client'
import { RegimeBadge } from './RegimeBadge'
import { C, F } from '../../styles/tokens'

// ── Symbols the engine can classify ───────────────────────────────────────────

const SYMBOLS = ['NIFTY50', 'BANKNIFTY', 'FINNIFTY', 'SENSEX']
const TIMEFRAMES = ['1d', '1w', '1h', '15m']

// ── Factor row ────────────────────────────────────────────────────────────────

function FactorRow({ factor }: { factor: QuantRegimeResult['contributingFactors'][0] }) {
  const impactColor =
    factor.impact === 'supporting'   ? C.green :
    factor.impact === 'contradicting' ? C.red   : C.textDim

  return (
    <div style={{
      display: 'grid', gridTemplateColumns: '130px 80px 1fr 90px',
      gap: 8, alignItems: 'start',
      padding: '6px 0', borderBottom: `1px solid ${C.border}`,
      fontSize: 12,
    }}>
      <span style={{ color: C.textSub, fontWeight: 600 }}>{factor.name}</span>
      <span style={{ color: C.text, fontFamily: F.mono, fontWeight: 700 }}>{factor.value}</span>
      <span style={{ color: C.textMuted }}>{factor.interpretation}</span>
      <span style={{
        fontSize: 10, color: impactColor, fontWeight: 600,
        textTransform: 'capitalize', textAlign: 'right',
      }}>
        {factor.impact}
      </span>
    </div>
  )
}

// ── Live regime panel ─────────────────────────────────────────────────────────

interface RegimePanelProps {
  defaultSymbol?: string
  defaultTimeframe?: string
}

export function RegimePanel({ defaultSymbol = 'NIFTY50', defaultTimeframe = '1d' }: RegimePanelProps) {
  const [symbol, setSymbol]       = useState(defaultSymbol)
  const [timeframe, setTimeframe] = useState(defaultTimeframe)

  const { data, isLoading, isError, refetch, isFetching } = useQuery({
    queryKey: ['regime-classify', symbol, timeframe],
    queryFn:  () => regimeApi.classify(symbol, timeframe).then(r => r.data.data),
    staleTime: 60_000,
    retry: 1,
  })

  const selectStyle: React.CSSProperties = {
    background: C.surface2, border: `1px solid ${C.border}`,
    borderRadius: 4, color: C.text, fontSize: 12, padding: '4px 8px',
    cursor: 'pointer',
  }

  return (
    <div style={{
      background: C.surface, border: `1px solid ${C.border}`,
      borderRadius: 8, padding: '16px 20px',
    }}>
      {/* Controls */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 16, flexWrap: 'wrap' }}>
        <span style={{ fontSize: 13, fontWeight: 700, color: C.text }}>Live Regime</span>

        <select value={symbol} onChange={e => setSymbol(e.target.value)} style={selectStyle}>
          {SYMBOLS.map(s => <option key={s} value={s}>{s}</option>)}
        </select>

        <select value={timeframe} onChange={e => setTimeframe(e.target.value)} style={selectStyle}>
          {TIMEFRAMES.map(t => <option key={t} value={t}>{t}</option>)}
        </select>

        <button
          onClick={() => refetch()}
          disabled={isFetching}
          style={{
            background: 'none', border: `1px solid ${C.border}`, borderRadius: 4,
            color: C.textSub, fontSize: 11, padding: '4px 10px', cursor: 'pointer',
          }}
        >
          {isFetching ? 'Refreshing…' : 'Refresh'}
        </button>

        {data?.computedAt && (
          <span style={{ fontSize: 10, color: C.textDim, marginLeft: 'auto' }}>
            {new Date(data.computedAt).toLocaleTimeString('en-IN', { hour: '2-digit', minute: '2-digit' })}
          </span>
        )}
      </div>

      {/* Loading / error states */}
      {isLoading && (
        <div style={{ color: C.textMuted, fontSize: 13, padding: '12px 0' }}>Classifying regime…</div>
      )}
      {isError && (
        <div style={{ color: C.red, fontSize: 13, padding: '12px 0' }}>
          Could not classify regime — candle data may not be available for this symbol/timeframe.
        </div>
      )}

      {/* Result */}
      {data && !isLoading && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
          {/* Traffic light + confidence */}
          <RegimeBadge
            regime={data.regime}
            trafficLight={data.trafficLight}
            confidence={data.confidence}
          />

          {/* Data warning */}
          {data.dataWarning && (
            <div style={{
              background: `${C.amber}18`, border: `1px solid ${C.amber}44`,
              borderRadius: 4, padding: '6px 10px', fontSize: 11, color: C.amber,
            }}>
              {data.dataWarning}
            </div>
          )}

          {/* Summary */}
          <div>
            <div style={{ fontSize: 10, fontWeight: 700, color: C.blue, textTransform: 'uppercase', letterSpacing: '0.06em', marginBottom: 4 }}>
              Summary
            </div>
            <p style={{ margin: 0, fontSize: 13, color: C.textSub, lineHeight: 1.55 }}>{data.summary}</p>
          </div>

          {/* Strategy guidance */}
          <div>
            <div style={{ fontSize: 10, fontWeight: 700, color: C.green, textTransform: 'uppercase', letterSpacing: '0.06em', marginBottom: 4 }}>
              Strategy guidance
            </div>
            <p style={{ margin: 0, fontSize: 13, color: C.textSub, lineHeight: 1.55 }}>{data.strategyGuidance}</p>
          </div>

          {/* Contributing factors */}
          <div>
            <div style={{ fontSize: 10, fontWeight: 700, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.06em', marginBottom: 8 }}>
              Contributing factors
            </div>
            <div style={{
              display: 'grid', gridTemplateColumns: '130px 80px 1fr 90px',
              gap: 8, padding: '4px 0 6px', borderBottom: `1px solid ${C.border2}`,
              fontSize: 10, color: C.textDim, fontWeight: 700, textTransform: 'uppercase',
            }}>
              <span>Factor</span><span>Value</span><span>Interpretation</span><span style={{ textAlign: 'right' }}>Impact</span>
            </div>
            {data.contributingFactors.map((f, i) => <FactorRow key={i} factor={f} />)}
          </div>
        </div>
      )}
    </div>
  )
}
