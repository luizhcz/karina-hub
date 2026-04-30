import type { CreateAgentRequest } from '../../api/agents'
import { tryParseSchema } from './components/SchemaEditor/utils'
import type { AgentFormValues } from './types'

export type FormToRequestResult =
  | { ok: true; body: CreateAgentRequest }
  | { ok: false; error: string }

export function formToRequest(values: AgentFormValues): FormToRequestResult {
  let parsedSchema: unknown | undefined
  if (values.structuredOutput.responseFormat !== 'text' && values.structuredOutput.schema) {
    const result = tryParseSchema(values.structuredOutput.schema)
    if (!result.ok) return { ok: false, error: `Schema inválido: ${result.error}` }
    parsedSchema = result.schema
  }

  return {
    ok: true,
    body: {
      id: values.id,
      name: values.name,
      description: values.description || undefined,
      model: {
        deploymentName: values.model.deploymentName,
        temperature: values.model.temperature,
        maxTokens: values.model.maxTokens,
      },
      provider: values.provider.type
        ? {
            type: values.provider.type,
            clientType: values.provider.clientType || undefined,
            endpoint: values.provider.endpoint || undefined,
          }
        : undefined,
      instructions: values.instructions || undefined,
      tools: (() => {
        const merged = [
          ...values.tools.map((name) => ({ type: 'function', name })),
          ...values.mcpServerIds.map((mcpServerId) => ({ type: 'mcp', mcpServerId })),
        ]
        return merged.length > 0 ? merged : undefined
      })(),
      structuredOutput: values.structuredOutput.responseFormat !== 'text'
        ? {
            responseFormat: values.structuredOutput.responseFormat,
            schemaName: values.structuredOutput.schemaName || undefined,
            schemaDescription: values.structuredOutput.schemaDescription || undefined,
            schema: parsedSchema,
          }
        : undefined,
      middlewares: values.middlewares.length > 0
        ? values.middlewares.map((m) => ({ type: m.type, enabled: true, settings: m.settings }))
        : undefined,
      resilience: {
        maxRetries: values.resilience.maxRetries,
        initialDelayMs: values.resilience.initialDelayMs,
        backoffMultiplier: values.resilience.backoffMultiplier,
      },
      costBudget: values.budget.maxCostUsd > 0
        ? { maxCostUsd: values.budget.maxCostUsd }
        : undefined,
      skillRefs: values.skills.length > 0
        ? values.skills.map((skillId) => ({ skillId }))
        : undefined,
      metadata: Object.keys(values.metadata).length > 0 ? values.metadata : undefined,
    },
  }
}
