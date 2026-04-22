import { useMemo } from 'react'
import { useAgents } from '../../api/agents'
import { useWorkflows } from '../../api/workflows'

/**
 * Referência resumida para mostrar em badges/modais — não carrega o definition inteiro.
 */
export interface ResourceRef {
  id: string
  name: string
}

/**
 * Mapeia qual agent/workflow usa cada tool. Calculado client-side a partir de
 * useAgents() + useWorkflows() já cacheados pelo TanStack Query. Zero endpoint novo.
 * Se a lista crescer muito, backlog BC-TOOLS-USAGE-1: mover para endpoint backend.
 */
export interface ToolUsage {
  /** name da function tool (ex: "search_asset") → agents que referenciam */
  functions: Map<string, ResourceRef[]>
  /** functionName do executor (ex: "service_pre_processor") → workflows que referenciam */
  executors: Map<string, ResourceRef[]>
  /** type do middleware (ex: "AccountGuard") → agents com enabled=true */
  middlewares: Map<string, ResourceRef[]>
  /** name do MCP server → agents que referenciam via mcpServerId */
  mcpServers: Map<string, ResourceRef[]>
  isLoading: boolean
}

export function useToolUsage(): ToolUsage {
  const agentsQuery = useAgents()
  const workflowsQuery = useWorkflows()

  return useMemo(() => {
    const functions = new Map<string, ResourceRef[]>()
    const middlewares = new Map<string, ResourceRef[]>()
    const mcpServers = new Map<string, ResourceRef[]>()
    const executors = new Map<string, ResourceRef[]>()

    const pushRef = (map: Map<string, ResourceRef[]>, key: string, ref: ResourceRef) => {
      const list = map.get(key)
      if (list) list.push(ref)
      else map.set(key, [ref])
    }

    for (const a of agentsQuery.data ?? []) {
      const ref: ResourceRef = { id: a.id, name: a.name }
      for (const t of a.tools ?? []) {
        if (t.type === 'function' && t.name) pushRef(functions, t.name, ref)
        else if (t.type === 'mcp' && t.mcpServerId) pushRef(mcpServers, t.mcpServerId, ref)
      }
      for (const m of a.middlewares ?? []) {
        if (m.enabled !== false) pushRef(middlewares, m.type, ref)
      }
    }

    for (const w of workflowsQuery.data ?? []) {
      const ref: ResourceRef = { id: w.id, name: w.name }
      for (const e of w.executors ?? []) {
        if (e.functionName) pushRef(executors, e.functionName, ref)
      }
    }

    return {
      functions,
      executors,
      middlewares,
      mcpServers,
      isLoading: agentsQuery.isLoading || workflowsQuery.isLoading,
    }
  }, [agentsQuery.data, workflowsQuery.data, agentsQuery.isLoading, workflowsQuery.isLoading])
}
