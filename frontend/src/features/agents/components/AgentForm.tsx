import { useMemo } from 'react'
import { useForm, FormProvider, useFormContext } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Card } from '../../../shared/ui/Card'
import { Input } from '../../../shared/ui/Input'
import { Select } from '../../../shared/ui/Select'
import { Textarea } from '../../../shared/ui/Textarea'
import { Button } from '../../../shared/ui/Button'
import { MonacoEditor } from '../../../shared/editors/MonacoEditor'
import { SchemaEditor } from './SchemaEditor'
import { useFunctions } from '../../../api/tools'
import { ToolPicker } from './ToolPicker'
import { MiddlewarePicker, type MiddlewareEntry } from './MiddlewarePicker'
import { useSkills } from '../../../api/skills'
import { useMcpServers } from '../../../api/mcpServers'
import { useModelCatalog } from '../../../api/modelCatalog'
import type { AgentFormValues } from '../types'
import type { AgentDef } from '../../../api/agents'
import { PROVIDER_OPTIONS, CATALOG_TO_PROVIDER } from '../../../constants/providers'
import { RESPONSE_FORMAT_OPTIONS, AGENT_DEFAULTS } from '../../../constants/agent'


export const agentFormSchema = z.object({
  id: z.string().min(1, 'ID is required'),
  name: z.string().min(1, 'Nome is required'),
  description: z.string().optional().default(''),
  metadata: z.record(z.string()).optional().default({}),
  model: z.object({
    deploymentName: z.string().min(1, 'Deployment name is required'),
    temperature: z.number().min(0).max(2).optional().default(0.7),
    maxTokens: z.number().min(0).optional().default(4096),
  }),
  provider: z.object({
    type: z.string().optional().default(''),
    clientType: z.string().optional().default(''),
    endpoint: z.string().optional().default(''),
  }).optional().default({}),
  fallbackProvider: z.object({
    enabled: z.boolean().optional().default(false),
    type: z.string().optional().default(''),
    endpoint: z.string().optional().default(''),
  }).optional().default({}),
  instructions: z.string().optional().default(''),
  tools: z.array(z.string()).optional().default([]),
  mcpServerIds: z.array(z.string()).optional().default([]),
  skills: z.array(z.string()).optional().default([]),
  structuredOutput: z.object({
    responseFormat: z.string().optional().default('text'),
    schemaName: z.string().optional().default(''),
    schemaDescription: z.string().optional().default(''),
    schema: z.string().optional().default(''),
  }).optional().default({}),
  middlewares: z.array(z.object({
    type: z.string(),
    enabled: z.boolean(),
    settings: z.record(z.string()),
  })).optional().default([]),
  resilience: z.object({
    maxRetries: z.number().min(0).optional().default(3),
    initialDelayMs: z.number().min(0).optional().default(1000),
    backoffMultiplier: z.number().min(1).optional().default(2),
  }).optional().default({}),
  budget: z.object({
    maxTokensPerExecution: z.number().min(0).optional().default(0),
    maxCostUsd: z.number().min(0).optional().default(0),
  }).optional().default({}),
})


const defaultValues: AgentFormValues = {
  id: '',
  name: '',
  description: '',
  metadata: {},
  model: { deploymentName: '', temperature: AGENT_DEFAULTS.temperature, maxTokens: AGENT_DEFAULTS.maxTokens },
  provider: { type: '', clientType: '', endpoint: '' },
  fallbackProvider: { enabled: false, type: '', endpoint: '' },
  instructions: '',
  tools: [],
  mcpServerIds: [],
  skills: [],
  structuredOutput: { responseFormat: 'text', schemaName: '', schemaDescription: '', schema: '' },
  middlewares: [],
  resilience: { maxRetries: AGENT_DEFAULTS.maxRetries, initialDelayMs: AGENT_DEFAULTS.initialDelayMs, backoffMultiplier: AGENT_DEFAULTS.backoffMultiplier },
  budget: { maxTokensPerExecution: 0, maxCostUsd: 0 },
}


