import { useState } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { newsApi, NewsItem } from '../api/client'
import { C, CONTENT_PAD } from '../styles/tokens'
import { formatIst } from '../utils/datetime'

// ── Sentiment badge ───────────────────────────────────────────────────────────

function SentimentBadge({ sentiment }: { sentiment?: string | null }) {
  if (!sentiment || sentiment === 'Unknown') return null
  const cfg: Record<string, { bg: string; fg: string }> = {
    Positive: { bg: C.greenBg,  fg: C.green  },
    Negative: { bg: C.redBg,    fg: C.red    },
    Neutral:  { bg: C.surface2, fg: C.textSub },
  }
  const s = cfg[sentiment] ?? { bg: C.surface2, fg: C.textSub }
  return (
    <span style={{
      padding: '1px 7px', borderRadius: 3, fontSize: 10, fontWeight: 700,
      background: s.bg, color: s.fg, textTransform: 'uppercase', letterSpacing: '0.05em',
    }}>
      {sentiment}
    </span>
  )
}

// ── Create news form ──────────────────────────────────────────────────────────

function CreateNewsForm({ onDone }: { onDone: () => void }) {
  const qc = useQueryClient()
  const [symbol,    setSymbol]    = useState('')
  const [headline,  setHeadline]  = useState('')
  const [summary,   setSummary]   = useState('')
  const [source,    setSource]    = useState('manual')
  const [url,       setUrl]       = useState('')
  const [sentiment, setSentiment] = useState<string>('Unknown')
  const [tags,      setTags]      = useState('')

  const mut = useMutation({
    mutationFn: () => newsApi.create({
      symbol:      symbol  || null,
      headline,
      summary:     summary || null,
      source,
      url:         url     || null,
      publishedAt: new Date().toISOString(),
      sentiment:   (sentiment as NewsItem['sentiment']) ?? null,
      tags:        tags    || null,
    }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['news'] })
      onDone()
    },
  })

  const inputStyle: React.CSSProperties = {
    background: C.surface2, border: `1px solid ${C.border}`,
    borderRadius: 5, color: C.text, fontSize: 12, padding: '5px 8px', width: '100%',
  }

  return (
    <div style={{
      background: C.surface, border: `1px solid ${C.border}`,
      borderRadius: 8, padding: 16, marginBottom: 16,
    }}>
      <div style={{ fontSize: 12, fontWeight: 700, color: C.textMuted, textTransform: 'uppercase', letterSpacing: '0.06em', marginBottom: 12 }}>
        Add News Item
      </div>
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8, marginBottom: 8 }}>
        <div>
          <div style={{ fontSize: 11, color: C.textMuted, marginBottom: 3 }}>Symbol (optional)</div>
          <input value={symbol} onChange={e => setSymbol(e.target.value.toUpperCase())}
            placeholder="RELIANCE" style={inputStyle} />
        </div>
        <div>
          <div style={{ fontSize: 11, color: C.textMuted, marginBottom: 3 }}>Source</div>
          <input value={source} onChange={e => setSource(e.target.value)}
            placeholder="manual" style={inputStyle} />
        </div>
      </div>
      <div style={{ marginBottom: 8 }}>
        <div style={{ fontSize: 11, color: C.textMuted, marginBottom: 3 }}>Headline *</div>
        <input value={headline} onChange={e => setHeadline(e.target.value)}
          placeholder="Enter headline…" style={inputStyle} />
      </div>
      <div style={{ marginBottom: 8 }}>
        <div style={{ fontSize: 11, color: C.textMuted, marginBottom: 3 }}>Summary</div>
        <textarea value={summary} onChange={e => setSummary(e.target.value)}
          placeholder="Optional detail…" rows={2}
          style={{ ...inputStyle, resize: 'vertical', fontFamily: 'inherit' }} />
      </div>
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: 8, marginBottom: 12 }}>
        <div>
          <div style={{ fontSize: 11, color: C.textMuted, marginBottom: 3 }}>URL</div>
          <input value={url} onChange={e => setUrl(e.target.value)}
            placeholder="https://…" style={inputStyle} />
        </div>
        <div>
          <div style={{ fontSize: 11, color: C.textMuted, marginBottom: 3 }}>Sentiment</div>
          <select value={sentiment} onChange={e => setSentiment(e.target.value)}
            style={{ ...inputStyle }}>
            <option>Unknown</option>
            <option>Positive</option>
            <option>Negative</option>
            <option>Neutral</option>
          </select>
        </div>
        <div>
          <div style={{ fontSize: 11, color: C.textMuted, marginBottom: 3 }}>Tags</div>
          <input value={tags} onChange={e => setTags(e.target.value)}
            placeholder="earnings,results" style={inputStyle} />
        </div>
      </div>
      <div style={{ display: 'flex', gap: 8 }}>
        <button
          onClick={() => mut.mutate()}
          disabled={!headline || mut.isPending}
          style={{
            padding: '5px 16px', borderRadius: 5, border: 'none',
            background: !headline || mut.isPending ? C.surface2 : C.blue,
            color: !headline || mut.isPending ? C.textMuted : '#fff',
            cursor: !headline || mut.isPending ? 'default' : 'pointer', fontSize: 12, fontWeight: 600,
          }}
        >
          {mut.isPending ? 'Saving…' : 'Save'}
        </button>
        <button
          onClick={onDone}
          style={{
            padding: '5px 12px', borderRadius: 5, border: `1px solid ${C.border}`,
            background: 'transparent', color: C.textMuted, cursor: 'pointer', fontSize: 12,
          }}
        >
          Cancel
        </button>
      </div>
    </div>
  )
}

