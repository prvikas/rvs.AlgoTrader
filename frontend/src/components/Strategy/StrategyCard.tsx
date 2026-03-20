import { useMutation, useQueryClient } from '@tanstack/react-query'
import { StrategyInstance, strategiesApi } from '../../api/client'
import { formatIst, formatInr } from '../../utils/datetime'

const STATUS_COLORS: Record<string, string> = {
  Draft: '#94a3b8',
  Running: '#16a34a',
  Paused: '#f59e0b',
  Stopped: '#6b7280',
  Scheduled: '#3b82f6',
  Error: '#dc2626',
}

interface Props {
  instance: StrategyInstance
}

export function StrategyCard({ instance }: Props) {
  const qc = useQueryClient()

  const startMutation = useMutation({
    mutationFn: () => strategiesApi.start(instance.id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['strategies'] })
  })

  const stopMutation = useMutation({
    mutationFn: () => strategiesApi.stop(instance.id, 'MANUAL'),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['strategies'] })
  })

  const pauseMutation = useMutation({
    mutationFn: () => strategiesApi.pause(instance.id, 'MANUAL'),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['strategies'] })
  })

  const isRunning = instance.status === 'Running'
  const isPaused = instance.status === 'Paused'
  const statusColor = STATUS_COLORS[instance.status] ?? '#6b7280'

  return (
    <div style={{
      background: '#1e1e2e',
      border: '1px solid #2d2d3f',
      borderRadius: 8,
      padding: 16,
      minWidth: 280,
    }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: 8 }}>
        <div>
          <div style={{ fontWeight: 700, fontSize: 15, color: '#e2e8f0' }}>{instance.name}</div>
          <div style={{ fontSize: 12, color: '#94a3b8', marginTop: 2 }}>
            {instance.strategyType} · {instance.internalSymbol} · {instance.timeframe}
          </div>
        </div>
        <span style={{
          background: statusColor + '22',
          color: statusColor,
          borderRadius: 12,
          padding: '2px 10px',
          fontSize: 11,
          fontWeight: 700,
        }}>
          {instance.status}
        </span>
      </div>

      <div style={{ display: 'flex', gap: 16, marginBottom: 12, fontSize: 12, color: '#94a3b8' }}>
        <span>Broker: <strong style={{ color: '#e2e8f0' }}>{instance.brokerName}</strong></span>
        <span>Mode: <strong style={{ color: '#e2e8f0' }}>{instance.mode}</strong></span>
        {instance.allocatedCapital && (
          <span>Capital: <strong style={{ color: '#e2e8f0' }}>{formatInr(instance.allocatedCapital)}</strong></span>
        )}
      </div>

      <div style={{ display: 'flex', gap: 8 }}>
        {!isRunning && (
          <button
            onClick={() => startMutation.mutate()}
            disabled={startMutation.isPending}
            style={{
              background: '#16a34a',
              color: 'white',
              border: 'none',
              borderRadius: 4,
              padding: '6px 14px',
              cursor: 'pointer',
              fontSize: 12,
              fontWeight: 600
            }}
          >
            {startMutation.isPending ? '...' : '▶ Start'}
          </button>
        )}
        {isRunning && (
          <button
            onClick={() => pauseMutation.mutate()}
            disabled={pauseMutation.isPending}
            style={{
              background: '#f59e0b',
              color: '#1c1917',
              border: 'none',
              borderRadius: 4,
              padding: '6px 14px',
              cursor: 'pointer',
              fontSize: 12,
              fontWeight: 600
            }}
          >
            {pauseMutation.isPending ? '...' : '⏸ Pause'}
          </button>
        )}
        {(isRunning || isPaused) && (
          <button
            onClick={() => stopMutation.mutate()}
            disabled={stopMutation.isPending}
            style={{
              background: '#dc2626',
              color: 'white',
              border: 'none',
              borderRadius: 4,
              padding: '6px 14px',
              cursor: 'pointer',
              fontSize: 12,
              fontWeight: 600
            }}
          >
            {stopMutation.isPending ? '...' : '⏹ Stop'}
          </button>
        )}
      </div>
    </div>
  )
}
