import { useEffect, useRef, useState } from 'react'
import { Button } from './Button'
import { IconButton } from './IconButton'
import { Input } from './Input'
import { Modal } from './Modal'
import { Select, type SelectOption } from './Select'
import { CloseIcon, PlusIcon, SparklesIcon } from './Icons'
import { cn } from './cn'

// Editor visual de JSON Schema. Cada campo tem: nome, tipo, obrigatório,
// descrição. Suporta primitivos (string/number/integer/boolean), `object`
// (com sub-campos editáveis recursivamente) e `array` (com `items` primitivo).
//
// Suporta dois shapes de raiz: objeto puro ({type:'object',...}) ou lista de
// objetos ({type:'array',items:{type:'object',...}}). O toggle "Objeto /
// Lista de objetos" alterna entre os dois sem deixar o user preso no modo JSON.
//
// Toggle "Visual / JSON" alterna entre o editor de campos e a representação
// JSON crua editável. Schemas com features avançadas (anyOf, $ref, array de
// primitivo na raiz) caem automaticamente em modo JSON e o botão Visual fica
// desabilitado.

const PRIMITIVE_TYPES = ['string', 'number', 'integer', 'boolean'] as const
type PrimitiveType = (typeof PRIMITIVE_TYPES)[number]

// JSON Schema não tem `type:'date'`. Datas viram `{type:'string', format:'date'|'date-time'}`,
// mas no builder elas aparecem como tipos próprios pra UX — converte-se no
// parse/serialize. Mantém os dois constantes pareados.
const STRING_FORMAT_TYPES = ['date', 'datetime'] as const
type StringFormatType = (typeof STRING_FORMAT_TYPES)[number]

const STRING_FORMAT_FOR_TYPE: Record<StringFormatType, string> = {
  date: 'date',
  datetime: 'date-time',
}

const FIELD_TYPES = [...PRIMITIVE_TYPES, ...STRING_FORMAT_TYPES, 'object', 'array'] as const
type FieldType = (typeof FIELD_TYPES)[number]

// itemType de array — primitivos + datas + 'object'. Quando é 'object', os campos
// do item ficam em FieldRow.children (mesma estrutura que type=object).
const ARRAY_ITEM_TYPES = [...PRIMITIVE_TYPES, ...STRING_FORMAT_TYPES, 'object'] as const
type ArrayItemType = (typeof ARRAY_ITEM_TYPES)[number]

const FIELD_TYPE_OPTIONS: SelectOption[] = [
  { value: 'string', label: 'texto' },
  { value: 'number', label: 'número' },
  { value: 'integer', label: 'inteiro' },
  { value: 'boolean', label: 'boolean' },
  { value: 'date', label: 'data' },
  { value: 'datetime', label: 'data e hora' },
  { value: 'object', label: 'objeto' },
  { value: 'array', label: 'lista' },
]

const ARRAY_ITEM_OPTIONS: SelectOption[] = [
  { value: 'string', label: 'lista de textos' },
  { value: 'number', label: 'lista de números' },
  { value: 'integer', label: 'lista de inteiros' },
  { value: 'boolean', label: 'lista de booleans' },
  { value: 'date', label: 'lista de datas' },
  { value: 'datetime', label: 'lista de datas e horas' },
  { value: 'object', label: 'lista de objetos' },
]

function isArrayItemType(t: unknown): t is ArrayItemType {
  return typeof t === 'string' && (ARRAY_ITEM_TYPES as readonly string[]).includes(t)
}

function stringFormatToType(format: unknown): StringFormatType | null {
  if (format === 'date') return 'date'
  if (format === 'date-time') return 'datetime'
  return null
}

interface FieldRow {
  id: string
  name: string
  type: FieldType
  required: boolean
  // Campo aceita null (em adição ao type). Persistido como
  // `anyOf:[{type:T,...}, {type:'null'}]` — padrão idiomático de Foundry
  // strict mode, que rejeita o `type:['T','null']` standard. Frontend lê e
  // re-emite preservando este shape; tipos arbitrários de anyOf (ex.:
  // string|number) continuam caindo em modo JSON.
  nullable: boolean
  description: string
  // Lista fechada de valores aceitos. Aplicável a primitivos
  // (string/number/integer/boolean). Vazio = sem enum. Strings são
  // mantidas como entrada do usuário; o serializer faz coerce pro tipo
  // declarado (parseFloat pra number/integer; "true"/"false" pra boolean)
  // ao emitir o JSON.
  enumValues: string[]
  // Bounds numéricos pra type=number/integer. Strings vazias = ausente.
  // Parser/serializer ignoram pra tipos não-numéricos. Foundry strict
  // aceita ambos juntos; sem clamp de UI — usuário pode digitar valores
  // inválidos e o save valida no backend.
  numberMin: string
  numberMax: string
  // Quando true, emite `additionalProperties:false` no objeto. Aplicável
  // a type=object ou a type=array+itemType=object (afeta items). Foundry
  // strict exige essa flag em todo objeto pra schemas válidos. Default
  // false pra preservar compat com tools genéricas que tem body livre.
  strictObject: boolean
  itemType: ArrayItemType
  // Sub-campos pra type='object' OU pra type='array' com itemType='object'.
  // Reusa o mesmo array porque o user troca entre essas formas e queremos
  // preservar edits quando ele alterna o tipo.
  children: FieldRow[]
}

type Mode = 'visual' | 'json'

// Shape da raiz do schema. 'object' = `{type:'object',properties:{...}}`;
// 'array-of-object' = `{type:'array',items:{type:'object',properties:{...}}}`.
// Em ambos os casos os campos editados na UI são os do objeto (raiz ou item).
type RootKind = 'object' | 'array-of-object'

interface JsonSchemaBuilderProps {
  value: string
  onChange: (next: string) => void
  /** quando true, restringe a primitivos (sem object/array) — usado p/ FormUrlEncoded */
  flatOnly?: boolean
  /** label do botão de adicionar — default "Adicionar campo" */
  addLabel?: string
  /** mensagem mostrada quando não há campos */
  emptyHint?: string
}

interface ParsedSchema {
  ok: boolean
  fields: FieldRow[]
  rootKind: RootKind
  // `additionalProperties:false` no objeto raiz (ou no items do array raiz).
  // Foundry strict exige; o serializer re-emite quando true.
  rootStrict: boolean
  // Nome da feature JSON Schema que travou o modo visual (ex.: 'pattern',
  // 'oneOf'). Usado pelo banner pra explicar pro user o motivo. Só populado
  // quando ok=false e a feature foi detectada na raiz — features aninhadas
  // ficam com este campo undefined.
  unsupportedFeature?: string
}

function shortId() {
  return Math.random().toString(36).slice(2, 10)
}

function isPrimitive(t: unknown): t is PrimitiveType {
  return typeof t === 'string' && (PRIMITIVE_TYPES as readonly string[]).includes(t)
}

function emptyField(): FieldRow {
  return {
    id: shortId(),
    name: '',
    type: 'string',
    required: false,
    nullable: false,
    description: '',
    enumValues: [],
    numberMin: '',
    numberMax: '',
    strictObject: false,
    itemType: 'string',
    children: [],
  }
}

