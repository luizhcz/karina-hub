import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import {
  useGenericTool,
  useCreateGenericTool,
  useUpdateGenericTool,
  extractPlaceholders,
  type GenericTool,
  type HttpMethodType,
  type InputContentType,
  type OutputContentType,
  type ParamDefinition,
} from '../../api/genericTools'
import { Card } from '../../shared/ui/Card'
import { Input } from '../../shared/ui/Input'
import { Textarea } from '../../shared/ui/Textarea'
import { Button } from '../../shared/ui/Button'
import { Select } from '../../shared/ui/Select'
import { Badge } from '../../shared/ui/Badge'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ApiError } from '../../api/client'
import { SchemaEditor } from '../agents/components/SchemaEditor/SchemaEditor'
import { KeyValueListEditor } from './components/KeyValueListEditor'

const PARAM_TYPE_OPTIONS = [
  { value: 'string', label: 'string' },
  { value: 'number', label: 'number' },
  { value: 'integer', label: 'integer' },
  { value: 'boolean', label: 'boolean' },
]

const RESERVED_HEADERS = ['Content-Type', 'Accept']

const EMPTY_OBJECT_SCHEMA = '{\n  "type": "object",\n  "properties": {}\n}'

interface FormState {
  id: string
  name: string
  description: string
  httpMethod: HttpMethodType
  urlTemplate: string
  pathParams: Record<string, ParamDefinition>
  queryParams: Record<string, ParamDefinition>
  customHeaders: Record<string, string>
  inputContentType: InputContentType
  inputSchema: string
  textBodyFieldName: string
  outputContentType: OutputContentType
  outputSchema: string
  outputDescription: string
  timeoutSecondsOverride: string
}

function buildInitialState(): FormState {
  return {
    id: '',
    name: '',
    description: '',
    httpMethod: 'GET',
    urlTemplate: '',
    pathParams: {},
    queryParams: {},
    customHeaders: {},
    inputContentType: 'None',
    inputSchema: EMPTY_OBJECT_SCHEMA,
    textBodyFieldName: 'body',
    outputContentType: 'Json',
    outputSchema: EMPTY_OBJECT_SCHEMA,
    outputDescription: '',
    timeoutSecondsOverride: '',
  }
}

function fromTool(tool: GenericTool): FormState {
  let textFieldName = 'body'
  if (tool.inputContentType === 'Text' && tool.inputSchema) {
    try {
      const parsed = JSON.parse(tool.inputSchema)
      const props = parsed?.properties
      if (props && typeof props === 'object') {
        const first = Object.keys(props)[0]
        if (first) textFieldName = first
      }
    } catch {
      // fallback no default 'body'
    }
  }

  return {
    id: tool.id,
    name: tool.name,
    description: tool.description ?? '',
    httpMethod: tool.httpMethod,
    urlTemplate: tool.urlTemplate,
    pathParams: tool.pathParams ?? {},
    queryParams: tool.queryParams ?? {},
    customHeaders: tool.customHeaders ?? {},
    inputContentType: tool.inputContentType,
    inputSchema: tool.inputSchema || EMPTY_OBJECT_SCHEMA,
    textBodyFieldName: textFieldName,
    outputContentType: tool.outputContentType,
    outputSchema: tool.outputSchema || EMPTY_OBJECT_SCHEMA,
    outputDescription: tool.outputContentType === 'Text' ? (tool.outputSchema ?? '') : '',
    timeoutSecondsOverride: tool.timeoutSecondsOverride?.toString() ?? '',
  }
}