export function agentToFormValues(agent: AgentDef): AgentFormValues {
  return {
    id: agent.id,
    name: agent.name,
    description: agent.description ?? '',
    metadata: agent.metadata ?? {},
    model: {
      deploymentName: agent.model.deploymentName,
      temperature: agent.model.temperature ?? AGENT_DEFAULTS.temperature,
      maxTokens: agent.model.maxTokens ?? AGENT_DEFAULTS.maxTokens,
    },
    provider: {
      type: agent.provider?.type ?? '',
      clientType: agent.provider?.clientType ?? '',
      endpoint: agent.provider?.endpoint ?? '',
    },
    fallbackProvider: {
      enabled: !!agent.fallbackProvider?.type,
      type: agent.fallbackProvider?.type ?? '',
      endpoint: agent.fallbackProvider?.endpoint ?? '',
    },
    instructions: agent.instructions ?? '',
    tools: agent.tools?.filter((t) => t.type === 'function').map((t) => t.name ?? '').filter(Boolean) ?? [],
    // Extrai ids de MCP tools que usam o novo contrato (mcpServerId). Agents com
    // config inline legacy são preservados no submit (ver formToRequest do caller),
    // mas não aparecem no picker — a UI é id-based only.
    mcpServerIds: agent.tools?.filter((t) => t.type === 'mcp' && !!t.mcpServerId).map((t) => t.mcpServerId!) ?? [],
    skills: agent.skillRefs?.map((s) => s.skillId) ?? [],
    structuredOutput: {
      responseFormat: agent.structuredOutput?.responseFormat ?? 'text',
      schemaName: agent.structuredOutput?.schemaName ?? '',
      schemaDescription: agent.structuredOutput?.schemaDescription ?? '',
      schema: agent.structuredOutput?.schema ? JSON.stringify(agent.structuredOutput.schema, null, 2) : '',
    },
    middlewares: (agent.middlewares ?? [])
      .filter((m) => m.enabled !== false)
      .map((m) => ({
        type: m.type,
        enabled: m.enabled !== false,
        settings: m.settings ?? {},
      })),
    resilience: {
      maxRetries: agent.resilience?.maxRetries ?? 3,
      initialDelayMs: agent.resilience?.initialDelayMs ?? 1000,
      backoffMultiplier: agent.resilience?.backoffMultiplier ?? 2,
    },
    budget: {
      maxTokensPerExecution: 0,
      maxCostUsd: agent.costBudget?.maxCostUsd ?? 0,
    },
  }
}


function IdentitySection({ isEdit }: { isEdit: boolean }) {
  const { register, formState: { errors } } = useFormContext<AgentFormValues>()
  return (
    <Card title="Identidade">
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        <Input
          label="ID"
          {...register('id')}
          error={errors.id?.message}
          disabled={isEdit}
          placeholder="meu-agente"
        />
        <Input
          label="Nome"
          {...register('name')}
          error={errors.name?.message}
          placeholder="Meu Agente"
        />
      </div>
      <div className="mt-4">
        <Textarea
          label="Descricao"
          {...register('description')}
          placeholder="Descricao do agente..."
          rows={3}
        />
      </div>
      <div className="mt-4">
        <label className="text-xs font-medium text-text-muted">Tags (metadata)</label>
        <p className="text-xs text-text-dimmed mt-1">
          Adicione metadados como pares chave:valor separados por virgula.
        </p>
      </div>
    </Card>
  )
}

