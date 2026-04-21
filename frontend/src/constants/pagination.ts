// ── Pagination constants ─────────────────────────────────────────────────────

export const PAGE_SIZES = {
  small: 10,
  default: 20,
  medium: 25,
  large: 50,
  xlarge: 100,
  dashboard: 5,
} as const

export const PAGE_SIZE_OPTIONS_STANDARD: { value: string; label: string }[] = [
  { label: '10', value: '10' },
  { label: '20', value: '20' },
  { label: '50', value: '50' },
]

export const PAGE_SIZE_OPTIONS_AUDIT: { value: string; label: string }[] = [
  { value: '25', label: '25' },
  { value: '50', label: '50' },
  { value: '100', label: '100' },
]
