import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { listAgents, type Agent } from '../../api/agents'
import {
  PIPELINE_DEPLOYMENT_KIND,
  createWorkflow,
  getWorkflow,
  pipelineWorkflowId,
  updateWorkflow,
  type Workflow,
  type WorkflowAgentReference,
} from '../../api/workflows'
import { ApiError, friendlyError } from '../../api/client'
import { getSystemInfo, type ConsumeHeader } from '../../api/system'
import { HowToConsumeWorkflow } from '../../components/HowToConsumeWorkflow'
import {
  ArrowLeftIcon,
  Badge,
  BoltIcon,
  Button,
  Card,
  CardHeader,
  ErrorMessage,
  Input,
  Spinner,
  Textarea,
} from '../../ui'
import { WorkflowSequenceBuilder, type PipelineStep } from './WorkflowSequenceBuilder'

interface FormState {
  id: string
  name: string
  description: string
  steps: PipelineStep[]
}

function emptyForm(): FormState {
  return { id: pipelineWorkflowId(), name: '', description: '', steps: [] }
}

/**
 * Hidrata o form a partir de um Workflow existente. Resolve nomes dos agentes
 * via `listAgents` pra exibir nome humano nos cards (cache em memória).
 */
function workflowToForm(workflow: Workflow, agents: Map<string, Agent>): FormState {
  return {
    id: workflow.id,
    name: workflow.name,
    description: workflow.description ?? '',
    steps: (workflow.agents ?? []).map((ref) => {
      const a = agents.get(ref.agentId)
      return {
        agentId: ref.agentId,
        agentVersionId: ref.agentVersionId ?? null,
        role: ref.role ?? null,
        name: a?.name,
        description: a?.description ?? null,
      }
    }),
  }
}

function buildPayload(form: FormState, prev: Workflow | null) {
  const agents: WorkflowAgentReference[] = form.steps.map((s) => ({
    agentId: s.agentId,
    agentVersionId: s.agentVersionId ?? null,
    role: s.role ?? null,
  }))
  // Preserva campos opaque do workflow existente via spread (mesmo padrão do
  // AgentEditor formCodec). PUT inclui todos os campos do CreateWorkflowBody —
  // backend valida o conjunto.
  const prevMetadata = (prev as { metadata?: Record<string, string> | null } | null)?.metadata ?? {}
  return {
    id: form.id,
    name: form.name.trim(),
    description: form.description.trim() ? form.description.trim() : null,
    version: prev?.version ?? '1.0.0',
    orchestrationMode: 'Sequential' as const,
    agents,
    executors: [],
    edges: [],
    configuration: {
      maxRounds: prev?.configuration?.maxRounds ?? 1,
      timeoutSeconds: prev?.configuration?.timeoutSeconds ?? 300,
      checkpointMode: prev?.configuration?.checkpointMode ?? 'InMemory',
      inputMode: prev?.configuration?.inputMode ?? 'Standalone',
      enableHumanInTheLoop: prev?.configuration?.enableHumanInTheLoop ?? false,
      exposeAsAgent: prev?.configuration?.exposeAsAgent ?? false,
    },
    metadata: { ...prevMetadata, deploymentKind: PIPELINE_DEPLOYMENT_KIND },
    visibility: 'project' as const,
  }
}

