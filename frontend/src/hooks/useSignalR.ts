import { useEffect, useRef, useState, useCallback } from 'react'
import * as signalR from '@microsoft/signalr'

interface UseSignalROptions {
  hubUrl: string
  onConnected?: (connection: signalR.HubConnection) => void
  onDisconnected?: () => void
}

export function useSignalR({ hubUrl, onConnected, onDisconnected }: UseSignalROptions) {
  const connectionRef = useRef<signalR.HubConnection | null>(null)
  const [isConnected, setIsConnected] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const token = localStorage.getItem('jwt_token')
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: () => token ?? '',
        transport: signalR.HttpTransportType.WebSockets,
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build()

    connectionRef.current = connection

    connection.onreconnecting(() => setIsConnected(false))
    connection.onreconnected(() => {
      setIsConnected(true)
      onConnected?.(connection)
    })
    connection.onclose(() => {
      setIsConnected(false)
      onDisconnected?.()
    })

    connection.start()
      .then(() => {
        setIsConnected(true)
        setError(null)
        onConnected?.(connection)
      })
      .catch(err => {
        setError(String(err))
        setIsConnected(false)
      })

    return () => {
      connection.stop()
    }
  }, [hubUrl])

  const invoke = useCallback(async (method: string, ...args: unknown[]) => {
    if (connectionRef.current?.state === signalR.HubConnectionState.Connected) {
      return await connectionRef.current.invoke(method, ...args)
    }
  }, [])

  const on = useCallback((event: string, handler: (...args: unknown[]) => void) => {
    connectionRef.current?.on(event, handler)
    return () => connectionRef.current?.off(event, handler)
  }, [])

  return { isConnected, error, invoke, on, connection: connectionRef.current }
}

// Specialized hook for quote streaming
export function useQuoteStream(symbols: string[]) {
  const [quotes, setQuotes] = useState<Record<string, { ltp: number; change: number; volume: number }>>({})
  const symbolsRef = useRef<string[]>([])

  const { isConnected, invoke, on } = useSignalR({
    hubUrl: '/hubs/quotes',
    onConnected: async (conn) => {
      if (symbolsRef.current.length > 0) {
        await conn.invoke('SubscribeToSymbols', symbolsRef.current)
      }
    }
  })

  useEffect(() => {
    symbolsRef.current = symbols
    if (isConnected) {
      invoke('SubscribeToSymbols', symbols)
    }
  }, [symbols, isConnected])

  useEffect(() => {
    const cleanup = on('QuoteUpdate', (symbol: unknown, ltp: unknown, change: unknown, volume: unknown) => {
      setQuotes(prev => ({
        ...prev,
        [symbol as string]: { ltp: ltp as number, change: change as number, volume: volume as number }
      }))
    })
    return cleanup
  }, [on])

  return { quotes, isConnected }
}

// Specialized hook for strategy signals
export function useStrategyStream() {
  const [signals, setSignals] = useState<Array<{
    instanceId: string
    symbol: string
    signal: string
    price?: number
    timestamp: string
  }>>([])
  const [coldRestartPaused, setColdRestartPaused] = useState<Array<{
    instanceId: string
    strategyName: string
    reason: string
  }>>([])

  const { isConnected, on } = useSignalR({ hubUrl: '/hubs/strategies' })

  useEffect(() => {
    const cleanupSignal = on('SignalGenerated', (...args: unknown[]) => {
      const [instanceId, strategyName, symbol, , signal, price, , , , , occurredAt] = args
      setSignals(prev => [{
        instanceId: instanceId as string,
        symbol: symbol as string,
        signal: signal as string,
        price: price as number,
        timestamp: occurredAt as string
      }, ...prev].slice(0, 100))
    })

    const cleanupColdRestart = on('ColdRestartPauseNotification', (...args: unknown[]) => {
      const [instanceId, strategyName, reason] = args
      setColdRestartPaused(prev => [...prev, {
        instanceId: instanceId as string,
        strategyName: strategyName as string,
        reason: reason as string
      }])
    })

    return () => {
      cleanupSignal()
      cleanupColdRestart()
    }
  }, [on])

  return { signals, coldRestartPaused, isConnected }
}
