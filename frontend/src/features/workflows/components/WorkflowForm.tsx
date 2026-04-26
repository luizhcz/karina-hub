import { useState } from 'react'
import { useForm, useFieldArray, Controller, useWatch } from 'react-hook-form'
import type { Control, FieldPath, UseFormRegister } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Tabs } from '../../../shared/ui/Tabs'
import { Button } from '../../../shared/ui/Button'
import { Card } from '../../../shared/ui/Card'
import { Input } from '../../../shared/ui/Input'
import { Select } from '../../../shared/ui/Select'
import { Textarea } from '../../../shared/ui/Textarea'
import { CronEditor } from '../../../shared/editors/CronEditor'
import { useAgents } from '../../../api/agents'
import { useFunctions } from '../../../api/tools'
import type { WorkflowDef } from '../../../api/workflows'
import type { WorkflowFormValues } from '../types'
import { HITL_DEFAULTS } from '../types'
import {
  INPUT_MODE_OPTIONS,
  CHECKPOINT_MODE_OPTIONS,
  WORKFLOW_DEFAULTS,
  ORCHESTRATION_MODE_LABELS,
  EDGE_TYPE_LABELS,
  TRIGGER_TYPE_LABELS,
  enumToOptions,
} from '../../../constants/workflow'
import { useEnums } from '../../../api/enums'

const hitlConfigSchema = z.object({
  enabled: z.boolean(),
  when: z.enum(['before', 'after']),
  interactionType: z.enum(['Approval', 'Input', 'Choice']),
  prompt: z.string(),
  showOutput: z.boolean(),
  options: z.string(),
  timeoutSeconds: z.number().min(1),
})

const agentRefSchema = z.object({
  agentId: z.string().min(1, 'Agent ID is required'),
  role: z.string(),
  hitl: hitlConfigSchema,
})

const edgeCaseSchema = z.object({
  condition: z.string(),
  target: z.string(),
  isDefault: z.boolean(),
})

const edgeSchema = z.object({
  from: z.string(),
  to: z.string(),
  edgeType: z.enum(['Direct', 'Conditional', 'Switch', 'FanOut', 'FanIn']),
  condition: z.string(),
  cases: z.array(edgeCaseSchema),
  targets: z.array(z.string()),
  inputSource: z.string(),
})

const executorSchema = z.object({
  id: z.string().min(1, 'ID is required'),
  functionName: z.string().min(1, 'Function name is required'),
  description: z.string(),
  hitl: hitlConfigSchema,
})

const schema = z.object({
  id: z.string().min(1, 'ID is required').regex(/^[a-zA-Z0-9_-]+$/, 'Only alphanumeric, _ and - allowed'),
  name: z.string().min(1, 'Name is required'),
  description: z.string(),
  orchestrationMode: z.string().min(1, 'Mode is required'),
  version: z.string(),
  agents: z.array(agentRefSchema),
  executors: z.array(executorSchema),
  edges: z.array(edgeSchema),
  configuration: z.object({
    maxRounds: z.number().min(0),
    timeoutSeconds: z.number().min(0),
    enableHumanInTheLoop: z.boolean(),
    checkpointMode: z.string(),
    exposeAsAgent: z.boolean(),
    inputMode: z.string(),
  }),
  trigger: z.object({
    type: z.enum(['OnDemand', 'Scheduled', 'EventDriven']),
    cronExpression: z.string(),
    eventTopic: z.string(),
    enabled: z.boolean(),
  }),
  metadata: z.array(z.object({ key: z.string(), value: z.string() })),
})

const DEFAULT_VALUES: WorkflowFormValues = {
  id: '',
  name: '',
  description: '',
  orchestrationMode: 'Graph',
  version: '',
  agents: [],
  executors: [],
  edges: [],
  configuration: {
    maxRounds: WORKFLOW_DEFAULTS.maxRounds,
    timeoutSeconds: WORKFLOW_DEFAULTS.timeoutSeconds,
    enableHumanInTheLoop: false,
    checkpointMode: WORKFLOW_DEFAULTS.checkpointMode,
    exposeAsAgent: false,
    inputMode: WORKFLOW_DEFAULTS.inputMode,
  },
  trigger: {
    type: 'OnDemand',
    cronExpression: WORKFLOW_DEFAULTS.cronExpression,
    eventTopic: '',
    enabled: true,
  },
  metadata: [],
}

