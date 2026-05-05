/**
 * datetime.ts — timezone-aware display helpers.
 *
 * API returns UTC ISO-8601.  Display timezone comes from the broker's
 * market_timezone_id column (read via the strategy/broker context, NOT a global config).
 *
 * For UI pages that are broker-specific (e.g. Live Orders for Zerodha vs IBKR),
 * pass the broker's timezoneId explicitly:
 *
 *   formatMarketDateTime(row.placedAt, broker.marketTimezoneId)
 *   // Zerodha order → "04 May 2026, 09:15:03" (IST)
 *   // IBKR order    → "04 May 2026, 09:30:01" (ET)
 *
 * For pages without broker context, falls back to the browser's local timezone.
 */

// ── Core helper ──────────────────────────────────────────────────────────────

function toZonedDisplay(
  utcIso: string | null | undefined,
  tzId: string,
  opts: Intl.DateTimeFormatOptions
): string {
  if (!utcIso) return '—'
  try {
    return new Date(utcIso).toLocaleString('en-IN', { timeZone: tzId, ...opts })
  } catch {
    return utcIso
  }
}

// ── Public API ────────────────────────────────────────────────────────────────

/**
 * Full date + time in the given broker's market timezone.
 * Example: "04 May 2026, 09:15:03"
 *
 * @param utcIso  - UTC ISO string from API
 * @param tzId    - IANA timezone id from broker_credentials.market_timezone_id
 */
export function formatMarketDateTime(
  utcIso: string | null | undefined,
  tzId: string
): string {
  return toZonedDisplay(utcIso, tzId, {
    day: '2-digit', month: 'short', year: 'numeric',
    hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false,
  })
}

/**
 * Time only (HH:mm:ss) in the broker's market timezone.
 * Used for order timestamps, signal entries, etc.
 */
export function formatMarketTime(
  utcIso: string | null | undefined,
  tzId: string
): string {
  return toZonedDisplay(utcIso, tzId, {
    hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false,
  })
}

/**
 * Short date (dd MMM yyyy) in the broker's market timezone.
 * Used for backtest date ranges, trade date columns, etc.
 */
export function formatMarketDate(
  utcIso: string | null | undefined,
  tzId: string
): string {
  return toZonedDisplay(utcIso, tzId, {
    day: '2-digit', month: 'short', year: 'numeric',
  })
}

/**
 * Current time as HH:mm in the given broker's market timezone.
 * Used to pre-fill session window defaults.
 */
export function marketTimeNow(tzId: string): string {
  return new Date().toLocaleTimeString('en-IN', {
    timeZone: tzId,
    hour: '2-digit', minute: '2-digit', hour12: false,
  })
}

/**
 * Helper: formats using browser local timezone when no broker context is known.
 * Use sparingly — prefer passing tzId explicitly.
 */
export function formatLocalDateTime(utcIso: string | null | undefined): string {
  if (!utcIso) return '—'
  return new Date(utcIso).toLocaleString(undefined, {
    day: '2-digit', month: 'short', year: 'numeric',
    hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false,
  })
}