// Features de JSON Schema que o builder NÃO sabe editar visualmente —
// presença em qualquer nível força o modo JSON pra preservar fidelidade
// no round-trip. Sem este guard, o serializer dropa silenciosamente
// `enum`, `additionalProperties:false`, `minimum`/`maximum`, etc., e
// schemas Foundry strict (que exigem `additionalProperties:false`) ou
// agentes Router (que dependem de `enum`) quebram ao editar.
// `enum`, `minimum`, `maximum` e `additionalProperties` AGORA são editáveis no
// modo visual — parser hidrata em FieldRow.enumValues/numberMin/numberMax/
// strictObject e o serializer regenera. As demais permanecem dropadas, então
// continuam forçando JSON.
const UNSUPPORTED_PROP_KEYS: ReadonlySet<string> = new Set([
  'const',
  'exclusiveMinimum',
  'exclusiveMaximum',
  'minLength',
  'maxLength',
  'pattern',
  'minItems',
  'maxItems',
  'uniqueItems',
  'oneOf',
  'allOf',
  'not',
  '$ref',
  // Semântica condicional / dependente: dropar silenciosamente quebraria a
  // validação. Improvável nos schemas internos, mas pode entrar via import
  // Foundry — fechar antes que vire bug.
  'if',
  'then',
  'else',
  'contains',
  'propertyNames',
  'dependentRequired',
  'dependentSchemas',
])

// Retorna o nome da primeira feature não suportada encontrada (em ordem de
// declaração), ou null se o schema for editável. Diferente de um boolean,
// o nome serve pro banner mostrar pro user QUAL feature travou o modo
// visual ("usa pattern", "usa oneOf") em vez de mensagem genérica.
//
// `additionalProperties` com **schema inline** (não apenas `true`/`false`)
// também trava — o builder só sabe codificar/decodificar o caso `false`
// (Foundry strict). Valores `true` e schemas inline forçam JSON.
function findUnsupportedFeature(schema: Record<string, unknown>): string | null {
  for (const key of Object.keys(schema)) {
    if (UNSUPPORTED_PROP_KEYS.has(key)) return key
  }
  if ('additionalProperties' in schema) {
    const ap = schema.additionalProperties
    if (ap !== false && ap !== true) return 'additionalProperties'
    // ap=true é equivalente ao default e pode ser dropado sem perda — o
    // builder não emite, ignora silenciosamente. ap=false vai pro FieldRow.
  }
  return null
}

function hasUnsupportedFeatures(schema: Record<string, unknown>): boolean {
  return findUnsupportedFeature(schema) !== null
}

// Reconhece dois padrões idiomáticos de "campo aceita null":
//
//   (1) `anyOf:[{type:'null'},{...inner}]` — jeito Foundry strict mode, que
//       rejeita o `type:[T,'null']` standard.
//   (2) `type:['T','null']` (ou `['null','T']`) — jeito JSON Schema standard,
//       semanticamente equivalente. Aceitos no parse pra que schemas externos
//       (importados, gerados por outros wizards) entrem no modo visual.
//
// Em ambos os casos a função extrai o schema inner pra que o parser normal
// possa processá-lo e sinaliza `nullable: true` no FieldRow. O serializer
// emite SEMPRE no shape (1) — round-trip normaliza standard → Foundry, o que
// é aceitável (equivalente semântico, e Foundry strict é nosso target principal).
//
// Qualquer outro shape (anyOf com 3+ entries, type-array sem null, etc.)
// retorna isNullable=false e o parent decide cair pro modo JSON.
function extractNullableInner(propSchema: Record<string, unknown>): {
  inner: Record<string, unknown>
  isNullable: boolean
} {
  // (2) type:['T','null'] — checa primeiro porque é mais barato e mais comum
  // em schemas standard. Quando case, retorna inner com `type:'T'` puro.
  const t = propSchema.type
  if (Array.isArray(t) && t.length === 2) {
    const types = t as unknown[]
    const hasNull = types.includes('null')
    const nonNull = types.find((x) => x !== 'null')
    if (hasNull && typeof nonNull === 'string') {
      const inner: Record<string, unknown> = { ...propSchema, type: nonNull }
      return { inner, isNullable: true }
    }
  }

  // (1) anyOf:[{type:'null'},{...inner}]
  const anyOf = propSchema.anyOf
  if (!Array.isArray(anyOf) || anyOf.length !== 2) {
    return { inner: propSchema, isNullable: false }
  }
  const [a, b] = anyOf as unknown[]
  if (!a || typeof a !== 'object' || !b || typeof b !== 'object') {
    return { inner: propSchema, isNullable: false }
  }
  const aObj = a as Record<string, unknown>
  const bObj = b as Record<string, unknown>
  const aIsNull = aObj.type === 'null'
  const bIsNull = bObj.type === 'null'
  // Exatamente um lado precisa ser `{type:'null'}` — descarta `[null,null]`
  // (sem sentido) e `[T,U]` (anyOf real, não nullable).
  if (aIsNull === bIsNull) return { inner: propSchema, isNullable: false }
  const innerCore = aIsNull ? bObj : aObj
  // Description do wrapper externo tem precedência (padrão visível em UIs),
  // mas cai pro inner quando ausente — preserva descrições legacy.
  const description =
    typeof propSchema.description === 'string'
      ? propSchema.description
      : typeof innerCore.description === 'string'
        ? innerCore.description
        : undefined
  const inner: Record<string, unknown> = { ...innerCore }
  if (description !== undefined && inner.description === undefined) {
    inner.description = description
  }
  return { inner, isNullable: true }
}

// Extrai enum como array de strings (tipos primitivos viram string —
// number/integer/boolean serão coerce-back no serializer). Aceita só
// `Array<string|number|boolean>`; mistura ou tipos exóticos retorna [].
function extractEnumValues(schema: Record<string, unknown>): string[] {
  const raw = schema.enum
  if (!Array.isArray(raw)) return []
  const out: string[] = []
  for (const v of raw) {
    if (typeof v === 'string') out.push(v)
    else if (typeof v === 'number') out.push(String(v))
    else if (typeof v === 'boolean') out.push(v ? 'true' : 'false')
    else return []
  }
  return out
}

// Numeric bounds → strings (preserva representação original; vazio = ausente).
function extractNumberBound(value: unknown): string {
  if (typeof value === 'number' && Number.isFinite(value)) return String(value)
  return ''
}

// `additionalProperties: false` → strict; qualquer outro valor (incluindo
// ausência) → não-strict. ap=true e schemas inline já foram filtrados em
// `findUnsupportedFeature` (caem em JSON), então aqui é seguro tratar como
// boolean.
function extractStrictObject(schema: Record<string, unknown>): boolean {
  return schema.additionalProperties === false
}

