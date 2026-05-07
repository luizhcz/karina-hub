// Tipos compartilhados entre os módulos JS via JSDoc + // @ts-check.
// Não é compilado — apenas referência pra `npx tsc --noEmit --checkJs` no
// dev local. Reflete os types do backend que circulam pelas APIs.

export interface Identity {
  /** Nome humano-legível pra exibição no header */
  name: string;
  /** Header `x-efs-account` em todas as chamadas */
  account: string;
  /** Header `x-efs-project-id` (escopo das chamadas project-scoped) */
  projectId: string;
  /** Cache do nome do projeto pra display sem refetch */
  projectName: string;
}

// ── Domain types do backend (espelham mvp/src/api/*.ts) ────────────────────

export type AgentStatus = 'Draft' | 'PendingApproval' | 'Approved' | 'Rejected';

export interface AgentDraft {
  id: string;
  payload: Record<string, unknown>;
  status: AgentStatus;
  rejectionFeedback?: string | null;
  createdBy?: string | null;
  createdAt: string;
  updatedAt: string;
  submittedAt?: string | null;
}

export interface Agent {
  id: string;
  name: string;
  description?: string;
  data?: Record<string, unknown>;
  projectId?: string;
  visibility?: 'project' | 'global';
  enabled: boolean;
  originProjectId?: string;
  createdAt: string;
  updatedAt: string;
}

export interface Project {
  id: string;
  name: string;
  tenantId?: string;
  description?: string;
  settings?: Record<string, unknown>;
  budget?: { dailyUsd?: number; monthlyUsd?: number } | null;
  createdAt: string;
  updatedAt: string;
}

// ── Polling fallback HTTP (substitui SSE em clients sem EventSource) ───────

export interface EventPollingItem {
  seq: number;
  type: string;
  payload: Record<string, unknown>;
  occurredAt: string;
}

export interface EventPollingResponse {
  events: EventPollingItem[];
  nextSince: number;
  terminal: boolean;
}

// ── Eval ──────────────────────────────────────────────────────────────────

export type AutoDeployPreset = 'basic' | 'medium' | 'advanced';

export interface AutoDeployResponse {
  runId: string | null;
  testSetVersionId: string | null;
  evaluatorConfigVersionId: string | null;
  preset: AutoDeployPreset;
  caseCount: number;
  estimatedCostUsd: number;
  estimatedDurationSeconds: number;
  status: string | null;
  deduplicatedFromExisting: boolean;
  generatorFailed: boolean;
}
