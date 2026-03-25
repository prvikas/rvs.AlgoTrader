import { describe, it, expect, vi } from 'vitest';
import { formatIst, isMarketHours, formatInr, formatPct } from '../../utils/datetime';

describe('formatIst', () => {
  it('formats UTC ISO string to IST display with IST label', () => {
    // 2024-01-15T03:45:00Z = 2024-01-15 09:15:00 IST
    const result = formatIst('2024-01-15T03:45:00Z', 'yyyy-MM-dd HH:mm:ss');
    expect(result).toBe('2024-01-15 09:15:00 IST');
  });

  it('handles null/undefined gracefully', () => {
    expect(formatIst(null as any, 'HH:mm')).toBe('--');
    expect(formatIst(undefined as any, 'HH:mm')).toBe('--');
    expect(formatIst('', 'HH:mm')).toBe('--');
  });

  it('formats time-only correctly with IST label', () => {
    // 2024-01-15T09:00:00Z = 14:30 IST
    const result = formatIst('2024-01-15T09:00:00Z', 'HH:mm');
    expect(result).toBe('14:30 IST');
  });
});

describe('isMarketHours', () => {
  it('returns false on Saturday', () => {
    // 2024-01-20 is a Saturday
    vi.setSystemTime(new Date('2024-01-20T05:00:00Z')); // 10:30 IST Saturday
    expect(isMarketHours()).toBe(false);
    vi.useRealTimers();
  });

  it('returns false on Sunday', () => {
    vi.setSystemTime(new Date('2024-01-21T05:00:00Z')); // 10:30 IST Sunday
    expect(isMarketHours()).toBe(false);
    vi.useRealTimers();
  });

  it('returns true during market hours on weekday', () => {
    // 2024-01-15 Monday, 04:00 UTC = 09:30 IST (within 09:15–15:30)
    vi.setSystemTime(new Date('2024-01-15T04:00:00Z'));
    expect(isMarketHours()).toBe(true);
    vi.useRealTimers();
  });

  it('returns false before market open', () => {
    // 2024-01-15 Monday, 03:00 UTC = 08:30 IST (before 09:15)
    vi.setSystemTime(new Date('2024-01-15T03:00:00Z'));
    expect(isMarketHours()).toBe(false);
    vi.useRealTimers();
  });

  it('returns false after market close', () => {
    // 2024-01-15 Monday, 10:30 UTC = 16:00 IST (after 15:30)
    vi.setSystemTime(new Date('2024-01-15T10:30:00Z'));
    expect(isMarketHours()).toBe(false);
    vi.useRealTimers();
  });
});

describe('formatInr', () => {
  it('formats number in Indian locale with ₹ prefix', () => {
    const result = formatInr(1234567.89);
    expect(result).toContain('₹');
    expect(result).toContain('1'); // contains digits
  });

  it('formats zero correctly', () => {
    expect(formatInr(0)).toContain('0');
  });

  it('formats negative correctly', () => {
    const result = formatInr(-5000);
    expect(result).toContain('5');
  });
});

describe('formatPct', () => {
  it('formats fraction as percentage', () => {
    expect(formatPct(0.1234)).toContain('12.34');
  });

  it('handles zero', () => {
    expect(formatPct(0)).toContain('0');
  });

  it('formats negative percentage', () => {
    const result = formatPct(-0.05);
    expect(result).toContain('-');
    expect(result).toContain('5');
  });
});
