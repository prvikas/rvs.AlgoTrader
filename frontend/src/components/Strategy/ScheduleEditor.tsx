import { useState } from 'react'

/** Mirrors schedule_json DB column — CLAUDE.md Rule #15 (IST only for trading schedule). */
export interface ScheduleConfig {
  days: string[]             // ["MON","TUE","WED","THU","FRI"]
  session_start: string      // "HH:MM" IST — e.g. "09:20"
  session_stop: string       // "HH:MM" IST — e.g. "15:10"
  timezone: 'Asia/Kolkata'   // ALWAYS "Asia/Kolkata" — CLAUDE.md Rule #15
  auto_resume_on_restart: boolean
  missed_session_behavior: 'SKIP' | 'START_LATE'
  force_exit_on_session_end: boolean
}

const DEFAULT_SCHEDULE: ScheduleConfig = {
  days: ['MON', 'TUE', 'WED', 'THU', 'FRI'],
  session_start: '09:20',
  session_stop: '15:10',
  timezone: 'Asia/Kolkata',
  auto_resume_on_restart: false,   // safer default — explicit manual restart required
  missed_session_behavior: 'SKIP',
  force_exit_on_session_end: true,
}

const ALL_DAYS = ['MON', 'TUE', 'WED', 'THU', 'FRI'] as const
const DAY_LABELS: Record<string, string> = {
  MON: 'Mon', TUE: 'Tue', WED: 'Wed', THU: 'Thu', FRI: 'Fri',
}

interface Props {
  value?: ScheduleConfig
  onChange: (cfg: ScheduleConfig) => void
}

const inp: React.CSSProperties = {
  padding: '6px 10px',
  background: '#0f0f1a',
  border: '1px solid #2d2d3f',
  borderRadius: 6,
  color: '#e2e8f0',
  fontSize: 13,
}

const label12: React.CSSProperties = {
  fontSize: 12,
  fontWeight: 600,
  color: '#94a3b8',
  display: 'block',
  marginBottom: 6,
}

/**
 * Converts "HH:MM" IST string to the user's local browser timezone.
 * IST = UTC+5:30 → subtract 5h30m to get UTC, then format in local tz.
 * Date.UTC handles negative components by borrowing (e.g. h=3, m=-10 → h=2, m=50).
 */
function istToLocal(timeStr: string): string {
  try {
    const [h, m] = timeStr.split(':').map(Number)
    // IST = UTC+5:30, so UTC = IST − 5:30
    const utcMs = Date.UTC(2000, 0, 3, h - 5, m - 30)
    return new Date(utcMs).toLocaleTimeString(undefined, {
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
    })
  } catch {
    return '?'
  }
}

/** Format the days list as a readable summary, e.g. "Mon–Fri" or "Mon, Wed, Fri" */
function formatDaysSummary(days: string[]): string {
  if (days.length === 5 && ALL_DAYS.every(d => days.includes(d))) return 'Mon–Fri'
  if (days.length === 0) return 'No days selected'
  return days.map(d => DAY_LABELS[d] ?? d).join(', ')
}

/**
 * Schedule editor for strategy instances.
 * Produces a ScheduleConfig (= schedule_json) for the create/edit form.
 * All times are ALWAYS in IST — CLAUDE.md Rule #15.
 * Shows a live IST → user-local conversion hint beneath each time field.
 */
