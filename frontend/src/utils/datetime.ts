/**
 * datetime.ts — timezone-aware display helpers.
 *
 * The API always returns UTC ISO-8601 strings.  Display times are converted to
 * the market timezone returned by the backend (GET /api/settings/timezone).
 *
 * IMPORTANT: Do NOT hardcode 'Asia/Kolkata' here.  The server is the single
 * source of truth for the market timezone so the UI works for any exchange
 * (India, US, UK, Singapore, etc.) without frontend code changes.
 *
 * Usage:
 *   import { formatMarketTime, formatMarketDate } from '../utils/datetime'
 *   formatMarketTime(row.placedAt)  // → "09:15:03 IST" or "09:30:01 EST" etc.
 */

import { useAppStore } from '../stores/appStore'

// ── Helpers ────────────────────────────────────────────────────────────────────

/**
 * Returns the market IANA timezone from the global app store.
 * Falls back to the browser's local timezone if the server hasn't responded yet.
 */
export function getMarketTimezone(): string {
  return useAppStore.getState().marketTimezone ?? Intl.DateTimeFormat().resolvedOptions().timeZone
}

/**
 * Formats a UTC ISO string as a human-readable date+time in the market timezone.
 * Example output: "04 May 2026, 09:15:03"
 */
export function formatMarketDateTime(utcIso: string | null | undefined): string {
  if (!utcIso) return '—'
  try {
    return new Date(utcIso).toLocaleString('en-IN', {
      timeZone: getMarketTimezone(),
      day:      '2-digit',
      month:    'short',
      year:     'numeric',
      hour:     '2-digit',
      minute:   '2-digit',
      second:   '2-digit',
      hour12:   false,
    })
  } catch {
    return utcIso
  }
}

/**
 * Formats a UTC ISO string as HH:mm:ss in the market timezone.
 * Used for order timestamps, signal journal entries, etc.
 */
export function formatMarketTime(utcIso: string | null | undefined): string {
  if (!utcIso) return '—'
  try {
    return new Date(utcIso).toLocaleTimeString('en-IN', {
      timeZone: getMarketTimezone(),
      hour:     '2-digit',
      minute:   '2-digit',
      second:   '2-digit',
      hour12:   false,
    })
  } catch {
    return utcIso
  }
}

/**
 * Formats a UTC ISO string as a short date (dd MMM yyyy) in the market timezone.
 * Used for table columns, backtest date ranges, etc.
 */
export function formatMarketDate(utcIso: string | null | undefined): string {
  if (!utcIso) return '—'
  try {
    return new Date(utcIso).toLocaleDateString('en-IN', {
      timeZone: getMarketTimezone(),
      day:      '2-digit',
      month:    'short',
      year:     'numeric',
    })
  } catch {
    return utcIso
  }
}

/**
 * Returns "HH:mm" today in the market timezone — used for session window defaults.
 */
export function marketTimeNow(): string {
  return new Date().toLocaleTimeString('en-IN', {
    timeZone: getMarketTimezone(),
    hour:     '2-digit',
    minute:   '2-digit',
    hour12:   false,
  })
}
