// ── Agent form constants ─────────────────────────────────────────────────────

export const RESPONSE_FORMAT_OPTIONS: { value: string; label: string }[] = [
  { value: 'text', label: 'Text' },
  { value: 'json', label: 'JSON' },
  { value: 'json_schema', label: 'JSON Schema' },
]

export const AGENT_DEFAULTS = {
  temperature: 0.7,
  maxTokens: 4096,
  maxRetries: 3,
  initialDelayMs: 1000,
  backoffMultiplier: 2,
} as const
