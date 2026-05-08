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
  description: string
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
    description: '',
    itemType: 'string',
    children: [],
  }
}

// Parser recursivo. Aceita schema vazio (`{}` ou ausente). Retorna ok=false
// quando algum campo usa estrutura não suportada (anyOf, $ref, array de
// objetos), forçando o modo JSON.
function parseProperties(
  props: Record<string, unknown>,
  requiredSet: Set<string>,
): FieldRow[] | null {
  const fields: FieldRow[] = []
  for (const [name, schema] of Object.entries(props)) {
    if (!schema || typeof schema !== 'object') return null
    const propSchema = schema as Record<string, unknown>
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
        description,
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
        description,
        children,
      })
      continue
    }

    if (t === 'array') {
      const items = propSchema.items
      if (!items || typeof items !== 'object') return null
      const itemsObj = items as Record<string, unknown>
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
          description,
          itemType: itemKind,
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
          description,
          itemType: 'object',
          children,
        })
        continue
      }

      // tipo de items desconhecido — aborta pra forçar modo JSON.
      return null
    }

    // tipo desconhecido (null union, anyOf etc.) — aborta.
    return null
  }
  return fields
}

function parseSchema(raw: string): ParsedSchema {
  const fallback: ParsedSchema = { ok: false, fields: [], rootKind: 'object' }
  if (!raw || !raw.trim()) return { ok: true, fields: [], rootKind: 'object' }

  let parsed: unknown
  try {
    parsed = JSON.parse(raw)
  } catch {
    return fallback
  }

  if (!parsed || typeof parsed !== 'object') return fallback
  const root = parsed as Record<string, unknown>

  // Raiz = lista de objetos: aceita {type:'array',items:{type:'object',...}}
  // e edita os campos do item como se fossem campos da raiz.
  if (root.type === 'array') {
    const items = root.items
    if (!items || typeof items !== 'object') return fallback
    const itemsObj = items as Record<string, unknown>
    if (itemsObj.type !== 'object') return fallback
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
    return { ok: true, fields, rootKind: 'array-of-object' }
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
  return { ok: true, fields, rootKind: 'object' }
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
    const schema: Record<string, unknown> =
      f.type === 'date' || f.type === 'datetime'
        ? { type: 'string', format: STRING_FORMAT_FOR_TYPE[f.type] }
        : { type: f.type }
    if (f.description.trim()) schema.description = f.description.trim()

    if (f.type === 'array') {
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
        schema.items = items
      } else if (f.itemType === 'date' || f.itemType === 'datetime') {
        schema.items = { type: 'string', format: STRING_FORMAT_FOR_TYPE[f.itemType] }
      } else {
        schema.items = { type: f.itemType }
      }
    } else if (f.type === 'object') {
      const sub = buildSchemaObject(f.children)
      schema.properties = sub.properties ?? {}
      if (Array.isArray(sub.required) && (sub.required as string[]).length > 0) {
        schema.required = sub.required
      }
    }

    properties[name] = schema
    if (f.required) required.push(name)
  }

  const result: Record<string, unknown> = { type: 'object', properties }
  if (required.length > 0) result.required = required
  return result
}

function serializeSchema(fields: FieldRow[], rootKind: RootKind): string {
  const inner = buildSchemaObject(fields)
  if (rootKind === 'array-of-object') {
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
  const [mode, setMode] = useState<Mode>(initialParse.ok ? 'visual' : 'json')
  const [jsonText, setJsonText] = useState<string>(value)
  const [visualLocked, setVisualLocked] = useState<boolean>(!initialParse.ok)
  const [importOpen, setImportOpen] = useState(false)
  const [importText, setImportText] = useState('')
  const [importError, setImportError] = useState<string | null>(null)

  const lastEmittedRef = useRef<string>(
    initialParse.ok ? serializeSchema(initialParse.fields, initialParse.rootKind) : value,
  )

  useEffect(() => {
    if (value === lastEmittedRef.current) return
    setJsonText(value)
    const reparsed = parseSchema(value)
    if (reparsed.ok) {
      setFields(reparsed.fields)
      setRootKind(reparsed.rootKind)
      setVisualLocked(false)
    } else {
      setVisualLocked(true)
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
    const json = serializeSchema(next, rootKind)
    setJsonText(json)
    emit(json)
  }

  const updateRootKind = (next: RootKind) => {
    if (next === rootKind) return
    setRootKind(next)
    const json = serializeSchema(fields, next)
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
          Schema com estruturas avançadas — edite simplificando pra um objeto plano antes de
          voltar ao modo Visual.
        </div>
      )}

      {mode === 'visual' ? (
        <div className="space-y-3">
          {!flatOnly && (
            <RootKindToggle rootKind={rootKind} onChange={updateRootKind} />
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
        <div className="col-span-5 md:col-span-3 flex items-end">
          <label className="inline-flex items-center gap-2 pb-2 text-xs text-fg-muted">
            <input
              type="checkbox"
              className="h-3.5 w-3.5 accent-accent"
              checked={field.required}
              onChange={(e) => onChange({ required: e.target.checked })}
            />
            obrigatório
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
      </div>

      {/* Sub-builder pra objetos aninhados (type=object ou lista-de-objetos).
          Indentado com borda esquerda destacada pra reforçar hierarquia. */}
      {(field.type === 'object' ||
        (field.type === 'array' && field.itemType === 'object')) && (
        <div className="mt-4 border-l-2 border-accent/40 pl-4">
          <div className="mb-2 flex items-center gap-2 text-[11px] font-medium text-fg-muted">
            <svg
              viewBox="0 0 24 24"
              className="h-3.5 w-3.5"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
            >
              <path d="M9 18 15 12 9 6" />
            </svg>
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
          </div>
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
        </div>
      )}
    </div>
  )
}