function ModelSection() {
  const { register, watch, setValue, formState: { errors } } = useFormContext<AgentFormValues>()
  const temperature = watch('model.temperature')
  const currentDeployment = watch('model.deploymentName')

  const { data: models = [] } = useModelCatalog()

  const handleModelChange = (modelId: string) => {
    setValue('model.deploymentName', modelId)
    const catalogEntry = models.find((m) => m.id === modelId)
    const currentProvider = watch('provider.type')
    if (catalogEntry && !currentProvider) {
      setValue('provider.type', CATALOG_TO_PROVIDER[catalogEntry.provider] ?? '')
    }
  }

  const isCustom = !!currentDeployment && !models.find((m) => m.id === currentDeployment)

  return (
    <Card title="Modelo">
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-text-muted">
            Modelo
            {errors.model?.deploymentName && (
              <span className="text-red-400 ml-1">{errors.model.deploymentName.message}</span>
            )}
          </label>
          {models.length > 0 ? (
            <select
              value={currentDeployment}
              onChange={(e) => handleModelChange(e.target.value)}
              className="bg-bg-tertiary border border-border-secondary rounded-md px-2.5 py-1.5 text-sm text-text-primary focus:outline-none focus:border-accent-blue"
            >
              <option value="">Selecione um modelo...</option>
              {isCustom && (
                <option value={currentDeployment}>{currentDeployment} (personalizado)</option>
              )}
              {models.map((m) => (
                <option key={`${m.provider}/${m.id}`} value={m.id}>
                  {m.displayName} · {m.provider}
                </option>
              ))}
            </select>
          ) : (
            <Input
              {...register('model.deploymentName')}
              error={errors.model?.deploymentName?.message}
              placeholder="gpt-4o"
            />
          )}
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-text-muted">
            Temperature: {temperature}
          </label>
          <input
            type="range"
            min={0}
            max={2}
            step={0.1}
            value={temperature}
            onChange={(e) => setValue('model.temperature', parseFloat(e.target.value))}
            className="mt-2 accent-accent-blue"
          />
        </div>
        <Input
          label="Max Tokens"
          type="number"
          {...register('model.maxTokens', { valueAsNumber: true })}
          placeholder="4096"
        />
      </div>
    </Card>
  )
}

function useProviderOptions(): { value: string; label: string }[] {
  const { data: funcs } = useFunctions()
  if (funcs?.availableProviders?.length) {
    return [
      { value: '', label: 'Selecione...' },
      ...funcs.availableProviders.map((p) => ({
        value: CATALOG_TO_PROVIDER[p.type] ?? p.type,
        label: CATALOG_TO_PROVIDER[p.type] ?? p.type,
      })),
    ]
  }
  return PROVIDER_OPTIONS
}

function ProviderSection() {
  const { register, watch } = useFormContext<AgentFormValues>()
  const providerType = watch('provider.type')
  const providerOptions = useProviderOptions()

  return (
    <Card title="Provider">
      <p className="text-xs text-text-dimmed mb-3">Preenchido automaticamente ao selecionar o modelo. Altere apenas para sobrescrever.</p>
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        <Select
          label="Tipo"
          options={providerOptions}
          {...register('provider.type')}
        />
        {providerType && (
          <>
            <Input
              label="Endpoint (override)"
              {...register('provider.endpoint')}
              placeholder="https://..."
            />
            <Input
              label="Client Type (override)"
              {...register('provider.clientType')}
              placeholder="Opcional"
            />
          </>
        )}
      </div>
    </Card>
  )
}

function FallbackSection() {
  const { register, watch } = useFormContext<AgentFormValues>()
  const enabled = watch('fallbackProvider.enabled')
  const providerOptions = useProviderOptions()

  return (
    <Card title="Fallback Provider">
      <label className="flex items-center gap-2 text-sm text-text-secondary mb-4">
        <input
          type="checkbox"
          {...register('fallbackProvider.enabled')}
          className="accent-accent-blue"
        />
        Habilitar provider de fallback
      </label>
      {enabled && (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          <Select
            label="Tipo"
            options={providerOptions}
            {...register('fallbackProvider.type')}
          />
          <Input
            label="Endpoint"
            {...register('fallbackProvider.endpoint')}
            placeholder="https://..."
          />
        </div>
      )}
    </Card>
  )
}

function PromptSection() {
  const { watch, setValue } = useFormContext<AgentFormValues>()
  const instructions = watch('instructions')

  return (
    <Card title="Instrucoes (Prompt)">
      <MonacoEditor
        value={instructions}
        onChange={(v) => setValue('instructions', v)}
        language="markdown"
        height="250px"
      />
    </Card>
  )
}

