import type { JsonSchemaObject } from '../../api/tools'
import type { WorkflowFormValues } from './types'

/**
 * Source-node "tem schema" para fins de gating de Conditional/Switch na UI.
 * Schema é considerado válido quando declara properties (objeto JSON) — primitivos
 * isolados não dão paths pra predicate apontar.
 */
export function hasSchemaForNode(schema: JsonSchemaObject | undefined): boolean {
  return !!schema && (!!schema.properties || schema.type === 'object' || schema.type === 'array')
}

/**
 * Converte EdgePredicateForm (form-friendly strings) → EdgePredicate (formato API com Value tipado).
 * Operadores unários ignoram value; In/NotIn parseiam CSV em array.
 */
export function formToEdgePredicate(p: WorkflowFormValues['edges'][number]['predicate']) {
  const isUnary = p.operator === 'IsNull' || p.operator === 'IsNotNull'
  if (isUnary) {
    return {
      path: p.path,
      operator: p.operator,
      valueType: p.valueType,
    }
  }

  const isList = p.operator === 'In' || p.operator === 'NotIn'
  const raw = p.valueRaw

  if (isList) {
    const items = raw.split(',').map((s) => s.trim()).filter(Boolean)
    return {
      path: p.path,
      operator: p.operator,
      value: items.map((it) => parseScalarByValueType(it, p.valueType)),
      valueType: p.valueType,
    }
  }

  return {
    path: p.path,
    operator: p.operator,
    value: parseScalarByValueType(raw, p.valueType),
    valueType: p.valueType,
  }
}

function parseScalarByValueType(raw: string, valueType: string): unknown {
  switch (valueType) {
    case 'Number':
    case 'Integer': {
      const n = Number(raw)
      return Number.isFinite(n) ? n : raw
    }
    case 'Boolean':
      return raw === 'true' || raw === '1'
    default:
      return raw
  }
}
