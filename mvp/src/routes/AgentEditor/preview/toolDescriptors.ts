import type { GenericTool, ParamDefinition } from '../../../api/genericTools'
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

export type EnrichedToolDescriptor = EnrichedHttpToolDescriptor

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

/**
 * Constrói descritores das tools HTTP selecionadas. Ids órfãos (tool deletada
 * externamente) são silenciosamente ignorados — a UI mostra o que estiver
 * disponível agora pra não bloquear a revisão.
 */
export function buildEnrichedDescriptors(
  toolIds: string[],
  toolsCatalog: GenericTool[],
): EnrichedToolDescriptor[] {
  const result: EnrichedToolDescriptor[] = []
  for (const id of toolIds) {
    const t = toolsCatalog.find((x) => x.id === id)
    if (t) result.push(fromGenericTool(t))
  }
  return result
}
