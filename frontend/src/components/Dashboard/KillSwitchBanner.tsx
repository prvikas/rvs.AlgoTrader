import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { killSwitchApi } from '../../api/client'
import { useAppStore } from '../../stores/appStore'

export function KillSwitchBanner() {
  const [reason, setReason] = useState('')
  const { killSwitchActive, setKillSwitchActive } = useAppStore()
  const qc = useQueryClient()

  useQuery({
    queryKey: ['kill-switch-status'],
    queryFn: async () => {
      const res = await killSwitchApi.status()
      setKillSwitchActive(res.data.data ?? false)
      return res.data.data
    },
    refetchInterval: 5000,
  })

  const activateMutation = useMutation({
    mutationFn: () => killSwitchApi.activate(reason || 'Manual activation'),
    onSuccess: () => {
      setKillSwitchActive(true)
      qc.invalidateQueries({ queryKey: ['kill-switch-status'] })
    }
  })

  const deactivateMutation = useMutation({
    mutationFn: () => killSwitchApi.deactivate(reason || 'Manual deactivation'),
    onSuccess: () => {
      setKillSwitchActive(false)
      qc.invalidateQueries({ queryKey: ['kill-switch-status'] })
    }
  })

  if (!killSwitchActive) return null

  return (
    <div style={{
      background: '#dc2626',
      color: 'white',
      padding: '12px 24px',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      fontSize: '14px',
      fontWeight: 600,
    }}>
      <span>🛑 KILL SWITCH ACTIVE — All new orders are BLOCKED</span>
      <button
        onClick={() => deactivateMutation.mutate()}
        disabled={deactivateMutation.isPending}
        style={{
          background: 'white',
          color: '#dc2626',
          border: 'none',
          padding: '6px 16px',
          borderRadius: '4px',
          cursor: 'pointer',
          fontWeight: 600
        }}
      >
        {deactivateMutation.isPending ? 'Deactivating...' : 'Deactivate Kill Switch'}
      </button>
    </div>
  )
}