// Parser recursivo. Aceita schema vazio (`{}` ou ausente). Retorna ok=false
// quando algum campo usa estrutura não suportada (anyOf não-nullable, $ref,
// pattern, oneOf...), forçando o modo JSON.
function parseProperties(
  props: Record<string, unknown>,
  requiredSet: Set<string>,
): FieldRow[] | null {
  const fields: FieldRow[] = []
  for (const [name, schema] of Object.entries(props)) {
    if (!schema || typeof schema !== 'object') return null
    const rawPropSchema = schema as Record<string, unknown>
    // Descasca `anyOf:[T, null]` (nullable Foundry-strict) antes de inspecionar
    // o type. Qualquer outro shape de anyOf cai pro return null abaixo.
    const { inner: propSchema, isNullable } = extractNullableInner(rawPropSchema)
    // Features não-editáveis no visual (enum, additionalProperties≠false,
    // minimum/maximum, pattern, oneOf, $ref...) tanto no wrapper externo
    // quanto no inner forçam JSON pra evitar dropar essas chaves no
    // round-trip — bug que existia silenciosamente antes do suporte a
    // nullable e que ficou exposto ao destravar `anyOf [T,null]`.
    if (hasUnsupportedFeatures(rawPropSchema) || hasUnsupportedFeatures(propSchema)) {
      return null
    }
    const description = typeof propSchema.description === 'string' ? propSchema.description : ''
    const t = propSchema.type

    if (isPrimitive(t)) {
      // String com format reconhecido (date / date-time) vira tipo lógico próprio.
      const kind: FieldType =
        t === 'string' ? (stringFormatToType(propSchema.format) ?? 'string') : t
      fields.push({
        ...emptyField(),
        id: shortId(),
        name,
        type: kind,
        required: requiredSet.has(name),
        nullable: isNullable,
        description,
        enumValues: extractEnumValues(propSchema),
        numberMin: extractNumberBound(propSchema.minimum),
        numberMax: extractNumberBound(propSchema.maximum),
      })
      continue
    }

    if (t === 'object') {
      const subProps = propSchema.properties
      let children: FieldRow[] = []
      if (subProps && typeof subProps === 'object') {
        const subRequired = Array.isArray(propSchema.required)
          ? (propSchema.required as unknown[]).filter((r): r is string => typeof r === 'string')
          : []
        const parsed = parseProperties(
          subProps as Record<string, unknown>,
          new Set(subRequired),
        )
        if (parsed === null) return null
        children = parsed
      }
      fields.push({
        ...emptyField(),
        id: shortId(),
        name,
        type: 'object',
        required: requiredSet.has(name),
        nullable: isNullable,
        description,
        strictObject: extractStrictObject(propSchema),
        children,
      })
      continue
    }

    if (t === 'array') {
      const items = propSchema.items
      if (!items || typeof items !== 'object') return null
      const itemsObj = items as Record<string, unknown>
      // items podem ter features não-suportadas (pattern, oneOf, $ref...)
      // que o serializer não regenera — força JSON antes de fragmentar.
      // enum/min/max/strict são suportados; ficam no FieldRow do array.
      if (hasUnsupportedFeatures(itemsObj)) return null
      const itemTypeRaw = itemsObj.type

      if (isPrimitive(itemTypeRaw)) {
        const itemKind: ArrayItemType =
          itemTypeRaw === 'string'
            ? (stringFormatToType(itemsObj.format) ?? 'string')
            : itemTypeRaw
        fields.push({
          ...emptyField(),
          id: shortId(),
          name,
          type: 'array',
          required: requiredSet.has(name),
          nullable: isNullable,
          description,
          itemType: itemKind,
          // Enum/min/max em items primitivos hidratam no FieldRow do array
          // — UI mostra os mesmos controles aplicados aos items.
          enumValues: extractEnumValues(itemsObj),
          numberMin: extractNumberBound(itemsObj.minimum),
          numberMax: extractNumberBound(itemsObj.maximum),
        })
        continue
      }

      if (itemTypeRaw === 'object') {
        // Lista de objetos — os campos do item viram `children` desta linha,
        // editados pelo mesmo sub-builder usado em type=object.
        const subProps = itemsObj.properties
        let children: FieldRow[] = []
        if (subProps && typeof subProps === 'object') {
          const subRequired = Array.isArray(itemsObj.required)
            ? (itemsObj.required as unknown[]).filter(
                (r): r is string => typeof r === 'string',
              )
            : []
          const parsed = parseProperties(
            subProps as Record<string, unknown>,
            new Set(subRequired),
          )
          if (parsed === null) return null
          children = parsed
        }
        fields.push({
          ...emptyField(),
          id: shortId(),
          name,
          type: 'array',
          required: requiredSet.has(name),
          nullable: isNullable,
          description,
          itemType: 'object',
          // strictObject do array hidrata do `items` (não do array em si).
          // O serializer re-aplica em items quando emite.
          strictObject: extractStrictObject(itemsObj),
          children,
        })
        continue
      }

      // tipo de items desconhecido — aborta pra forçar modo JSON.
      return null
    }

    // tipo desconhecido (anyOf não-nullable, $ref, etc.) — aborta.
    return null
  }
  return fields
}

function parseSchema(raw: string): ParsedSchema {
  const fallback: ParsedSchema = { ok: false, fields: [], rootKind: 'object', rootStrict: false }
  if (!raw || !raw.trim()) {
    return { ok: true, fields: [], rootKind: 'object', rootStrict: false }
  }

  let parsed: unknown
  try {
    parsed = JSON.parse(raw)
  } catch {
    return fallback
  }

  if (!parsed || typeof parsed !== 'object') return fallback
  const root = parsed as Record<string, unknown>

  // Features não suportadas na raiz (oneOf, $ref, pattern, etc.) forçam JSON.
  // `additionalProperties:false` é suportado e hidrata em `rootStrict`.
  const rootFeature = findUnsupportedFeature(root)
  if (rootFeature !== null) return { ...fallback, unsupportedFeature: rootFeature }

  // Raiz = lista de objetos: aceita {type:'array',items:{type:'object',...}}
  // e edita os campos do item como se fossem campos da raiz.
  if (root.type === 'array') {
    const items = root.items
    if (!items || typeof items !== 'object') return fallback
    const itemsObj = items as Record<string, unknown>
    if (itemsObj.type !== 'object') return fallback
    if (hasUnsupportedFeatures(itemsObj)) return fallback
    const subProps = itemsObj.properties
    if (subProps !== undefined && (typeof subProps !== 'object' || subProps === null)) {
      return fallback
    }
    const subRequired = Array.isArray(itemsObj.required)
      ? (itemsObj.required as unknown[]).filter((r): r is string => typeof r === 'string')
      : []
    const props = (subProps ?? {}) as Record<string, unknown>
    const fields = parseProperties(props, new Set(subRequired))
    if (fields === null) return fallback
    return {
      ok: true,
      fields,
      rootKind: 'array-of-object',
      // Pra array-of-object, "strict" refere-se aos items (que é o
      // objeto editável). Foundry strict aplica ali.
      rootStrict: extractStrictObject(itemsObj),
    }
  }

  if (root.type !== undefined && root.type !== 'object') return fallback

  const properties = root.properties
  if (properties !== undefined && (typeof properties !== 'object' || properties === null)) {
    return fallback
  }
  const requiredArr = Array.isArray(root.required)
    ? (root.required as unknown[]).filter((r): r is string => typeof r === 'string')
    : []
  const props = (properties ?? {}) as Record<string, unknown>
  const fields = parseProperties(props, new Set(requiredArr))
  if (fields === null) return fallback
  return {
    ok: true,
    fields,
    rootKind: 'object',
    rootStrict: extractStrictObject(root),
  }
}

