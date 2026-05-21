import type { GenericTool, ParamDefinition } from '../../../api/genericTools'
import type { McpServer } from '../../../api/mcpServers'
import { parseSchema } from './synthesizeArgs'

// Descritor estruturado consumido pela UI do step Revisão. Diferente do antigo
// `ToolDescriptor` (string markdown que ia pro prompt), esses dados são
// renderizados em componentes React — preservam tipos e schemas parseados
// pra mostrar JSON Schema highlight, badges de source e exemplo de tool_call.

export interface EnrichedHttpToolDescriptor {
  source: 'http'
  id: string
  name: string
  httpMethod: string
  urlTemplate: string
  pathParams: Record<string, ParamDefinition>
  queryParams: Record<string, ParamDefinition>
  inputContentType: string
  /** Schema parseado pra objeto JSON quando válido. Null = sem schema ou inválido. */
  inputSchema: unknown
  outputContentType: string
  outputSchema: unknown
}

export interface EnrichedMcpToolDescriptor {
  source: 'mcp'
  id: string
  name: string
  description: string
  allowedTools: string[]
  requiresApprovalAlways: boolean
  serverLabel: string
}

export type EnrichedToolDescriptor = EnrichedHttpToolDescriptor | EnrichedMcpToolDescriptor

function fromGenericTool(tool: GenericTool): EnrichedHttpToolDescriptor {
  return {
    source: 'http',
    id: tool.id,
    name: tool.name,
    httpMethod: tool.httpMethod,
    urlTemplate: tool.urlTemplate,
    pathParams: tool.pathParams,
    queryParams: tool.queryParams,
    inputContentType: tool.inputContentType,
    inputSchema: parseSchema(tool.inputSchema),
    outputContentType: tool.outputContentType,
    outputSchema: parseSchema(tool.outputSchema),
  }
}

function fromMcpServer(mcp: McpServer): EnrichedMcpToolDescriptor {
  return {
    source: 'mcp',
    id: mcp.id,
    name: mcp.name || mcp.serverLabel,
    description: mcp.description ?? '',
    allowedTools: mcp.allowedTools,
    requiresApprovalAlways: mcp.requireApproval === 'always',
    serverLabel: mcp.serverLabel,
  }
}

/**
 * Constrói descritores na ordem (HTTP tools primeiro, MCPs depois) com base
 * na seleção atual e nos catálogos carregados. Ids órfãos (tool deletada
 * externamente, MCP desativado) são silenciosamente ignorados — a UI mostra
 * o que estiver disponível agora pra não bloquear a revisão.
 */
export function buildEnrichedDescriptors(
  toolIds: string[],
  mcpIds: string[],
  toolsCatalog: GenericTool[],
  mcpsCatalog: McpServer[],
): EnrichedToolDescriptor[] {
  const result: EnrichedToolDescriptor[] = []
  for (const id of toolIds) {
    const t = toolsCatalog.find((x) => x.id === id)
    if (t) result.push(fromGenericTool(t))
  }
  for (const id of mcpIds) {
    const m = mcpsCatalog.find((x) => x.id === id)
    if (m) result.push(fromMcpServer(m))
  }
  return result
}
