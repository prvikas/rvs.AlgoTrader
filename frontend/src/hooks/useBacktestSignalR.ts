import { useEffect, useRef, useState } from 'react'
import * as signalR from '@microsoft/signalr'
import { BacktestJobStatus, BacktestChartBar } from '../api/client'

const HUB_URL = '/hubs/backtest'

interface UseBacktestSignalROptions {
  jobId: string | null
  onProgress?: (status: BacktestJobStatus) => void
  onCompleted?: (status: BacktestJobStatus) => void
}

interface UseBacktestSignalRResult {
  /** Rolling 200-bar window updated during the run */
  liveChartBars: BacktestChartBar[]
  isConnected: boolean
}

export function useBacktestSignalR({
  jobId,
  onProgress,
  onCompleted,
}: UseBacktestSignalROptions): UseBacktestSignalRResult {
  const connectionRef = useRef<signalR.HubConnection | null>(null)
  const [isConnected, setIsConnected] = useState(false)
  const [liveChartBars, setLiveChartBars] = useState<BacktestChartBar[]>([])
  const subscribedJobRef = useRef<string | null>(null)

  // Build / tear down the SignalR connection once
  useEffect(() => {
    const token = localStorage.getItem('jwt_token')
    const conn = new signalR.HubConnectionBuilder()
      .withUrl(HUB_URL, {
        accessTokenFactory: () => token ?? '',
        transport: signalR.HttpTransportType.WebSockets,
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000])
      .configureLogging(signalR.LogLevel.Warning)
      .build()

    connectionRef.current = conn

    conn.onreconnected(() => {
      setIsConnected(true)
      // Re-subscribe if we had an active job
      if (subscribedJobRef.current) {
        conn.invoke('SubscribeToJob', subscribedJobRef.current).catch(() => {})
      }
    })
    conn.onclose(() => setIsConnected(false))

    // BacktestProgress — lean status (bar counts, equity)
    conn.on('BacktestProgress', (status: BacktestJobStatus) => {
      onProgress?.(status)
    })

    // BacktestChartUpdate — rolling 200-bar window
    conn.on('BacktestChartUpdate', (payload: { jobId: string; bars: BacktestChartBar[] }) => {
      if (!payload?.bars?.length) return
      setLiveChartBars(payload.bars)
    })

    // BacktestCompleted — final status (includes result.chartSample)
    conn.on('BacktestCompleted', (status: BacktestJobStatus) => {
      onCompleted?.(status)
      // Clear live bars once completed (full chart comes from result.chartSample)
      setLiveChartBars([])
      subscribedJobRef.current = null
    })

    conn.start()
      .then(() => setIsConnected(true))
      .catch(() => setIsConnected(false))

    return () => {
      conn.stop()
    }
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  // Subscribe / unsubscribe when jobId changes
  useEffect(() => {
    const conn = connectionRef.current
    if (!conn) return

    // Unsubscribe from previous job
    if (subscribedJobRef.current && subscribedJobRef.current !== jobId) {
      if (conn.state === signalR.HubConnectionState.Connected) {
        conn.invoke('UnsubscribeFromJob', subscribedJobRef.current).catch(() => {})
      }
      subscribedJobRef.current = null
    }

    if (!jobId) {
      setLiveChartBars([])
      return
    }

    // Subscribe to new job (immediately if connected, else on reconnect handler above)
    if (conn.state === signalR.HubConnectionState.Connected) {
      conn.invoke('SubscribeToJob', jobId).catch(() => {})
      subscribedJobRef.current = jobId
    } else {
      subscribedJobRef.current = jobId  // reconnect handler will call SubscribeToJob
    }

    setLiveChartBars([])  // clear previous run's data
  }, [jobId])

  return { liveChartBars, isConnected }
}