// Converte strings de enum/bound de volta pro tipo declarado. Valores
// inválidos (ex.: "abc" pra number) caem na string original — Foundry
// rejeita no save e o user vê o erro inline. Não fazemos validação preemptiva
// pra não engolir input no meio da digitação.
//
// `type` é o tipo EFETIVO (o do próprio campo pra primitivos OU o itemType
// quando o campo é array de primitivo). Passar `f.type` direto seria bug em
// arrays — caía no branch default e mantinha tudo como string.
function coerceEnumValue(raw: string, type: FieldType): unknown {
  if (type === 'number' || type === 'integer') {
    const n = Number(raw)
    return Number.isFinite(n) ? n : raw
  }
  if (type === 'boolean') {
    // Aceita 'True'/'TRUE'/'true' (e idem pra false) — JSON Schema importado
    // de outras fontes (Python, YAML) costuma vir em PascalCase.
    const lower = raw.trim().toLowerCase()
    if (lower === 'true') return true
    if (lower === 'false') return false
    return raw
  }
  return raw
}

function coerceNumberBound(raw: string): number | null {
  const trimmed = raw.trim()
  if (!trimmed) return null
  const n = Number(trimmed)
  return Number.isFinite(n) ? n : null
}

// Aplica enum/min/max sobre um schema-base já com type/format setados.
// `effectiveType` é o tipo do schema sendo construído (primitivo do field
// pra primitivos, OU itemType quando o caller está emitindo items de array
// de primitivo). Sem esse parâmetro o helper enxergaria `f.type === 'array'`
// e nunca aplicaria min/max — bug clássico encontrado em review.
function applyPrimitiveConstraints(
  schema: Record<string, unknown>,
  f: FieldRow,
  effectiveType: FieldType,
): void {
  const enums = f.enumValues
    .map((v) => v.trim())
    .filter((v) => v.length > 0)
  if (enums.length > 0) {
    schema.enum = enums.map((v) => coerceEnumValue(v, effectiveType))
  }
  if (effectiveType === 'number' || effectiveType === 'integer') {
    const min = coerceNumberBound(f.numberMin)
    const max = coerceNumberBound(f.numberMax)
    if (min !== null) schema.minimum = min
    if (max !== null) schema.maximum = max
  }
}

// Serializa fields recursivamente. Produz `{ type, properties, required? }`
// canônico pra cada nível.
function buildSchemaObject(fields: FieldRow[]): Record<string, unknown> {
  const properties: Record<string, Record<string, unknown>> = {}
  const required: string[] = []

  for (const f of fields) {
    const name = f.name.trim()
    if (!name) continue
    // date/datetime são tipos lógicos: serializam como string + format JSON Schema.
    const inner: Record<string, unknown> =
      f.type === 'date' || f.type === 'datetime'
        ? { type: 'string', format: STRING_FORMAT_FOR_TYPE[f.type] }
        : { type: f.type }

    if (f.type === 'array') {
      // Para array de primitivo, enum/min/max do FieldRow aplicam aos items
      // (não ao array em si — array com enum não faz sentido em JSON Schema).
      if (f.itemType === 'object') {
        // items vira um schema de objeto recursivo (mesma forma de type=object).
        const sub = buildSchemaObject(f.children)
        const items: Record<string, unknown> = {
          type: 'object',
          properties: sub.properties ?? {},
        }
        if (Array.isArray(sub.required) && (sub.required as string[]).length > 0) {
          items.required = sub.required
        }
        if (f.strictObject) items.additionalProperties = false
        inner.items = items
      } else if (f.itemType === 'date' || f.itemType === 'datetime') {
        const itemsSchema: Record<string, unknown> = {
          type: 'string',
          format: STRING_FORMAT_FOR_TYPE[f.itemType],
        }
        // effectiveType=itemType: enum/min/max do FieldRow se aplicam aos
        // items, não ao array. Sem isso, `f.type === 'array'` cairia no
        // branch default e os bounds nunca seriam emitidos.
        applyPrimitiveConstraints(itemsSchema, f, f.itemType)
        inner.items = itemsSchema
      } else {
        const itemsSchema: Record<string, unknown> = { type: f.itemType }
        applyPrimitiveConstraints(itemsSchema, f, f.itemType)
        inner.items = itemsSchema
      }
    } else if (f.type === 'object') {
      const sub = buildSchemaObject(f.children)
      inner.properties = sub.properties ?? {}
      if (Array.isArray(sub.required) && (sub.required as string[]).length > 0) {
        inner.required = sub.required
      }
      if (f.strictObject) inner.additionalProperties = false
    } else {
      // Primitivos (string/number/integer/boolean/date/datetime).
      applyPrimitiveConstraints(inner, f, f.type)
    }

    // Quando nullable, embrulha em `anyOf:[inner,{type:'null'}]` (shape
    // canônico Foundry strict). Description fica no wrapper externo —
    // alguns leitores de UI mostram lá; o parser aceita ambos os lugares.
    let schema: Record<string, unknown>
    if (f.nullable) {
      schema = { anyOf: [inner, { type: 'null' }] }
      if (f.description.trim()) schema.description = f.description.trim()
    } else {
      schema = inner
      if (f.description.trim()) schema.description = f.description.trim()
    }

    properties[name] = schema
    if (f.required) required.push(name)
  }

  const result: Record<string, unknown> = { type: 'object', properties }
  if (required.length > 0) result.required = required
  return result
}

function serializeSchema(
  fields: FieldRow[],
  rootKind: RootKind,
  rootStrict: boolean,
): string {
  const inner = buildSchemaObject(fields)
  if (rootStrict) inner.additionalProperties = false
  if (rootKind === 'array-of-object') {
    // array-of-object: rootStrict aplica no items (objeto editável), não
    // no array — array com additionalProperties não tem semântica.
    return JSON.stringify({ type: 'array', items: inner }, null, 2)
  }
  return JSON.stringify(inner, null, 2)
}

