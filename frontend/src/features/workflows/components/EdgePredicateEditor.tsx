import { Controller } from 'react-hook-form'
import type { Control, FieldPath, UseFormRegister } from 'react-hook-form'
import { Input } from '../../../shared/ui/Input'
import { Select } from '../../../shared/ui/Select'
import type { JsonSchemaObject } from '../../../api/tools'
import type { WorkflowFormValues } from '../types'

/**
 * Editor inline de EdgePredicate (Path/Operator/Value/ValueType).
 *
 * Modo "schema-aware":
 *   - <code>schema</code> presente → combobox de paths derivados, operadores filtrados pelo
 *     tipo do field selecionado, ValueType pré-selecionado. Tudo é hint de UX — backend
 *     valida via <code>EdgeInvariantsValidator</code>.
 *   - <code>schema</code> ausente → mostra warning vermelho explicando que Conditional/Switch
 *     não é permitido nessa origem (regra absoluta de domínio). O input ainda é editável
 *     pra não travar o save (que vai falhar com erro estruturado pra renderizar inline).
 */

export type PredicatePrefix =
  | `edges.${number}.predicate`
  | `edges.${number}.cases.${number}.predicate`

interface PathInfo {
  path: string
  type: SchemaType
  enumValues?: string[]
}

type SchemaType = 'string' | 'number' | 'integer' | 'boolean' | 'enum' | 'object' | 'array' | 'unknown'

const MAX_DEPTH = 5

const ALL_OPERATORS: { value: string; label: string }[] = [
  { value: 'Eq', label: 'Eq (=)' },
  { value: 'NotEq', label: 'NotEq (≠)' },
  { value: 'Gt', label: 'Gt (>)' },
  { value: 'Gte', label: 'Gte (≥)' },
  { value: 'Lt', label: 'Lt (<)' },
  { value: 'Lte', label: 'Lte (≤)' },
  { value: 'Contains', label: 'Contains' },
  { value: 'StartsWith', label: 'StartsWith' },
  { value: 'EndsWith', label: 'EndsWith' },
  { value: 'MatchesRegex', label: 'MatchesRegex' },
  { value: 'In', label: 'In (lista)' },
  { value: 'NotIn', label: 'NotIn (lista)' },
  { value: 'IsNull', label: 'IsNull' },
  { value: 'IsNotNull', label: 'IsNotNull' },
]

const VALUE_TYPES = [
  { value: 'Auto', label: 'Auto' },
  { value: 'String', label: 'String' },
  { value: 'Number', label: 'Number' },
  { value: 'Integer', label: 'Integer' },
  { value: 'Boolean', label: 'Boolean' },
  { value: 'Enum', label: 'Enum' },
]

function inferType(s: JsonSchemaObject): SchemaType {
  if (Array.isArray(s.enum) && s.enum.length > 0) return 'enum'
  const t = Array.isArray(s.type) ? s.type[0] : s.type
  switch (t) {
    case 'string':
      return 'string'
    case 'number':
      return 'number'
    case 'integer':
      return 'integer'
    case 'boolean':
      return 'boolean'
    case 'object':
      return 'object'
    case 'array':
      return 'array'
    default:
      return 'unknown'
  }
}

function operatorsForType(type: SchemaType): string[] {
  switch (type) {
    case 'number':
    case 'integer':
      return ['Eq', 'NotEq', 'Gt', 'Gte', 'Lt', 'Lte', 'In', 'NotIn', 'IsNull', 'IsNotNull']
    case 'string':
      return ['Eq', 'NotEq', 'Contains', 'StartsWith', 'EndsWith', 'MatchesRegex', 'In', 'NotIn', 'IsNull', 'IsNotNull']
    case 'boolean':
      return ['Eq', 'NotEq', 'IsNull', 'IsNotNull']
    case 'enum':
      return ['Eq', 'NotEq', 'In', 'NotIn', 'IsNull', 'IsNotNull']
    default:
      return ALL_OPERATORS.map((o) => o.value)
  }
}

function typeToValueType(type: SchemaType): WorkflowFormValues['edges'][number]['predicate']['valueType'] {
  switch (type) {
    case 'number':
      return 'Number'
    case 'integer':
      return 'Integer'
    case 'boolean':
      return 'Boolean'
    case 'enum':
      return 'Enum'
    case 'string':
      return 'String'
    default:
      return 'Auto'
  }
}

/**
 * Walk schema produzindo lista de paths resolvidos (até <code>MAX_DEPTH</code> níveis).
 * Arrays expõem <code>$.list[0]</code> como exemplo — usuário pode editar índice se precisar.
 */
function resolvePaths(
  schema: JsonSchemaObject | undefined,
  prefix = '$',
  depth = 0,
): PathInfo[] {
  if (!schema || depth > MAX_DEPTH) return []
  const result: PathInfo[] = []
  if (schema.properties) {
    for (const [key, child] of Object.entries(schema.properties)) {
      const path = `${prefix}.${key}`
      const type = inferType(child)
      const enumValues = Array.isArray(child.enum)
        ? (child.enum as unknown[]).map((v) => String(v))
        : undefined
      result.push({ path, type, enumValues })
      if (type === 'object') {
        result.push(...resolvePaths(child, path, depth + 1))
      } else if (type === 'array') {
        const items = Array.isArray(child.items) ? child.items[0] : child.items
        if (items) {
          const itemsPath = `${path}[0]`
          const itemsType = inferType(items)
          result.push({
            path: itemsPath,
            type: itemsType,
            enumValues: Array.isArray(items.enum)
              ? (items.enum as unknown[]).map((v) => String(v))
              : undefined,
          })
          if (itemsType === 'object') {
            result.push(...resolvePaths(items, itemsPath, depth + 1))
          }
        }
      }
    }
  }
  return result
}

