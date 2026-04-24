
/** UI labels for orchestration modes (values come from GET /api/enums) */
export const ORCHESTRATION_MODE_LABELS: Record<string, string> = {
  Sequential: 'Sequential — pipeline em ordem',
  Concurrent: 'Concurrent — todos em paralelo',
  Handoff: 'Handoff — roteamento dinâmico entre agentes',
  GroupChat: 'GroupChat — manager + participantes',
  Graph: 'Graph — grafo dirigido com edges explícitas',
}

/** UI labels for edge types (values come from GET /api/enums) */
export const EDGE_TYPE_LABELS: Record<string, string> = {
  Direct: 'Direct — simple A → B',
  Conditional: 'Conditional — boolean condition',
  Switch: 'Switch — multi-case routing',
  FanOut: 'Fan Out — one → many',
  FanIn: 'Fan In — many → one',
}

/** UI labels for trigger types (values come from GET /api/enums) */
export const TRIGGER_TYPE_LABELS: Record<string, string> = {
  OnDemand: 'On Demand',
  Scheduled: 'Scheduled',
  EventDriven: 'Event Driven',
}

/** Converts enum values array + label map into Select options */
export function enumToOptions(
  values: string[] | undefined,
  labels: Record<string, string>,
): { value: string; label: string }[] {
  if (!values) return Object.entries(labels).map(([value, label]) => ({ value, label }))
  return values.map((v) => ({ value: v, label: labels[v] ?? v }))
}

export const INPUT_MODE_OPTIONS: { value: string; label: string }[] = [
  { label: 'Standalone', value: 'Standalone' },
  { label: 'Chat', value: 'Chat' },
]

export const CHECKPOINT_MODE_OPTIONS: { value: string; label: string }[] = [
  { label: 'In Memory', value: 'InMemory' },
  { label: 'Blob', value: 'Blob' },
]

export const WORKFLOW_DEFAULTS = {
  maxRounds: 10,
  timeoutSeconds: 300,
  checkpointMode: 'InMemory',
  inputMode: 'Standalone',
  cronExpression: '0 9 * * *',
} as const