// Infere um JSON Schema a partir de um valor de exemplo (resposta real da API).
// Para arrays, faz merge recursivo dos schemas dos itens — properties viram a
// união das chaves vistas e `required` fica como interseção (chave presente em
// TODOS os itens). Tipos numéricos divergentes (integer vs number) caem em
// number; tipos totalmente diferentes caem em string como denominador comum.
function inferSchema(value: unknown): Record<string, unknown> {
  if (Array.isArray(value)) {
    if (value.length === 0) return { type: 'array', items: { type: 'string' } }
    let merged = inferSchema(value[0])
    for (let i = 1; i < value.length; i++) {
      merged = mergeSchemas(merged, inferSchema(value[i]))
    }
    return { type: 'array', items: merged }
  }
  if (value === null || value === undefined) return { type: 'string' }
  const t = typeof value
  if (t === 'boolean') return { type: 'boolean' }
  if (t === 'number') {
    return Number.isInteger(value as number) ? { type: 'integer' } : { type: 'number' }
  }
  if (t === 'string') {
    const s = value as string
    // YYYY-MM-DD puro → format: 'date'.
    if (/^\d{4}-\d{2}-\d{2}$/.test(s)) return { type: 'string', format: 'date' }
    // ISO 8601 com componente de tempo → format: 'date-time'.
    if (/^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}/.test(s)) {
      return { type: 'string', format: 'date-time' }
    }
    return { type: 'string' }
  }
  if (t === 'object') {
    const obj = value as Record<string, unknown>
    const properties: Record<string, unknown> = {}
    const required: string[] = []
    for (const [k, v] of Object.entries(obj)) {
      properties[k] = inferSchema(v)
      required.push(k)
    }
    const result: Record<string, unknown> = { type: 'object', properties }
    if (required.length > 0) result.required = required
    return result
  }
  return { type: 'string' }
}

function mergeSchemas(
  a: Record<string, unknown>,
  b: Record<string, unknown>,
): Record<string, unknown> {
  const at = a.type
  const bt = b.type
  if (at !== bt) {
    if ((at === 'integer' && bt === 'number') || (at === 'number' && bt === 'integer')) {
      return { type: 'number' }
    }
    return { type: 'string' }
  }
  // String com format só permanece se ambos os lados concordam — caso contrário
  // descarta o format e mantém só `type:'string'` (denominador comum).
  if (at === 'string') {
    const af = a.format
    const bf = b.format
    if (af && af === bf) return { type: 'string', format: af }
    return { type: 'string' }
  }
  if (at === 'object') {
    const aProps = (a.properties ?? {}) as Record<string, Record<string, unknown>>
    const bProps = (b.properties ?? {}) as Record<string, Record<string, unknown>>
    const aReq = new Set(Array.isArray(a.required) ? (a.required as string[]) : [])
    const bReq = new Set(Array.isArray(b.required) ? (b.required as string[]) : [])
    const allKeys = new Set([...Object.keys(aProps), ...Object.keys(bProps)])
    const properties: Record<string, unknown> = {}
    const required: string[] = []
    for (const k of allKeys) {
      const av = aProps[k]
      const bv = bProps[k]
      properties[k] = av && bv ? mergeSchemas(av, bv) : (av ?? bv ?? { type: 'string' })
      // Só é required quando estava em ambos os lados (presente em todos os itens).
      if (av && bv && aReq.has(k) && bReq.has(k)) required.push(k)
    }
    const result: Record<string, unknown> = { type: 'object', properties }
    if (required.length > 0) result.required = required
    return result
  }
  if (at === 'array') {
    const ai = (a.items ?? { type: 'string' }) as Record<string, unknown>
    const bi = (b.items ?? { type: 'string' }) as Record<string, unknown>
    return { type: 'array', items: mergeSchemas(ai, bi) }
  }
  return a
}

