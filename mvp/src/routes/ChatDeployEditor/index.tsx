import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { getAgent, listAgents, type Agent } from '../../api/agents'
import {
  getRouterIntent,
  type RouterIntent,
} from '../../api/routerIntents'
import {
  CHAT_DEPLOYMENT_KIND,
  chatWorkflowId,
  type ChatValidationWarning,
  formatRevisionLabel,
  createWorkflow,
  getWorkflow,
  updateWorkflow,
  type CreateWorkflowBody,
  type Workflow,
} from '../../api/workflows'
import { ApiError, friendlyError } from '../../api/client'
import { WorkflowVersionsModal } from '../../components/WorkflowVersionsModal'
import { useIdentity } from '../../stores/identity'
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
    id: chatWorkflowId(),
    name: '',
    description: '',
    routerAgentId: '',
    branches: [],
    fallbackAgentId: '',
  }
}

// Hidrata o FormState a partir de um workflow Chat existente. Convenção do save:
// agents[role='Router'] entry node; agents[role='Fallback'] default branch;
// edge Switch carrega 1 case por intent + default case → fallback agent.
function workflowToForm(workflow: Workflow): FormState {
  const agents = workflow.agents ?? []
  const router = agents.find((a) => a.role === 'Router')
  const fallback = agents.find((a) => a.role === 'Fallback')
  const edges = (workflow as { edges?: unknown[] }).edges ?? []
  // Backend serializa em camelCase (edgeType, cases, predicate, value, targets,
  // isDefault); mas o payload de save usa PascalCase. Lemos ambos com fallback
  // pra resistir a serializações em qualquer convenção sem quebrar o parse.
  type EdgeCase = {
    predicate?: { value?: unknown } | null
    Predicate?: { Value?: unknown } | null
    targets?: string[]
    Targets?: string[]
    isDefault?: boolean
    IsDefault?: boolean
  }
  type EdgeShape = {
    edgeType?: string
    EdgeType?: string
    cases?: EdgeCase[]
    Cases?: EdgeCase[]
  }
  const switchEdge = edges.find((e): e is EdgeShape => {
    if (!e || typeof e !== 'object') return false
    const shape = e as EdgeShape
    return (shape.edgeType ?? shape.EdgeType) === 'Switch'
  })
  const branches: BranchEntry[] = []
  const cases = switchEdge?.cases ?? switchEdge?.Cases ?? []
  for (const c of cases) {
    const isDefault = c.isDefault ?? c.IsDefault ?? false
    if (isDefault) continue
    const value = c.predicate?.value ?? c.Predicate?.Value
    const intentName = typeof value === 'string' ? value : ''
    const targets = c.targets ?? c.Targets ?? []
    const targetAgentId = targets[0] ?? ''
    if (intentName && targetAgentId) {
      branches.push({ intentId: intentName, intentName, agentId: targetAgentId })
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
  // Switch edge: predicates comparam $.intent (Router formal sempre emite
  // {intent, confidence, reason}) com o nome técnico de cada intent.
  // Default case manda pro fallback. Mesma estrutura do Routing deploy.
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
      maxRounds: prev?.configuration?.maxRounds ?? 10,
      timeoutSeconds: prev?.configuration?.timeoutSeconds ?? 300,
      checkpointMode: prev?.configuration?.checkpointMode ?? 'InMemory',
      // InputMode HARD 'Chat' — Chat deploy SEMPRE roda em workflow com chat
      // session. Backend ConversationFacade exige isso pra aceitar sessões.
      inputMode: 'Chat',
      enableHumanInTheLoop: prev?.configuration?.enableHumanInTheLoop ?? false,
      exposeAsAgent: prev?.configuration?.exposeAsAgent ?? false,
      maxHistoryMessages: prev?.configuration?.maxHistoryMessages ?? 20,
      maxAgentInvocations: prev?.configuration?.maxAgentInvocations ?? 20,
    },
    metadata: { ...prevMetadata, deploymentKind: CHAT_DEPLOYMENT_KIND },
    visibility: 'project',
  }
}

interface Validation {
  error: string | null
}

function validateForm(form: FormState): Validation {
  if (!form.name.trim()) return { error: 'Informe um nome para a implantação.' }
  if (!form.routerAgentId) return { error: 'Selecione o Router conversacional.' }
  if (form.branches.length === 0) {
    return { error: 'O Router selecionado não tem intents — cadastre intents no Router antes de implantar.' }
  }
  const unmapped = form.branches.filter((b) => !b.agentId)
  if (unmapped.length > 0) {
    return {
      error: `Falta atribuir agente Conversational pra ${unmapped.length} intent${unmapped.length === 1 ? '' : 's'}: ${unmapped
        .map((b) => b.intentDisplayName || b.intentName)
        .join(', ')}.`,
    }
  }
  if (!form.fallbackAgentId) {
    return {
      error:
        'Agente Conversational de fallback é obrigatório — Router pode emitir intent inesperada e o chat precisa de um caminho default.',
    }
  }
  return { error: null }
}

