// ── Chart color palette ──────────────────────────────────────────────────────

export const CHART_COLORS = [
  '#0057E0',
  '#10B981',
  '#F59E0B',
  '#EF4444',
  '#7C3AED',
  '#06B6D4',
  '#6B7280',
] as const

/** Status-specific colors for execution charts */
export const STATUS_COLORS = {
  completed: '#10B981',
  failed: '#EF4444',
  running: '#3B82F6',
  pending: '#F59E0B',
  cancelled: '#6B7280',
} as const