const TAB_ITEMS = [
  { key: 'basic', label: 'Basic Info' },
  { key: 'agents', label: 'Agents' },
  { key: 'executors', label: 'Executors' },
  { key: 'edges', label: 'Edges' },
  { key: 'configuration', label: 'Configuration' },
  { key: 'trigger', label: 'Trigger' },
  { key: 'metadata', label: 'Metadata' },
]

interface WorkflowFormProps {
  initialValues?: WorkflowDef
  onSubmit: (values: WorkflowFormValues) => void
  loading?: boolean
  isEdit?: boolean
}

function workflowDefToFormValues(wf: WorkflowDef): WorkflowFormValues {
  return {
    id: wf.id,
    name: wf.name,
    description: wf.description ?? '',
    orchestrationMode: wf.orchestrationMode,
    version: wf.version ?? '',
    agents: (wf.agents ?? []).map((a) => ({
      agentId: a.agentId,
      role: a.role ?? '',
      hitl: a.hitl
        ? {
            enabled: true,
            when: a.hitl.when,
            interactionType: a.hitl.interactionType ?? 'Approval',
            prompt: a.hitl.prompt,
            showOutput: a.hitl.showOutput ?? false,
            options: a.hitl.options?.join(', ') ?? '',
            timeoutSeconds: a.hitl.timeoutSeconds ?? 300,
          }
        : { ...HITL_DEFAULTS },
    })),
    executors: (wf.executors ?? []).map((ex) => ({
      id: ex.id,
      functionName: ex.functionName,
      description: ex.description ?? '',
      hitl: ex.hitl
        ? {
            enabled: true,
            when: ex.hitl.when,
            interactionType: ex.hitl.interactionType ?? 'Approval',
            prompt: ex.hitl.prompt,
            showOutput: ex.hitl.showOutput ?? false,
            options: ex.hitl.options?.join(', ') ?? '',
            timeoutSeconds: ex.hitl.timeoutSeconds ?? 300,
          }
        : { ...HITL_DEFAULTS },
    })),
    edges: (wf.edges ?? []).map((e) => ({
      from: e.from ?? '',
      to: e.to ?? '',
      edgeType: e.edgeType,
      condition: e.condition ?? '',
      cases: (e.cases ?? []).map((c) => ({
        condition: c.condition ?? '',
        target: c.targets?.[0] ?? '',
        isDefault: c.isDefault,
      })),
      targets: e.targets ?? [],
      inputSource: e.inputSource ?? '',
    })),
    configuration: {
      maxRounds: wf.configuration?.maxRounds ?? 10,
      timeoutSeconds: wf.configuration?.timeoutSeconds ?? 300,
      enableHumanInTheLoop: wf.configuration?.enableHumanInTheLoop ?? false,
      checkpointMode: wf.configuration?.checkpointMode ?? 'InMemory',
      exposeAsAgent: wf.configuration?.exposeAsAgent ?? false,
      inputMode: wf.configuration?.inputMode ?? 'Standalone',
    },
    trigger: {
      type: (wf.trigger?.type as WorkflowFormValues['trigger']['type']) ?? 'OnDemand',
      cronExpression: wf.trigger?.cronExpression ?? '0 9 * * *',
      eventTopic: wf.trigger?.eventTopic ?? '',
      enabled: wf.trigger?.enabled ?? true,
    },
    metadata: Object.entries(wf.metadata ?? {}).map(([key, value]) => ({ key, value })),
  }
}

interface EdgeItemProps {
  idx: number
  control: Control<WorkflowFormValues>
  register: UseFormRegister<WorkflowFormValues>
  nodeOptions: { label: string; value: string }[]
  onRemove: () => void
}

