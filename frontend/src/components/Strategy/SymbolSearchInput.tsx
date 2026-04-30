import { useState, useEffect, useRef, useCallback } from 'react'
import { instrumentsApi, Instrument } from '../../api/client'
import { C } from '../../styles/tokens'

interface Props {
  value: string
  onChange: (symbol: string) => void
  placeholder?: string
  disabled?: boolean
}

/**
 * Symbol search input with live autocomplete.
 * Debounces the query by 300ms, calls instrumentsApi.search(), shows a dropdown.
 * On selection the internalSymbol is written into the parent form.
 */
export function SymbolSearchInput({ value, onChange, placeholder = 'e.g. NSE:BANKNIFTY', disabled }: Props) {
  const [query, setQuery]       = useState(value)
  const [results, setResults]   = useState<Instrument[]>([])
  const [open, setOpen]         = useState(false)
  const [loading, setLoading]   = useState(false)
  const [selected, setSelected] = useState(false)   // true once user picks from dropdown
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const wrapperRef  = useRef<HTMLDivElement>(null)

  // Sync external value changes (e.g. form reset)
  useEffect(() => {
    if (value !== query && value === '') { setQuery(''); setSelected(false) }
  }, [value])

  // Close dropdown on outside click
  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (wrapperRef.current && !wrapperRef.current.contains(e.target as Node)) {
        setOpen(false)
      }
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [])

  const doSearch = useCallback((q: string) => {
    if (q.length < 2) { setResults([]); setOpen(false); return }
    setLoading(true)
    instrumentsApi.search(q)
      .then(res => {
        setResults(res.data.data?.items ?? [])
        setOpen(true)
      })
      .catch(() => setResults([]))
      .finally(() => setLoading(false))
  }, [])

  const handleInput = (e: React.ChangeEvent<HTMLInputElement>) => {
    const v = e.target.value
    setQuery(v)
    setSelected(false)
    onChange('')           // clear parent until a valid instrument is selected
    if (debounceRef.current) clearTimeout(debounceRef.current)
    debounceRef.current = setTimeout(() => doSearch(v), 300)
  }

  const handleSelect = (inst: Instrument) => {
    setQuery(inst.internalSymbol)
    onChange(inst.internalSymbol)
    setSelected(true)
    setOpen(false)
    setResults([])
  }

  const inputStyle: React.CSSProperties = {
    width: '100%',
    padding: '8px 10px',
    background: C.bg,
    border: `1px solid ${selected ? C.green : C.border2}`,
    borderRadius: 6,
    color: C.text,
    fontSize: 13,
    boxSizing: 'border-box',
  }

  return (
    <div ref={wrapperRef} style={{ position: 'relative' }}>
      <div style={{ position: 'relative' }}>
        <input
          type="text"
          value={query}
          onChange={handleInput}
          onFocus={() => { if (results.length > 0) setOpen(true) }}
          placeholder={placeholder}
          disabled={disabled}
          style={inputStyle}
          autoComplete="off"
        />
        {loading && (
          <span style={{ position: 'absolute', right: 10, top: '50%', transform: 'translateY(-50%)', fontSize: 11, color: C.textMuted }}>
            ⟳
          </span>
        )}
        {selected && !loading && (
          <span style={{ position: 'absolute', right: 10, top: '50%', transform: 'translateY(-50%)', color: C.green, fontSize: 13 }}>
            ✓
          </span>
        )}
      </div>

      {open && results.length > 0 && (
        <div style={{
          position: 'absolute',
          zIndex: 200,
          top: '100%',
          left: 0,
          right: 0,
          background: C.surface,
          border: `1px solid ${C.blue}`,
          borderRadius: 6,
          maxHeight: 220,
          overflowY: 'auto',
          marginTop: 2,
          boxShadow: '0 4px 20px rgba(0,0,0,0.5)',
        }}>
          {results.map(inst => (
            <div
              key={inst.internalSymbol}
              onMouseDown={() => handleSelect(inst)}
              style={{
                padding: '8px 12px',
                cursor: 'pointer',
                borderBottom: `1px solid ${C.border2}`,
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
              }}
              onMouseEnter={e => (e.currentTarget.style.background = C.surface3)}
              onMouseLeave={e => (e.currentTarget.style.background = 'transparent')}
            >
              <div>
                <span style={{ fontSize: 13, fontWeight: 600, color: C.textSub }}>{inst.tradingSymbol}</span>
                <span style={{ fontSize: 11, color: C.textMuted, marginLeft: 8 }}>{inst.internalSymbol}</span>
              </div>
              <div style={{ display: 'flex', gap: 6 }}>
                <span style={{ fontSize: 10, padding: '2px 6px', borderRadius: 4, background: C.blueBg, color: C.blue }}>
                  {inst.exchange}
                </span>
                {inst.instrumentType && (
                  <span style={{ fontSize: 10, padding: '2px 6px', borderRadius: 4, background: C.surface3, color: C.textSub }}>
                    {inst.instrumentType}
                  </span>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {open && results.length === 0 && !loading && query.length >= 2 && (
        <div style={{
          position: 'absolute', zIndex: 200, top: '100%', left: 0, right: 0,
          background: C.surface, border: `1px solid ${C.border2}`, borderRadius: 6,
          padding: '10px 12px', fontSize: 12, color: C.textMuted, marginTop: 2,
        }}>
          No instruments found for "{query}" — try a shorter query or refresh master data.
        </div>
      )}
    </div>
  )
}
