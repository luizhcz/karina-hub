// ── Gauge threshold constants ────────────────────────────────────────────────

export const LATENCY_GAUGE = {
  maxMs: 10_000,
  thresholds: { green: 2000, yellow: 5000 },
} as const

export const ACTIVE_SLOTS_GAUGE = {
  maxSlots: 20,
  thresholds: { green: 10, yellow: 16 },
} as const