export function JsonSchemaBuilder({
  value,
  onChange,
  flatOnly = false,
  addLabel = 'Adicionar campo',
  emptyHint = 'Nenhum campo definido ainda.',
}: JsonSchemaBuilderProps) {
  const initialParse = parseSchema(value)
  const [fields, setFields] = useState<FieldRow[]>(initialParse.fields)
  const [rootKind, setRootKind] = useState<RootKind>(initialParse.rootKind)
  // Marca o objeto raiz (ou items do array raiz) com `additionalProperties:false`
  // ao serializar. Foundry strict exige; outros consumidores (HTTP body) podem
  // querer deixar false pra aceitar props extras.
  const [rootStrict, setRootStrict] = useState<boolean>(initialParse.rootStrict)
  const [mode, setMode] = useState<Mode>(initialParse.ok ? 'visual' : 'json')
  const [jsonText, setJsonText] = useState<string>(value)
  const [visualLocked, setVisualLocked] = useState<boolean>(!initialParse.ok)
  const [lockedFeature, setLockedFeature] = useState<string | undefined>(
    initialParse.ok ? undefined : initialParse.unsupportedFeature,
  )
  const [importOpen, setImportOpen] = useState(false)
  const [importText, setImportText] = useState('')
  const [importError, setImportError] = useState<string | null>(null)

  // Guarda exatamente o que o parent já tem em `value` — não a forma
  // re-serializada. Sem isso, qualquer re-render do parent passando o mesmo
  // `value` original dispara um useEffect que reescreve o textarea, abrindo
  // brecha pra UI mostrar uma string e emitir outra. Atualiza só quando o
  // emit() acontecer de verdade.
  const lastEmittedRef = useRef<string>(value)

  useEffect(() => {
    if (value === lastEmittedRef.current) return
    setJsonText(value)
    const reparsed = parseSchema(value)
    if (reparsed.ok) {
      setFields(reparsed.fields)
      setRootKind(reparsed.rootKind)
      setRootStrict(reparsed.rootStrict)
      setVisualLocked(false)
      setLockedFeature(undefined)
    } else {
      setVisualLocked(true)
      setLockedFeature(reparsed.unsupportedFeature)
      setMode('json')
    }
    lastEmittedRef.current = value
  }, [value])

  const emit = (json: string) => {
    lastEmittedRef.current = json
    onChange(json)
  }

  const updateFields = (next: FieldRow[]) => {
    setFields(next)
    const json = serializeSchema(next, rootKind, rootStrict)
    setJsonText(json)
    emit(json)
  }

  const updateRootKind = (next: RootKind) => {
    if (next === rootKind) return
    setRootKind(next)
    const json = serializeSchema(fields, next, rootStrict)
    setJsonText(json)
    emit(json)
  }

  const updateRootStrict = (next: boolean) => {
    if (next === rootStrict) return
    setRootStrict(next)
    const json = serializeSchema(fields, rootKind, next)
    setJsonText(json)
    emit(json)
  }

  const handleJsonChange = (text: string) => {
    setJsonText(text)
    emit(text)
    const reparsed = parseSchema(text)
    if (reparsed.ok) {
      setFields(reparsed.fields)
      setRootKind(reparsed.rootKind)
      setRootStrict(reparsed.rootStrict)
      setVisualLocked(false)
    } else {
      setVisualLocked(true)
    }
  }

  const handleSwitchToVisual = () => {
    if (visualLocked) return
    setMode('visual')
  }

  const handleSwitchToJson = () => {
    setMode('json')
  }

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(jsonText)
    } catch {
      // Clipboard pode falhar em contexto não-secure — ignora.
    }
  }

  const openImport = () => {
    setImportText('')
    setImportError(null)
    setImportOpen(true)
  }

  const closeImport = () => {
    setImportOpen(false)
  }

  const applyImport = () => {
    const trimmed = importText.trim()
    if (!trimmed) {
      setImportError('Cole um JSON de exemplo antes de converter.')
      return
    }
    let sample: unknown
    try {
      sample = JSON.parse(trimmed)
    } catch {
      setImportError('JSON inválido. Verifique a sintaxe e tente novamente.')
      return
    }
    // Só objetos ou listas-de-objetos viram schema editável (raiz). Primitivos
    // ou listas-de-primitivos na raiz não cabem no builder visual.
    const isObject = sample !== null && typeof sample === 'object' && !Array.isArray(sample)
    const isArrayOfObjects =
      Array.isArray(sample) &&
      sample.length > 0 &&
      sample.every((item) => item !== null && typeof item === 'object' && !Array.isArray(item))
    if (!isObject && !isArrayOfObjects) {
      setImportError('A raiz precisa ser um objeto ou uma lista de objetos.')
      return
    }
    const inferred = inferSchema(sample)
    const json = JSON.stringify(inferred, null, 2)
    setJsonText(json)
    emit(json)
    const reparsed = parseSchema(json)
    if (reparsed.ok) {
      setFields(reparsed.fields)
      setRootKind(reparsed.rootKind)
      setVisualLocked(false)
      setMode('visual')
    }
    setImportOpen(false)
  }

  return (
    <div className="space-y-3">
      <ModeToggle
        mode={mode}
        visualLocked={visualLocked}
        onVisual={handleSwitchToVisual}
        onJson={handleSwitchToJson}
        onCopy={handleCopy}
        onImport={openImport}
      />

      {visualLocked && mode === 'json' && (
        <div className="rounded-lg border border-warning/40 bg-warning/10 px-3 py-2 text-[11px] text-warning">
          {lockedFeature ? (
            <>
              Schema usa{' '}
              <code className="rounded bg-warning/20 px-1 py-0.5 font-mono text-warning">
                {lockedFeature}
              </code>{' '}
              — o editor visual não suporta esta feature; remova-a do JSON pra voltar ao modo
              Visual.
            </>
          ) : (
            <>
              Schema com estruturas avançadas — edite simplificando pra um objeto plano antes de
              voltar ao modo Visual.
            </>
          )}
        </div>
      )}

      {mode === 'visual' ? (
        <div className="space-y-3">
          {!flatOnly && (
            <div className="flex flex-wrap items-center gap-3">
              <RootKindToggle rootKind={rootKind} onChange={updateRootKind} />
              <RootStrictToggle
                strict={rootStrict}
                isArrayRoot={rootKind === 'array-of-object'}
                onChange={updateRootStrict}
              />
            </div>
          )}
          <FieldList
            fields={fields}
            flatOnly={flatOnly}
            addLabel={addLabel}
            emptyHint={
              rootKind === 'array-of-object'
                ? 'Cada item da lista ainda não tem campos.'
                : emptyHint
            }
            onChange={updateFields}
            depth={0}
          />
        </div>
      ) : (
        <textarea
          className="min-h-[260px] w-full resize-y rounded-lg border border-border bg-surface px-3 py-2 font-mono text-[12px] leading-5 text-fg placeholder:text-fg-dim focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/30"
          value={jsonText}
          onChange={(e) => handleJsonChange(e.target.value)}
          spellCheck={false}
        />
      )}

      <Modal
        open={importOpen}
        onClose={closeImport}
        size="lg"
        title="Importar do JSON de exemplo"
        description="Cole um JSON de exemplo (ex.: a resposta real da sua API) e nós inferimos o schema editável."
        footer={
          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={closeImport}>
              Cancelar
            </Button>
            <Button onClick={applyImport} leftIcon={<SparklesIcon className="h-4 w-4" />}>
              Converter
            </Button>
          </div>
        }
      >
        <div className="space-y-3">
          <p className="text-[11px] text-fg-muted">
            Tipos são inferidos pelos valores. Para listas, o builder funde os schemas dos itens —
            chaves presentes em todos viram <span className="font-mono">required</span>.
          </p>
          <textarea
            className="min-h-[260px] w-full resize-y rounded-lg border border-border bg-surface px-3 py-2 font-mono text-[12px] leading-5 text-fg placeholder:text-fg-dim focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/30"
            value={importText}
            onChange={(e) => {
              setImportText(e.target.value)
              if (importError) setImportError(null)
            }}
            placeholder={'{\n  "items": [\n    { "id": 1, "name": "foo" }\n  ],\n  "total": 42\n}'}
            spellCheck={false}
            autoFocus
          />
          {importError && (
            <div className="rounded-lg border border-danger/40 bg-danger/10 px-3 py-2 text-[11px] text-danger">
              {importError}
            </div>
          )}
        </div>
      </Modal>
    </div>
  )
}

interface RootKindToggleProps {
  rootKind: RootKind
  onChange: (next: RootKind) => void
}

function RootKindToggle({ rootKind, onChange }: RootKindToggleProps) {
  return (
    <div className="flex items-center gap-2 text-[11px] text-fg-muted">
      <span className="uppercase tracking-wider">Tipo do schema</span>
      <div className="inline-flex items-center rounded-lg border border-border bg-bg-soft p-0.5">
        <ModeButton active={rootKind === 'object'} onClick={() => onChange('object')}>
          Objeto
        </ModeButton>
        <ModeButton
          active={rootKind === 'array-of-object'}
          onClick={() => onChange('array-of-object')}
        >
          Lista de objetos
        </ModeButton>
      </div>
    </div>
  )
}

interface RootStrictToggleProps {
  strict: boolean
  /** Quando true, o toggle se aplica aos items do array raiz — copy
   *  adapta pra refletir isso. */
  isArrayRoot: boolean
  onChange: (next: boolean) => void
}

// Toggle pra `additionalProperties:false` na raiz (ou nos items, quando
// a raiz é array). Foundry strict EXIGE essa flag em todo objeto pra que
// o LLM gere outputs válidos; tools genéricas HTTP que aceitam body
// arbitrário precisam desligar. Default herda do schema importado.
function RootStrictToggle({ strict, isArrayRoot, onChange }: RootStrictToggleProps) {
  return (
    <label
      className="inline-flex items-center gap-2 rounded-lg border border-border bg-bg-soft px-2 py-1.5 text-[11px] text-fg-muted"
      title={
        isArrayRoot
          ? 'Quando ligado, cada item da lista rejeita campos não declarados (additionalProperties:false). Necessário em outputs Foundry strict.'
          : 'Quando ligado, o objeto rejeita campos não declarados (additionalProperties:false). Necessário em outputs Foundry strict.'
      }
    >
      <input
        type="checkbox"
        className="h-3.5 w-3.5 accent-accent"
        checked={strict}
        onChange={(e) => onChange(e.target.checked)}
      />
      <span className="uppercase tracking-wider">Modo strict</span>
    </label>
  )
}

interface ModeToggleProps {
  mode: Mode
  visualLocked: boolean
  onVisual: () => void
  onJson: () => void
  onCopy: () => void
  onImport: () => void
}

