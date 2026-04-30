/**
 * BacktestComparisonPanel — side-by-side comparison of backtest vs forward test metrics.
 * Shown inside ForwardTestCard when sourceBacktest is present.
 */

import { BacktestSnapshot, ForwardTestSession } from '../../api/client'
import { C } from '../../styles/tokens'
import { formatInr } from '../../utils/datetime'

interface Props {
  session: ForwardTestSession
  source: BacktestSnapshot
}

interface ComparisonRow {
  label: string
  bt: string
  fwd: string
  delta: string
  deltaGoodWhenPositive: boolean
  btRaw: number
  fwdRaw: number
}

function buildRows(session: ForwardTestSession, source: BacktestSnapshot): ComparisonRow[] {
  const pct = (v: number) => `${(v * 100).toFixed(1)}%`
  const num = (v: number) => v.toFixed(2)

  return [
    {
      label: 'Win Rate',
      bt: pct(source.winRate), fwd: pct(session.winRate),
      delta: `${((session.winRate - source.winRate) * 100).toFixed(1)}pp`,
      deltaGoodWhenPositive: true, btRaw: source.winRate, fwdRaw: session.winRate,
    },
    {
      label: 'Net P&L',
      bt: formatInr(source.totalPnl), fwd: formatInr(session.totalPnl),
      delta: (session.totalPnl - source.totalPnl >= 0 ? '+' : '') + formatInr(session.totalPnl - source.totalPnl),
      deltaGoodWhenPositive: true, btRaw: source.totalPnl, fwdRaw: session.totalPnl,
    },
    {
      label: 'Return',
      bt: pct(source.totalReturn), fwd: pct(session.totalReturn),
      delta: `${((session.totalReturn - source.totalReturn) * 100).toFixed(1)}pp`,
      deltaGoodWhenPositive: true, btRaw: source.totalReturn, fwdRaw: session.totalReturn,
    },
    {
      label: 'Max Drawdown',
      bt: pct(source.maxDrawdown), fwd: pct(session.maxDrawdown),
      delta: `${((session.maxDrawdown - source.maxDrawdown) * 100).toFixed(1)}pp`,
      deltaGoodWhenPositive: false, btRaw: source.maxDrawdown, fwdRaw: session.maxDrawdown,
    },
    {
      label: 'Sharpe Ratio',
      bt: num(source.sharpeRatio), fwd: num(session.sharpeRatio),
      delta: (session.sharpeRatio - source.sharpeRatio >= 0 ? '+' : '') + (session.sharpeRatio - source.sharpeRatio).toFixed(2),
      deltaGoodWhenPositive: true, btRaw: source.sharpeRatio, fwdRaw: session.sharpeRatio,
    },
    {
      label: 'Expectancy/Trade',
      bt: formatInr(source.expectancyPerTrade), fwd: '—',
      delta: '—',
      deltaGoodWhenPositive: true, btRaw: source.expectancyPerTrade, fwdRaw: 0,
    },
    {
      label: 'Trades',
      bt: String(source.totalTrades), fwd: String(session.totalTrades),
      delta: `${session.totalTrades - source.totalTrades >= 0 ? '+' : ''}${session.totalTrades - source.totalTrades}`,
      deltaGoodWhenPositive: true, btRaw: source.totalTrades, fwdRaw: session.totalTrades,
    },
  ]
}

function deltaColor(row: ComparisonRow): string {
  if (row.delta === '—') return C.textMuted
  const better = row.fwdRaw > row.btRaw
  const good = row.deltaGoodWhenPositive ? better : !better
  return good ? C.green : row.fwdRaw === row.btRaw ? C.textMuted : C.red
}

export function BacktestComparisonPanel({ session, source }: Props) {
  const rows = buildRows(session, source)

  return (
    <div style={{ marginTop: 12 }}>
      <div style={{ fontSize: 11, fontWeight: 700, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.06em', marginBottom: 8 }}>
        vs Source Backtest
      </div>
      <div style={{ overflowX: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
          <thead>
            <tr style={{ borderBottom: `1px solid ${C.border2}` }}>
              <th style={{ padding: '6px 10px', textAlign: 'left', color: C.textMuted, fontWeight: 600 }}>Metric</th>
              <th style={{ padding: '6px 10px', textAlign: 'right', color: C.blue, fontWeight: 600 }}>Backtest</th>
              <th style={{ padding: '6px 10px', textAlign: 'right', color: C.green, fontWeight: 600 }}>Forward</th>
              <th style={{ padding: '6px 10px', textAlign: 'right', color: C.textSub, fontWeight: 600 }}>Δ</th>
            </tr>
          </thead>
          <tbody>
            {rows.map(row => (
              <tr key={row.label} style={{ borderBottom: `1px solid ${C.border}` }}>
                <td style={{ padding: '7px 10px', color: C.textSub }}>{row.label}</td>
                <td style={{ padding: '7px 10px', textAlign: 'right', color: C.textSub }}>{row.bt}</td>
                <td style={{ padding: '7px 10px', textAlign: 'right', color: C.text }}>{row.fwd}</td>
                <td style={{ padding: '7px 10px', textAlign: 'right', color: deltaColor(row), fontWeight: 700 }}>{row.delta}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}