export function GenericToolEditorPage() {
  const navigate = useNavigate()
  const { id } = useParams<{ id?: string }>()
  const isEdit = Boolean(id) && id !== 'new'
  const { data: existing, isLoading, error: loadError } = useGenericTool(id ?? '', isEdit)

  const create = useCreateGenericTool()
  const update = useUpdateGenericTool()
  const [form, setForm] = useState<FormState>(buildInitialState)
  const [submitError, setSubmitError] = useState<string | null>(null)

  useEffect(() => {
    if (existing) setForm(fromTool(existing))
  }, [existing])

  const detectedPlaceholders = useMemo(
    () => extractPlaceholders(form.urlTemplate),
    [form.urlTemplate],
  )

  // Auto-popula path params com placeholders detectados que ainda não têm definição.
  useEffect(() => {
    setForm((prev) => {
      const next = { ...prev.pathParams }
      let changed = false
      for (const placeholder of detectedPlaceholders) {
        if (!next[placeholder]) {
          next[placeholder] = { type: 'string', description: '', required: true }
          changed = true
        }
      }
      return changed ? { ...prev, pathParams: next } : prev
    })
  }, [detectedPlaceholders])

  const set = <K extends keyof FormState>(key: K, value: FormState[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }))
  }

  const onSubmit = async () => {
    setSubmitError(null)

    if (!form.name.trim()) {
      setSubmitError('Nome é obrigatório.')
      return
    }
    if (!form.urlTemplate.trim()) {
      setSubmitError('UrlTemplate é obrigatório.')
      return
    }

    let inputSchemaToSend: string | null = null
    if (form.httpMethod === 'POST') {
      if (form.inputContentType === 'Json' || form.inputContentType === 'FormUrlEncoded') {
        inputSchemaToSend = form.inputSchema
      } else if (form.inputContentType === 'Text') {
        const fieldName = form.textBodyFieldName.trim() || 'body'
        inputSchemaToSend = JSON.stringify({
          type: 'object',
          properties: { [fieldName]: { type: 'string' } },
          required: [fieldName],
        })
      }
    }

    let outputSchemaToSend: string | null = null
    if (form.outputContentType === 'Json' || form.outputContentType === 'Csv') {
      outputSchemaToSend = form.outputSchema
    } else {
      outputSchemaToSend = form.outputDescription.trim() || null
    }

    const payload = {
      name: form.name.trim(),
      description: form.description,
      httpMethod: form.httpMethod,
      urlTemplate: form.urlTemplate.trim(),
      pathParams: form.pathParams,
      queryParams: form.queryParams,
      customHeaders: form.customHeaders,
      inputContentType: form.httpMethod === 'GET' ? ('None' as InputContentType) : form.inputContentType,
      inputSchema: form.httpMethod === 'GET' ? null : inputSchemaToSend,
      outputContentType: form.outputContentType,
      outputSchema: form.outputContentType === 'Text' ? null : outputSchemaToSend,
      timeoutSecondsOverride: form.timeoutSecondsOverride
        ? Number(form.timeoutSecondsOverride)
        : null,
    }

    try {
      if (isEdit && existing) {
        await update.mutateAsync({
          id: existing.id,
          body: { ...payload, expectedUpdatedAt: existing.updatedAt },
        })
      } else {
        await create.mutateAsync({
          ...payload,
          id: form.id.trim() || undefined,
        })
      }
      navigate('/generic-tools')
    } catch (err) {
      if (err instanceof ApiError) {
        setSubmitError(err.message)
      } else {
        setSubmitError('Erro ao salvar tool.')
      }
    }
  }

  if (isEdit && isLoading) return <PageLoader />
  if (isEdit && loadError) {
    return (
      <ErrorCard
        message={
          loadError instanceof ApiError ? loadError.message : 'Erro ao carregar tool'
        }
      />
    )
  }

  const showBodySection = form.httpMethod === 'POST'
  const showInputSchemaEditor =
    showBodySection &&
    (form.inputContentType === 'Json' || form.inputContentType === 'FormUrlEncoded')
  const showTextBody = showBodySection && form.inputContentType === 'Text'
  const showOutputSchemaEditor =
    form.outputContentType === 'Json' || form.outputContentType === 'Csv'
  const showOutputText = form.outputContentType === 'Text'

  return (
    <div className="flex flex-col gap-6 p-6 max-w-4xl">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="sm" onClick={() => navigate('/generic-tools')}>
          ← Voltar
        </Button>
        <div>
          <h1 className="text-2xl font-bold text-text-primary">
            {isEdit ? 'Editar Generic Tool' : 'Novo Generic Tool'}
          </h1>
          <p className="text-sm text-text-muted mt-1">
            Tool HTTP genérico exclusivo do projeto atual.
          </p>
        </div>
      </div>

      {submitError && <ErrorCard message={submitError} />}

      <Card title="Identificação">
        <div className="flex flex-col gap-4">
          {!isEdit && (
            <Input
              label="Id (opcional — gerado automaticamente se vazio)"
              value={form.id}
              onChange={(e) => set('id', e.target.value)}
              placeholder="search-users"
            />
          )}
          <Input
            label="Nome *"
            value={form.name}
            onChange={(e) => set('name', e.target.value)}
            placeholder="search-users"
          />
          <Textarea
            label="Descrição"
            value={form.description}
            onChange={(e) => set('description', e.target.value)}
            placeholder="O que esse tool faz? (será usado pelo LLM pra decidir quando chamar)"
          />
        </div>
      </Card>

      <Card title="Request">
        <div className="flex flex-col gap-4">
          <Select
            label="Método HTTP"
            value={form.httpMethod}
            onChange={(e) => set('httpMethod', e.target.value as HttpMethodType)}
            options={[
              { value: 'GET', label: 'GET' },
              { value: 'POST', label: 'POST' },
            ]}
          />
          <Input
            label="UrlTemplate *"
            value={form.urlTemplate}
            onChange={(e) => set('urlTemplate', e.target.value)}
            placeholder="https://api.example.com/users/{id}"
          />
          {detectedPlaceholders.length > 0 && (
            <div className="flex flex-wrap items-center gap-2">
              <span className="text-xs text-text-muted">Placeholders detectados:</span>
              {detectedPlaceholders.map((p) => (
                <Badge key={p} variant="blue">
                  {`{${p}}`}
                </Badge>
              ))}
            </div>
          )}
        </div>
      </Card>

      <Card title="Path Params">
        <KeyValueListEditor<ParamDefinition>
          value={form.pathParams}
          onChange={(next) => set('pathParams', next)}
          keyLabel="Nome (deve corresponder a placeholder na URL)"
          valueLabel="Tipo / Descrição"
          buildEmptyValue={() => ({ type: 'string', description: '', required: true })}
          renderValueEditor={(val, onChange) => (
            <ParamDefinitionEditor value={val} onChange={onChange} requiredLocked />
          )}
        />
      </Card>

      <Card title="Query Params">
        <KeyValueListEditor<ParamDefinition>
          value={form.queryParams}
          onChange={(next) => set('queryParams', next)}
          keyLabel="Nome"
          valueLabel="Tipo / Descrição / Required"
          buildEmptyValue={() => ({ type: 'string', description: '', required: false })}
          renderValueEditor={(val, onChange) => (
            <ParamDefinitionEditor value={val} onChange={onChange} />
          )}
        />
      </Card>

      <Card title="Headers customizados">
        <p className="text-xs text-text-muted mb-2">
          <code>Content-Type</code> e <code>Accept</code> são automáticos — definidos pelo
          executor em runtime conforme o content-type configurado.
        </p>
        <KeyValueListEditor<string>
          value={form.customHeaders}
          onChange={(next) => set('customHeaders', next)}
          keyLabel="Header"
          valueLabel="Valor"
          buildEmptyValue={() => ''}
          forbidKeys={RESERVED_HEADERS}
          renderValueEditor={(val, onChange) => (
            <Input value={val} onChange={(e) => onChange(e.target.value)} />
          )}
        />
      </Card>

      {showBodySection && (
        <Card title="Body">
          <div className="flex flex-col gap-4">
            <Select
              label="Content-Type do envio"
              value={form.inputContentType}
              onChange={(e) => set('inputContentType', e.target.value as InputContentType)}
              options={[
                { value: 'None', label: 'Nenhum' },
                { value: 'Json', label: 'application/json' },
                { value: 'Text', label: 'text/plain' },
                { value: 'FormUrlEncoded', label: 'application/x-www-form-urlencoded' },
              ]}
            />
            {showInputSchemaEditor && (
              <div>
                <span className="text-xs font-medium text-text-muted mb-1 block">
                  Schema do body
                  {form.inputContentType === 'FormUrlEncoded' && (
                    <span className="text-text-dimmed">
                      {' '}— deve ser plano (sem aninhamento).
                    </span>
                  )}
                </span>
                <SchemaEditor
                  value={form.inputSchema}
                  onChange={(v) => set('inputSchema', v)}
                />
              </div>
            )}
            {showTextBody && (
              <Input
                label="Nome do campo string que vira o body"
                value={form.textBodyFieldName}
                onChange={(e) => set('textBodyFieldName', e.target.value)}
                placeholder="raw"
              />
            )}
          </div>
        </Card>
      )}

      <Card title="Output">
        <div className="flex flex-col gap-4">
          <Select
            label="Content-Type esperado"
            value={form.outputContentType}
            onChange={(e) => set('outputContentType', e.target.value as OutputContentType)}
            options={[
              { value: 'Json', label: 'application/json' },
              { value: 'Text', label: 'text/plain' },
              { value: 'Csv', label: 'text/csv (parseado pra array)' },
            ]}
          />
          {showOutputSchemaEditor && (
            <div>
              <span className="text-xs font-medium text-text-muted mb-1 block">
                Schema da resposta
                {form.outputContentType === 'Csv' && (
                  <span className="text-text-dimmed">
                    {' '}— colunas do CSV (primeira linha é header).
                  </span>
                )}
              </span>
              <SchemaEditor
                value={form.outputSchema}
                onChange={(v) => set('outputSchema', v)}
              />
            </div>
          )}
          {showOutputText && (
            <Textarea
              label="Descrição livre da resposta esperada"
              value={form.outputDescription}
              onChange={(e) => set('outputDescription', e.target.value)}
              placeholder="Ex: resposta em texto puro com a confirmação."
            />
          )}
        </div>
      </Card>

      <Card title="Avançado">
        <Input
          label="Timeout override (segundos)"
          type="number"
          min={1}
          max={600}
          value={form.timeoutSecondsOverride}
          onChange={(e) => set('timeoutSecondsOverride', e.target.value)}
          placeholder="120"
        />
        <p className="text-xs text-text-muted mt-2">
          Quando vazio, usa o default global. Não pode exceder o máximo configurado no
          servidor (rejeita com 400).
        </p>
      </Card>

      <div className="flex items-center justify-end gap-2">
        <Button variant="ghost" onClick={() => navigate('/generic-tools')}>
          Cancelar
        </Button>
        <Button
          onClick={onSubmit}
          disabled={create.isPending || update.isPending}
        >
          {create.isPending || update.isPending ? 'Salvando…' : 'Salvar'}
        </Button>
      </div>
    </div>
  )
}

interface ParamDefinitionEditorProps {
  value: ParamDefinition
  onChange: (next: ParamDefinition) => void
  requiredLocked?: boolean
}

function ParamDefinitionEditor({ value, onChange, requiredLocked }: ParamDefinitionEditorProps) {
  return (
    <div className="flex items-start gap-2">
      <Select
        value={value.type}
        onChange={(e) => onChange({ ...value, type: e.target.value })}
        options={PARAM_TYPE_OPTIONS}
        className="w-28"
      />
      <Input
        value={value.description}
        onChange={(e) => onChange({ ...value, description: e.target.value })}
        placeholder="Descrição (lida pelo LLM)"
        className="flex-1"
      />
      <label className="inline-flex items-center gap-1 text-xs text-text-muted whitespace-nowrap mt-2">
        <input
          type="checkbox"
          checked={requiredLocked ? true : value.required}
          disabled={requiredLocked}
          onChange={(e) => onChange({ ...value, required: e.target.checked })}
        />
        Required
      </label>
    </div>
  )
}