function ModeToggle({ mode, visualLocked, onVisual, onJson, onCopy, onImport }: ModeToggleProps) {
  return (
    <div className="flex items-center justify-between gap-2">
      <div className="inline-flex items-center rounded-lg border border-border bg-bg-soft p-0.5">
        <ModeButton active={mode === 'visual'} disabled={visualLocked} onClick={onVisual}>
          Visual
        </ModeButton>
        <ModeButton active={mode === 'json'} onClick={onJson}>
          JSON
        </ModeButton>
      </div>
      <div className="flex items-center gap-1">
        <button
          type="button"
          onClick={onImport}
          title="Cole um JSON de exemplo e o builder gera o schema"
          className="inline-flex items-center gap-1.5 rounded-md px-2 py-1 text-[10px] uppercase tracking-wider text-fg-muted hover:bg-surface hover:text-fg"
        >
          <SparklesIcon className="h-3.5 w-3.5" />
          Importar JSON
        </button>
        {mode === 'json' && (
          <button
            type="button"
            onClick={onCopy}
            className="rounded-md px-2 py-1 text-[10px] uppercase tracking-wider text-fg-muted hover:bg-surface hover:text-fg"
          >
            copiar
          </button>
        )}
      </div>
    </div>
  )
}

function ModeButton({
  active,
  disabled,
  onClick,
  children,
}: {
  active: boolean
  disabled?: boolean
  onClick: () => void
  children: React.ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      title={disabled ? 'Schema avançado não suportado pelo modo Visual' : undefined}
      className={cn(
        'rounded-md px-3 py-1 text-xs font-medium transition',
        active ? 'bg-surface text-fg shadow-sm' : 'text-fg-muted hover:text-fg',
        disabled && 'cursor-not-allowed opacity-40 hover:text-fg-muted',
      )}
    >
      {children}
    </button>
  )
}

interface FieldListProps {
  fields: FieldRow[]
  flatOnly: boolean
  addLabel: string
  emptyHint: string
  depth: number
  onChange: (fields: FieldRow[]) => void
}

// Lista plana de campos do mesmo nível — recursiva via FieldEditor quando o
// tipo é object. Limita profundidade visual em 4 (proteção contra schemas
// extremamente aninhados que estouram a UI).
const MAX_DEPTH = 4

function FieldList({
  fields,
  flatOnly,
  addLabel,
  emptyHint,
  depth,
  onChange,
}: FieldListProps) {
  const setField = (id: string, patch: Partial<FieldRow>) => {
    onChange(fields.map((f) => (f.id === id ? { ...f, ...patch } : f)))
  }

  const setChildren = (id: string, children: FieldRow[]) => {
    onChange(fields.map((f) => (f.id === id ? { ...f, children } : f)))
  }

  const removeField = (id: string) => {
    onChange(fields.filter((f) => f.id !== id))
  }

  const addField = () => {
    onChange([...fields, emptyField()])
  }

  return (
    <div className="space-y-3">
      {fields.length === 0 && (
        <div className="rounded-lg border border-dashed border-border px-4 py-6 text-center text-xs text-fg-muted">
          {emptyHint}
        </div>
      )}

      {fields.map((field) => (
        <FieldEditor
          key={field.id}
          field={field}
          flatOnly={flatOnly}
          depth={depth}
          siblings={fields}
          onChange={(patch) => setField(field.id, patch)}
          onChildrenChange={(children) => setChildren(field.id, children)}
          onRemove={() => removeField(field.id)}
        />
      ))}

      <Button
        variant="secondary"
        size="sm"
        leftIcon={<PlusIcon className="h-3.5 w-3.5" />}
        onClick={addField}
      >
        {addLabel}
      </Button>
    </div>
  )
}

interface FieldEditorProps {
  field: FieldRow
  flatOnly: boolean
  depth: number
  siblings: FieldRow[]
  onChange: (patch: Partial<FieldRow>) => void
  onChildrenChange: (children: FieldRow[]) => void
  onRemove: () => void
}

function FieldEditor({
  field,
  flatOnly,
  depth,
  siblings,
  onChange,
  onChildrenChange,
  onRemove,
}: FieldEditorProps) {
  const duplicate =
    field.name.trim() !== '' &&
    siblings.some(
      (other) => other.id !== field.id && other.name.trim() === field.name.trim(),
    )

  // Profundidade máxima atingida: bloqueia object/array pra evitar UI infinita.
  const reachedMaxDepth = depth >= MAX_DEPTH - 1
  const typeOptions = (flatOnly || reachedMaxDepth)
    ? FIELD_TYPE_OPTIONS.filter((o) => o.value !== 'object' && o.value !== 'array')
    : FIELD_TYPE_OPTIONS

  // Sub-builder de object/array-of-object pode crescer demais em schemas
  // populados (5+ campos aninhados deslocam o resto da UI pra fora da tela).
  // Default colapsado quando o field já chega com children (típico de edição
  // de schema existente); expandido quando vazio (usuário acabou de
  // adicionar e quer preencher).
  const hasNested =
    field.type === 'object' ||
    (field.type === 'array' && field.itemType === 'object')
  const [collapsed, setCollapsed] = useState<boolean>(
    hasNested && field.children.length > 0,
  )

  return (
    <div
      className={cn(
        'rounded-xl border bg-bg-soft p-3 transition',
        duplicate ? 'border-danger/40' : 'border-border',
      )}
    >
      <div className="grid grid-cols-12 gap-3">
        <div className="col-span-12 md:col-span-5">
          <Input
            label="Campo"
            value={field.name}
            onChange={(e) => onChange({ name: e.target.value })}
            placeholder="ex.: nomeCompleto"
            monospace
            error={duplicate ? 'nome duplicado' : undefined}
          />
        </div>
        <div className="col-span-7 md:col-span-3">
          <Select
            label="Tipo"
            value={field.type}
            onChange={(e) => onChange({ type: e.target.value as FieldType })}
            options={typeOptions}
          />
        </div>
        <div className="col-span-5 md:col-span-3 flex items-end gap-3 pb-2">
          <label className="inline-flex items-center gap-2 text-xs text-fg-muted">
            <input
              type="checkbox"
              className="h-3.5 w-3.5 accent-accent"
              checked={field.required}
              onChange={(e) => onChange({ required: e.target.checked })}
            />
            obrigatório
          </label>
          {/* "Permite vazio" emite anyOf:[T,null] na serialização — padrão
              Foundry strict pra campos nullable. Ortogonal a obrigatório:
              um required pode aceitar null (presença obrigatória, valor
              opcional). */}
          <label
            className="inline-flex items-center gap-2 text-xs text-fg-muted"
            title="Permite que o campo venha com valor null (Foundry strict: anyOf [tipo, null])."
          >
            <input
              type="checkbox"
              className="h-3.5 w-3.5 accent-accent"
              checked={field.nullable}
              onChange={(e) => onChange({ nullable: e.target.checked })}
            />
            permite vazio
          </label>
        </div>
        <div className="col-span-12 md:col-span-1 flex items-end justify-end pb-1">
          <IconButton
            aria-label="Remover campo"
            variant="danger"
            size="sm"
            onClick={onRemove}
          >
            <CloseIcon className="h-4 w-4" />
          </IconButton>
        </div>

        {field.type === 'array' && (
          <div className="col-span-12 md:col-span-5">
            <Select
              label="O que a lista contém"
              value={field.itemType}
              onChange={(e) => {
                const next = e.target.value as ArrayItemType
                if (!isArrayItemType(next)) return
                onChange({ itemType: next })
              }}
              options={
                // Bloqueia "lista de objetos" quando profundidade max atingida
                // ou quando flatOnly (FormUrlEncoded).
                reachedMaxDepth || flatOnly
                  ? ARRAY_ITEM_OPTIONS.filter((o) => o.value !== 'object')
                  : ARRAY_ITEM_OPTIONS
              }
            />
          </div>
        )}

        <div className="col-span-12">
          <Input
            label="Descrição"
            value={field.description}
            onChange={(e) => onChange({ description: e.target.value })}
            placeholder="Para que serve esse campo? O agente lê essa descrição."
          />
        </div>

        {/* Constraints específicas do tipo: enum, min/max numérico e strict
            object. Mostradas só pros tipos compatíveis pra não poluir a UI
            de tipos triviais. Pra arrays, os controles aplicam aos items
            (que é onde JSON Schema permite essas constraints). */}
        <FieldConstraints field={field} onChange={onChange} />
      </div>

      {/* Sub-builder pra objetos aninhados (type=object ou lista-de-objetos).
          Indentado com borda esquerda destacada pra reforçar hierarquia.
          Header é botão clicável que minimiza/maximiza a lista de campos —
          essencial em schemas grandes pra não deslocar o resto da UI. */}
      {hasNested && (
        <div className="mt-4 border-l-2 border-accent/40 pl-4">
          <button
            type="button"
            onClick={() => setCollapsed((c) => !c)}
            aria-expanded={!collapsed}
            className="mb-2 flex w-full items-center gap-2 text-left text-[11px] font-medium text-fg-muted transition hover:text-fg"
          >
            <svg
              viewBox="0 0 24 24"
              className={cn(
                'h-3.5 w-3.5 transition-transform',
                !collapsed && 'rotate-90',
              )}
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
            >
              <path d="M9 18 15 12 9 6" />
            </svg>
            <span>
              {field.type === 'array' ? (
                <>
                  Campos de cada item em{' '}
                  <span className="font-mono text-accent">{field.name.trim() || 'lista'}</span>
                </>
              ) : (
                <>
                  Campos de{' '}
                  <span className="font-mono text-accent">{field.name.trim() || 'objeto'}</span>
                </>
              )}
            </span>
            {/* Contador discreto à direita quando colapsado pra dar contexto
                sem precisar expandir. "1 campo" / "5 campos" / "vazio". */}
            <span className="ml-auto text-[10px] uppercase tracking-wider text-fg-dim">
              {field.children.length === 0
                ? 'vazio'
                : `${field.children.length} ${field.children.length === 1 ? 'campo' : 'campos'}`}
            </span>
          </button>
          {!collapsed && (
            <FieldList
              fields={field.children}
              flatOnly={flatOnly}
              addLabel={
                field.type === 'array' ? 'Adicionar campo do item' : 'Adicionar sub-campo'
              }
              emptyHint={
                field.type === 'array'
                  ? 'Cada item dessa lista ainda não tem campos.'
                  : 'Esse objeto ainda não tem sub-campos.'
              }
              depth={depth + 1}
              onChange={onChildrenChange}
            />
          )}
        </div>
      )}
    </div>
  )
}

