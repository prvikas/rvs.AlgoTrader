/**
 * BacktestReplayChart — TradingView Lightweight Charts v5
 *
 * Modes
 *   isLive=true  → bar-by-bar drip-feed animation via requestAnimationFrame
 *   isLive=false → bulk load; shows last 150 bars centred on latest candle
 *
 * Price-scale routing
 *   "right"         → candles + price-space indicators (EMA, VWAP, BB, rangeHigh/Low)
 *   "vol"           → volume histogram, bottom 20%
 *   "_ind_<key>"    → non-price indicators (ATR, …) each on a hidden scale
 *
 * Marker fix (v5)
 *   createSeriesMarkers() plugin is re-created after every setData() call.
 *   Markers are always sorted ascending by time (required by the v5 plugin).
 *
 * Drip-feed (live mode)
 *   New bars arriving in batch are pushed to pendingRef and consumed one per
 *   animation frame (~60 fps) so the chart animates tick-by-tick.
 */

import { useEffect, useRef, useMemo, useCallback } from 'react'
import {
  createChart,
  CandlestickSeries,
  LineSeries,
  HistogramSeries,
  createSeriesMarkers,
  IChartApi,
  ISeriesApi,
  ISeriesMarkersPluginApi,
  Time,
} from 'lightweight-charts'
import { BacktestChartBar } from '../../api/client'
import { C, CHART } from '../../styles/tokens'

// ── Colour palette ───────────────────────────────────────────────────────────
const INDICATOR_COLORS: Record<string, string> = {
  ema5:      CHART.ema5,
  ema9:      CHART.ema9,
  ema20:     CHART.ema20,
  ema21:     CHART.ema21,
  ema50:     CHART.ema50,
  vwap:      CHART.vwap,
  bbUpper:   CHART.bbUpper,
  bbMid:     CHART.bbMid,
  bbLower:   CHART.bbLower,
  atr:       C.textSub,
  rangeHigh: C.green,
  rangeLow:  C.red,
}
const FALLBACK_COLORS = CHART.palette

function indicatorColor(key: string, idx: number) {
  return INDICATOR_COLORS[key] ?? FALLBACK_COLORS[idx % FALLBACK_COLORS.length]
}

function isDashed(key: string) { return key.startsWith('bb') }

function isPriceSpace(key: string) {
  return (
    key.startsWith('ema')   ||
    key.startsWith('bb')    ||
    key === 'vwap'          ||
    key === 'rangeHigh'     ||
    key === 'rangeLow'
  )
}

// ── Types ────────────────────────────────────────────────────────────────────
type Marker = Parameters<ISeriesMarkersPluginApi<Time>['setMarkers']>[0][0]

function makeMarker(b: BacktestChartBar): Marker {
  const isBuy = b.signal === 'BUY'
  return {
    time:     Math.floor(b.timeMs / 1000) as Time,
    position: (isBuy ? 'belowBar' : 'aboveBar') as 'belowBar' | 'aboveBar',
    color:    isBuy ? C.green : C.red,
    shape:    (isBuy ? 'arrowUp' : 'arrowDown') as 'arrowUp' | 'arrowDown',
    text:     isBuy ? 'B' : 'S',
    size:     1.5,
  }
}

// ── Props ────────────────────────────────────────────────────────────────────
interface Props {
  bars:               BacktestChartBar[]
  isLive:             boolean
  height?:            number
  title?:             string
  /** If true, component uses position:fixed / full-viewport layout */
  fullscreen?:        boolean
  onToggleFullscreen?: () => void
}

