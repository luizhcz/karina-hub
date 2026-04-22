import type { JsonSchemaNode, ResolvedType, SchemaType } from './types'

export function deepClone<T>(obj: T): T {
  return JSON.parse(JSON.stringify(obj))
}

export function resolveType(prop: JsonSchemaNode): ResolvedType {
  if (prop.type && typeof prop.type === 'string' && prop.type !== 'null') {
    return { types: [prop.type as SchemaType], nullable: false, resolved: prop }
  }
  if (prop.anyOf) {
    const nonNull = prop.anyOf.filter((a) => a.type !== 'null')
    const hasNull = prop.anyOf.some((a) => a.type === 'null')
    return {
      types: nonNull
        .map((a) => a.type)
        .filter((t): t is SchemaType => typeof t === 'string' && t !== 'null'),
      nullable: hasNull,
      resolved: nonNull[0] ?? prop,
    }
  }
  return { types: ['string'], nullable: false, resolved: prop }
}

export function navigateToParent(schema: JsonSchemaNode, path: string[]): JsonSchemaNode {
  let node = schema
  for (const seg of path) {
    if (node.properties?.[seg]) {
      const { resolved } = resolveType(node.properties[seg])
      if (resolved.type === 'array' && resolved.items?.type === 'object') {
        node = resolved.items
      } else if (resolved.type === 'object') {
        node = resolved
      }
    }
  }
  return node
}

export function rebuildProp(type: SchemaType, desc: string, nullable: boolean): JsonSchemaNode {
  const base: JsonSchemaNode = { type }
  if (desc) base.description = desc
  if (type === 'object') {
    base.properties = {}
    base.required = []
    base.additionalProperties = false
  }
  if (nullable) {
    return { anyOf: [base, { type: 'null' }], ...(desc ? { description: desc } : {}) }
  }
  return base
}

export function tryParseSchema(json: string): { ok: true; schema: JsonSchemaNode } | { ok: false; error: string } {
  if (!json.trim()) {
    return { ok: true, schema: { type: 'object', properties: {}, required: [], additionalProperties: false } }
  }
  try {
    const parsed = JSON.parse(json) as unknown
    if (typeof parsed !== 'object' || parsed === null) {
      return { ok: false, error: 'Schema deve ser um objeto JSON' }
    }
    return { ok: true, schema: parsed as JsonSchemaNode }
  } catch (e) {
    return { ok: false, error: (e as Error).message }
  }
}

export function hasChildren(resolved: JsonSchemaNode): boolean {
  return (
    (resolved.type === 'object' && !!resolved.properties) ||
    (resolved.type === 'array' && resolved.items?.type === 'object' && !!resolved.items.properties)
  )
}

export function getChildProps(resolved: JsonSchemaNode): Record<string, JsonSchemaNode> | null {
  if (resolved.type === 'object' && resolved.properties) return resolved.properties
  if (resolved.type === 'array' && resolved.items?.type === 'object' && resolved.items.properties) return resolved.items.properties
  return null
}

export function getChildRequired(resolved: JsonSchemaNode): string[] {
  if (resolved.type === 'object') return resolved.required || []
  if (resolved.type === 'array' && resolved.items?.type === 'object') return resolved.items.required || []
  return []
}
