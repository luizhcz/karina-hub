import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { getAgent, listAgents, type Agent } from '../../api/agents'
import {
  getRouterIntent,
  type RouterIntent,
} from '../../api/routerIntents'
import {
  ROUTING_DEPLOYMENT_KIND,
  createWorkflow,
  getWorkflow,
  routingWorkflowId,
  updateWorkflow,
  type CreateWorkflowBody,
  type Workflow,
} from '../../api/workflows'
import { ApiError, friendlyError } from '../../api/client'
import { getSystemInfo } from '../../api/system'
import { useIsAdmin } from '../../stores/me'
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
  Select,
  Spinner,
  Textarea,
} from '../../ui'

interface BranchEntry {
  intentId: string
  intentName: string
  intentDisplayName?: string | null
  agentId: string
}

interface FormState {
  id: string
  name: string
  description: string
  routerAgentId: string
  branches: BranchEntry[]
  fallbackAgentId: string
}

function emptyForm(): FormState {
  return {
    id: routingWorkflowId(),
    name: '',
    description: '',
    routerAgentId: '',
    branches: [],
    fallbackAgentId: '',
  }
}

// Reconstroi o FormState a partir de um workflow Routing existente.
// Convenção do save: agents[0]=Router, agents[last]=Fallback (role='Fallback');
// edge Switch contém os mapeamentos { intent_name → branch_agent_id }.
function workflowToForm(workflow: Workflow): FormState {
  const agents = workflow.agents ?? []
  const router = agents.find((a) => a.role === 'Router')
  const fallback = agents.find((a) => a.role === 'Fallback')
  const edges = (workflow as { edges?: unknown[] }).edges ?? []
  const switchEdge = edges.find(
    (e): e is { Cases?: { Predicate?: { Value?: unknown }; Targets?: string[]; IsDefault?: boolean }[] } => {
      if (!e || typeof e !== 'object') return false
      return (e as { EdgeType?: string }).EdgeType === 'Switch'
    },
  )
  const branches: BranchEntry[] = []
  if (switchEdge?.Cases) {
    for (const c of switchEdge.Cases) {
      if (c.IsDefault) continue
      const value = c.Predicate?.Value
      const intentName = typeof value === 'string' ? value : ''
      const targetAgentId = c.Targets?.[0] ?? ''
      if (intentName && targetAgentId) {
        branches.push({
          intentId: intentName,
          intentName,
          agentId: targetAgentId,
        })
      }
    }
  }
  return {
    id: workflow.id,
    name: workflow.name,
    description: workflow.description ?? '',
    routerAgentId: router?.agentId ?? '',
    branches,
    fallbackAgentId: fallback?.agentId ?? '',
  }
}

interface BuildPayloadInput {
  form: FormState
  prev: Workflow | null
}

function buildPayload({ form, prev }: BuildPayloadInput): CreateWorkflowBody {
  const branchAgentIds = Array.from(new Set(form.branches.map((b) => b.agentId)))
  const agents = [
    { agentId: form.routerAgentId, agentVersionId: null, role: 'Router' },
    ...branchAgentIds.map((id) => ({ agentId: id, agentVersionId: null, role: 'BranchAgent' })),
    { agentId: form.fallbackAgentId, agentVersionId: null, role: 'Fallback' },
  ]
  // Switch edge: 1 case por intent + default case pro fallback. Predicates
  // comparam $.intent (path canônico do schema Router formal) com o nome
  // técnico da intent (snake_case). Mesma forma que os seeds atendimento-*
  // usam (eles ramificam por $.target_agent porque são Routers custom; aqui
  // o Router é do tipo formal e emite $.intent).
  const edges = [
    {
      From: form.routerAgentId,
      EdgeType: 'Switch',
      Cases: [
        ...form.branches.map((b) => ({
          Predicate: {
            Path: '$.intent',
            Operator: 'Eq',
            Value: b.intentName,
            ValueType: 'String',
          },
          Targets: [b.agentId],
          IsDefault: false,
        })),
        {
          Predicate: null,
          Targets: [form.fallbackAgentId],
          IsDefault: true,
        },
      ],
      InputSource: 'WorkflowInput',
    },
  ]
  const prevMetadata =
    (prev as { metadata?: Record<string, string> | null } | null)?.metadata ?? {}
  return {
    id: form.id,
    name: form.name.trim(),
    description: form.description.trim() ? form.description.trim() : null,
    version: prev?.version ?? '1.0.0',
    orchestrationMode: 'Graph',
    agents,
    executors: [],
    edges,
    configuration: {
      maxRounds: prev?.configuration?.maxRounds ?? 1,
      timeoutSeconds: prev?.configuration?.timeoutSeconds ?? 300,
      checkpointMode: prev?.configuration?.checkpointMode ?? 'InMemory',
      inputMode: prev?.configuration?.inputMode ?? 'Standalone',
      enableHumanInTheLoop: prev?.configuration?.enableHumanInTheLoop ?? false,
      exposeAsAgent: prev?.configuration?.exposeAsAgent ?? false,
    },
    metadata: { ...prevMetadata, deploymentKind: ROUTING_DEPLOYMENT_KIND },
    visibility: 'project',
  }
}

