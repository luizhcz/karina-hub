export type TimeRange = '1h' | '6h' | '24h' | '7d' | '30d'

export function getFromDate(range: TimeRange): string {
  const now = new Date()
  // Truncate to minute so the value is stable within a render cycle
  // and doesn't cause infinite re-render loops via query key churn.
  now.setSeconds(0, 0)
  const ms: Record<TimeRange, number> = {
    '1h': 3_600_000,
    '6h': 21_600_000,
    '24h': 86_400_000,
    '7d': 604_800_000,
    '30d': 2_592_000_000,
  }
  return new Date(now.getTime() - ms[range]).toISOString()
}

export function toISODate(date: Date): string {
  return date.toISOString().split('T')[0]!
}