export function ChatDeployEditor() {
  const { id } = useParams<{ id?: string }>()
  const navigate = useNavigate()
  const identity = useIdentity()
  const isEdit = !!id

  const [form, setForm] = useState<FormState>(emptyForm)
  const [loadedWorkflow, setLoadedWorkflow] = useState<Workflow | null>(null)
  const [versionsOpen, setVersionsOpen] = useState(false)
  const [agents, setAgents] = useState<Agent[]>([])
  const [loadingAgents, setLoadingAgents] = useState(true)
  const [loading, setLoading] = useState(isEdit)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [validationWarnings, setValidationWarnings] = useState<ChatValidationWarning[]>([])
  const [intentsLoading, setIntentsLoading] = useState(false)
  const [intentsError, setIntentsError] = useState<string | null>(null)

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
    return () => {
      cancelled = true
    }
  }, [])

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
            setLoadError('Implantação de chat não encontrada.')
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

  // Ao trocar Router, faz GET /agents/{id} pra obter routerIntentIds atualizado
  // (listAgents não popula esse campo). Depois resolve cada intent via
  // getRouterIntent em batch. Preserva mapeamentos por intentName.
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
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [form.routerAgentId])

  // Routers permitidos no Chat: hard-filtrados por `metadata['x-router-for-chat']='true'`.
  // User precisa marcar a flag no wizard do Router antes de aparecer aqui.
  const chatRouters = useMemo(
    () =>
      agents.filter(
        (a) =>
          a.type === 'Router'
          && a.metadata?.['x-router-for-chat']?.toLowerCase() === 'true',
      ),
    [agents],
  )

  // Branches/fallback do Chat: SOMENTE Conversational. Regra do produto:
  // "Router só pode referenciar agentes Conversational".
  const conversationalAgents = useMemo(
    () => agents.filter((a) => a.type === 'Conversational'),
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
      // Warnings de Chat Sandbox validation vêm no response. Frontend só
      // renderiza — authority do gate é backend (warning, não bloqueio).
      setValidationWarnings(wf.validationWarnings ?? [])
      if (!isEdit) {
        navigate(`/implantacoes/chat/${wf.id}`, { replace: true })
      }
    } catch (err) {
      setSaveError(friendlyError(err, 'Não foi possível salvar a implantação.'))
    } finally {
      setSaving(false)
    }
  }

  // Defesa-em-profundidade no frontend: identidade local não permite,
  // mensagem clara antes de abrir o editor.
  if (identity && !identity.chatDeploymentAllowed) {
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
        </div>
        <Card>
          <CardHeader
            title="Implantação Chat não autorizada"
            description="Implantações tipo Chat são permitidas apenas no projeto Sales Trader AI. Troque de projeto pelo menu de identidade pra acessar essa rota."
          />
        </Card>
      </div>
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
    ? form.name.trim() || 'Chat sem nome'
    : 'Nova implantação de Chat'

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
            Router conversacional classifica a mensagem; cada intent mapeia pra um agente Conversational. Workflow em InputMode=Chat com STATE_DELTA via SSE.
          </p>
        </div>
        {isEdit && loadedWorkflow && (
          <Badge tone="accent">Chat · {form.branches.length} branches</Badge>
        )}
      </div>

      <Card className="space-y-4">
        <CardHeader title="Identificação" description="Como essa implantação aparece na lista." />
        <Input
          label="Nome"
          placeholder="Ex.: Atendimento PIX Chat"
          value={form.name}
          onChange={(e) => setForm({ ...form, name: e.target.value })}
          maxLength={120}
        />
        <Textarea
          label="Descrição"
          placeholder="Pra que serve esse chat e em que canal é usado…"
          value={form.description}
          onChange={(e) => setForm({ ...form, description: e.target.value })}
          rows={2}
        />
      </Card>

      <Card className="space-y-4">
        <CardHeader
          title="Router conversacional"
          description="Agente Router com toggle 'Uso no chat' ligado (metadata['x-router-for-chat']='true'). Só Routers chat publicados aparecem aqui."
        />
        {loadingAgents ? (
          <Spinner className="h-5 w-5 text-fg-muted" />
        ) : chatRouters.length === 0 ? (
          <div className="rounded-lg border border-warning/40 bg-warning/10 px-3 py-2 text-xs text-warning">
            Nenhum Router conversacional publicado no projeto. Crie um agente do tipo Router, marque o toggle "Uso no chat" no wizard, submeta pra aprovação e volte aqui.
          </div>
        ) : (
          <Select
            label="Router"
            value={form.routerAgentId}
            onChange={(e) => setForm({ ...form, routerAgentId: e.target.value })}
            placeholder="Selecionar…"
            options={chatRouters.map((r) => ({ value: r.id, label: `${r.name} (${r.id})` }))}
          />
        )}
      </Card>

      <Card className="space-y-4">
        <CardHeader
          title="Mapeamento intent → agente Conversational"
          description="Pra cada intent do Router selecionado, escolha o agente Conversational que cuida do diálogo subsequente. Saída desse agente vira a próxima mensagem da conversa."
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
        ) : conversationalAgents.length === 0 ? (
          <p className="rounded-lg border border-warning/40 bg-warning/10 px-3 py-2 text-xs text-warning">
            Nenhum agente Conversational publicado no projeto. Crie ao menos um agente Conversational pra mapear nas branches.
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
                  placeholder="Selecionar Conversational…"
                  options={conversationalAgents.map((a) => ({
                    value: a.id,
                    label: a.name,
                  }))}
                />
              </div>
            ))}
          </div>
        )}
      </Card>

      <Card className="space-y-4">
        <CardHeader
          title="Fallback Conversational (default)"
          description="Agente Conversational acionado quando a intent emitida pelo Router não bate com nenhuma das mapeadas. Obrigatório — sem default, o chat trava em intents inesperadas."
        />
        <Select
          label="Agente de fallback"
          value={form.fallbackAgentId}
          onChange={(e) => setForm({ ...form, fallbackAgentId: e.target.value })}
          placeholder="Selecionar Conversational…"
          options={conversationalAgents.map((a) => ({
            value: a.id,
            label: a.name,
          }))}
        />
      </Card>

      <Card className="space-y-3 border border-teal-500/30 bg-teal-500/5">
        <CardHeader title="Como o chat funciona" />
        <ul className="space-y-1.5 text-xs leading-relaxed text-fg-muted">
          <li>• Mensagem do usuário chega no <strong>Router conversacional</strong>.</li>
          <li>• Router emite JSON com <code>intent</code> ∈ enum declarado + <code>confidence</code> + <code>reason</code>.</li>
          <li>• Switch edge compara <code>$.intent</code> e ativa o <strong>Conversational</strong> correspondente.</li>
          <li>• Quando nenhuma intent bate, o <strong>fallback</strong> é ativado.</li>
          <li>• Workflow em <code>InputMode=Chat</code> mantém histórico/conversationId entre turns.</li>
          <li>• Middleware <code>StructuredOutputState</code> dispara <code>STATE_DELTA</code> via SSE pro frontend chat renderizar componentes em tempo real.</li>
        </ul>
      </Card>

      {saveError && <ErrorMessage message={saveError} />}

      {validationWarnings.length > 0 && (
        <ChatValidationWarningsPanel warnings={validationWarnings} />
      )}

      <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border pt-4">
        <p className="text-[11px] text-fg-dim">
          {isEdit
            ? 'Alterações salvas criam uma nova revisão do workflow automaticamente.'
            : 'Após implantar, a rota fica disponível pra teste no sandbox de chat.'}
        </p>
        <div className="flex items-center gap-2">
          {isEdit && loadedWorkflow && (
            <Button variant="ghost" onClick={() => setVersionsOpen(true)}>
              Versões
            </Button>
          )}
          {isEdit && (
            <Button
              variant="secondary"
              onClick={() => navigate(`/implantacoes/chat/${form.id}/sandbox`)}
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

      <WorkflowVersionsModal
        open={versionsOpen}
        workflow={loadedWorkflow}
        onClose={() => setVersionsOpen(false)}
        onRolledBack={(wf) => {
          setLoadedWorkflow(wf)
          setForm(workflowToForm(wf))
        }}
      />
    </div>
  )
}