function ToolsSection() {
  const { watch, setValue } = useFormContext<AgentFormValues>()
  const selected = watch('tools')
  const { data: funcs } = useFunctions()

  return (
    <Card title="Tools">
      <ToolPicker
        functionTools={funcs?.functionTools ?? []}
        codeExecutors={[]}
        selected={selected}
        onChange={(next) => setValue('tools', next)}
      />
    </Card>
  )
}

/**
 * Seletor de MCP servers. Mostra checkboxes com os MCPs registrados em
 * /mcp-servers do projeto atual; o agent guarda só o id e o backend resolve
 * serverLabel/serverUrl/allowedTools/headers em runtime.
 */
function McpToolsSection() {
  const { watch, setValue } = useFormContext<AgentFormValues>()
  const selected = watch('mcpServerIds')
  const { data: mcpServers } = useMcpServers()

  const toggle = (id: string) => {
    const next = selected.includes(id)
      ? selected.filter((s) => s !== id)
      : [...selected, id]
    setValue('mcpServerIds', next)
  }

  return (
    <Card title="MCP Tools">
      <p className="text-xs text-text-muted mb-3">
        Selecione os MCP servers cadastrados em{' '}
        <code className="text-xs bg-bg-tertiary px-1 py-0.5 rounded">/mcp-servers</code>.
        As tools permitidas (<code className="text-xs">allowedTools</code>) de cada registro
        são resolvidas em runtime — editar o registry afeta este agent imediatamente.
      </p>
      {!mcpServers || mcpServers.length === 0 ? (
        <p className="text-sm text-text-dimmed">
          Nenhum MCP server cadastrado. Registre um primeiro em <code className="text-xs">/mcp-servers</code>.
        </p>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-2 max-h-60 overflow-y-auto">
          {mcpServers.map((srv) => (
            <label
              key={srv.id}
              className="flex items-start gap-2 text-sm text-text-secondary hover:text-text-primary cursor-pointer p-2 rounded hover:bg-bg-tertiary"
            >
              <input
                type="checkbox"
                checked={selected.includes(srv.id)}
                onChange={() => toggle(srv.id)}
                className="accent-accent-blue mt-0.5"
              />
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2">
                  <span className="font-medium">{srv.name}</span>
                  <span className="text-[10px] text-text-dimmed font-mono">{srv.id}</span>
                </div>
                <div className="text-xs text-text-muted font-mono truncate">{srv.serverUrl}</div>
                <div className="text-[11px] text-text-dimmed mt-0.5">
                  {srv.allowedTools.length} tool(s) — {srv.requireApproval === 'always' ? '⚑ always approval' : 'sem HITL'}
                </div>
              </div>
            </label>
          ))}
        </div>
      )}
    </Card>
  )
}

function SkillsSection() {
  const { watch, setValue } = useFormContext<AgentFormValues>()
  const selected = watch('skills')
  const { data: skills } = useSkills()

  const toggle = (id: string) => {
    const next = selected.includes(id)
      ? selected.filter((s) => s !== id)
      : [...selected, id]
    setValue('skills', next)
  }

  return (
    <Card title="Skills">
      {!skills || skills.length === 0 ? (
        <p className="text-sm text-text-dimmed">Nenhuma skill disponivel.</p>
      ) : (
        <div className="grid grid-cols-2 md:grid-cols-3 gap-2 max-h-60 overflow-y-auto">
          {skills.map((skill) => (
            <label key={skill.id} className="flex items-center gap-2 text-sm text-text-secondary hover:text-text-primary cursor-pointer">
              <input
                type="checkbox"
                checked={selected.includes(skill.id)}
                onChange={() => toggle(skill.id)}
                className="accent-accent-blue"
              />
              {skill.name}
            </label>
          ))}
        </div>
      )}
    </Card>
  )
}