// ── Component ────────────────────────────────────────────────────────────────
export function BacktestReplayChart({
  bars, isLive, height = 480, title,
  fullscreen = false, onToggleFullscreen,
}: Props) {
  const containerRef  = useRef<HTMLDivElement>(null)
  const chartRef      = useRef<IChartApi | null>(null)
  const candleRef     = useRef<ISeriesApi<'Candlestick'> | null>(null)
  const volumeRef     = useRef<ISeriesApi<'Histogram'> | null>(null)
  const markersRef    = useRef<ISeriesMarkersPluginApi<Time> | null>(null)
  const lineSeriesRef = useRef<Map<string, ISeriesApi<'Line'>>>(new Map())

  // Live-mode animation state
  const lastDrawnTimeRef  = useRef<number>(0)  // last bar timestamp actually drawn
  const lastQueuedTimeRef = useRef<number>(0)  // last bar pushed to pending queue
  const pendingRef        = useRef<BacktestChartBar[]>([])  // drip-feed queue
  const rafRef            = useRef<number | null>(null)
  const liveMarkersRef    = useRef<Marker[]>([])  // accumulated signal markers (live)
  const totalDrawnRef     = useRef<number>(0)    // total bars added to chart (cumulative across batches)

  // ResizeObserver stale-closure guards
  const isLiveRef  = useRef(isLive)
  const barsLenRef = useRef(0)
  useEffect(() => { isLiveRef.current  = isLive     }, [isLive])
  useEffect(() => { barsLenRef.current = bars.length }, [bars])

  const toCandle = (b: BacktestChartBar) => ({
    time:  Math.floor(b.timeMs / 1000) as Time,
    open: b.open, high: b.high, low: b.low, close: b.close,
  })
  const toVolume = (b: BacktestChartBar) => ({
    time:  Math.floor(b.timeMs / 1000) as Time,
    value: b.volume,
    color: b.close >= b.open ? C.green55 : C.red55,
  })

  // ── Mount: create chart + series ──────────────────────────────────────────
  useEffect(() => {
    if (!containerRef.current) return

    const chart = createChart(containerRef.current, {
      width:  containerRef.current.clientWidth,
      height,
      layout: { background: { color: C.surface }, textColor: C.textSub },
      grid:   { vertLines: { color: C.border }, horzLines: { color: C.border } },
      crosshair: { mode: 1 },
      rightPriceScale: {
        borderColor:  C.border,
        autoScale:    true,
        scaleMargins: { top: 0.06, bottom: 0.22 },
      },
      timeScale: { borderColor: C.border, timeVisible: true, secondsVisible: false },
    })

    const candle = chart.addSeries(CandlestickSeries, {
      upColor:         C.green,
      downColor:       C.red,
      borderUpColor:   C.green,
      borderDownColor: C.red,
      wickUpColor:     C.green,
      wickDownColor:   C.red,
    })

    const volume = chart.addSeries(HistogramSeries, {
      priceFormat:  { type: 'volume' },
      priceScaleId: 'vol',
    })
    chart.priceScale('vol').applyOptions({ scaleMargins: { top: 0.80, bottom: 0 } })

    chartRef.current      = chart
    candleRef.current     = candle
    volumeRef.current     = volume
    markersRef.current    = createSeriesMarkers(candle)
    lineSeriesRef.current.clear()
    lastDrawnTimeRef.current  = 0
    lastQueuedTimeRef.current = 0
    liveMarkersRef.current    = []
    totalDrawnRef.current     = 0

    const ro = new ResizeObserver(() => {
      const c  = chartRef.current
      const el = containerRef.current
      if (!c || !el) return
      c.applyOptions({ width: el.clientWidth })
      if (isLiveRef.current) {
        const lastIdx = barsLenRef.current - 1
        if (lastIdx < 0) return
        const vis   = c.timeScale().getVisibleLogicalRange()
        const halfW = vis ? Math.max(25, (vis.to - vis.from) / 2) : 50
        c.timeScale().setVisibleLogicalRange({ from: lastIdx - halfW, to: lastIdx + halfW })
      } else {
        c.timeScale().fitContent()
      }
      c.priceScale('right').applyOptions({ autoScale: true })
    })
    ro.observe(containerRef.current)

    return () => {
      if (rafRef.current) cancelAnimationFrame(rafRef.current)
      rafRef.current  = null
      pendingRef.current = []
      ro.disconnect()
      chart.remove()
      chartRef.current      = null
      candleRef.current     = null
      volumeRef.current     = null
      markersRef.current    = null
      lineSeriesRef.current.clear()
      lastDrawnTimeRef.current  = 0
      lastQueuedTimeRef.current = 0
      totalDrawnRef.current     = 0
    }
  }, [height]) // eslint-disable-line react-hooks/exhaustive-deps

  // ── Lazily create a line series for an indicator ──────────────────────────
  const getOrCreateLineSeries = useCallback((key: string): ISeriesApi<'Line'> | null => {
    if (!chartRef.current) return null
    const hit = lineSeriesRef.current.get(key)
    if (hit) return hit

    const idx          = lineSeriesRef.current.size
    const priceScaleId = isPriceSpace(key) ? 'right' : `_ind_${key}`

    const series = chartRef.current.addSeries(LineSeries, {
      color:            indicatorColor(key, idx),
      lineWidth:        1,
      lineStyle:        isDashed(key) ? 2 : 0,
      title:            key,
      lastValueVisible: false,
      priceLineVisible: false,
      priceScaleId,
    })

    if (!isPriceSpace(key)) {
      chartRef.current.priceScale(`_ind_${key}`).applyOptions({
        visible:      false,
        scaleMargins: { top: 0.7, bottom: 0.1 },
      })
    }

    lineSeriesRef.current.set(key, series)
    return series
  }, [])

  // ── Data effect ───────────────────────────────────────────────────────────
  useEffect(() => {
    const candle = candleRef.current
    const volume = volumeRef.current
    const chart  = chartRef.current
    if (!candle || !volume || !chart || bars.length === 0) return

    const isFirstLoad = lastDrawnTimeRef.current === 0

    if (isFirstLoad || !isLive) {
      // ──────────────────────────────────────────────────────────────────────
      // BULK PATH: initial live snapshot OR full replay chart after completion
      // ──────────────────────────────────────────────────────────────────────

      // Cancel any in-progress drip animation from a previous run
      if (rafRef.current) { cancelAnimationFrame(rafRef.current); rafRef.current = null }
      pendingRef.current = []

      candle.setData(bars.map(toCandle))
      volume.setData(bars.map(toVolume))
      totalDrawnRef.current = bars.length

      // ── Re-create markers plugin after setData ──────────────────────────
      // In v5, the createSeriesMarkers plugin can lose its internal state when
      // setData() is called on the underlying series.  Re-creating it here
      // ensures markers are always rendered correctly on every bulk load.
      markersRef.current = createSeriesMarkers(candle)
      liveMarkersRef.current = []

      // ── Indicator series ────────────────────────────────────────────────
      const allKeys = new Set<string>()
      bars.forEach(b => { if (b.indicators) Object.keys(b.indicators).forEach(k => allKeys.add(k)) })
      allKeys.forEach(key => {
        const series = getOrCreateLineSeries(key)
        if (!series) return
        const pts = bars
          .filter(b => b.indicators?.[key] != null)
          .map(b => ({ time: Math.floor(b.timeMs / 1000) as Time, value: b.indicators![key] }))
        if (pts.length) series.setData(pts)
      })

      // ── Markers: sorted ascending (required by v5 plugin) ───────────────
      const signalBars = bars.filter(b => b.signal === 'BUY' || b.signal === 'SELL')
      if (markersRef.current && signalBars.length > 0) {
        markersRef.current.setMarkers(
          [...signalBars.map(makeMarker)].sort((a, b) => Number(a.time) - Number(b.time))
        )
      }

      const lastTs  = bars[bars.length - 1].timeMs
      lastDrawnTimeRef.current  = lastTs
      lastQueuedTimeRef.current = lastTs

      // ── X / Y layout ────────────────────────────────────────────────────
      const lastIdx = bars.length - 1
      if (!isLive) {
        // Replay: show last ~150 bars with latest near the right edge.
        // User can press "Fit" to see the entire run.
        chart.timeScale().setVisibleLogicalRange({
          from: Math.max(0, lastIdx - 140),
          to:   lastIdx + 10,
        })
      } else {
        // Initial live snapshot: centre on latest bar
        chart.timeScale().setVisibleLogicalRange({ from: lastIdx - 50, to: lastIdx + 50 })
      }
      chart.priceScale('right').applyOptions({ autoScale: true })

    } else {
      // ──────────────────────────────────────────────────────────────────────
      // INCREMENTAL LIVE PATH: drip-feed new bars, one per animation frame
      // ──────────────────────────────────────────────────────────────────────

      const newBars = bars.filter(b => b.timeMs > lastQueuedTimeRef.current)
      if (newBars.length === 0) return

      pendingRef.current.push(...newBars)
      lastQueuedTimeRef.current = newBars[newBars.length - 1].timeMs

      // If the drip is already running it will consume the newly queued bars
      if (rafRef.current !== null) return

      const drip = () => {
        const b = pendingRef.current.shift()
        if (!b) { rafRef.current = null; return }

        candleRef.current?.update(toCandle(b))
        volumeRef.current?.update(toVolume(b))
        if (b.indicators) {
          for (const [key, val] of Object.entries(b.indicators)) {
            getOrCreateLineSeries(key)?.update({
              time: Math.floor(b.timeMs / 1000) as Time,
              value: val,
            })
          }
        }

        // Accumulate signal markers and keep them sorted
        if (b.signal === 'BUY' || b.signal === 'SELL') {
          liveMarkersRef.current.push(makeMarker(b))
          liveMarkersRef.current.sort((a, c) => Number(a.time) - Number(c.time))
          markersRef.current?.setMarkers(liveMarkersRef.current)
        }

        totalDrawnRef.current++
        lastDrawnTimeRef.current = b.timeMs

        // Re-centre on the latest drawn bar
        const c = chartRef.current
        if (c) {
          const lastIdx = totalDrawnRef.current - 1
          const vis  = c.timeScale().getVisibleLogicalRange()
          const halfW = vis ? Math.max(25, (vis.to - vis.from) / 2) : 50
          c.timeScale().setVisibleLogicalRange({ from: lastIdx - halfW, to: lastIdx + halfW })
          c.priceScale('right').applyOptions({ autoScale: true })
        }

        rafRef.current = requestAnimationFrame(drip)
      }
      rafRef.current = requestAnimationFrame(drip)
    }
  }, [bars, isLive, getOrCreateLineSeries]) // eslint-disable-line react-hooks/exhaustive-deps

  // ── "Fit All" handler ─────────────────────────────────────────────────────
  const fitAll = useCallback(() => {
    chartRef.current?.timeScale().fitContent()
    chartRef.current?.priceScale('right').applyOptions({ autoScale: true })
  }, [])

  // ── Legend data ───────────────────────────────────────────────────────────
  const legend = useMemo(() => {
    const keys = new Set<string>()
    bars.forEach(b => { if (b.indicators) Object.keys(b.indicators).forEach(k => keys.add(k)) })
    return {
      indicatorKeys: [...keys],
      signalCount:   bars.filter(b => b.signal === 'BUY' || b.signal === 'SELL').length,
    }
  }, [bars])

  if (bars.length === 0) return null

  const chartHeight = fullscreen ? 'calc(100vh - 44px)' : height

  return (
    <div style={fullscreen ? {
      position: 'fixed', inset: 0, zIndex: 9000,
      display: 'flex', flexDirection: 'column',
      background: C.bg,
    } : {
      background: C.surface, border: `1px solid ${C.border}`,
      borderRadius: 6, overflow: 'hidden',
    }}>

      {/* ── Legend / toolbar ─────────────────────────────────────────────── */}
      <div style={{
        display: 'flex', alignItems: 'center', gap: 12, padding: '6px 12px',
        borderBottom: `1px solid ${C.border}`, flexWrap: 'wrap', flexShrink: 0,
        background: C.surface2,
      }}>
        {title && (
          <span style={{ fontSize: 11, fontWeight: 700, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.07em', marginRight: 4 }}>
            {title}
          </span>
        )}

        <LegendItem label="Bull"  swatch={<ColorBox color={C.green} />} />
        <LegendItem label="Bear"  swatch={<ColorBox color={C.red}   />} />
        <LegendItem label="Vol"   swatch={<VolSwatch />} />

        {legend.indicatorKeys.map((key, i) => (
          <LegendItem key={key} label={key} swatch={<LineSwatch color={indicatorColor(key, i)} dashed={isDashed(key)} />} />
        ))}

        {legend.signalCount > 0 && (
          <>
            <LegendItem label="Buy"  swatch={<MarkerSwatch color={C.green} arrow="▲" />} />
            <LegendItem label="Sell" swatch={<MarkerSwatch color={C.red}   arrow="▼" />} />
          </>
        )}

        {/* Right-side controls */}
        <span style={{ marginLeft: 'auto', display: 'flex', gap: 6, alignItems: 'center' }}>
          {legend.signalCount > 0 && (
            <span style={{ fontSize: 10, color: C.textMuted }}>
              {legend.signalCount} signal{legend.signalCount !== 1 ? 's' : ''}
            </span>
          )}
          {isLive && (
            <span style={{ fontSize: 10, color: C.green, fontWeight: 700, display: 'flex', alignItems: 'center', gap: 4 }}>
              <span style={{ width: 6, height: 6, borderRadius: '50%', background: C.green, display: 'inline-block' }} />
              LIVE
            </span>
          )}
          <ToolBtn onClick={fitAll} title="Show all bars">Fit All</ToolBtn>
          {onToggleFullscreen && (
            <ToolBtn onClick={onToggleFullscreen} title={fullscreen ? 'Exit fullscreen' : 'Fullscreen'}>
              {fullscreen ? '✕ Exit' : '⤢ Full'}
            </ToolBtn>
          )}
        </span>
      </div>

      {/* ── Chart canvas ─────────────────────────────────────────────────── */}
      <div
        ref={containerRef}
        style={{ width: '100%', height: chartHeight, flex: fullscreen ? 1 : undefined }}
      />
    </div>
  )
}

// ── Small helpers ─────────────────────────────────────────────────────────────

function ToolBtn({ onClick, title, children }: { onClick: () => void; title?: string; children: React.ReactNode }) {
  return (
    <button
      onClick={onClick}
      title={title}
      style={{
        padding: '2px 8px', background: C.surface3, border: `1px solid ${C.border3}`,
        borderRadius: 3, color: C.textSub, fontSize: 10, cursor: 'pointer', fontWeight: 600,
        letterSpacing: '0.03em',
      }}
    >
      {children}
    </button>
  )
}

function LegendItem({ label, swatch }: { label: string; swatch: React.ReactNode }) {
  return (
    <span style={{ display: 'flex', alignItems: 'center', gap: 5, fontSize: 10, color: C.textSub }}>
      {swatch}
      <span>{label}</span>
    </span>
  )
}

function ColorBox({ color }: { color: string }) {
  return (
    <span style={{ width: 8, height: 8, background: color, borderRadius: 2, display: 'inline-block', flexShrink: 0 }} />
  )
}

function VolSwatch() {
  return (
    <span style={{ display: 'flex', gap: 1, alignItems: 'flex-end', height: 10, flexShrink: 0 }}>
      {([6, 10, 7, 9, 5] as number[]).map((h, i) => (
        <span key={i} style={{ width: 3, height: h, background: i % 2 === 0 ? C.green88 : C.red88, display: 'inline-block', borderRadius: 1 }} />
      ))}
    </span>
  )
}

function LineSwatch({ color, dashed }: { color: string; dashed: boolean }) {
  if (dashed) {
    return (
      <span style={{ display: 'inline-flex', alignItems: 'center', gap: 1, flexShrink: 0 }}>
        {([0, 1, 2] as number[]).map(i => (
          <span key={i} style={{ width: 5, height: 2, background: color, borderRadius: 1, display: 'inline-block' }} />
        ))}
      </span>
    )
  }
  return <span style={{ width: 18, height: 2, background: color, borderRadius: 1, display: 'inline-block', flexShrink: 0 }} />
}

function MarkerSwatch({ color, arrow }: { color: string; arrow: string }) {
  return (
    <span style={{ fontSize: 9, color, lineHeight: 1, display: 'inline-block', flexShrink: 0 }}>
      {arrow}
    </span>
  )
}