export function ScheduleEditor({ value, onChange }: Props) {
  const [cfg, setCfg] = useState<ScheduleConfig>(value ?? DEFAULT_SCHEDULE)

  const update = (patch: Partial<ScheduleConfig>) => {
    const next = { ...cfg, ...patch }
    setCfg(next)
    onChange(next)
  }

  const toggleDay = (day: string) => {
    const next = cfg.days.includes(day)
      ? cfg.days.filter(d => d !== day)
      : [...cfg.days, day]
    // Maintain canonical MON→FRI order
    update({ days: ALL_DAYS.filter(d => next.includes(d)) })
  }

  const localTz = Intl.DateTimeFormat().resolvedOptions().timeZone

  return (
    <div style={{
      background: '#161628',
      border: '1px solid #2d2d3f',
      borderRadius: 8,
      padding: 16,
    }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 14 }}>
        <h4 style={{ margin: 0, fontSize: 13, fontWeight: 700, color: '#93c5fd' }}>
          Trading Schedule
        </h4>
        <span style={{ fontSize: 11, color: '#64748b' }}>All times in IST (Asia/Kolkata)</span>
      </div>

      {/* Days of week */}
      <div style={{ marginBottom: 14 }}>
        <label style={label12}>Active Days</label>
        <div style={{ display: 'flex', gap: 6 }} role="group" aria-label="Active trading days">
          {ALL_DAYS.map(day => {
            const active = cfg.days.includes(day)
            return (
              <button
                key={day}
                type="button"
                onClick={() => toggleDay(day)}
                aria-pressed={active}
                aria-label={`${DAY_LABELS[day]} — ${active ? 'active, click to remove' : 'inactive, click to add'}`}
                style={{
                  padding: '5px 10px',
                  borderRadius: 6,
                  border: '2px solid',
                  fontSize: 12,
                  fontWeight: 700,
                  cursor: 'pointer',
                  transition: 'all 0.15s',
                  outline: 'none',
                  borderColor: active ? '#3b82f6' : '#2d2d3f',
                  background: active ? '#1e3a5f' : '#0f0f1a',
                  color: active ? '#93c5fd' : '#475569',
                }}
                onFocus={e => (e.currentTarget.style.boxShadow = '0 0 0 2px #3b82f660')}
                onBlur={e => (e.currentTarget.style.boxShadow = '')}
              >
                {DAY_LABELS[day]}
              </button>
            )
          })}
        </div>
      </div>

      {/* Session times */}
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12, marginBottom: 14 }}>
        <div>
          <label style={label12} htmlFor="schedule-start">Session Start (IST)</label>
          <input
            id="schedule-start"
            type="time"
            value={cfg.session_start}
            onChange={e => update({ session_start: e.target.value })}
            style={{ ...inp, width: '100%', boxSizing: 'border-box' }}
          />
          <span style={{ fontSize: 11, color: '#64748b', marginTop: 4, display: 'block' }}>
            {istToLocal(cfg.session_start)} {localTz}
          </span>
        </div>
        <div>
          <label style={label12} htmlFor="schedule-stop">Session Stop (IST)</label>
          <input
            id="schedule-stop"
            type="time"
            value={cfg.session_stop}
            onChange={e => update({ session_stop: e.target.value })}
            style={{ ...inp, width: '100%', boxSizing: 'border-box' }}
          />
          <span style={{ fontSize: 11, color: '#64748b', marginTop: 4, display: 'block' }}>
            {istToLocal(cfg.session_stop)} {localTz}
          </span>
        </div>
      </div>

      {/* Behavioral toggles */}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
        <ToggleRow
          label="Force exit all positions at session end"
          hint={`Closes all open trades automatically at ${cfg.session_stop} IST`}
          checked={cfg.force_exit_on_session_end}
          onChange={v => update({ force_exit_on_session_end: v })}
          accent="#f87171"
        />
        <ToggleRow
          label="Auto-resume on system restart"
          hint="Off = instance stays paused after restart and needs a manual Start (safer for live trading)"
          checked={cfg.auto_resume_on_restart}
          onChange={v => update({ auto_resume_on_restart: v })}
          accent="#fbbf24"
        />
        <div>
          <label style={label12} htmlFor="missed-session">
            If session window has already started at startup
          </label>
          <select
            id="missed-session"
            value={cfg.missed_session_behavior}
            onChange={e => update({ missed_session_behavior: e.target.value as 'SKIP' | 'START_LATE' })}
            style={{ ...inp, width: '100%' }}
          >
            <option value="SKIP">SKIP — wait for next scheduled session (safer)</option>
            <option value="START_LATE">START_LATE — begin trading immediately with a warning</option>
          </select>
        </div>
      </div>

      {/* Summary */}
      <div style={{
        marginTop: 14,
        padding: '8px 12px',
        background: '#0f172a',
        borderRadius: 6,
        fontSize: 12,
        color: '#64748b',
        lineHeight: 1.6,
      }}>
        <strong style={{ color: '#94a3b8' }}>Summary: </strong>
        {formatDaysSummary(cfg.days)}
        {' · '}
        {cfg.session_start}–{cfg.session_stop} IST
        {cfg.force_exit_on_session_end ? ' · auto-exit at stop' : ''}
        {cfg.auto_resume_on_restart ? ' · auto-resumes' : ' · manual restart'}
        {' · missed session: '}
        {cfg.missed_session_behavior === 'SKIP' ? 'skip' : 'start late'}
      </div>
    </div>
  )
}

function ToggleRow({
  label, hint, checked, onChange, accent = '#3b82f6',
}: { label: string; hint: string; checked: boolean; onChange: (v: boolean) => void; accent?: string }) {
  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault()
      onChange(!checked)
    }
  }

  return (
    <div>
      <div
        role="switch"
        aria-checked={checked}
        tabIndex={0}
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          cursor: 'pointer',
          outline: 'none',
          borderRadius: 4,
        }}
        onClick={() => onChange(!checked)}
        onKeyDown={handleKeyDown}
        onFocus={e => (e.currentTarget.style.boxShadow = '0 0 0 2px #3b82f640')}
        onBlur={e => (e.currentTarget.style.boxShadow = '')}
      >
        <div>
          <div style={{ fontSize: 12, fontWeight: 600, color: '#e2e8f0' }}>{label}</div>
          <div style={{ fontSize: 11, color: '#64748b', marginTop: 2 }}>{hint}</div>
        </div>
        {/* Toggle pill */}
        <div
          aria-hidden="true"
          style={{
            width: 40, height: 22, borderRadius: 11,
            background: checked ? accent : '#2d2d3f',
            position: 'relative',
            transition: 'background 0.2s',
            flexShrink: 0,
            marginLeft: 16,
          }}
        >
          <div style={{
            position: 'absolute',
            top: 3, left: checked ? 21 : 3,
            width: 16, height: 16,
            borderRadius: '50%',
            background: '#fff',
            transition: 'left 0.2s',
            boxShadow: '0 1px 3px rgba(0,0,0,0.4)',
          }} />
        </div>
      </div>
    </div>
  )
}

export const defaultScheduleJson = () => JSON.stringify(DEFAULT_SCHEDULE)
