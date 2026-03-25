import { useState } from 'react'
import { strategiesApi } from '../../api/client'
import { useMutation, useQueryClient } from '@tanstack/react-query'

interface ColdRestartItem {
  instanceId: string
  strategyName: string
  reason: string
}

interface Props {
  coldRestartPaused: ColdRestartItem[]
}

export function ColdRestartBanner({ coldRestartPaused }: Props) {
  const [dismissed, setDismissed] = useState<Set<string>>(new Set())
  const qc = useQueryClient()

  const resumeMutation = useMutation({
    mutationFn: (instanceId: string) => strategiesApi.start(instanceId),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['strategies'] })
  })

  const visible = coldRestartPaused.filter(p => !dismissed.has(p.instanceId))
  if (visible.length === 0) return null

  const dismiss = (instanceId: string) =>
    setDismissed(prev => new Set([...prev, instanceId]))

  return (
    <div
      role="alert"
      aria-live="polite"
      style={{
        background: '#1e3a5f',
        borderBottom: '1px solid #1d4ed8',
        padding: '10px 24px',
      }}
    >
      {/* Header row */}
      <div style={{
        display: 'flex',
        alignItems: 'center',
        gap: 8,
        marginBottom: 8,
      }}>
        <span style={{ fontSize: 14 }} aria-hidden="true">ℹ️</span>
        <span style={{ fontWeight: 700, fontSize: 13, color: '#bfdbfe' }}>
          System restarted — {visible.length} instance{visible.length !== 1 ? 's' : ''} paused
        </span>
        <span style={{ fontSize: 12, color: '#60a5fa', fontWeight: 400 }}>
          (auto-resume was disabled; manual start required)
        </span>
      </div>

      {/* Instance chips */}
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
        {visible.map(p => {
          const isResuming = resumeMutation.isPending && resumeMutation.variables === p.instanceId
          return (
            <div
              key={p.instanceId}
              style={{
                background: '#0f2a50',
                border: '1px solid #1d4ed8',
                borderRadius: 6,
                padding: '5px 10px',
                display: 'flex',
                alignItems: 'center',
                gap: 8,
                fontSize: 13,
              }}
            >
              <span style={{
                color: '#bfdbfe',
                fontWeight: 600,
                maxWidth: 180,
                overflow: 'hidden',
                textOverflow: 'ellipsis',
                whiteSpace: 'nowrap',
              }}
                title={p.strategyName}
              >
                {p.strategyName}
              </span>
              <button
                onClick={() => resumeMutation.mutate(p.instanceId)}
                disabled={isResuming}
                aria-label={`Start ${p.strategyName}`}
                style={{
                  background: isResuming ? '#166534' : '#16a34a',
                  color: 'white',
                  border: 'none',
                  borderRadius: 4,
                  padding: '3px 10px',
                  cursor: isResuming ? 'not-allowed' : 'pointer',
                  fontSize: 12,
                  fontWeight: 600,
                  flexShrink: 0,
                  transition: 'background 0.15s',
                }}
              >
                {isResuming ? 'Starting…' : '▶ Start'}
              </button>
              <button
                onClick={() => dismiss(p.instanceId)}
                aria-label={`Dismiss ${p.strategyName}`}
                title="Dismiss"
                style={{
                  background: 'none',
                  border: '1px solid #1d4ed8',
                  borderRadius: 3,
                  cursor: 'pointer',
                  fontSize: 11,
                  color: '#60a5fa',
                  padding: '2px 5px',
                  lineHeight: 1,
                  flexShrink: 0,
                }}
              >
                ✕
              </button>
            </div>
          )
        })}
      </div>
    </div>
  )
}