export function PipelineEditor() {
  const { id } = useParams<{ id?: string }>()
  const navigate = useNavigate()
  const isEdit = !!id

  const [form, setForm] = useState<FormState>(emptyForm)
  const [loadedWorkflow, setLoadedWorkflow] = useState<Workflow | null>(null)
  const [loading, setLoading] = useState(isEdit)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [publicBaseUrl, setPublicBaseUrl] = useState<string | null>(null)
  const [consumeHeaders, setConsumeHeaders] = useState<ConsumeHeader[]>([])

  // Resolve a base URL pública pra preencher o exemplo de consumo. Falha
  // silenciosa: o componente usa placeholder se ficar null.
  useEffect(() => {
    let cancelled = false
    getSystemInfo()
      .then((info) => {
        if (cancelled) return
        setPublicBaseUrl(info.publicBaseUrl)
        setConsumeHeaders(info.consumeHeaders ?? [])
      })
      .catch(() => {
        /* placeholder vai entrar no lugar */
      })
    return () => {
      cancelled = true
    }
  }, [])

  // Carregamento inicial: edit mode resolve agente + workflow em paralelo.
  // Create mode só carrega lista de agentes (não usado diretamente aqui — o
  // picker carrega sob demanda).
  useEffect(() => {
    if (!isEdit || !id) return
    let cancelled = false
    setLoading(true)
    setLoadError(null)
    Promise.all([getWorkflow(id), listAgents()])
      .then(([wf, list]) => {
        if (cancelled) return
        const map = new Map(list.map((a) => [a.id, a]))
        setLoadedWorkflow(wf)
        setForm(workflowToForm(wf, map))
      })
      .catch((err: unknown) => {
        if (!cancelled) {
          if (err instanceof ApiError && err.status === 404) {
            setLoadError('Pipeline não encontrado.')
          } else {
            setLoadError(friendlyError(err, 'Não foi possível carregar o pipeline.'))
          }
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [id, isEdit])

  const validation = useMemo(() => validateForm(form), [form])

  const handleSave = async () => {
    if (validation.error) {
      setSaveError(validation.error)
      return
    }
    setSaveError(null)
    setSaving(true)
    try {
      const body = buildPayload(form, loadedWorkflow)
      const wf = isEdit
        ? await updateWorkflow(form.id, body)
        : await createWorkflow(body)
      setLoadedWorkflow(wf)
      // Após criar, navega pro modo edit pra que reload preserve estado.
      if (!isEdit) {
        navigate(`/implantacoes/avancada/${wf.id}`, { replace: true })
      }
    } catch (err) {
      setSaveError(friendlyError(err, 'Não foi possível salvar o pipeline.'))
    } finally {
      setSaving(false)
    }
  }

  if (loading) {
    return (
      <Card className="mx-auto max-w-4xl flex items-center justify-center py-12">
        <Spinner className="h-6 w-6 text-fg-muted" />
      </Card>
    )
  }
  if (loadError) {
    return <ErrorMessage message={loadError} className="mx-auto max-w-4xl" />
  }

  const titleText = isEdit
    ? form.name.trim() || 'Pipeline sem nome'
    : 'Novo pipeline em sequência'

  return (
    <div className="mx-auto max-w-4xl space-y-6 pb-8">
      <div className="flex items-center gap-3">
        <Button
          variant="ghost"
          size="sm"
          leftIcon={<ArrowLeftIcon className="h-4 w-4" />}
          onClick={() => navigate('/implantacoes')}
        >
          Voltar
        </Button>
        <div className="min-w-0 flex-1">
          <h1 className="truncate text-2xl font-semibold tracking-tight">{titleText}</h1>
          <p className="mt-1 text-xs text-fg-muted">
            Implantação em sequência: vários agentes em fila — saída de um vira entrada do próximo.
          </p>
        </div>
        {isEdit && loadedWorkflow && (
          <Badge tone="accent">Pipeline · {form.steps.length} agentes</Badge>
        )}
      </div>

      <Card className="space-y-4">
        <CardHeader title="Identificação" description="Como esse pipeline aparece na lista de implantações." />
        <div className="grid grid-cols-1 gap-4">
          <Input
            label="Nome"
            placeholder="Ex.: Triagem completa"
            value={form.name}
            onChange={(e) => setForm({ ...form, name: e.target.value })}
            maxLength={120}
          />
          <Textarea
            label="Descrição"
            placeholder="Para que serve esse pipeline e em que situação é usado…"
            value={form.description}
            onChange={(e) => setForm({ ...form, description: e.target.value })}
            rows={2}
          />
        </div>
      </Card>

      <Card className="space-y-4">
        <CardHeader
          title="Agentes em sequência"
          description="A entrada do usuário chega no agente 1; cada saída vira input do próximo."
        />
        <WorkflowSequenceBuilder
          steps={form.steps}
          onChange={(steps) => setForm({ ...form, steps })}
        />
      </Card>

      <Card className="space-y-3 border border-accent/30 bg-accent-subtle/30">
        <CardHeader title="Como o pipeline funciona" />
        <ul className="space-y-1.5 text-xs leading-relaxed text-fg-muted">
          <li>• A entrada do usuário chega no <strong>agente 1</strong>.</li>
          <li>• A resposta do agente <strong>N</strong> vira o input do agente <strong>N+1</strong>, automaticamente.</li>
          <li>• A resposta final do pipeline é a saída do <strong>último</strong> agente.</li>
        </ul>
      </Card>

      {isEdit && loadedWorkflow && (
        <HowToConsumeWorkflow
          workflowId={loadedWorkflow.id}
          publicBaseUrl={publicBaseUrl}
          consumeHeaders={consumeHeaders}
          description="O pipeline é assíncrono: dispara com POST, retorna 202 + executionId, e o resultado é lido fazendo polling no GET de execução. O output final é o do último agente da sequência."
        />
      )}

      {saveError && <ErrorMessage message={saveError} />}
      {validation.warning && (
        <div className="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-xs text-warning">
          {validation.warning}
        </div>
      )}

      <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border pt-4">
        <p className="text-[11px] text-fg-dim">
          {isEdit
            ? 'Alterações salvas criam uma nova revisão do workflow automaticamente.'
            : 'Após implantar, o pipeline fica disponível pra consumo via API e pra teste no sandbox.'}
        </p>
        <div className="flex items-center gap-2">
          {isEdit && (
            <Button
              variant="secondary"
              onClick={() => navigate(`/implantacoes/${form.id}/sandbox`)}
              disabled={form.steps.length < 2}
            >
              Testar no sandbox
            </Button>
          )}
          <Button
            onClick={handleSave}
            loading={saving}
            disabled={!!validation.error}
            leftIcon={<BoltIcon className="h-4 w-4" />}
          >
            {isEdit ? 'Salvar' : 'Implantar'}
          </Button>
        </div>
      </div>
    </div>
  )
}

interface Validation {
  error: string | null
  warning: string | null
}

function validateForm(form: FormState): Validation {
  if (!form.name.trim()) return { error: 'Informe um nome para o pipeline.', warning: null }
  if (form.steps.length < 2) {
    return {
      error: 'O pipeline precisa de pelo menos 2 agentes em sequência. Para implantar 1 agente, use "Um agente" na lista de implantações.',
      warning: null,
    }
  }
  // Detecta duplicados (backend rejeita). Mostra warning antes do save pra
  // que o user não bata na rejeição do backend sem entender o motivo.
  const seen = new Map<string, number>()
  for (const step of form.steps) {
    seen.set(step.agentId, (seen.get(step.agentId) ?? 0) + 1)
  }
  const duplicates = [...seen.entries()].filter(([, n]) => n > 1).map(([id]) => id)
  if (duplicates.length > 0) {
    return {
      error: `Agente(s) duplicado(s) na sequência: ${duplicates.join(', ')}. O backend exige agentes únicos por workflow.`,
      warning: null,
    }
  }
  return { error: null, warning: null }
}