interface EdgePredicateEditorProps {
  prefix: PredicatePrefix
  control: Control<WorkflowFormValues>
  register: UseFormRegister<WorkflowFormValues>
  /** Schema do produtor (output) usado pra hint de UX. */
  schema?: JsonSchemaObject
  /** Watcher do valor atual do predicate — pra reagir a mudanças de path/operator. */
  current: WorkflowFormValues['edges'][number]['predicate']
  /** True quando origem não tem schema declarado — desabilita combobox e mostra warning. */
  noSchema: boolean
  /** Mensagem de warning customizada (ex: 'Origem sem schema declarado'). */
  warning?: string
}

export function EdgePredicateEditor({
  prefix,
  control,
  register,
  schema,
  current,
  noSchema,
  warning,
}: EdgePredicateEditorProps) {
  const paths = resolvePaths(schema)
  const matchedPath = paths.find((p) => p.path === current.path)
  const detectedType = matchedPath?.type ?? 'unknown'
  const allowedOps = operatorsForType(detectedType)
  const operatorOptions = ALL_OPERATORS.filter((o) => allowedOps.includes(o.value))
  const isUnary = current.operator === 'IsNull' || current.operator === 'IsNotNull'

  return (
    <div className="bg-bg-secondary border border-border-primary rounded-md p-3 flex flex-col gap-2">
      {noSchema && (
        <div className="text-xs text-amber-400 bg-amber-500/10 border border-amber-500/30 rounded px-2 py-1.5">
          {warning ??
            'Origem não declara schema JSON. Conditional/Switch só pode sair de agente com StructuredOutput.ResponseFormat="json_schema" ou executor Register&lt;TIn,TOut&gt;.'}
        </div>
      )}

      {!noSchema && paths.length > 0 ? (
        <Controller
          control={control}
          name={`${prefix}.path` as FieldPath<WorkflowFormValues>}
          render={({ field: f }) => (
            <Select
              label="Path"
              value={f.value as string}
              onChange={(e) => f.onChange(e.target.value)}
              options={[
                { label: 'Selecionar campo...', value: '' },
                ...paths.map((p) => ({
                  label: `${p.path}  (${p.type}${p.enumValues ? `: ${p.enumValues.join(' | ')}` : ''})`,
                  value: p.path,
                })),
              ]}
            />
          )}
        />
      ) : (
        <Input
          label="Path (JSONPath)"
          placeholder="$.field"
          {...register(`${prefix}.path` as FieldPath<WorkflowFormValues>)}
        />
      )}

      <div className="grid grid-cols-1 md:grid-cols-2 gap-2">
        <Controller
          control={control}
          name={`${prefix}.operator` as FieldPath<WorkflowFormValues>}
          render={({ field: f }) => (
            <Select
              label="Operator"
              value={f.value as string}
              onChange={(e) => {
                f.onChange(e.target.value)
              }}
              options={operatorOptions.length > 0 ? operatorOptions : ALL_OPERATORS}
            />
          )}
        />
        <Controller
          control={control}
          name={`${prefix}.valueType` as FieldPath<WorkflowFormValues>}
          render={({ field: f }) => (
            <Select
              label="Value Type"
              value={f.value as string}
              onChange={(e) => f.onChange(e.target.value)}
              options={VALUE_TYPES}
            />
          )}
        />
      </div>

      {!isUnary && (
        matchedPath?.enumValues && matchedPath.enumValues.length > 0 ? (
          <Controller
            control={control}
            name={`${prefix}.valueRaw` as FieldPath<WorkflowFormValues>}
            render={({ field: f }) => (
              <Select
                label="Value"
                value={f.value as string}
                onChange={(e) => f.onChange(e.target.value)}
                options={[
                  { label: 'Selecionar valor...', value: '' },
                  ...matchedPath.enumValues!.map((v) => ({ label: v, value: v })),
                ]}
              />
            )}
          />
        ) : (
          <Input
            label={
              current.operator === 'In' || current.operator === 'NotIn'
                ? 'Value (lista separada por vírgula)'
                : 'Value'
            }
            placeholder={
              current.valueType === 'Boolean'
                ? 'true | false'
                : current.valueType === 'Number' || current.valueType === 'Integer'
                  ? 'ex: 42'
                  : 'ex: aprovado'
            }
            {...register(`${prefix}.valueRaw` as FieldPath<WorkflowFormValues>)}
          />
        )
      )}

      {!noSchema && matchedPath && (
        <p className="text-xs text-text-dimmed">
          Tipo detectado: <span className="text-text-secondary">{matchedPath.type}</span>
          {detectedType !== 'unknown' && current.valueType !== typeToValueType(detectedType) && (
            <span className="ml-2 text-amber-400">
              (sugestão: usar valueType=<code>{typeToValueType(detectedType)}</code>)
            </span>
          )}
        </p>
      )}
    </div>
  )
}

