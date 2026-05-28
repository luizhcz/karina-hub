import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import {
  createGenericTool,
  executeGenericTool,
  extractPlaceholders,
  getGenericTool,
  testDraftGenericTool,
  updateGenericTool,
  type CreateGenericToolBody,
  type GenericTool,
  type GenericToolTestResult,
  type HttpMethodType,
  type InputContentType,
  type OutputContentType,
  type ParamDefinition,
  type SchemaWarning,
} from '../api/genericTools'
import { friendlyError } from '../api/client'
import { KvTable, type KvRow } from '../components/PostmanEditor/KvTable'
import {
  ArrowLeftIcon,
  Badge,
  BoltIcon,
  Button,
  Card,
  CardHeader,
  ErrorMessage,
  Input,
  JsonSchemaBuilder,
  Modal,
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
// Espelha GenericTool.NameRegex no backend. O Name aqui é o function name
// exposto ao LLM no schema da tool — OpenAI/Anthropic exigem esse formato.
const TOOL_NAME_REGEX = /^[a-zA-Z_][a-zA-Z0-9_-]{0,63}$/
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
  isExclusive: boolean
}

function emptyForm(): FormState {
  return {
    name: '',
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
    isExclusive: false,
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

/**
 * Build canônico do payload a partir do FormState. Função pura sem side
 * effects — retorna o body ou uma mensagem de erro pra UI exibir. Reusada
 * pelo onSave (modal de create/edit) e pelo TestToolModal (sandbox endpoint
 * recebe o mesmo shape).
 */
function buildBodyFromForm(form: FormState): { body?: CreateGenericToolBody; error?: string } {
  if (!form.name.trim()) {
    return { error: 'Informe um nome para a ferramenta.' }
  }
  if (!TOOL_NAME_REGEX.test(form.name)) {
    return { error: 'Nome inválido. Use snake_case ou kebab-case (ex: get_quote, lookup-user). Letras/dígitos/underscore/hífen, começa com letra ou underscore.' }
  }
  if (!form.url.trim()) {
    return { error: 'Informe a URL.' }
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

  // GET aceita None ou Json (input estruturado vira query string flattened).
  // FormUrlEncoded/Text só em POST (sem body em GET não faz sentido).
  const isGet = form.method === 'GET'
  const requestedInput = form.inputContentType
  const inputContentType: InputContentType =
    isGet && (requestedInput === 'FormUrlEncoded' || requestedInput === 'Text')
      ? 'None'
      : requestedInput
  let inputSchema: string | null = null
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

  let outputSchema: string | null = null
  if (form.outputContentType === 'Json' || form.outputContentType === 'Csv') {
    outputSchema = form.outputExample
  } else {
    outputSchema = form.outputDescription.trim() || null
  }

  return {
    body: {
      // Id é gerado pelo front no modo create (caller resolve via
      // generateToolId no onSave). No modo edit, id vai pela rota e o
      // backend ignora `id` no PUT.
      id: undefined,
      name: form.name.trim(),
      httpMethod: form.method,
      urlTemplate: form.url.trim(),
      pathParams,
      queryParams,
      customHeaders,
      inputContentType,
      inputSchema,
      outputContentType: form.outputContentType,
      outputSchema,
      // Timeout fica no default global do backend — config avançada removida do MVP.
      timeoutSecondsOverride: null,
      isExclusive: form.isExclusive,
      // Json/Csv sempre projetam (drop silencioso de extras + fail-loud em
      // required/type) — domain força Project no save. Text fica Off.
      outputProjectionMode: form.outputContentType === 'Text' ? 'Off' : 'Project',
    },
  }
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
    isExclusive: tool.isExclusive,
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
  const [testOpen, setTestOpen] = useState(false)
  // Gate de save: o PM precisa testar a ferramenta contra o endpoint real
  // antes de salvar. Resetado a cada mudança no form (qualquer alteração
  // pode invalidar o teste anterior — URL, params, headers, schema, etc.).
  const [testPassed, setTestPassed] = useState(false)
  // Warnings emitidos pelo backend quando o schema foi canonicalizado no save
  // (ex.: oneOf colapsado, pattern com lookahead removido). Mostra banner +
  // bloqueia navegação até o user clicar pra continuar.
  const [postSaveWarnings, setPostSaveWarnings] = useState<SchemaWarning[] | null>(null)

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
    // Qualquer mudança invalida o teste anterior — PM precisa re-testar.
    if (testPassed) setTestPassed(false)
  }

  // GET só aceita InputContentType None ou Json — se o user trocar método pra
  // GET enquanto Text/FormUrlEncoded estavam selecionados (estado carregado
  // do edit anterior ou switch interativo), clampa pra None.
  useEffect(() => {
    if (form.method === 'GET'
        && (form.inputContentType === 'Text' || form.inputContentType === 'FormUrlEncoded')) {
      setForm((prev) => ({ ...prev, inputContentType: 'None' }))
    }
  }, [form.method, form.inputContentType])

  const onSave = async () => {
    setError(null)
    setPostSaveWarnings(null)
    const built = buildBodyFromForm(form)
    if (built.error) {
      setError(built.error)
      return
    }
    const body = built.body!
    setSubmitting(true)
    try {
      let saved: GenericTool
      if (mode === 'edit' && id && existingUpdatedAt) {
        const { id: _omit, ...rest } = body
        void _omit
        saved = await updateGenericTool(id, { ...rest, expectedUpdatedAt: existingUpdatedAt })
      } else {
        saved = await createGenericTool({ ...body, id: generateToolId() })
      }
      // Backend retorna warnings quando o schema foi canonicalizado (oneOf
      // colapsado, pattern com lookahead removido, etc). Mostra antes de
      // navegar — autor precisa ver o que mudou pra revisar se faz sentido.
      if (saved.schemaWarnings && saved.schemaWarnings.length > 0) {
        setPostSaveWarnings(saved.schemaWarnings)
      } else {
        navigate('/ferramentas')
      }
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

  // POST sempre tem aba de body; GET tem quando InputContentType=Json
  // (envia body JSON — não-padrão HTTP mas comum em APIs internas tipo
  // Elasticsearch). Pra query string flat, o autor usa QueryParams.
  // Text/FormUrlEncoded só em POST (sem body em GET não faz sentido).
  const isGetWithStructuredInput =
    form.method === 'GET' && form.inputContentType === 'Json'
  const showBody = form.method === 'POST' || isGetWithStructuredInput
  const bodyTabLabel = 'Body'
  const inputTypeOptions =
    form.method === 'GET'
      ? [
          { value: 'None', label: 'Sem body' },
          { value: 'Json', label: 'JSON (não-padrão HTTP — APIs internas)' },
        ]
      : [
          { value: 'None', label: 'Sem body' },
          { value: 'Json', label: 'JSON' },
          { value: 'Text', label: 'Texto puro' },
          { value: 'FormUrlEncoded', label: 'Form URL-encoded' },
        ]

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
          placeholder="Ex.: get_quote ou lookup-user"
          hint="É como o LLM identifica esta ferramenta. Letras/dígitos/underscore/hífen, começa com letra ou underscore, máx 64 chars."
          autoFocus
          error={form.name.length > 0 && !TOOL_NAME_REGEX.test(form.name)
            ? 'Nome inválido. Use snake_case ou kebab-case (ex: get_quote, lookup-user).'
            : undefined}
        />
        <p className="text-[11px] text-fg-muted">
          Descrição semântica ("o que faz" / "quando usar") vive no prompt do agente
          (aba <strong>Perfil</strong>) — a ferramenta aqui é instrumento puro: nome + URL + schema.
        </p>

        <label className="mt-2 flex cursor-pointer items-start gap-3 rounded-lg border border-border bg-bg-soft p-3">
          <input
            type="checkbox"
            checked={form.isExclusive}
            onChange={(e) => set('isExclusive', e.target.checked)}
            className="mt-0.5 h-4 w-4 rounded border-border accent-accent"
          />
          <div>
            <div className="text-sm font-medium text-fg">Chamar em nome do usuário do chat</div>
            <p className="text-[11px] text-fg-muted">
              Ligue quando cada usuário tem permissões diferentes na API
              (ex: trader só enxerga as carteiras dos clientes dele, gestor só
              pode operar as contas que atende). A ferramenta vai identificar
              quem está conversando e só executa o que aquela pessoa pode fazer
              — se ela não tiver acesso, o agente recebe um aviso.
              <br />
              Deixe desligado quando a API responde igual pra todo mundo
              (cotação de ativo, indicador de mercado, etc.).
            </p>
          </div>
        </label>
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
          <TabButton active={tab === 'body'} onClick={() => setTab('body')} disabled={!showBody && form.method !== 'GET'}>
            {bodyTabLabel}
            {!showBody && form.method === 'GET' && (
              <span className="ml-1 text-[10px] text-fg-dim">(JSON only)</span>
            )}
            {form.method === 'GET' && showBody && (
              <span className="ml-1 text-[10px] text-warning">(non-standard)</span>
            )}
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
                options={inputTypeOptions}
                hint={
                  form.method === 'GET' && form.inputContentType === 'Json'
                    ? 'GET com body JSON é fora do padrão HTTP. Use só se a API consumidora aceita (Elasticsearch, APIs internas). Pra query string flat, use os campos da aba Params → Query.'
                    : undefined
                }
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

      {error && <ErrorMessage message={error} className="mb-4" />}

      {!testPassed && (
        <p className="mb-3 rounded-lg border border-warning/40 bg-warning/10 px-3 py-2 text-[11px] text-warning">
          Teste a ferramenta contra o endpoint real antes de salvar. O botão é
          habilitado quando o endpoint responde 2xx.
        </p>
      )}

      <div className="flex items-center justify-end gap-2">
        <Button
          variant={testPassed ? 'secondary' : 'primary'}
          leftIcon={<BoltIcon className="h-4 w-4" />}
          onClick={() => setTestOpen(true)}
        >
          {testPassed ? 'Testar novamente' : 'Testar ferramenta'}
        </Button>
        <Button variant="ghost" onClick={() => navigate('/ferramentas')}>
          Cancelar
        </Button>
        <Button
          onClick={onSave}
          loading={submitting}
          disabled={!testPassed}
          title={!testPassed ? 'Teste a ferramenta primeiro (endpoint precisa responder 2xx).' : undefined}
        >
          {mode === 'edit' ? 'Salvar alterações' : 'Criar ferramenta'}
        </Button>
      </div>

      <TestToolModal
        open={testOpen}
        onClose={() => setTestOpen(false)}
        mode={mode}
        toolId={id ?? null}
        form={form}
        onTestPassed={() => setTestPassed(true)}
      />

      <SchemaWarningsModal
        warnings={postSaveWarnings}
        onClose={() => {
          setPostSaveWarnings(null)
          navigate('/ferramentas')
        }}
      />
    </div>
  )
}

function SchemaWarningsModal({
  warnings,
  onClose,
}: {
  warnings: SchemaWarning[] | null
  onClose: () => void
}) {
  return (
    <Modal open={warnings !== null} onClose={onClose} title="Schema canonicalizado" size="lg">
      <div className="space-y-4">
        <p className="text-sm text-fg-muted">
          A ferramenta foi salva com sucesso. O backend converteu o schema
          para a forma canônica usada pelo LLM — algumas estruturas foram
          transformadas. Revise os avisos abaixo:
        </p>
        <ul className="space-y-2">
          {(warnings ?? []).map((w, i) => (
            <li
              key={`${w.code}-${i}`}
              className="rounded-lg border border-warning/40 bg-warning/10 px-3 py-2 text-[12px]"
            >
              <div className="flex items-baseline gap-2">
                <span className="font-mono text-[10px] uppercase tracking-wider text-warning">
                  {w.code}
                </span>
                {w.path && (
                  <span className="font-mono text-[10px] text-fg-dim">{w.path}</span>
                )}
              </div>
              <p className="mt-1 text-fg">{w.message}</p>
            </li>
          ))}
        </ul>
        <div className="flex justify-end pt-2">
          <Button onClick={onClose}>Entendi, voltar pra lista</Button>
        </div>
      </div>
    </Modal>
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

interface TestToolModalProps {
  open: boolean
  onClose: () => void
  /** Em modo 'edit' o teste vai contra a tool já persistida; em 'create' usa o sandbox endpoint que aceita a config inline. */
  mode: 'create' | 'edit'
  /** Id da tool já persistida (somente em modo 'edit'). */
  toolId: string | null
  form: FormState
  /** Disparado quando o teste passa (success && statusCode 2xx). Caller usa pra liberar o botão de salvar. */
  onTestPassed: () => void
}

// Pré-popula o JSON de args com chaves de path/query/body do schema atual.
// Valor vazio força o user a preencher antes de executar.
function buildArgsTemplate(form: FormState): string {
  const keys: string[] = []
  for (const r of form.pathRows) if (r.key.trim()) keys.push(r.key.trim())
  for (const r of form.queryRows) if (r.key.trim()) keys.push(r.key.trim())

  // Json input adiciona properties do schema como chaves — tanto em POST
  // (vira body) quanto em GET (vira query string flattened).
  if (form.inputContentType === 'Json' || form.inputContentType === 'FormUrlEncoded') {
    try {
      const schema = JSON.parse(form.inputBodyExample) as { properties?: Record<string, unknown> }
      if (schema.properties) keys.push(...Object.keys(schema.properties))
    } catch {
      // schema inválido — ignora; user preenche manualmente
    }
  } else if (form.method === 'POST' && form.inputContentType === 'Text') {
    keys.push(form.textBodyFieldName.trim() || 'body')
  }

  if (keys.length === 0) return '{}'
  const unique = Array.from(new Set(keys))
  const lines = unique.map((k) => `  "${k}": ""`).join(',\n')
  return `{\n${lines}\n}`
}

function TestToolModal({ open, onClose, mode, toolId, form, onTestPassed }: TestToolModalProps) {
  const [argsText, setArgsText] = useState('')
  const [running, setRunning] = useState(false)
  const [result, setResult] = useState<GenericToolTestResult | null>(null)
  const [argsError, setArgsError] = useState<string | null>(null)

  // Reseta state ao abrir e regenera template a partir do form atual.
  useEffect(() => {
    if (!open) return
    setArgsText(buildArgsTemplate(form))
    setResult(null)
    setArgsError(null)
  }, [open, form])

  const run = async () => {
    setArgsError(null)
    let parsed: Record<string, unknown>
    try {
      const v = JSON.parse(argsText)
      if (!v || typeof v !== 'object' || Array.isArray(v)) {
        setArgsError('Args precisa ser um objeto JSON.')
        return
      }
      parsed = v as Record<string, unknown>
    } catch {
      setArgsError('JSON inválido.')
      return
    }
    const built = buildBodyFromForm(form)
    if (built.error || !built.body) {
      setArgsError(built.error ?? 'Form inválido.')
      return
    }
    const body = built.body
    setRunning(true)
    setResult(null)
    try {
      // Edit usa o endpoint de execute em cima da tool persistida (preserva
      // exatamente o que vai rodar em prod). Create usa o sandbox endpoint
      // que aceita a config inline e não persiste nada.
      const r = mode === 'edit' && toolId
        ? await executeGenericTool(toolId, parsed)
        : await testDraftGenericTool(body, parsed)
      setResult(r)
      // Considera passou quando upstream respondeu 2xx e o tester reportou
      // sucesso (sem schema violation nem falha de parse).
      if (r.success && r.statusCode !== null && r.statusCode >= 200 && r.statusCode < 300) {
        onTestPassed()
      }
    } catch (err) {
      setResult({
        success: false,
        statusCode: null,
        durationMs: 0,
        url: '',
        method: '',
        requestBody: null,
        requestHeaders: {},
        responseBody: null,
        responseTruncated: false,
        responseHeaders: {},
        parsedData: null,
        projectedData: null,
        schemaErrors: [],
        projectionBypassed: true,
        error: friendlyError(err, 'Falha ao chamar o endpoint de teste.'),
      })
    } finally {
      setRunning(false)
    }
  }

  const isPost = form.method === 'POST'

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="lg"
      title="Testar ferramenta"
      description="Executa a request real contra o endpoint configurado. Timeout fixo de 10s. Sem audit, sem métricas — não afeta dashboard de uso."
      footer={
        <div className="flex justify-end gap-2">
          <Button variant="ghost" onClick={onClose}>
            Fechar
          </Button>
          <Button onClick={run} loading={running} leftIcon={<BoltIcon className="h-4 w-4" />}>
            Executar
          </Button>
        </div>
      }
    >
      <div className="space-y-4">
        {isPost && (
          <div className="rounded-lg border border-warning/40 bg-warning/10 px-3 py-2 text-[11px] text-warning">
            Atenção: <span className="font-mono">POST</span> executa de verdade — qualquer
            efeito colateral do endpoint (criar registro, enviar email, etc.) acontece.
          </div>
        )}

        <div>
          <label className="mb-1.5 block text-xs font-medium text-fg-muted">
            Args (JSON)
          </label>
          <p className="mb-2 text-[11px] text-fg-dim">
            Chaves correspondem a path/query/body da ferramenta. Pré-preenchido com os campos
            detectados — substitua os valores vazios pelos que quer testar.
          </p>
          <textarea
            className="min-h-[180px] w-full resize-y rounded-lg border border-border bg-surface px-3 py-2 font-mono text-[12px] leading-5 text-fg placeholder:text-fg-dim focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/30"
            value={argsText}
            onChange={(e) => {
              setArgsText(e.target.value)
              if (argsError) setArgsError(null)
            }}
            spellCheck={false}
          />
          {argsError && (
            <div className="mt-2 rounded-lg border border-danger/40 bg-danger/10 px-3 py-2 text-[11px] text-danger">
              {argsError}
            </div>
          )}
        </div>

        {result && <TestResultPanel result={result} />}
      </div>
    </Modal>
  )
}

function TestResultPanel({ result }: { result: GenericToolTestResult }) {
  const tone: 'success' | 'danger' | 'warning' = result.success
    ? 'success'
    : result.statusCode && result.statusCode >= 400
      ? 'danger'
      : 'warning'
  const toneClass =
    tone === 'success'
      ? 'border-success/40 bg-success/10 text-success'
      : tone === 'danger'
        ? 'border-danger/40 bg-danger/10 text-danger'
        : 'border-warning/40 bg-warning/10 text-warning'

  const parsedJson = useMemo(() => {
    if (result.parsedData === null || result.parsedData === undefined) return null
    try {
      return JSON.stringify(result.parsedData, null, 2)
    } catch {
      return String(result.parsedData)
    }
  }, [result.parsedData])

  const projectedJson = useMemo(() => {
    if (result.projectedData === null || result.projectedData === undefined) return null
    try {
      return JSON.stringify(result.projectedData, null, 2)
    } catch {
      return String(result.projectedData)
    }
  }, [result.projectedData])

  return (
    <div className="space-y-3 border-t border-border pt-4">
      <div className={cn('flex items-center justify-between rounded-lg border px-3 py-2 text-[11px]', toneClass)}>
        <div className="flex items-center gap-3">
          <span className="font-semibold uppercase tracking-wider">
            {result.success ? 'OK' : 'Falhou'}
          </span>
          {result.statusCode !== null && (
            <span className="font-mono">HTTP {result.statusCode}</span>
          )}
        </div>
        <span className="font-mono">{result.durationMs} ms</span>
      </div>

      {result.error && (
        <div className="rounded-lg border border-danger/40 bg-danger/10 px-3 py-2 text-[11px] text-danger">
          {result.error}
        </div>
      )}

      {result.url && (
        <div>
          <div className="mb-1 text-[10px] uppercase tracking-wider text-fg-dim">
            {result.method} URL
          </div>
          <div className="break-all rounded-lg border border-border bg-bg-soft px-3 py-2 font-mono text-[11px] text-fg">
            {result.url}
          </div>
        </div>
      )}

      {/* Projetada: response após filtro/validação contra outputSchema. É o
          que o LLM efetivamente vê em runtime (em modo Project/Strict). UI
          esconde quando projection foi bypass (modo Off ou schema ausente)
          pra não duplicar a tab "Resposta parseada". */}
      {!result.projectionBypassed && (result.schemaErrors?.length ?? 0) > 0 && (
        <div className="rounded-lg border border-danger/40 bg-danger/10 px-3 py-2 text-[11px] text-danger">
          <div className="mb-1 font-semibold uppercase tracking-wider">Schema violation</div>
          <ul className="ml-4 list-disc space-y-0.5">
            {(result.schemaErrors ?? []).map((err, i) => (
              <li key={i} className="font-mono">{err}</li>
            ))}
          </ul>
        </div>
      )}

      {!result.projectionBypassed && projectedJson && (
        <details open className="rounded-lg border border-accent/40 bg-accent/[0.04]">
          <summary className="cursor-pointer px-3 py-2 text-[11px] uppercase tracking-wider text-accent hover:text-accent">
            Projetada (o que o LLM vai receber)
          </summary>
          <pre className="max-h-72 overflow-auto px-3 pb-3 font-mono text-[11px] leading-5 text-fg">
            {projectedJson}
          </pre>
        </details>
      )}

      {parsedJson && (
        <details open={result.projectionBypassed} className="rounded-lg border border-border bg-bg-soft">
          <summary className="cursor-pointer px-3 py-2 text-[11px] uppercase tracking-wider text-fg-muted hover:text-fg">
            Resposta parseada {!result.projectionBypassed && <span className="text-fg-dim">(antes da projeção)</span>}
          </summary>
          <pre className="max-h-72 overflow-auto px-3 pb-3 font-mono text-[11px] leading-5 text-fg">
            {parsedJson}
          </pre>
        </details>
      )}

      {result.responseBody && (
        <details className="rounded-lg border border-border bg-bg-soft">
          <summary className="cursor-pointer px-3 py-2 text-[11px] uppercase tracking-wider text-fg-muted hover:text-fg">
            Body cru {result.responseTruncated && <span className="text-warning">(truncado)</span>}
          </summary>
          <pre className="max-h-72 overflow-auto px-3 pb-3 font-mono text-[11px] leading-5 text-fg">
            {result.responseBody}
          </pre>
        </details>
      )}

      {result.requestBody && (
        <details className="rounded-lg border border-border bg-bg-soft">
          <summary className="cursor-pointer px-3 py-2 text-[11px] uppercase tracking-wider text-fg-muted hover:text-fg">
            Request body
          </summary>
          <pre className="max-h-48 overflow-auto px-3 pb-3 font-mono text-[11px] leading-5 text-fg">
            {result.requestBody}
          </pre>
        </details>
      )}

      <details className="rounded-lg border border-border bg-bg-soft">
        <summary className="cursor-pointer px-3 py-2 text-[11px] uppercase tracking-wider text-fg-muted hover:text-fg">
          Headers
        </summary>
        <div className="space-y-3 px-3 pb-3 text-[11px]">
          <HeadersList title="Request" headers={result.requestHeaders} />
          <HeadersList title="Response" headers={result.responseHeaders} />
        </div>
      </details>
    </div>
  )
}

function HeadersList({ title, headers }: { title: string; headers: Record<string, string> }) {
  const entries = Object.entries(headers)
  if (entries.length === 0) {
    return (
      <div>
        <div className="text-[10px] uppercase tracking-wider text-fg-dim">{title}</div>
        <p className="text-fg-dim">— vazio —</p>
      </div>
    )
  }
  return (
    <div>
      <div className="mb-1 text-[10px] uppercase tracking-wider text-fg-dim">{title}</div>
      <div className="space-y-0.5 font-mono">
        {entries.map(([k, v]) => (
          <div key={k} className="break-all">
            <span className="text-fg-muted">{k}:</span> <span className="text-fg">{v}</span>
          </div>
        ))}
      </div>
    </div>
  )
}