interface Validation {
  error: string | null
}

function validateForm(form: FormState): Validation {
  if (!form.name.trim()) return { error: 'Informe um nome para a implantação.' }
  if (!form.routerAgentId) return { error: 'Selecione o Router que vai classificar a entrada.' }
  if (form.branches.length === 0) {
    return { error: 'O Router selecionado não tem intents — cadastre intents antes de implantar.' }
  }
  const unmapped = form.branches.filter((b) => !b.agentId)
  if (unmapped.length > 0) {
    return {
      error: `Falta atribuir agente pra ${unmapped.length} intent${unmapped.length === 1 ? '' : 's'}: ${unmapped
        .map((b) => b.intentDisplayName || b.intentName)
        .join(', ')}.`,
    }
  }
  if (!form.fallbackAgentId) {
    return {
      error:
        'Agente de fallback é obrigatório — Router pode emitir intent inesperada (confidence baixo, intent fora do enum) e o workflow precisa de um caminho default.',
    }
  }
  return { error: null }
}

export function RoutingDeployEditor() {
  const { id } = useParams<{ id?: string }>()
  const navigate = useNavigate()
  const isAdmin = useIsAdmin()
  const isEdit = !!id

  const [form, setForm] = useState<FormState>(emptyForm)
  const [loadedWorkflow, setLoadedWorkflow] = useState<Workflow | null>(null)
  const [agents, setAgents] = useState<Agent[]>([])
  const [loadingAgents, setLoadingAgents] = useState(true)
  const [loading, setLoading] = useState(isEdit)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [intentsLoading, setIntentsLoading] = useState(false)
  const [intentsError, setIntentsError] = useState<string | null>(null)
  const [publicBaseUrl, setPublicBaseUrl] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    setLoadingAgents(true)
    listAgents()
      .then((list) => {
        if (!cancelled) setAgents(list.filter((a) => a.enabled !== false))
      })
      .catch((err: unknown) => {
        if (!cancelled) setSaveError(friendlyError(err, 'Não foi possível carregar agentes.'))
      })
      .finally(() => {
        if (!cancelled) setLoadingAgents(false)
      })
    getSystemInfo()
      .then((info) => {
        if (!cancelled) setPublicBaseUrl(info.publicBaseUrl)
      })
      .catch(() => undefined)
    return () => {
      cancelled = true
    }
  }, [])

  // Edit mode: hidrata form do workflow existente.
  useEffect(() => {
    if (!isEdit || !id) return
    let cancelled = false
    setLoading(true)
    setLoadError(null)
    getWorkflow(id)
      .then((wf) => {
        if (cancelled) return
        setLoadedWorkflow(wf)
        setForm(workflowToForm(wf))
      })
      .catch((err: unknown) => {
        if (!cancelled) {
          if (err instanceof ApiError && err.status === 404) {
            setLoadError('Implantação de roteamento não encontrada.')
          } else {
            setLoadError(friendlyError(err, 'Não foi possível carregar a implantação.'))
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

  // Quando o user troca de Router, faz GET /agents/{id} pra carregar o set
  // atualizado de routerIntentIds (a listAgents NÃO popula esse campo — só
  // o getAgent individual). Depois resolve cada intent via getRouterIntent
  // em batch e remonta as branches. Preserva mapeamentos por intentName
  // quando coincidirem entre Routers diferentes.
  useEffect(() => {
    if (!form.routerAgentId) {
      if (form.branches.length > 0) {
        setForm((prev) => ({ ...prev, branches: [] }))
      }
      return
    }

    let cancelled = false
    setIntentsLoading(true)
    setIntentsError(null)
    getAgent(form.routerAgentId)
      .then(async (router) => {
        if (cancelled) return
        const intentIds = router.routerIntentIds ?? []
        if (intentIds.length === 0) {
          setForm((prev) => ({ ...prev, branches: [] }))
          return
        }
        // Skip refetch se as branches já refletem o set atual (caso edit
        // mode + Router não mudou) — evita resetar mapeamentos manuais.
        const currentIds = new Set(form.branches.map((b) => b.intentId))
        const wanted = new Set(intentIds)
        const sameSet =
          currentIds.size === wanted.size && [...currentIds].every((x) => wanted.has(x))
        if (sameSet && form.branches.every((b) => b.intentName)) return

        const results = await Promise.all(
          intentIds.map((iid) => getRouterIntent(iid).catch(() => null)),
        )
        if (cancelled) return
        const resolved: RouterIntent[] = results.filter((r): r is RouterIntent => !!r)
        const prevMappings = new Map(form.branches.map((b) => [b.intentName, b.agentId]))
        setForm((prev) => ({
          ...prev,
          branches: resolved.map((intent) => ({
            intentId: intent.id,
            intentName: intent.name,
            intentDisplayName: intent.displayName ?? null,
            agentId: prevMappings.get(intent.name) ?? '',
          })),
        }))
      })
      .catch((err: unknown) => {
        if (!cancelled) {
          setIntentsError(friendlyError(err, 'Não foi possível carregar as intents do Router.'))
        }
      })
      .finally(() => {
        if (!cancelled) setIntentsLoading(false)
      })
    return () => {
      cancelled = true
    }
    // form.branches intencionalmente fora — re-resolução só quando o Router muda.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [form.routerAgentId])

  const routers = useMemo(
    () => agents.filter((a) => a.type === 'Router'),
    [agents],
  )
  // Branch agents: agentes publicados que NÃO sejam Router (entry node
  // já é Router) e NÃO sejam Conversational (Conversational exige
  // workflow Chat dedicado, fora do shape Graph+Switch deste deploy).
  const branchAgents = useMemo(
    () => agents.filter((a) => a.type !== 'Router' && a.type !== 'Conversational'),
    [agents],
  )

  const setBranchAgent = (intentId: string, agentId: string) => {
    setForm((prev) => ({
      ...prev,
      branches: prev.branches.map((b) =>
        b.intentId === intentId ? { ...b, agentId } : b,
      ),
    }))
  }

  const validation = useMemo(() => validateForm(form), [form])

  const handleSave = async () => {
    if (validation.error) {
      setSaveError(validation.error)
      return
    }
    setSaveError(null)
    setSaving(true)
    try {
      const body = buildPayload({ form, prev: loadedWorkflow })
      const wf = isEdit
        ? await updateWorkflow(form.id, body)
        : await createWorkflow(body)
      setLoadedWorkflow(wf)
      if (!isEdit) {
        navigate(`/implantacoes/roteamento/${wf.id}`, { replace: true })
      }
    } catch (err) {
      setSaveError(friendlyError(err, 'Não foi possível salvar a implantação.'))
    } finally {
      setSaving(false)
    }
  }

  // Routing por intent é admin-only: o card no modal de "Nova implantação"
  // e o filtro pill já escondem o caminho na UI, mas a rota direta
  // (/implantacoes/roteamento[/:id]) ainda era acessível digitando a URL.
  // null = probe de /me ainda em vôo; renderiza spinner pra evitar flash
  // da tela de restrição antes da resolução.
  if (isAdmin === null) {
    return (
      <Card className="mx-auto max-w-4xl flex items-center justify-center py-12">
        <Spinner className="h-6 w-6 text-fg-muted" />
      </Card>
    )
  }
  if (isAdmin === false) {
    return (
      <ErrorMessage
        message="Esta tela é restrita a administradores. Se você precisa implantar um roteamento por intent, fale com o time de governança."
        className="mx-auto max-w-4xl"
      />
    )
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
    ? form.name.trim() || 'Roteamento sem nome'
    : 'Nova implantação por roteamento'

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
            Router classifica a entrada e o Switch edge encaminha pra branch que a intent indica. Fallback obrigatório cobre intents inesperadas.
          </p>
        </div>
        {isEdit && loadedWorkflow && (
          <Badge tone="accent">Roteamento · {form.branches.length} branches</Badge>
        )}
      </div>

      <Card className="space-y-4">
        <CardHeader title="Identificação" description="Como essa implantação aparece na lista." />
        <Input
          label="Nome"
          placeholder="Ex.: Atendimento PIX"
          value={form.name}
          onChange={(e) => setForm({ ...form, name: e.target.value })}
          maxLength={120}
        />
        <Textarea
          label="Descrição"
          placeholder="Pra que serve esse roteamento e em que situação é usado…"
          value={form.description}
          onChange={(e) => setForm({ ...form, description: e.target.value })}
          rows={2}
        />
      </Card>

      <Card className="space-y-4">
        <CardHeader
          title="Router"
          description="Agente Router publicado que classifica a entrada do usuário em uma intent."
        />
        {loadingAgents ? (
          <Spinner className="h-5 w-5 text-fg-muted" />
        ) : routers.length === 0 ? (
          <div className="rounded-lg border border-warning/40 bg-warning/10 px-3 py-2 text-xs text-warning">
            Nenhum Router publicado no projeto. Crie um agente do tipo Router em /agentes/novo, submeta pra aprovação e volte aqui.
          </div>
        ) : (
          <Select
            label="Router"
            value={form.routerAgentId}
            onChange={(e) => setForm({ ...form, routerAgentId: e.target.value })}
            placeholder="Selecionar…"
            options={routers.map((r) => ({ value: r.id, label: `${r.name} (${r.id})` }))}
          />
        )}
      </Card>

      <Card className="space-y-4">
        <CardHeader
          title="Mapeamento intent → agente"
          description="Para cada intent do Router selecionado, escolha o agente que cuida do caso. Saída desse agente vira a resposta final da implantação."
        />
        {!form.routerAgentId ? (
          <p className="text-xs italic text-fg-dim">
            Selecione um Router primeiro pra carregar as intents.
          </p>
        ) : intentsLoading ? (
          <Spinner className="h-5 w-5 text-fg-muted" />
        ) : intentsError ? (
          <ErrorMessage message={intentsError} />
        ) : form.branches.length === 0 ? (
          <p className="rounded-lg border border-warning/40 bg-warning/10 px-3 py-2 text-xs text-warning">
            O Router selecionado não tem intents marcadas. Edite o Router em /agentes e marque ao menos 2 intents do pool.
          </p>
        ) : (
          <div className="space-y-2">
            {form.branches.map((b) => (
              <div
                key={b.intentId}
                className="grid grid-cols-1 gap-2 rounded-lg border border-border bg-bg-soft px-3 py-2 sm:grid-cols-[1fr_2fr] sm:items-center"
              >
                <div className="min-w-0">
                  <p className="text-xs font-semibold text-fg">{b.intentDisplayName || b.intentName}</p>
                  <code className="block truncate font-mono text-[10px] text-fg-dim">{b.intentName}</code>
                </div>
                <Select
                  value={b.agentId}
                  onChange={(e) => setBranchAgent(b.intentId, e.target.value)}
                  placeholder="Selecionar agente…"
                  options={branchAgents.map((a) => ({
                    value: a.id,
                    label: a.type ? `${a.name} (${a.type})` : a.name,
                  }))}
                />
              </div>
            ))}
          </div>
        )}
      </Card>

      <Card className="space-y-4">
        <CardHeader
          title="Fallback (default)"
          description="Agente acionado quando a intent emitida pelo Router não bate com nenhuma das mapeadas (intent fora do enum, confidence baixo). Obrigatório — sem default, o workflow trava silenciosamente em intents inesperadas."
        />
        <Select
          label="Agente de fallback"
          value={form.fallbackAgentId}
          onChange={(e) => setForm({ ...form, fallbackAgentId: e.target.value })}
          placeholder="Selecionar agente…"
          options={branchAgents.map((a) => ({
            value: a.id,
            label: a.type ? `${a.name} (${a.type})` : a.name,
          }))}
        />
      </Card>

      <Card className="space-y-3 border border-accent/30 bg-accent-subtle/30">
        <CardHeader title="Como o roteamento funciona" />
        <ul className="space-y-1.5 text-xs leading-relaxed text-fg-muted">
          <li>• Entrada do usuário chega no <strong>Router</strong>.</li>
          <li>• Router emite JSON estruturado com <code>intent</code> ∈ enum declarado.</li>
          <li>• Switch edge compara <code>$.intent</code> com o nome técnico das intents e encaminha pra branch correspondente.</li>
          <li>• Quando nenhuma intent bate, o <strong>fallback</strong> é acionado.</li>
        </ul>
      </Card>

      {isEdit && loadedWorkflow && (
        <HowToConsumeWorkflow
          workflowId={loadedWorkflow.id}
          publicBaseUrl={publicBaseUrl}
          description="Implantação assíncrona: POST → 202 + executionId; GET de execução retorna a resposta da branch escolhida pelo Router."
        />
      )}

      {saveError && <ErrorMessage message={saveError} />}

      <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border pt-4">
        <p className="text-[11px] text-fg-dim">
          {isEdit
            ? 'Alterações salvas criam uma nova revisão do workflow automaticamente.'
            : 'Após implantar, a rota fica disponível pra consumo via API e pra teste no sandbox.'}
        </p>
        <div className="flex items-center gap-2">
          {isEdit && (
            <Button
              variant="secondary"
              onClick={() => navigate(`/implantacoes/${form.id}/sandbox`)}
              disabled={!!validation.error}
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
