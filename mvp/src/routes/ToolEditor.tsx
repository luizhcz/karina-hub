import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import {
  createGenericTool,
  extractPlaceholders,
  getGenericTool,
  updateGenericTool,
  type CreateGenericToolBody,
  type GenericTool,
  type HttpMethodType,
  type InputContentType,
  type OutputContentType,
  type ParamDefinition,
} from '../api/genericTools'
import { friendlyError } from '../api/client'
import { KvTable, type KvRow } from '../components/PostmanEditor/KvTable'
import {
  ArrowLeftIcon,
  Badge,
  Button,
  Card,
  CardHeader,
  ErrorMessage,
  Input,
  JsonSchemaBuilder,
  Select,
  Spinner,
  Textarea,
  cn,
} from '../ui'

interface Props {
  mode: 'create' | 'edit'
}

type Tab = 'params' | 'headers' | 'body' | 'response'

const DEFAULT_JSON = '{\n  "type": "object",\n  "properties": {}\n}'
const PARAM_TYPE_OPTIONS = [
  { value: 'string', label: 'string' },
  { value: 'number', label: 'number' },
  { value: 'integer', label: 'integer' },
  { value: 'boolean', label: 'boolean' },
]

interface ParamRow {
  type: string
  description: string
  required: boolean
  /** marca rows que vieram do auto-detect da URL */
  source?: 'path'
}

// Form state — Id da ferramenta NÃO faz parte: no modo create é gerado
// automaticamente via crypto.randomUUID() no momento do submit (PMs não
// precisam saber/escolher); no modo edit o id é imutável e vem da rota.
interface FormState {
  name: string
  description: string
  whenToUse: string
  method: HttpMethodType
  url: string
  pathRows: KvRow<ParamRow>[]
  queryRows: KvRow<ParamRow>[]
  headerRows: KvRow<string>[]
  inputContentType: InputContentType
  inputBodyExample: string
  textBodyFieldName: string
  outputContentType: OutputContentType
  outputExample: string
  outputDescription: string
  timeoutSeconds: string
}

function emptyForm(): FormState {
  return {
    name: '',
    description: '',
    whenToUse: '',
    method: 'GET',
    url: '',
    pathRows: [],
    queryRows: [],
    headerRows: [],
    inputContentType: 'None',
    // Strings vazias — JsonSchemaBuilder aceita schema vazio e o user começa
    // pelo botão "Adicionar campo" do próprio builder. Ao adicionar o primeiro
    // campo, o builder serializa o JSON Schema canônico via onChange.
    inputBodyExample: '',
    textBodyFieldName: 'body',
    outputContentType: 'Json',
    outputExample: '',
    outputDescription: '',
    timeoutSeconds: '',
  }
}

// Gera identificador único para nova ferramenta. Usa crypto.randomUUID quando
// disponível (browsers modernos via secure context), com fallback baseado em
// Math.random pra ambientes sem suporte (ex: file:// no Safari antigo).
function generateToolId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }
  const segment = () => Math.random().toString(36).slice(2, 10)
  return `${segment()}-${segment()}-${segment()}-${segment()}`
}

function newId() {
  return Math.random().toString(36).slice(2, 10)
}

function fromTool(tool: GenericTool): FormState {
  const pathRows: KvRow<ParamRow>[] = Object.entries(tool.pathParams).map(([key, def]) => ({
    id: newId(),
    key,
    val: { type: def.type || 'string', description: def.description, required: def.required, source: 'path' },
  }))
  const queryRows: KvRow<ParamRow>[] = Object.entries(tool.queryParams).map(([key, def]) => ({
    id: newId(),
    key,
    val: { type: def.type || 'string', description: def.description, required: def.required },
  }))
  const headerRows: KvRow<string>[] = Object.entries(tool.customHeaders).map(([key, val]) => ({
    id: newId(),
    key,
    val,
  }))

  let textField = 'body'
  if (tool.inputContentType === 'Text' && tool.inputSchema) {
    try {
      const parsed = JSON.parse(tool.inputSchema)
      const first = parsed?.properties && typeof parsed.properties === 'object'
        ? Object.keys(parsed.properties)[0]
        : null
      if (first) textField = first
    } catch {
      // mantém default
    }
  }

  return {
    name: tool.name,
    description: tool.description,
    whenToUse: tool.whenToUse ?? '',
    method: tool.httpMethod,
    url: tool.urlTemplate,
    pathRows,
    queryRows,
    headerRows,
    inputContentType: tool.inputContentType,
    inputBodyExample:
      tool.inputSchema && tool.inputContentType !== 'Text' ? tool.inputSchema : DEFAULT_JSON,
    textBodyFieldName: textField,
    outputContentType: tool.outputContentType,
    outputExample:
      tool.outputContentType !== 'Text' ? tool.outputSchema || DEFAULT_JSON : DEFAULT_JSON,
    outputDescription: tool.outputContentType === 'Text' ? tool.outputSchema || '' : '',
    timeoutSeconds: tool.timeoutSecondsOverride?.toString() ?? '',
  }
}

