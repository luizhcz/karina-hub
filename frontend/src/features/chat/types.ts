// ── Tipos de mensagens locais (in-flight) ─────────────────────────────────────

export type LocalMsg =
  | { kind: 'optimistic-user'; id: string; text: string }
  | { kind: 'streaming'; msgId: string; content: string }
  | { kind: 'approval'; toolCallId: string; question: string; options: string[] | null; interactionType?: 'Approval' | 'Input' | 'Choice'; resolved?: string; createdAt?: number }
  | { kind: 'tool-call'; toolCallId: string; toolName: string; done: boolean; args?: string; result?: string; startedAt?: number; endedAt?: number }
  | { kind: 'step'; stepId: string; stepName: string; done: boolean; timestamp?: number }
  | { kind: 'error'; text: string }
