import { formatInTimeZone, toZonedTime } from 'date-fns-tz'
import { format } from 'date-fns'

const IST = 'Asia/Kolkata'

/**
 * Format a UTC ISO string for display in IST.
 * Rule: ALL API timestamps are UTC. Display in IST (UTC+5:30).
 */
export function formatIst(utcIso: string | null | undefined, fmt = 'dd MMM HH:mm:ss'): string {
  if (!utcIso) return '--'
  try {
    return formatInTimeZone(new Date(utcIso), IST, fmt)
  } catch {
    return utcIso
  }
}

export function formatIstDate(utcIso: string | null | undefined): string {
  return formatIst(utcIso, 'dd MMM yyyy')
}

export function formatIstTime(utcIso: string | null | undefined): string {
  return formatIst(utcIso, 'HH:mm:ss')
}

export function formatIstFull(utcIso: string | null | undefined): string {
  return formatIst(utcIso, 'dd MMM yyyy HH:mm:ss')
}

/** Returns true if current IST time is within market hours (9:15-15:30, Mon-Fri) */
export function isMarketHours(): boolean {
  const now = toZonedTime(new Date(), IST)
  const day = now.getDay() // 0=Sun, 6=Sat
  if (day === 0 || day === 6) return false
  const hours = now.getHours()
  const minutes = now.getMinutes()
  const totalMinutes = hours * 60 + minutes
  return totalMinutes >= 9 * 60 + 15 && totalMinutes <= 15 * 60 + 30
}

/** Format as INR currency */
export function formatInr(value: number | null | undefined): string {
  if (value == null) return '--'
  return new Intl.NumberFormat('en-IN', {
    style: 'currency',
    currency: 'INR',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value)
}

/** Format percentage */
export function formatPct(value: number | null | undefined, decimals = 2): string {
  if (value == null) return '--'
  return `${(value * 100).toFixed(decimals)}%`
}