function MultiCheckboxField({
  control,
  name,
  label,
  options,
}: {
  control: Control<WorkflowFormValues>
  name: `edges.${number}.targets`
  label: string
  options: { label: string; value: string }[]
}) {
  return (
    <Controller
      control={control}
      name={name}
      render={({ field: f }) => (
        <div className="flex flex-col gap-1">
          <label className="text-xs font-medium text-text-secondary">{label}</label>
          {options.length === 0 && (
            <p className="text-xs text-text-dimmed italic">No nodes available. Add agents or executors first.</p>
          )}
          <div className="flex flex-col gap-1 mt-1">
            {options.map((opt) => (
              <label key={opt.value} className="flex items-center gap-2 cursor-pointer">
                <input
                  type="checkbox"
                  checked={(f.value as string[]).includes(opt.value)}
                  onChange={(e) => {
                    const cur = f.value as string[]
                    f.onChange(
                      e.target.checked
                        ? [...cur, opt.value]
                        : cur.filter((v) => v !== opt.value),
                    )
                  }}
                  className="w-4 h-4 accent-accent-blue"
                />
                <span className="text-sm text-text-primary">{opt.label}</span>
              </label>
            ))}
          </div>
        </div>
      )}
    />
  )
}

function EdgeItem({ idx, control, register, nodeOptions, onRemove }: EdgeItemProps) {
  const edgeType = useWatch({ control, name: `edges.${idx}.edgeType` })
  const { fields: caseFields, append: appendCase, remove: removeCase } = useFieldArray({
    control,
    name: `edges.${idx}.cases`,
  })

  const fromToGrid = (
    <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
      <Controller
        control={control}
        name={`edges.${idx}.from`}
        render={({ field: f }) => (
          <Select
            label="From Node"
            value={f.value}
            onChange={(e) => f.onChange(e.target.value)}
            placeholder="Select source node..."
            options={nodeOptions}
          />
        )}
      />
      <Controller
        control={control}
        name={`edges.${idx}.to`}
        render={({ field: f }) => (
          <Select
            label="To Node"
            value={f.value}
            onChange={(e) => f.onChange(e.target.value)}
            placeholder="Select target node..."
            options={nodeOptions}
          />
        )}
      />
    </div>
  )

  return (
    <div className="bg-bg-tertiary border border-border-secondary rounded-lg p-4 flex flex-col gap-3">
      <div className="flex items-center justify-between mb-1">
        <span className="text-xs font-semibold text-text-muted">Edge #{idx + 1}</span>
        <button
          type="button"
          onClick={onRemove}
          className="text-xs text-red-400 hover:text-red-300 transition-colors"
        >
          Remove
        </button>
      </div>

      <Controller
        control={control}
        name={`edges.${idx}.edgeType`}
        render={({ field: f }) => (
          <Select
            label="Edge Type"
            value={f.value}
            onChange={(e) =>
              f.onChange(e.target.value as WorkflowFormValues['edges'][number]['edgeType'])
            }
            options={enumToOptions(undefined, EDGE_TYPE_LABELS)}
          />
        )}
      />

      {edgeType === 'Direct' && fromToGrid}

      {edgeType === 'Conditional' && (
        <>
          {fromToGrid}
          <Input
            label="Condition"
            placeholder="e.g. result.status === 'approved'"
            {...register(`edges.${idx}.condition`)}
          />
        </>
      )}

      {edgeType === 'Switch' && (
        <>
          <Controller
            control={control}
            name={`edges.${idx}.from`}
            render={({ field: f }) => (
              <Select
                label="From Node (switch source)"
                value={f.value}
                onChange={(e) => f.onChange(e.target.value)}
                placeholder="Select source node..."
                options={nodeOptions}
              />
            )}
          />
          <div className="flex flex-col gap-2">
            <div className="flex items-center justify-between">
              <span className="text-xs font-medium text-text-secondary">Switch Cases</span>
              <button
                type="button"
                onClick={() => appendCase({ condition: '', target: '', isDefault: false })}
                className="text-xs text-accent-blue hover:underline"
              >
                + Add Case
              </button>
            </div>
            {caseFields.length === 0 && (
              <p className="text-xs text-text-dimmed italic">No cases defined.</p>
            )}
            {caseFields.map((cf, ci) => (
              <div
                key={cf.id}
                className="bg-bg-secondary border border-border-primary rounded-md p-3 flex flex-col gap-2"
              >
                <div className="flex items-center justify-between">
                  <span className="text-xs text-text-muted">Case #{ci + 1}</span>
                  <button
                    type="button"
                    onClick={() => removeCase(ci)}
                    className="text-xs text-red-400 hover:text-red-300"
                  >
                    Remove
                  </button>
                </div>
                <label className="flex items-center gap-2 cursor-pointer">
                  <input
                    type="checkbox"
                    {...register(`edges.${idx}.cases.${ci}.isDefault`)}
                    className="w-4 h-4 accent-accent-blue"
                  />
                  <span className="text-xs text-text-secondary">Default case (fallback)</span>
                </label>
                <Input
                  label="Condition"
                  placeholder="e.g. output === 'buy'"
                  {...register(`edges.${idx}.cases.${ci}.condition`)}
                />
                <Controller
                  control={control}
                  name={`edges.${idx}.cases.${ci}.target`}
                  render={({ field: f }) => (
                    <Select
                      label="Target Node"
                      value={f.value}
                      onChange={(e) => f.onChange(e.target.value)}
                      placeholder="Select target node..."
                      options={nodeOptions}
                    />
                  )}
                />
              </div>
            ))}
          </div>
        </>
      )}

      {edgeType === 'FanOut' && (
        <>
          <Controller
            control={control}
            name={`edges.${idx}.from`}
            render={({ field: f }) => (
              <Select
                label="From Node"
                value={f.value}
                onChange={(e) => f.onChange(e.target.value)}
                placeholder="Select source node..."
                options={nodeOptions}
              />
            )}
          />
          <MultiCheckboxField
            control={control}
            name={`edges.${idx}.targets`}
            label="Target Nodes (select all destinations)"
            options={nodeOptions}
          />
        </>
      )}

      {edgeType === 'FanIn' && (
        <>
          <MultiCheckboxField
            control={control}
            name={`edges.${idx}.targets`}
            label="Source Nodes (select all inputs)"
            options={nodeOptions}
          />
          <Controller
            control={control}
            name={`edges.${idx}.to`}
            render={({ field: f }) => (
              <Select
                label="To Node (convergence point)"
                value={f.value}
                onChange={(e) => f.onChange(e.target.value)}
                placeholder="Select target node..."
                options={nodeOptions}
              />
            )}
          />
        </>
      )}

      <Controller
        control={control}
        name={`edges.${idx}.inputSource`}
        render={({ field: f }) => (
          <Select
            label="Input Source"
            value={f.value}
            onChange={(e) => f.onChange(e.target.value)}
            options={[
              { label: 'Output do nó anterior (padrão)', value: '' },
              { label: 'Input original do workflow', value: 'WorkflowInput' },
            ]}
          />
        )}
      />
    </div>
  )
}