function StructuredOutputSection() {
  const { register, watch, setValue } = useFormContext<AgentFormValues>()
  const format = watch('structuredOutput.responseFormat')
  const schema = watch('structuredOutput.schema')

  return (
    <Card title="Structured Output">
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-4">
        <Select
          label="Formato"
          options={RESPONSE_FORMAT_OPTIONS}
          {...register('structuredOutput.responseFormat')}
        />
        {format === 'json_schema' && (
          <>
            <Input
              label="Schema Name"
              {...register('structuredOutput.schemaName')}
              placeholder="ResponseSchema"
            />
            <Input
              label="Schema Description"
              {...register('structuredOutput.schemaDescription')}
              placeholder="Descricao do schema"
            />
          </>
        )}
      </div>
      {format === 'json_schema' && (
        <SchemaEditor
          value={schema}
          onChange={(v) => setValue('structuredOutput.schema', v)}
        />
      )}
    </Card>
  )
}

function MiddlewaresSection() {
  const { watch, setValue } = useFormContext<AgentFormValues>()
  const selected = watch('middlewares')
  const { data: funcs } = useFunctions()
  const availableTypes = funcs?.middlewareTypes ?? []

  return (
    <Card title="Middlewares">
      <MiddlewarePicker
        availableTypes={availableTypes}
        value={selected as MiddlewareEntry[]}
        onChange={(next) => setValue('middlewares', next)}
      />
    </Card>
  )
}

function ResilienceSection() {
  const { register } = useFormContext<AgentFormValues>()
  return (
    <Card title="Resiliencia">
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        <Input
          label="Max Retries"
          type="number"
          {...register('resilience.maxRetries', { valueAsNumber: true })}
        />
        <Input
          label="Initial Delay (ms)"
          type="number"
          {...register('resilience.initialDelayMs', { valueAsNumber: true })}
        />
        <Input
          label="Backoff Multiplier"
          type="number"
          step="0.1"
          {...register('resilience.backoffMultiplier', { valueAsNumber: true })}
        />
      </div>
    </Card>
  )
}

function BudgetSection() {
  const { register } = useFormContext<AgentFormValues>()
  return (
    <Card title="Budget">
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        <Input
          label="Max Tokens por Execucao"
          type="number"
          {...register('budget.maxTokensPerExecution', { valueAsNumber: true })}
        />
        <Input
          label="Max Cost (USD)"
          type="number"
          step="0.01"
          {...register('budget.maxCostUsd', { valueAsNumber: true })}
        />
      </div>
    </Card>
  )
}


interface AgentFormProps {
  initialValues?: AgentDef
  onSubmit: (values: AgentFormValues) => void
  loading?: boolean
  existingIds?: Set<string>
}

export function AgentForm({ initialValues, onSubmit, loading, existingIds }: AgentFormProps) {
  const isEdit = !!initialValues

  const schema = useMemo(() => {
    if (isEdit || !existingIds || existingIds.size === 0) return agentFormSchema
    return agentFormSchema.refine((vals) => !existingIds.has(vals.id), {
      path: ['id'],
      message: 'Já existe um agente com esse ID.',
    })
  }, [isEdit, existingIds])

  const methods = useForm<AgentFormValues>({
    resolver: zodResolver(schema) as never,
    defaultValues: initialValues ? agentToFormValues(initialValues) : defaultValues,
  })

  return (
    <FormProvider {...methods}>
      <form onSubmit={methods.handleSubmit(onSubmit)} className="flex flex-col gap-6">
        <IdentitySection isEdit={isEdit} />
        <ModelSection />
        <ProviderSection />
        <FallbackSection />
        <PromptSection />
        <ToolsSection />
        <McpToolsSection />
        <SkillsSection />
        <StructuredOutputSection />
        <MiddlewaresSection />
        <ResilienceSection />
        <BudgetSection />

        <div className="flex justify-end">
          <Button type="submit" loading={loading}>
            {isEdit ? 'Salvar Alteracoes' : 'Criar Agente'}
          </Button>
        </div>
      </form>
    </FormProvider>
  )
}
