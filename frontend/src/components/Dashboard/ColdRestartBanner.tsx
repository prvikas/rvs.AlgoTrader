import { useState } from 'react'
import { useStrategyStream } from '../../hooks/useSignalR'
import { strategiesApi } from '../../api/client'
import { useMutation, useQueryClient } from '@tanstack/react-query'

export function ColdRestartBanner() {
  const { coldRestartPaused } = useStrategyStream()
  const [dismissed, setDismissed] = useState<Set<string>>(new Set())
  const qc = useQueryClient()

  const resumeMutation = useMutation({
    mutationFn: (instanceId: string) => strategiesApi.start(instanceId),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['strategies'] })
  })

  const visible = coldRestartPaused.filter(p => !dismissed.has(p.instanceId))
  if (visible.length === 0) return null

  return (
    <div style={{
      background: '#f59e0b',
      color: '#1c1917',
      padding: '12px 24px',
      borderBottom: '1px solid #d97706'
    }}>
      <div style={{ fontWeight: 700, marginBottom: 8 }}>
        ⚠️ Cold Restart — {visible.length} strategy instance(s) paused and require manual restart:
      </div>
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
        {visible.map(p => (
          <div key={p.instanceId} style={{
            background: 'white',
            borderRadius: 6,
            padding: '6px 12px',
            display: 'flex',
            alignItems: 'center',
            gap: 8,
            fontSize: 13
          }}>
            <span>{p.strategyName}</span>
            <button
              onClick={() => resumeMutation.mutate(p.instanceId)}
              style={{
                background: '#16a34a',
                color: 'white',
                border: 'none',
                borderRadius: 4,
                padding: '3px 10px',
                cursor: 'pointer',
                fontSize: 12
              }}
            >
              Resume
            </button>
            <button
              onClick={() => setDismissed(prev => new Set([...prev, p.instanceId]))}
              style={{ background: 'none', border: 'none', cursor: 'pointer', fontSize: 12 }}
            >
              ✕
            </button>
          </div>
        ))}
      </div>
    </div>
  )
}
