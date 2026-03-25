import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { killSwitchApi } from '../../api/client'
import { useAppStore } from '../../stores/appStore'

export function KillSwitchBanner() {
  const { killSwitchActive, setKillSwitchActive } = useAppStore()
  const qc = useQueryClient()

  useQuery({
    queryKey: ['kill-switch-status'],
    queryFn: async () => {
      const res = await killSwitchApi.status()
      setKillSwitchActive(res.data.data ?? false)
      return res.data.data
    },
    // Kill-switch is a rare event — poll every 30s to avoid hammering the server.
    // When activated, we get immediate update via SignalR; this is just a fallback.
    refetchInterval: 30_000,
  })

  const deactivateMutation = useMutation({
    mutationFn: () => killSwitchApi.deactivate('Manual deactivation'),
    onSuccess: () => {
      setKillSwitchActive(false)
      qc.invalidateQueries({ queryKey: ['kill-switch-status'] })
    }
  })

  if (!killSwitchActive) return null

  return (
    <div
      role="alert"
      aria-live="assertive"
      style={{
        background: '#991b1b',
        borderBottom: '3px solid #7f1d1d',
        color: 'white',
        padding: '10px 24px',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        gap: 16,
        fontSize: '14px',
        fontWeight: 600,
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
        {/* Pulsing indicator */}
        <span style={{
          display: 'inline-block',
          width: 10,
          height: 10,
          borderRadius: '50%',
          background: '#fca5a5',
          boxShadow: '0 0 0 3px rgba(252,165,165,0.3)',
          flexShrink: 0,
        }} aria-hidden="true" />
        <span>
          KILL SWITCH ACTIVE
          <span style={{ fontWeight: 400, marginLeft: 8, color: '#fecaca' }}>
            — All new orders are blocked
          </span>
        </span>
      </div>

      <button
        onClick={() => deactivateMutation.mutate()}
        disabled={deactivateMutation.isPending}
        aria-label="Deactivate kill switch and resume trading"
        aria-disabled={deactivateMutation.isPending}
        style={{
          background: 'white',
          color: '#991b1b',
          border: 'none',
          padding: '6px 18px',
          borderRadius: '4px',
          cursor: deactivateMutation.isPending ? 'not-allowed' : 'pointer',
          fontWeight: 700,
          fontSize: 13,
          flexShrink: 0,
          opacity: deactivateMutation.isPending ? 0.7 : 1,
          transition: 'opacity 0.15s',
        }}
      >
        {deactivateMutation.isPending ? 'Deactivating…' : 'Deactivate'}
      </button>
    </div>
  )
}
