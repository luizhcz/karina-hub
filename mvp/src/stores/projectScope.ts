import { getIdentity } from './identity'

/**
 * Filtro de UX: o backend retorna agentes/workflows com Visibility=global
 * cross-project (HasQueryFilter), mas no frontend o usuário só deve ver
 * recursos do projeto dele. Visibility continua válido pra runtime/consumo —
 * é só separação visual.
 *
 * Aceita itens com `originProjectId` (Agent, Workflow) OU `projectId` (AgentDraft).
 * Quando a identidade ainda não foi resolvida (mount inicial), retorna true
 * pra evitar flash de lista vazia — o filter é re-aplicado quando o componente
 * re-renderiza após `subscribeIdentity`.
 */
export function isInCurrentProject(
  item: { projectId?: string | null; originProjectId?: string | null },
): boolean {
  const currentId = getIdentity()?.projectId
  if (!currentId) return true
  const owner = item.originProjectId ?? item.projectId ?? null
  if (!owner) return false
  return owner === currentId
}