/**
 * Paths de HITL config permitidos no form. Restringe o componente para nunca receber
 * um path arbitrário — o type checker valida `${prefix}.enabled` etc. contra a shape real.
 */
type HitlFieldsPrefix =
  | `agents.${number}.hitl`
  | `executors.${number}.hitl`

function HitlFields({
  prefix,
  control,
  register,
  watch,
}: {
  prefix: HitlFieldsPrefix
  control: Control<WorkflowFormValues>
  register: UseFormRegister<WorkflowFormValues>
  watch: ReturnType<typeof useForm<WorkflowFormValues>>['watch']
}) {
  // FieldPath<...> em vez de `as any` — valida contra a shape do form
  const enabled = watch(`${prefix}.enabled` as FieldPath<WorkflowFormValues>)
  const interactionType = watch(`${prefix}.interactionType` as FieldPath<WorkflowFormValues>)

  return (
    <div className="border-t border-border-secondary pt-3 mt-1">
      <Controller
        control={control}
        name={`${prefix}.enabled` as 'agents.0.hitl.enabled'}
        render={({ field }) => (
          <label className="flex items-center gap-2 cursor-pointer">
            <input
              type="checkbox"
              checked={field.value}
              onChange={(e) => field.onChange(e.target.checked)}
              className="rounded"
            />
            <span className="text-sm font-medium text-text-primary">Human-in-the-Loop</span>
          </label>
        )}
      />

      {enabled && (
        <div className="flex flex-col gap-3 mt-3 pl-1">
          <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
            <Controller
              control={control}
              name={`${prefix}.when` as 'agents.0.hitl.when'}
              render={({ field: f }) => (
                <Select
                  label="Quando"
                  value={f.value}
                  onChange={(e) => f.onChange(e.target.value)}
                  options={[
                    { value: 'before', label: 'Antes da execução' },
                    { value: 'after', label: 'Depois da execução' },
                  ]}
                />
              )}
            />
            <Controller
              control={control}
              name={`${prefix}.interactionType` as 'agents.0.hitl.interactionType'}
              render={({ field: f }) => (
                <Select
                  label="Tipo de interação"
                  value={f.value}
                  onChange={(e) => f.onChange(e.target.value)}
                  options={[
                    { value: 'Approval', label: 'Aprovação' },
                    { value: 'Input', label: 'Texto livre' },
                    { value: 'Choice', label: 'Escolha múltipla' },
                  ]}
                />
              )}
            />
            <Input
              label="Timeout (s)"
              type="number"
              {...register(`${prefix}.timeoutSeconds` as 'agents.0.hitl.timeoutSeconds', { valueAsNumber: true })}
            />
          </div>
          <Input
            label="Pergunta *"
            placeholder="Ex: Aprovar execução deste passo?"
            {...register(`${prefix}.prompt` as 'agents.0.hitl.prompt')}
          />
          {interactionType === 'Choice' && (
            <Input
              label="Opções (separadas por vírgula)"
              placeholder="Ex: Confirmar, Cancelar, Voltar"
              {...register(`${prefix}.options` as 'agents.0.hitl.options')}
            />
          )}
          <Controller
            control={control}
            name={`${prefix}.showOutput` as 'agents.0.hitl.showOutput'}
            render={({ field }) => (
              <label className="flex items-center gap-2 cursor-pointer">
                <input
                  type="checkbox"
                  checked={field.value}
                  onChange={(e) => field.onChange(e.target.checked)}
                  className="rounded"
                />
                <span className="text-xs text-text-muted">Mostrar output do nó no popup de aprovação</span>
              </label>
            )}
          />
        </div>
      )}
    </div>
  )
}