// ── News feed item ────────────────────────────────────────────────────────────

function NewsFeedItem({ item, onDelete }: { item: NewsItem; onDelete: (id: string) => void }) {
  return (
    <div style={{
      background: C.surface, border: `1px solid ${C.border}`,
      borderRadius: 8, padding: 14, display: 'flex', gap: 12,
    }}>
      {/* Left: symbol badge */}
      {item.symbol ? (
        <div style={{
          width: 64, flexShrink: 0, background: C.surface2, borderRadius: 5,
          display: 'flex', alignItems: 'center', justifyContent: 'center',
          fontSize: 10, fontWeight: 700, color: C.blue, letterSpacing: '0.04em',
          padding: '4px 0',
        }}>
          {item.symbol}
        </div>
      ) : (
        <div style={{
          width: 64, flexShrink: 0, background: C.surface2, borderRadius: 5,
          display: 'flex', alignItems: 'center', justifyContent: 'center',
          fontSize: 10, color: C.textMuted,
        }}>
          MARKET
        </div>
      )}

      {/* Content */}
      <div style={{ flex: 1, minWidth: 0 }}>
        <div style={{ display: 'flex', alignItems: 'flex-start', gap: 8, marginBottom: 4 }}>
          <div style={{ fontSize: 13, fontWeight: 600, color: C.text, flex: 1 }}>
            {item.url ? (
              <a href={item.url} target="_blank" rel="noopener noreferrer"
                style={{ color: C.text, textDecoration: 'none' }}
                onMouseEnter={e => (e.currentTarget.style.color = C.blue)}
                onMouseLeave={e => (e.currentTarget.style.color = C.text)}
              >
                {item.headline}
              </a>
            ) : item.headline}
          </div>
          <SentimentBadge sentiment={item.sentiment} />
        </div>

        {item.summary && (
          <div style={{ fontSize: 12, color: C.textSub, marginBottom: 6, lineHeight: 1.5 }}>
            {item.summary}
          </div>
        )}

        <div style={{ display: 'flex', alignItems: 'center', gap: 10, fontSize: 11, color: C.textMuted }}>
          <span>{item.source}</span>
          <span>·</span>
          <span>{formatIst(item.publishedAt)}</span>
          {item.tags && (
            <>
              <span>·</span>
              {item.tags.split(',').map(t => t.trim()).filter(Boolean).map(t => (
                <span key={t} style={{
                  padding: '1px 6px', borderRadius: 3, fontSize: 10,
                  background: C.surface2, color: C.textMuted,
                }}>{t}</span>
              ))}
            </>
          )}
        </div>
      </div>

      {/* Delete */}
      <button
        onClick={() => onDelete(item.id)}
        style={{
          flexShrink: 0, background: 'none', border: 'none',
          color: C.textMuted, cursor: 'pointer', fontSize: 14, padding: '2px 6px',
          borderRadius: 3,
        }}
        title="Delete"
      >
        ✕
      </button>
    </div>
  )
}

