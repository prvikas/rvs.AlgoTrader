import React from 'react'
import ReactDOM from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { Dashboard } from './pages/Dashboard'
import { Login } from './pages/Login'
import { useAppStore } from './stores/appStore'
import { isTokenValid } from './utils/auth'

// ── Dev-only preview token ────────────────────────────────────────────────────
// Injected before React mounts so the Zustand store reads it on first init.
// Stripped entirely from production builds via import.meta.env.DEV (Vite dead-code).
if (import.meta.env.DEV && !localStorage.getItem('jwt_token')) {
  const p = btoa(JSON.stringify({ sub: 'dev-preview', exp: 9999999999 }))
  localStorage.setItem('jwt_token', `eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.${p}.dev`)
}

// ── Global Error Boundary ─────────────────────────────────────────────────────
// Prevents blank screen on render errors — shows a recoverable error panel instead.
class ErrorBoundary extends React.Component<
  { children: React.ReactNode },
  { error: Error | null }
> {
  constructor(props: { children: React.ReactNode }) {
    super(props)
    this.state = { error: null }
  }
  static getDerivedStateFromError(error: Error) {
    return { error }
  }
  render() {
    if (this.state.error) {
      return (
        <div style={{
          display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
          minHeight: '100vh', background: '#090910', color: '#e2e8f0', fontFamily: "'Inter', system-ui, sans-serif",
          padding: 32,
        }}>
          <div style={{ maxWidth: 600, width: '100%', background: '#1e1e2e', border: '1px solid #7f1d1d', borderRadius: 10, padding: 28 }}>
            <h2 style={{ color: '#fca5a5', marginTop: 0, fontSize: 18 }}>Something went wrong</h2>
            <pre style={{
              background: '#0f0f1a', color: '#fca5a5', borderRadius: 6,
              padding: 14, fontSize: 12, overflowX: 'auto', whiteSpace: 'pre-wrap', wordBreak: 'break-word',
            }}>
              {this.state.error.message}
              {'\n\n'}
              {this.state.error.stack}
            </pre>
            <button
              onClick={() => { this.setState({ error: null }); window.location.reload() }}
              style={{ marginTop: 16, padding: '8px 20px', background: '#3b82f6', color: '#fff', border: 'none', borderRadius: 6, cursor: 'pointer', fontWeight: 700 }}
            >
              Reload
            </button>
          </div>
        </div>
      )
    }
    return this.props.children
  }
}

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 2,
      staleTime: 30_000,
      refetchOnWindowFocus: false,
    }
  }
})

function ProtectedRoute({ element }: { element: React.ReactElement }) {
  const isAuthenticated = useAppStore(s => s.isAuthenticated)
  return isAuthenticated() ? element : <Navigate to="/login" replace />
}

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <ErrorBoundary>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <Routes>
          <Route path="/login" element={<Login />} />
          <Route path="/" element={<ProtectedRoute element={<Dashboard />} />} />
          <Route path="/dashboard" element={<Navigate to="/" replace />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </BrowserRouter>
    </QueryClientProvider>
    </ErrorBoundary>
  </React.StrictMode>
)
