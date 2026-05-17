// Helpers de síntese de exemplo: a partir de um JSON Schema (string ou obj)
// produzem um valor concreto pra renderizar `arguments` no preview de
// tool_call. O objetivo é ilustrar a forma — não validar nem invocar. Schemas
// complexos (`$ref`, `allOf`, `oneOf`, `anyOf`) caem num placeholder genérico
// em vez de tentar resolução; a preview ainda transmite "este formato".

interface JsonSchema {
  type?: string | string[]
  properties?: Record<string, JsonSchema>
  required?: string[]
  items?: JsonSchema | JsonSchema[]
  enum?: unknown[]
  example?: unknown
  default?: unknown
  format?: string
}

const SAMPLE_BY_FORMAT: Record<string, string> = {
  'date': '2026-01-01',
  'date-time': '2026-01-01T00:00:00Z',
  'time': '00:00:00',
  'email': 'exemplo@dominio.com',
  'uri': 'https://exemplo.com',
  'uuid': '00000000-0000-0000-0000-000000000000',
}

/**
 * Tenta parsear schema raw (string JSON) pra objeto. Retorna null em falha —
 * caller deve degradar pra placeholder.
 */
export function parseSchema(raw: string | null | undefined): JsonSchema | null {
  if (!raw) return null
  const trimmed = raw.trim()
  if (!trimmed) return null
  try {
    const parsed = JSON.parse(trimmed)
    if (parsed === null || typeof parsed !== 'object') return null
    return parsed as JsonSchema
  } catch {
    return null
  }
}

/**
 * Produz um valor exemplo coerente com o schema. Preferência:
 *   1. `example` declarado
 *   2. `default` declarado
 *   3. primeiro `enum` quando presente
 *   4. amostra padrão por `format` (date, email, uri, ...)
 *   5. amostra padrão por `type`
 * Inclui apenas campos `required` em objetos pra manter o exemplo enxuto.
 */
export function synthesizeArgs(schema: JsonSchema | null): unknown {
  if (!schema) return '<exemplo>'
  if (schema.example !== undefined) return schema.example
  if (schema.default !== undefined) return schema.default
  if (Array.isArray(schema.enum) && schema.enum.length > 0) return schema.enum[0]

  const t = Array.isArray(schema.type) ? schema.type[0] : schema.type
  switch (t) {
    case 'string':
      if (schema.format && SAMPLE_BY_FORMAT[schema.format]) return SAMPLE_BY_FORMAT[schema.format]
      return 'exemplo'
    case 'number':
    case 'integer':
      return 0
    case 'boolean':
      return false
    case 'array': {
      const itemSchema = Array.isArray(schema.items) ? schema.items[0] : schema.items
      return [synthesizeArgs(itemSchema ?? null)]
    }
    case 'object': {
      return synthesizeObject(schema)
    }
    default:
      // Tipo ausente ou `null`: assume objeto se houver `properties`.
      if (schema.properties) return synthesizeObject(schema)
      return '<exemplo>'
  }
}

function synthesizeObject(schema: JsonSchema): Record<string, unknown> {
  const required = new Set(schema.required ?? [])
  const props = schema.properties ?? {}
  const result: Record<string, unknown> = {}
  for (const [key, sub] of Object.entries(props)) {
    // Inclui sempre campos required; opcionais entram apenas se vierem com
    // example/default/enum (sinal de que o autor quis destacar valor padrão).
    if (required.has(key) || sub.example !== undefined || sub.default !== undefined || sub.enum !== undefined) {
      result[key] = synthesizeArgs(sub)
    }
  }
  return result
}
