import { formatInTimeZone, toZonedTime } from 'date-fns-tz'

const IST = 'Asia/Kolkata'

/**
 * Format a UTC ISO string for display in IST.
 * Rule: ALL API timestamps are UTC. Display in IST (UTC+5:30).
 * The "IST" timezone label is appended so users are never confused about which timezone they're seeing.
 */
export function formatIst(utcIso: string | null | undefined, fmt = 'dd MMM HH:mm'): string {
  if (!utcIso) return '--'
  try {
    return `${formatInTimeZone(new Date(utcIso), IST, fmt)} IST`
  } catch {
    return utcIso
  }
}

/** Full date + time in IST. */
export function formatIstFull(utcIso: string | null | undefined): string {
  if (!utcIso) return '--'
  try {
    return `${formatInTimeZone(new Date(utcIso), IST, 'dd MMM yyyy HH:mm:ss')} IST`
  } catch {
    return utcIso ?? '--'
  }
}

/** Date only in IST, no timezone label needed. */
export function formatIstDate(utcIso: string | null | undefined): string {
  if (!utcIso) return '--'
  try {
    return formatInTimeZone(new Date(utcIso), IST, 'dd MMM yyyy')
  } catch {
    return utcIso
  }
}

/** Time only in IST (HH:mm:ss). */
export function formatIstTime(utcIso: string | null | undefined): string {
  if (!utcIso) return '--'
  try {
    return `${formatInTimeZone(new Date(utcIso), IST, 'HH:mm:ss')} IST`
  } catch {
    return utcIso
  }
}

/**
 * Returns true if the current IST time falls within NSE market hours.
 *
 * NSE official hours: 09:15–15:30 IST, Monday–Friday.
 * Note: Indian market holidays are not checked here — those require a backend call to
 * IMarketCalendarService. This check is for UI display purposes only (e.g. "Market Open" indicator).
 * Do NOT use this for order placement decisions on the backend.
 */
export function isMarketHours(): boolean {
  const now = toZonedTime(new Date(), IST)
  const day = now.getDay() // 0=Sun, 6=Sat
  if (day === 0 || day === 6) return false
  const totalMinutes = now.getHours() * 60 + now.getMinutes()
  // 9:15 = 555 minutes, 15:30 = 930 minutes
  return totalMinutes >= 9 * 60 + 15 && totalMinutes <= 15 * 60 + 30
}

/**
 * Format a number as Indian Rupees.
 * Uses 'en-IN' locale: produces "₹1,00,000.00" format (Indian numbering system).
 */
export function formatInr(value: number | null | undefined): string {
  if (value == null) return '--'
  return new Intl.NumberFormat('en-IN', {
    style: 'currency',
    currency: 'INR',
    minimumFractionDigits: 0,
    maximumFractionDigits: 2,
  }).format(value)
}

/**
 * Format a decimal ratio as a percentage string.
 * Input 0.1234 → "12.34%"
 */
export function formatPct(value: number | null | undefined, decimals = 2): string {
  if (value == null) return '--'
  return `${(value * 100).toFixed(decimals)}%`
}