// ── Main Page ─────────────────────────────────────────────────────────────────

export function NewsPage() {
  const qc = useQueryClient()
  const [symbol, setSymbol] = useState('')
  const [days,   setDays]   = useState(7)
  const [adding, setAdding] = useState(false)

  const { data, isLoading, isError } = useQuery({
    queryKey: ['news', days, symbol],
    queryFn: () => newsApi.getRecent({ days, symbol: symbol || undefined }).then(r => r.data.data ?? []),
    refetchInterval: 60_000,
  })

  const deleteMut = useMutation({
    mutationFn: (id: string) => newsApi.delete(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['news'] }),
  })

  const items: NewsItem[] = data ?? []

  const inputStyle: React.CSSProperties = {
    background: C.surface2, border: `1px solid ${C.border}`,
    borderRadius: 5, color: C.text, fontSize: 12, padding: '4px 8px',
  }

  return (
    <div style={{ padding: CONTENT_PAD }}>
      {/* Header */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 16 }}>
        <div style={{ fontSize: 18, fontWeight: 700, color: C.text }}>News</div>
        <div style={{ flex: 1 }} />

        {/* Symbol filter */}
        <input
          value={symbol}
          onChange={e => setSymbol(e.target.value.toUpperCase())}
          placeholder="Filter by symbol…"
          style={{ ...inputStyle, width: 160 }}
        />

        {/* Days filter */}
        <select value={days} onChange={e => setDays(Number(e.target.value))} style={inputStyle}>
          <option value={1}>Last 1 day</option>
          <option value={3}>Last 3 days</option>
          <option value={7}>Last 7 days</option>
          <option value={14}>Last 14 days</option>
          <option value={30}>Last 30 days</option>
        </select>

        <button
          onClick={() => setAdding(v => !v)}
          style={{
            padding: '4px 14px', borderRadius: 5, border: `1px solid ${C.blue}`,
            background: adding ? C.blue : 'transparent',
            color: adding ? '#fff' : C.blue,
            cursor: 'pointer', fontSize: 12, fontWeight: 600,
          }}
        >
          {adding ? '× Close' : '+ Add'}
        </button>
      </div>

      {/* Create form */}
      {adding && <CreateNewsForm onDone={() => setAdding(false)} />}

      {/* Feed */}
      {isLoading && (
        <div style={{ color: C.textMuted, fontSize: 13, padding: 32, textAlign: 'center' }}>
          Loading news…
        </div>
      )}

      {isError && (
        <div style={{ color: C.red, fontSize: 13, padding: 32, textAlign: 'center' }}>
          Failed to load news.
        </div>
      )}

      {!isLoading && !isError && items.length === 0 && (
        <div style={{
          color: C.textMuted, fontSize: 13, padding: 48, textAlign: 'center',
          background: C.surface, borderRadius: 8, border: `1px solid ${C.border}`,
        }}>
          No news items for the selected period.{' '}
          <span
            style={{ color: C.blue, cursor: 'pointer' }}
            onClick={() => setAdding(true)}
          >
            Add one manually.
          </span>
        </div>
      )}

      {items.length > 0 && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
          {items.map(item => (
            <NewsFeedItem
              key={item.id}
              item={item}
              onDelete={id => deleteMut.mutate(id)}
            />
          ))}
        </div>
      )}
    </div>
  )
}
