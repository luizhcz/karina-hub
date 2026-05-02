import type { AgentDraftPayload, AgentDraft } from '../../api/agentDrafts'
import type { AgentDef, CreateAgentRequest } from '../../api/agents'

/**
 * Converte o body de CreateAgent (output canônico do form) pra AgentDraftPayload.
 * Drafts aceitam todos os campos como opcionais; mantemos os mesmos valores aqui
 * pra round-trip fiel quando o user "salva como rascunho" um agent já preenchido.
 */
export function requestToDraftPayload(req: CreateAgentRequest): AgentDraftPayload {
  return {
    name: req.name,
    description: req.description,
    model: req.model,
    provider: req.provider,
    instructions: req.instructions,
    tools: req.tools,
    structuredOutput: req.structuredOutput,
    middlewares: req.middlewares,
    resilience: req.resilience,
    costBudget: req.costBudget,
    skillRefs: req.skillRefs,
    metadata: req.metadata,
    visibility: req.visibility,
    allowedProjectIds: req.allowedProjectIds,
  }
}

/**
 * Reconstrói um AgentDef "fake" a partir do draft, pra alimentar AgentForm.initialValues
 * sem ter que desacoplar o form. Campos faltantes recebem defaults seguros que o
 * zod do form aceita (deploymentName="", name=""). O id vem do próprio draft.
 */
export function draftToAgentDef(draft: AgentDraft): AgentDef {
  const p = draft.payload
  return {
    id: draft.id,
    name: p.name ?? draft.name ?? '',
    description: p.description,
    model: p.model ?? { deploymentName: '' },
    provider: p.provider,
    fallbackProvider: p.fallbackProvider,
    instructions: p.instructions,
    tools: p.tools,
    structuredOutput: p.structuredOutput,
    middlewares: p.middlewares,
    resilience: p.resilience,
    costBudget: p.costBudget,
    skillRefs: p.skillRefs,
    metadata: p.metadata,
    visibility: p.visibility,
    originProjectId: draft.projectId,
    originTenantId: draft.tenantId,
    allowedProjectIds: p.allowedProjectIds,
    enabled: p.enabled,
    createdAt: draft.createdAt,
    updatedAt: draft.updatedAt,
  }
}
