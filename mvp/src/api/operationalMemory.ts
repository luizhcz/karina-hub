import { del, get } from './client'

export interface OperationalMemory {
  agentId: string
  scopeType: 'conversation' | 'session'
  scopeId: string
  payload: unknown
  version: number
  createdAt: string
  updatedAt: string
}

const base = (agentId: string) =>
  `/agents/${encodeURIComponent(agentId)}/operational-memory`

/**
 * Estado canônico da memória pra um escopo (chat ou sandbox). Retorna `null`
 * em 404 — escopo ainda não escreveu nada.
 */
export const getOperationalMemory = async (
  agentId: string,
  scopeId: string,
  scopeType: 'conversation' | 'session' = 'conversation',
): Promise<OperationalMemory | null> => {
  try {
    return await get<OperationalMemory>(
      `${base(agentId)}/${encodeURIComponent(scopeId)}?scopeType=${scopeType}`,
    )
  } catch (err) {
    if (err && typeof err === 'object' && 'status' in err && (err as { status: number }).status === 404) {
      return null
    }
    throw err
  }
}

export const deleteOperationalMemory = (
  agentId: string,
  scopeId: string,
  scopeType: 'conversation' | 'session' = 'conversation',
) =>
  del<void>(
    `${base(agentId)}/${encodeURIComponent(scopeId)}?scopeType=${scopeType}`,
  )