export function WorkflowForm({ initialValues, onSubmit, loading, isEdit }: WorkflowFormProps) {
  const [activeTab, setActiveTab] = useState('basic')
  const { data: availableAgents } = useAgents()
  const { data: availableFunctions } = useFunctions()
  const { data: enums } = useEnums()

  const {
    register,
    control,
    handleSubmit,
    watch,
    formState: { errors },
  } = useForm<WorkflowFormValues>({
    resolver: zodResolver(schema),
    defaultValues: initialValues ? workflowDefToFormValues(initialValues) : DEFAULT_VALUES,
  })

  const agentFields = useFieldArray({ control, name: 'agents' })
  const executorFields = useFieldArray({ control, name: 'executors' })
  const edgeFields = useFieldArray({ control, name: 'edges' })
  const metaFields = useFieldArray({ control, name: 'metadata' })

  const watchedAgents = watch('agents')
  const watchedExecutors = watch('executors')
  const triggerType = watch('trigger.type')

  const agentIdOptions = (availableAgents ?? []).map((a) => ({ label: `${a.name} (${a.id})`, value: a.id }))
  const executorNameOptions = (availableFunctions?.codeExecutors ?? [])
    .map((e) => ({ label: e.name, value: e.name }))

  // Nós conectáveis por edges — agents + executors
  const nodeOptions = [
    ...watchedAgents
      .filter((a) => a.agentId)
      .map((a) => {
        const agent = availableAgents?.find((ag) => ag.id === a.agentId)
        return { label: agent ? `${agent.name} (${a.agentId})` : a.agentId, value: a.agentId }
      }),
    ...watchedExecutors
      .filter((e) => e.id)
      .map((e) => ({ label: `${e.functionName || e.id} [executor]`, value: e.id })),
  ]

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-6">
      <Tabs items={TAB_ITEMS} active={activeTab} onChange={setActiveTab} />

      {activeTab === 'basic' && (
        <Card title="Basic Information">
          <div className="flex flex-col gap-4">
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <div className="flex flex-col gap-1">
                <Input
                  label="Workflow ID *"
                  placeholder="my-workflow-id"
                  disabled={isEdit}
                  {...register('id')}
                />
                {errors.id && <span className="text-xs text-red-400">{errors.id.message}</span>}
              </div>
              <div className="flex flex-col gap-1">
                <Input label="Name *" placeholder="My Workflow" {...register('name')} />
                {errors.name && <span className="text-xs text-red-400">{errors.name.message}</span>}
              </div>
            </div>

            <Textarea
              label="Description"
              placeholder="Describe what this workflow does..."
              rows={3}
              {...register('description')}
            />

            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <Controller
                control={control}
                name="orchestrationMode"
                render={({ field }) => (
                  <Select
                    label="Orchestration Mode *"
                    value={field.value}
                    onChange={(e) => field.onChange(e.target.value)}
                    options={enumToOptions(enums?.orchestrationModes, ORCHESTRATION_MODE_LABELS)}
                  />
                )}
              />
              <Input label="Version" placeholder="1.0.0" {...register('version')} />
            </div>
          </div>
        </Card>
      )}

      {activeTab === 'agents' && (
        <Card
          title="Agents"
          actions={
            <Button
              type="button"
              variant="secondary"
              size="sm"
              onClick={() => agentFields.append({ agentId: '', role: '', hitl: { ...HITL_DEFAULTS } })}
            >
              + Add Agent
            </Button>
          }
        >
          <div className="flex flex-col gap-4">
            {agentFields.fields.length === 0 && (
              <p className="text-sm text-text-dimmed text-center py-6">
                No agents added. Click "Add Agent" to add one.
              </p>
            )}
            {agentFields.fields.map((field, idx) => (
              <div
                key={field.id}
                className="bg-bg-tertiary border border-border-secondary rounded-lg p-4 flex flex-col gap-3"
              >
                <div className="flex items-center justify-between mb-1">
                  <span className="text-xs font-semibold text-text-muted">Agent #{idx + 1}</span>
                  <button
                    type="button"
                    onClick={() => agentFields.remove(idx)}
                    className="text-xs text-red-400 hover:text-red-300 transition-colors"
                  >
                    Remove
                  </button>
                </div>
                <Controller
                  control={control}
                  name={`agents.${idx}.agentId`}
                  render={({ field: f }) => (
                    <Select
                      label="Agent *"
                      value={f.value}
                      onChange={(e) => f.onChange(e.target.value)}
                      placeholder="Select agent..."
                      options={agentIdOptions}
                    />
                  )}
                />
                {errors.agents?.[idx]?.agentId && (
                  <span className="text-xs text-red-400">
                    {errors.agents[idx].agentId?.message}
                  </span>
                )}
                <Input
                  label="Role"
                  placeholder="e.g. orchestrator, validator..."
                  {...register(`agents.${idx}.role`)}
                />
                <HitlFields prefix={`agents.${idx}.hitl`} control={control} register={register} watch={watch} />
              </div>
            ))}
          </div>
        </Card>
      )}

      {activeTab === 'executors' && (
        <Card
          title="Executor Nodes"
          actions={
            <Button
              type="button"
              variant="secondary"
              size="sm"
              onClick={() => executorFields.append({ id: '', functionName: '', description: '', hitl: { ...HITL_DEFAULTS } })}
            >
              + Add Executor
            </Button>
          }
        >
          <div className="flex flex-col gap-4">
            <p className="text-xs text-text-muted">
              Executor nodes run code functions (non-LLM steps) that can be connected via edges in
              Graph workflows.
            </p>
            {executorFields.fields.length === 0 && (
              <p className="text-sm text-text-dimmed text-center py-6">
                No executors defined. Click "Add Executor" to add one.
              </p>
            )}
            {executorFields.fields.map((field, idx) => (
              <div
                key={field.id}
                className="bg-bg-tertiary border border-border-secondary rounded-lg p-4 flex flex-col gap-3"
              >
                <div className="flex items-center justify-between mb-1">
                  <span className="text-xs font-semibold text-text-muted">Executor #{idx + 1}</span>
                  <button
                    type="button"
                    onClick={() => executorFields.remove(idx)}
                    className="text-xs text-red-400 hover:text-red-300 transition-colors"
                  >
                    Remove
                  </button>
                </div>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                  <div className="flex flex-col gap-1">
                    <Input
                      label="Executor ID *"
                      placeholder="e.g. risk-calculator"
                      {...register(`executors.${idx}.id`)}
                    />
                    {errors.executors?.[idx]?.id && (
                      <span className="text-xs text-red-400">
                        {errors.executors[idx].id?.message}
                      </span>
                    )}
                  </div>
                  <div className="flex flex-col gap-1">
                    <Controller
                      control={control}
                      name={`executors.${idx}.functionName`}
                      render={({ field: f }) => (
                        <Select
                          label="Function Name *"
                          value={f.value}
                          onChange={(e) => f.onChange(e.target.value)}
                          placeholder="Selecione um executor..."
                          options={executorNameOptions}
                        />
                      )}
                    />
                    {errors.executors?.[idx]?.functionName && (
                      <span className="text-xs text-red-400">
                        {errors.executors[idx].functionName?.message}
                      </span>
                    )}
                  </div>
                </div>
                <Input
                  label="Description"
                  placeholder="What does this executor do?"
                  {...register(`executors.${idx}.description`)}
                />
                <HitlFields prefix={`executors.${idx}.hitl`} control={control} register={register} watch={watch} />
              </div>
            ))}
          </div>
        </Card>
      )}

      {activeTab === 'edges' && (
        <Card
          title="Edges"
          actions={
            <Button
              type="button"
              variant="secondary"
              size="sm"
              onClick={() =>
                edgeFields.append({
                  from: '',
                  to: '',
                  edgeType: 'Direct',
                  condition: '',
                  cases: [],
                  targets: [],
                  inputSource: '',
                })
              }
            >
              + Add Edge
            </Button>
          }
        >
          <div className="flex flex-col gap-4">
            {edgeFields.fields.length === 0 && (
              <p className="text-sm text-text-dimmed text-center py-6">
                No edges defined. Add agents/executors first, then connect them here.
              </p>
            )}
            {edgeFields.fields.map((field, idx) => (
              <EdgeItem
                key={field.id}
                idx={idx}
                control={control}
                register={register}
                nodeOptions={nodeOptions}
                onRemove={() => edgeFields.remove(idx)}
              />
            ))}
          </div>
        </Card>
      )}

      {activeTab === 'configuration' && (
        <Card title="Configuration">
          <div className="flex flex-col gap-5">
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <Input
                label="Max Rounds"
                type="number"
                {...register('configuration.maxRounds', { valueAsNumber: true })}
              />
              <Input
                label="Timeout (seconds)"
                type="number"
                {...register('configuration.timeoutSeconds', { valueAsNumber: true })}
              />
            </div>

            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <Controller
                control={control}
                name="configuration.checkpointMode"
                render={({ field }) => (
                  <Select
                    label="Checkpoint Mode"
                    value={field.value}
                    onChange={(e) => field.onChange(e.target.value)}
                    options={CHECKPOINT_MODE_OPTIONS}
                  />
                )}
              />
              <Controller
                control={control}
                name="configuration.inputMode"
                render={({ field }) => (
                  <Select
                    label="Input Mode"
                    value={field.value}
                    onChange={(e) => field.onChange(e.target.value)}
                    options={INPUT_MODE_OPTIONS}
                  />
                )}
              />
            </div>

            <div className="flex flex-col gap-3">
              <Controller
                control={control}
                name="configuration.enableHumanInTheLoop"
                render={({ field }) => (
                  <label className="flex items-center gap-3 cursor-pointer">
                    <input
                      type="checkbox"
                      checked={field.value}
                      onChange={(e) => field.onChange(e.target.checked)}
                      className="w-4 h-4 accent-accent-blue"
                    />
                    <div>
                      <p className="text-sm font-medium text-text-primary">
                        Enable Human in the Loop
                      </p>
                      <p className="text-xs text-text-muted">
                        Pause workflow for human review at checkpoints
                      </p>
                    </div>
                  </label>
                )}
              />
              <Controller
                control={control}
                name="configuration.exposeAsAgent"
                render={({ field }) => (
                  <label className="flex items-center gap-3 cursor-pointer">
                    <input
                      type="checkbox"
                      checked={field.value}
                      onChange={(e) => field.onChange(e.target.checked)}
                      className="w-4 h-4 accent-accent-blue"
                    />
                    <div>
                      <p className="text-sm font-medium text-text-primary">Expose as Agent</p>
                      <p className="text-xs text-text-muted">
                        Make this workflow callable as an agent
                      </p>
                    </div>
                  </label>
                )}
              />
            </div>
          </div>
        </Card>
      )}

      {activeTab === 'trigger' && (
        <Card title="Trigger">
          <div className="flex flex-col gap-5">
            <Controller
              control={control}
              name="trigger.type"
              render={({ field }) => (
                <Select
                  label="Trigger Type"
                  value={field.value}
                  onChange={(e) =>
                    field.onChange(e.target.value as WorkflowFormValues['trigger']['type'])
                  }
                  options={enumToOptions(enums?.triggerTypes, TRIGGER_TYPE_LABELS)}
                />
              )}
            />

            {triggerType === 'Scheduled' && (
              <Controller
                control={control}
                name="trigger.cronExpression"
                render={({ field }) => (
                  <CronEditor value={field.value} onChange={field.onChange} />
                )}
              />
            )}

            {triggerType === 'EventDriven' && (
              <Input
                label="Event Topic"
                placeholder="e.g. orders.created"
                {...register('trigger.eventTopic')}
              />
            )}

            <Controller
              control={control}
              name="trigger.enabled"
              render={({ field }) => (
                <label className="flex items-center gap-3 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={field.value}
                    onChange={(e) => field.onChange(e.target.checked)}
                    className="w-4 h-4 accent-accent-blue"
                  />
                  <div>
                    <p className="text-sm font-medium text-text-primary">Trigger Enabled</p>
                    <p className="text-xs text-text-muted">
                      {triggerType === 'Scheduled'
                        ? 'Enable or disable the scheduled trigger'
                        : triggerType === 'EventDriven'
                          ? 'Enable or disable event-driven triggering'
                          : 'Allow on-demand execution'}
                    </p>
                  </div>
                </label>
              )}
            />
          </div>
        </Card>
      )}

      {activeTab === 'metadata' && (
        <Card
          title="Metadata"
          actions={
            <Button
              type="button"
              variant="secondary"
              size="sm"
              onClick={() => metaFields.append({ key: '', value: '' })}
            >
              + Add Entry
            </Button>
          }
        >
          <div className="flex flex-col gap-3">
            {metaFields.fields.length === 0 && (
              <p className="text-sm text-text-dimmed text-center py-6">
                No metadata entries. Click "Add Entry" to add key-value pairs.
              </p>
            )}
            {metaFields.fields.map((field, idx) => (
              <div key={field.id} className="flex items-center gap-3">
                <div className="flex-1">
                  <Input placeholder="Key" {...register(`metadata.${idx}.key`)} />
                </div>
                <div className="flex-1">
                  <Input placeholder="Value" {...register(`metadata.${idx}.value`)} />
                </div>
                <button
                  type="button"
                  onClick={() => metaFields.remove(idx)}
                  className="text-red-400 hover:text-red-300 transition-colors p-1 mt-1"
                  title="Remove"
                >
                  ✕
                </button>
              </div>
            ))}
          </div>
        </Card>
      )}

      <div className="flex items-center justify-between pt-2">
        <div className="flex items-center gap-2">
          {Object.keys(errors).length > 0 && (
            <span className="text-xs text-red-400">
              Please fix the errors above before saving.
            </span>
          )}
        </div>
        <div className="flex items-center gap-2">
          <span className="text-xs text-text-dimmed">
            {TAB_ITEMS.findIndex((t) => t.key === activeTab) + 1} / {TAB_ITEMS.length} tabs
          </span>
          {activeTab !== TAB_ITEMS[TAB_ITEMS.length - 1].key && (
            <Button
              type="button"
              variant="secondary"
              size="sm"
              onClick={() => {
                const idx = TAB_ITEMS.findIndex((t) => t.key === activeTab)
                if (idx < TAB_ITEMS.length - 1) setActiveTab(TAB_ITEMS[idx + 1].key)
              }}
            >
              Next →
            </Button>
          )}
          <Button type="submit" loading={loading}>
            {isEdit ? 'Save Changes' : 'Create Workflow'}
          </Button>
        </div>
      </div>
    </form>
  )
}