export function ToolEditor({ mode }: Props) {
  const navigate = useNavigate()
  const { id } = useParams<{ id?: string }>()
  const [form, setForm] = useState<FormState>(emptyForm)
  const [tab, setTab] = useState<Tab>('params')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [loading, setLoading] = useState(mode === 'edit')
  const [existingUpdatedAt, setExistingUpdatedAt] = useState<string | null>(null)

  // Carrega tool existente em modo edit.
  useEffect(() => {
    if (mode !== 'edit' || !id) return
    let cancelled = false
    setLoading(true)
    getGenericTool(id)
      .then((tool) => {
        if (cancelled) return
        setForm(fromTool(tool))
        setExistingUpdatedAt(tool.updatedAt)
      })
      .catch((err: unknown) => {
        if (cancelled) return
        setLoadError(friendlyError(err, 'Não foi possível carregar a ferramenta.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [mode, id])

  const detectedPlaceholders = useMemo(() => extractPlaceholders(form.url), [form.url])

  // Auto-popula path rows com placeholders detectados na URL. Mantém locked
  // (chave vem da URL) e remove rows antigas cuja chave saiu da URL.
  useEffect(() => {
    setForm((prev) => {
      const placeholderSet = new Set(detectedPlaceholders)
      const existingKeys = new Set(prev.pathRows.map((r) => r.key))
      const filtered: KvRow<ParamRow>[] = []
      for (const r of prev.pathRows) {
        if (placeholderSet.has(r.key)) {
          filtered.push({ ...r, locked: true })
        }
      }
      for (const p of detectedPlaceholders) {
        if (!existingKeys.has(p)) {
          filtered.push({
            id: newId(),
            key: p,
            val: { type: 'string', description: '', required: true, source: 'path' },
            locked: true,
          })
        }
      }
      if (
        filtered.length === prev.pathRows.length &&
        filtered.every((r, i) => r === prev.pathRows[i])
      ) {
        return prev
      }
      return { ...prev, pathRows: filtered }
    })
  }, [detectedPlaceholders])

  const set = <K extends keyof FormState>(key: K, value: FormState[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }))
  }

  const buildBody = (): CreateGenericToolBody | null => {
    if (!form.name.trim()) {
      setError('Informe um nome para a ferramenta.')
      return null
    }
    if (!form.url.trim()) {
      setError('Informe a URL.')
      return null
    }

    const pathParams: Record<string, ParamDefinition> = {}
    for (const r of form.pathRows) {
      if (!r.key.trim()) continue
      pathParams[r.key.trim()] = {
        type: r.val.type || 'string',
        description: r.val.description,
        required: true,
      }
    }

    const queryParams: Record<string, ParamDefinition> = {}
    for (const r of form.queryRows) {
      if (!r.key.trim()) continue
      queryParams[r.key.trim()] = {
        type: r.val.type || 'string',
        description: r.val.description,
        required: r.val.required,
      }
    }

    const customHeaders: Record<string, string> = {}
    for (const r of form.headerRows) {
      const key = r.key.trim()
      if (!key) continue
      const lower = key.toLowerCase()
      if (lower === 'content-type' || lower === 'accept') continue
      customHeaders[key] = r.val
    }

    const isPost = form.method === 'POST'
    const inputContentType: InputContentType = isPost ? form.inputContentType : 'None'
    let inputSchema: string | null = null
    if (isPost) {
      if (inputContentType === 'Json' || inputContentType === 'FormUrlEncoded') {
        inputSchema = form.inputBodyExample
      } else if (inputContentType === 'Text') {
        const fieldName = form.textBodyFieldName.trim() || 'body'
        inputSchema = JSON.stringify({
          type: 'object',
          properties: { [fieldName]: { type: 'string' } },
          required: [fieldName],
        })
      }
    }

    let outputSchema: string | null = null
    if (form.outputContentType === 'Json' || form.outputContentType === 'Csv') {
      outputSchema = form.outputExample
    } else {
      outputSchema = form.outputDescription.trim() || null
    }

    const timeout = form.timeoutSeconds.trim() ? Number(form.timeoutSeconds) : null
    if (timeout !== null && (Number.isNaN(timeout) || timeout <= 0)) {
      setError('Timeout deve ser um número maior que zero.')
      return null
    }

    return {
      // Id é gerado pelo front no modo create (o caller resolve via
      // generateToolId no onSave). No modo edit, o id vai pela rota e
      // CreateGenericToolBody.id é ignorado pelo backend no PUT.
      id: undefined,
      name: form.name.trim(),
      description: form.description,
      whenToUse: form.whenToUse.trim() || null,
      httpMethod: form.method,
      urlTemplate: form.url.trim(),
      pathParams,
      queryParams,
      customHeaders,
      inputContentType,
      inputSchema,
      outputContentType: form.outputContentType,
      outputSchema,
      timeoutSecondsOverride: timeout,
    }
  }

  const onSave = async () => {
    setError(null)
    const body = buildBody()
    if (!body) return
    setSubmitting(true)
    try {
      if (mode === 'edit' && id && existingUpdatedAt) {
        const { id: _omit, ...rest } = body
        void _omit
        await updateGenericTool(id, { ...rest, expectedUpdatedAt: existingUpdatedAt })
      } else {
        await createGenericTool({ ...body, id: generateToolId() })
      }
      navigate('/ferramentas')
    } catch (err) {
      setError(friendlyError(err, 'Não foi possível salvar.'))
    } finally {
      setSubmitting(false)
    }
  }

  if (loading) {
    return (
      <Card className="mx-auto max-w-5xl flex items-center justify-center py-12">
        <Spinner className="h-6 w-6 text-fg-muted" />
      </Card>
    )
  }
  if (loadError) {
    return <ErrorMessage message={loadError} className="mx-auto max-w-5xl" />
  }

  const showBody = form.method === 'POST'

  return (
    <div className="mx-auto max-w-5xl">
      <div className="mb-6 flex items-center justify-between">
        <div>
          <Button
            variant="ghost"
            size="sm"
            leftIcon={<ArrowLeftIcon className="h-4 w-4" />}
            onClick={() => navigate('/ferramentas')}
            className="-ml-2 mb-1"
          >
            Voltar
          </Button>
          <h1 className="text-2xl font-semibold tracking-tight">
            {mode === 'edit' ? 'Editar ferramenta' : 'Nova ferramenta'}
          </h1>
        </div>
      </div>

      {/* Identificação */}
      <Card className="mb-5 space-y-4">
        <Input
          label="Nome"
          value={form.name}
          onChange={(e) => set('name', e.target.value)}
          placeholder="Ex.: Buscar Cliente"
          autoFocus
        />
        <Textarea
          label="Descrição"
          value={form.description}
          onChange={(e) => set('description', e.target.value)}
          placeholder="O que o endpoint faz e o que ele retorna."
          hint="Esse texto vai pro prompt do agente como descrição da ferramenta."
        />
        <Textarea
          label="Quando usar"
          value={form.whenToUse}
          onChange={(e) => set('whenToUse', e.target.value)}
          placeholder="Ex.: o usuário pergunta sobre preço ou variação de um ticker específico."
          hint="Gatilho que orienta o agente a invocar essa tool. Vira a linha 'Use quando: ...' no prompt."
        />
      </Card>

      {/* URL bar estilo Postman */}
      <Card className="mb-5" padded={false}>
        <div className="p-4">
          <div className="flex items-stretch gap-2">
            <select
              value={form.method}
              onChange={(e) => set('method', e.target.value as HttpMethodType)}
              className={cn(
                'h-9 rounded-lg border bg-surface px-3 text-sm font-bold focus:outline-none focus:ring-2 focus:ring-accent/30',
                form.method === 'GET'
                  ? 'border-success/40 text-success'
                  : 'border-warning/40 text-warning',
              )}
            >
              <option value="GET">GET</option>
              <option value="POST">POST</option>
            </select>
            <input
              className="h-9 flex-1 rounded-lg border border-border bg-surface px-3 font-mono text-[13px] text-fg placeholder:text-fg-dim focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/30"
              value={form.url}
              onChange={(e) => set('url', e.target.value)}
              placeholder="https://api.exemplo.com/clientes/{id}"
            />
          </div>
          {detectedPlaceholders.length > 0 && (
            <div className="mt-3 flex flex-wrap items-center gap-2">
              <span className="text-[10px] uppercase tracking-wider text-fg-dim">Placeholders</span>
              {detectedPlaceholders.map((p) => (
                <Badge key={p} tone="accent" className="font-mono">
                  {`{${p}}`}
                </Badge>
              ))}
            </div>
          )}
        </div>
      </Card>

      {/* Tabs */}
      <Card className="mb-5" padded={false}>
        <div className="flex items-center gap-1 border-b border-border px-4 pt-3">
          <TabButton active={tab === 'params'} onClick={() => setTab('params')}>
            Params
          </TabButton>
          <TabButton active={tab === 'headers'} onClick={() => setTab('headers')}>
            Headers
            {form.headerRows.filter((r) => r.key.trim()).length > 0 && (
              <span className="ml-2 rounded-full bg-bg-soft px-1.5 text-[10px] text-fg-muted">
                {form.headerRows.filter((r) => r.key.trim()).length}
              </span>
            )}
          </TabButton>
          <TabButton active={tab === 'body'} onClick={() => setTab('body')} disabled={!showBody}>
            Body {!showBody && <span className="ml-1 text-[10px] text-fg-dim">(POST)</span>}
          </TabButton>
          <TabButton active={tab === 'response'} onClick={() => setTab('response')}>
            Resposta
          </TabButton>
        </div>

        <div className="px-5 py-5">
          {tab === 'params' && (
            <div className="space-y-6">
              <section>
                <CardHeader
                  title="Path params"
                  description={
                    <>
                      Detectados automaticamente da URL (entre <code>{'{}'}</code>). Configure tipo e descrição.
                    </>
                  }
                  className="mb-2"
                />
                <KvTable<ParamRow>
                  rows={form.pathRows}
                  onChange={(rows) => set('pathRows', rows)}
                  keyPlaceholder="nome"
                  valLabel="tipo / descrição"
                  buildEmpty={() => ({
                    type: 'string',
                    description: '',
                    required: true,
                    source: 'path',
                  })}
                  renderVal={(val, onChange) => (
                    <ParamValEditor val={val} onChange={onChange} requiredLocked />
                  )}
                />
              </section>

              <section>
                <CardHeader
                  title="Query params"
                  description="Vão para a query string. Marque os obrigatórios."
                  className="mb-2"
                />
                <KvTable<ParamRow>
                  rows={form.queryRows}
                  onChange={(rows) => set('queryRows', rows)}
                  keyPlaceholder="nome"
                  valLabel="tipo / descrição / obrigatório"
                  buildEmpty={() => ({ type: 'string', description: '', required: false })}
                  renderVal={(val, onChange) => <ParamValEditor val={val} onChange={onChange} />}
                />
              </section>
            </div>
          )}

          {tab === 'headers' && (
            <div className="space-y-2">
              <p className="mb-1 text-[11px] text-fg-muted">
                <code>Content-Type</code> e <code>Accept</code> são automáticos — não precisa adicionar.
              </p>
              <KvTable<string>
                rows={form.headerRows}
                onChange={(rows) => set('headerRows', rows)}
                keyPlaceholder="header"
                valLabel="valor"
                buildEmpty={() => ''}
                forbidKeys={['Content-Type', 'Accept']}
                renderVal={(val, onChange) => (
                  <Input
                    value={val}
                    onChange={(e) => onChange(e.target.value)}
                    placeholder="ex.: Bearer ${TOKEN}"
                  />
                )}
              />
            </div>
          )}

          {tab === 'body' && (
            <div className="space-y-4">
              <Select
                label="Tipo do body"
                className="max-w-xs"
                value={form.inputContentType}
                onChange={(e) => set('inputContentType', e.target.value as InputContentType)}
                options={[
                  { value: 'None', label: 'Sem body' },
                  { value: 'Json', label: 'JSON' },
                  { value: 'Text', label: 'Texto puro' },
                  { value: 'FormUrlEncoded', label: 'Form URL-encoded' },
                ]}
              />

              {(form.inputContentType === 'Json' || form.inputContentType === 'FormUrlEncoded') && (
                <div>
                  <label className="mb-2 block text-xs font-medium text-fg-muted">
                    Estrutura esperada
                  </label>
                  <JsonSchemaBuilder
                    value={form.inputBodyExample}
                    onChange={(next) => set('inputBodyExample', next)}
                    flatOnly={form.inputContentType === 'FormUrlEncoded'}
                    emptyHint={
                      form.inputContentType === 'FormUrlEncoded'
                        ? 'Form URL-encoded só aceita campos planos (sem objetos aninhados).'
                        : 'Adicione os campos que o body deve conter.'
                    }
                  />
                </div>
              )}

              {form.inputContentType === 'Text' && (
                <Input
                  label="Nome do campo que vira o texto"
                  value={form.textBodyFieldName}
                  onChange={(e) => set('textBodyFieldName', e.target.value)}
                  placeholder="conteudo"
                  monospace
                  className="max-w-xs"
                />
              )}
            </div>
          )}

          {tab === 'response' && (
            <div className="space-y-4">
              <Select
                label="Tipo da resposta"
                className="max-w-xs"
                value={form.outputContentType}
                onChange={(e) => set('outputContentType', e.target.value as OutputContentType)}
                options={[
                  { value: 'Json', label: 'JSON' },
                  { value: 'Text', label: 'Texto puro' },
                  { value: 'Csv', label: 'CSV' },
                ]}
              />

              {(form.outputContentType === 'Json' || form.outputContentType === 'Csv') && (
                <div>
                  <label className="mb-2 block text-xs font-medium text-fg-muted">
                    Estrutura da resposta
                  </label>
                  <JsonSchemaBuilder
                    value={form.outputExample}
                    onChange={(next) => set('outputExample', next)}
                    emptyHint={
                      form.outputContentType === 'Csv'
                        ? 'Adicione as colunas que o CSV vai conter.'
                        : 'Adicione os campos que a resposta vai trazer.'
                    }
                    addLabel={form.outputContentType === 'Csv' ? 'Adicionar coluna' : undefined}
                  />
                </div>
              )}

              {form.outputContentType === 'Text' && (
                <Textarea
                  label="Descrição do que a API retorna"
                  className="min-h-[100px]"
                  value={form.outputDescription}
                  onChange={(e) => set('outputDescription', e.target.value)}
                  placeholder="Ex.: confirmação textual da operação."
                />
              )}
            </div>
          )}
        </div>
      </Card>

      {/* Avançado */}
      <details className="mb-5 rounded-xl border border-border bg-surface px-5 py-3 shadow-card">
        <summary className="cursor-pointer text-xs font-medium text-fg-muted hover:text-fg">
          Configurações avançadas
        </summary>
        <div className="mt-4 max-w-xs">
          <Input
            label="Timeout (segundos)"
            type="number"
            min={1}
            value={form.timeoutSeconds}
            onChange={(e) => set('timeoutSeconds', e.target.value)}
            placeholder="usa o padrão se vazio"
          />
        </div>
      </details>

      {error && <ErrorMessage message={error} className="mb-4" />}

      <div className="flex items-center justify-end gap-2">
        <Button variant="ghost" onClick={() => navigate('/ferramentas')}>
          Cancelar
        </Button>
        <Button onClick={onSave} loading={submitting}>
          {mode === 'edit' ? 'Salvar alterações' : 'Criar ferramenta'}
        </Button>
      </div>
    </div>
  )
}

function TabButton({
  active,
  onClick,
  disabled,
  children,
}: {
  active: boolean
  onClick: () => void
  disabled?: boolean
  children: React.ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className={cn(
        'flex items-center gap-1 border-b-2 px-3 py-2 text-xs font-medium transition',
        active ? 'border-accent text-fg' : 'border-transparent text-fg-muted hover:text-fg',
        disabled && 'cursor-not-allowed opacity-40',
      )}
    >
      {children}
    </button>
  )
}

interface ParamValEditorProps {
  val: ParamRow
  onChange: (next: ParamRow) => void
  requiredLocked?: boolean
}

function ParamValEditor({ val, onChange, requiredLocked }: ParamValEditorProps) {
  return (
    <div className="grid grid-cols-12 items-center gap-2">
      <div className="col-span-3">
        <Select
          options={PARAM_TYPE_OPTIONS}
          value={val.type}
          onChange={(e) => onChange({ ...val, type: e.target.value })}
        />
      </div>
      <div className="col-span-7">
        <Input
          value={val.description}
          onChange={(e) => onChange({ ...val, description: e.target.value })}
          placeholder="o que é esse parâmetro"
        />
      </div>
      <label className="col-span-2 inline-flex items-center gap-1.5 text-[11px] text-fg-muted">
        <input
          type="checkbox"
          className="accent-accent"
          checked={val.required}
          disabled={requiredLocked}
          onChange={(e) => onChange({ ...val, required: e.target.checked })}
        />
        obrigatório
      </label>
    </div>
  )
}
