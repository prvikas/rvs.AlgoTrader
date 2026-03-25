import { useMemo } from 'react'
import {
  AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip,
  ResponsiveContainer, ReferenceLine,
} from 'recharts'
import { BacktestTradeResult } from '../../api/client'
import { formatInr } from '../../utils/datetime'
import { formatInTimeZone } from 'date-fns-tz'

interface Props {
  trades: BacktestTradeResult[]
  initialCapital: number
}

interface CurvePoint {
  label: string
  equity: number
  pnl: number
}

// Custom tooltip
function ChartTooltip({ active, payload }: { active?: boolean; payload?: any[] }) {
  if (!active || !payload?.length) return null
  const d = payload[0].payload as CurvePoint
  const color = d.pnl >= 0 ? '#10b981' : '#ef4444'
  return (
    <div style={{
      background: '#1e1e2e', border: '1px solid #2d2d3f',
      borderRadius: 6, padding: '8px 12px', fontSize: 12,
    }}>
      <div style={{ color: '#8b8b9f', marginBottom: 4 }}>{d.label}</div>
      <div style={{ color: '#e2e8f0', fontWeight: 700 }}>Equity: {formatInr(d.equity)}</div>
      <div style={{ color, fontWeight: 600 }}>P&amp;L: {d.pnl >= 0 ? '+' : ''}{formatInr(d.pnl)}</div>
    </div>
  )
}

export function EquityCurveChart({ trades, initialCapital }: Props) {
  const data = useMemo<CurvePoint[]>(() => {
    let equity = initialCapital
    return [
      { label: 'Start', equity: initialCapital, pnl: 0 },
      ...trades.map((t, i) => {
        equity += t.netPnl
        const label = t.entryTime
          ? formatInTimeZone(new Date(t.entryTime), 'Asia/Kolkata', 'dd MMM')
          : `T${i + 1}`
        return { label, equity, pnl: equity - initialCapital }
      }),
    ]
  }, [trades, initialCapital])

  const minEquity = Math.min(...data.map(d => d.equity))
  const maxEquity = Math.max(...data.map(d => d.equity))
  const padding = (maxEquity - minEquity) * 0.1 || initialCapital * 0.05

  return (
    <div style={{ width: '100%', height: 200 }}>
      <ResponsiveContainer>
        <AreaChart data={data} margin={{ top: 4, right: 8, bottom: 0, left: 8 }}>
          <defs>
            <linearGradient id="equityGrad" x1="0" y1="0" x2="0" y2="1">
              <stop offset="5%"  stopColor="#3b82f6" stopOpacity={0.25} />
              <stop offset="95%" stopColor="#3b82f6" stopOpacity={0.02} />
            </linearGradient>
          </defs>
          <CartesianGrid strokeDasharray="3 3" stroke="#2d2d3f" vertical={false} />
          <XAxis
            dataKey="label"
            tick={{ fill: '#64748b', fontSize: 10 }}
            tickLine={false}
            axisLine={false}
            interval="preserveStartEnd"
          />
          <YAxis
            domain={[minEquity - padding, maxEquity + padding]}
            tick={{ fill: '#64748b', fontSize: 10 }}
            tickLine={false}
            axisLine={false}
            tickFormatter={v => `₹${(v / 1000).toFixed(0)}k`}
            width={52}
          />
          <Tooltip content={<ChartTooltip />} />
          <ReferenceLine y={initialCapital} stroke="#475569" strokeDasharray="4 2" />
          <Area
            type="monotone"
            dataKey="equity"
            stroke="#3b82f6"
            strokeWidth={2}
            fill="url(#equityGrad)"
            dot={false}
            activeDot={{ r: 4, fill: '#3b82f6' }}
          />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  )
}