interface FieldConstraintsProps {
  field: FieldRow
  onChange: (patch: Partial<FieldRow>) => void
}

// Determina o "tipo efetivo" pra fins de constraints:
// - primitivos: o próprio type
// - array de primitivo: o itemType (constraints aplicam aos items)
// - array de object / object / date / datetime: null (sem constraints
//   numéricas/enum significativas)
function effectiveConstraintType(field: FieldRow): PrimitiveType | null {
  if (isPrimitive(field.type)) return field.type
  if (field.type === 'array' && isPrimitive(field.itemType)) return field.itemType
  return null
}

function FieldConstraints({ field, onChange }: FieldConstraintsProps) {
  const constraintType = effectiveConstraintType(field)
  const isNumeric = constraintType === 'number' || constraintType === 'integer'
  const supportsEnum = constraintType !== null
  const supportsStrict =
    field.type === 'object' || (field.type === 'array' && field.itemType === 'object')

  // Quando o tipo não tolera enum/min/max/strict, esconde a linha inteira
  // pra evitar inputs órfãos no FieldEditor.
  if (!supportsEnum && !isNumeric && !supportsStrict) return null

  // Enum: textarea com um valor por linha. Trim e drop de vazios acontece
  // no serializer; aqui guardamos o texto cru pro user editar livremente.
  // Pra strings, suporta valores arbitrários; pra number/integer/boolean,
  // o user precisa digitar valores do tipo correto (coerce no serializer
  // tenta converter, valores inválidos viajam como string e o backend
  // reporta erro de schema).
  const enumPlaceholder =
    constraintType === 'string'
      ? 'um valor por linha (ex.: pendente, aprovado)'
      : constraintType === 'number' || constraintType === 'integer'
        ? 'um número por linha'
        : 'true ou false'

  return (
    <>
      {supportsEnum && (
        <div className="col-span-12">
          <label className="mb-1 block text-[11px] font-semibold uppercase tracking-wider text-fg-dim">
            {field.type === 'array'
              ? 'Valores permitidos em cada item (opcional)'
              : 'Valores permitidos (opcional)'}
          </label>
          <textarea
            className="min-h-[60px] w-full resize-y rounded-lg border border-border bg-surface px-2.5 py-1.5 font-mono text-[12px] leading-5 text-fg placeholder:text-fg-dim focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/30"
            value={field.enumValues.join('\n')}
            placeholder={enumPlaceholder}
            onChange={(e) =>
              onChange({
                enumValues: e.target.value.split('\n'),
              })
            }
          />
          <p className="mt-1 text-[10px] text-fg-dim">
            Deixe vazio pra aceitar qualquer valor do tipo. Cada linha é um valor exato
            {field.type === 'array' ? ' aceito em cada item da lista.' : ' aceito no campo.'}
          </p>
        </div>
      )}

      {isNumeric && (
        <>
          <div className="col-span-6 md:col-span-3">
            <Input
              label="Mínimo (opcional)"
              value={field.numberMin}
              onChange={(e) => onChange({ numberMin: e.target.value })}
              placeholder="ex.: 0"
              monospace
            />
          </div>
          <div className="col-span-6 md:col-span-3">
            <Input
              label="Máximo (opcional)"
              value={field.numberMax}
              onChange={(e) => onChange({ numberMax: e.target.value })}
              placeholder="ex.: 100"
              monospace
            />
          </div>
        </>
      )}

      {supportsStrict && (
        <div className="col-span-12">
          <label
            className="inline-flex items-center gap-2 text-xs text-fg-muted"
            title="Quando ligado, rejeita campos não declarados (additionalProperties:false). Necessário pra outputs Foundry strict."
          >
            <input
              type="checkbox"
              className="h-3.5 w-3.5 accent-accent"
              checked={field.strictObject}
              onChange={(e) => onChange({ strictObject: e.target.checked })}
            />
            modo strict — rejeitar campos não declarados
          </label>
        </div>
      )}
    </>
  )
}