/**
 * Painel inline de warnings de Chat Sandbox validation, exibido pós-save. UI
 * só renderiza o que o backend retornou em <code>validationWarnings</code>;
 * autoridade do gate é backend. Save NÃO foi bloqueado (warning, não erro).
 */
function ChatValidationWarningsPanel({ warnings }: { warnings: ChatValidationWarning[] }) {
  return (
    <Card padded className="border-warning/40 bg-warning/5">
      <div className="space-y-3">
        <div>
          <h3 className="text-sm font-semibold text-warning">
            ⚠ Branch agents sem validation em Chat Sandbox
          </h3>
          <p className="mt-1 text-[11px] text-fg-muted">
            O save foi bem-sucedido. Estes agentes Conversational podem rodar em produção, mas
            recomendamos testá-los em Chat Sandbox antes pra confirmar comportamento.
          </p>
        </div>
        <ul className="space-y-1.5 text-xs">
          {warnings.map((w) => (
            <li key={w.agentId} className="flex flex-wrap items-center gap-2">
              <span className="font-mono font-semibold text-fg">{w.agentName}</span>
              <span className="text-fg-muted">
                pin: {formatRevisionLabel(w.pinnedRevision)}
              </span>
              {w.reason === 'no_chat_sandbox_validation' && (
                <span className="rounded-md bg-warning/10 px-1.5 py-0.5 text-[10px] uppercase tracking-wider text-warning">
                  nunca validado
                </span>
              )}
              {w.reason === 'validation_stale' && (
                <span className="rounded-md bg-warning/10 px-1.5 py-0.5 text-[10px] uppercase tracking-wider text-warning">
                  validado em {formatRevisionLabel(w.validatedRevision)}
                </span>
              )}
            </li>
          ))}
        </ul>
      </div>
    </Card>
  )
}
