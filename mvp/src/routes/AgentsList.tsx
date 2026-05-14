import { useEffect, useMemo, useState } from 'react'
import { useLocation, useNavigate, useSearchParams } from 'react-router'
import { listAgentDrafts, type AgentDraft, type AgentDraftStatus } from '../api/agentDrafts'
import {
  listAgents,
  createEditDraft,
  getApprovalHistory,
  updateAgentEnabled,
  type Agent,
  type ApprovalAction,
  type ApprovalHistoryEntry,
} from '../api/agents'
import { ApiError, friendlyError } from '../api/client'
import { createChatSandboxSession } from '../api/chatSandbox'
import { getIdentity } from '../stores/identity'
import {
  deployedAgentId,
  isAgentDeployment,
  listWorkflows,
  type Workflow,
} from '../api/workflows'
import { NewAgentModeModal, type NewAgentSelection } from '../components/NewAgentModeModal'
import {
  AgentIcon,
  Badge,
  BoltIcon,
  Button,
  Card,
  CheckIcon,
  CloseIcon,
  ErrorMessage,
  Input,
  Modal,
  PlusIcon,
  SearchIcon,
  Spinner,
  Textarea,
  cn,
} from '../ui'

interface FlashMessage {
  tone: 'success' | 'accent'
  title: string
  body?: string
}

type Tab = 'drafts' | 'published'

type StatusTone = 'neutral' | 'accent' | 'warning' | 'success' | 'danger'

const STATUS_TONE: Record<AgentDraftStatus, StatusTone> = {
  Draft: 'neutral',
  PendingApproval: 'accent',
  Rejected: 'warning',
}

const STATUS_LABEL: Record<AgentDraftStatus, string> = {
  Draft: 'Rascunho',
  PendingApproval: 'Aguardando aprovação',
  Rejected: 'Rejeitado',
}

const STATUS_ACCENT: Record<AgentDraftStatus, string> = {
  Draft: 'before:bg-fg-dim/30',
  PendingApproval: 'before:bg-accent',
  Rejected: 'before:bg-warning',
}

export function AgentsList() {
  const navigate = useNavigate()
  const [searchParams, setSearchParams] = useSearchParams()
  const initialTab: Tab = searchParams.get('tab') === 'published' ? 'published' : 'drafts'
  const [activeTab, setActiveTab] = useState<Tab>(initialTab)
  const [search, setSearch] = useState('')
  const [modeModalOpen, setModeModalOpen] = useState(false)
  const [onlyMine, setOnlyMine] = useState(false)

  // Flash de confirmação pós-submit. Vem via location.state quando o
  // AgentEditor navega de volta. Persiste até o user fechar — feedback
  // explícito de "submeti, e agora?". Identidade do user atual é usada pra
  // dar o filtro 'Meus rascunhos' já existir mesmo após F5.
  const location = useLocation()
  const initialFlash = (location.state as { flash?: FlashMessage } | null)?.flash ?? null
  const [flash, setFlash] = useState<FlashMessage | null>(initialFlash)
  const identity = useMemo(() => getIdentity(), [])
  const myAccount = identity?.account ?? null

  // Drafts state
  const [drafts, setDrafts] = useState<AgentDraft[]>([])
  const [draftsLoading, setDraftsLoading] = useState(true)
  const [draftsError, setDraftsError] = useState<string | null>(null)

  // Published state
  const [agents, setAgents] = useState<Agent[]>([])
  const [agentsLoading, setAgentsLoading] = useState(true)
  const [agentsError, setAgentsError] = useState<string | null>(null)

  // Edit-draft fork state
  const [forkingId, setForkingId] = useState<string | null>(null)
  const [forkError, setForkError] = useState<string | null>(null)

  // Spinner por card enquanto o POST /chat-sandbox-sessions roda.
  const [testingInChatId, setTestingInChatId] = useState<string | null>(null)
  const [testingInChatError, setTestingInChatError] = useState<string | null>(null)

  // Approval history modal state
  const [historyAgent, setHistoryAgent] = useState<Agent | null>(null)
  const [history, setHistory] = useState<ApprovalHistoryEntry[]>([])
  const [historyLoading, setHistoryLoading] = useState(false)
  const [historyError, setHistoryError] = useState<string | null>(null)

  // Enable/disable confirmation modal state
  const [toggleAgent, setToggleAgent] = useState<Agent | null>(null)
  const [toggleReason, setToggleReason] = useState('')
  const [toggling, setToggling] = useState(false)
  const [toggleError, setToggleError] = useState<string | null>(null)

  // Carrega ambos em paralelo no mount — usuário pode trocar de tab sem espera.
  useEffect(() => {
    let cancelled = false
    listAgentDrafts()
      .then((list) => {
        if (!cancelled) setDrafts(list)
      })
      .catch((err: unknown) => {
        if (!cancelled) setDraftsError(friendlyError(err, 'Não foi possível carregar os rascunhos.'))
      })
      .finally(() => {
        if (!cancelled) setDraftsLoading(false)
      })
    listAgents('project')
      .then((list) => {
        if (!cancelled) setAgents(list)
      })
      .catch((err: unknown) => {
        if (!cancelled) setAgentsError(friendlyError(err, 'Não foi possível carregar os agentes publicados.'))
      })
      .finally(() => {
        if (!cancelled) setAgentsLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [])

  // Filtro server-side via ?scope=project nos endpoints — drafts já vêm strict
  // pelo HasQueryFilter, então `drafts` e `agents` aqui já são "do meu projeto".
  const ownDrafts = drafts
  const ownAgents = agents

  const filteredDrafts = useMemo(() => {
    const q = search.trim().toLowerCase()
    return ownDrafts.filter((d) => {
      if (onlyMine && myAccount && d.createdBy !== myAccount) return false
      if (!q) return true
      const name = (d.name || d.payload?.name || '').toLowerCase()
      const desc = (d.payload?.description ?? '').toLowerCase()
      return name.includes(q) || desc.includes(q)
    })
  }, [ownDrafts, search, onlyMine, myAccount])

  const myDraftsCount = useMemo(
    () => (myAccount ? ownDrafts.filter((d) => d.createdBy === myAccount).length : 0),
    [ownDrafts, myAccount],
  )

  const filteredAgents = useMemo(() => {
    const q = search.trim().toLowerCase()
    if (!q) return ownAgents
    return ownAgents.filter((a) => {
      const name = (a.name ?? '').toLowerCase()
      const desc = (a.description ?? '').toLowerCase()
      return name.includes(q) || desc.includes(q)
    })
  }, [ownAgents, search])

  const handleSelectNewAgent = (selection: NewAgentSelection) => {
    setModeModalOpen(false)
    const qs = new URLSearchParams({ mode: selection.mode, type: selection.type })
    if (selection.template) qs.set('template', selection.template)
    navigate(`/agentes/novo?${qs.toString()}`)
  }

  const handleEdit = async (agentId: string) => {
    setForkingId(agentId)
    setForkError(null)
    try {
      // GET prévio: se já existe edit-draft pra esse agentId, navega direto
      // pra ele em vez de tentar criar e cair no 409. Evita o ruído vermelho
      // no console do DevTools sem mudar a UX.
      try {
        const all = await listAgentDrafts()
        const existing = all.find(
          (d) => d.isEditDraft && d.baseAgentId === agentId,
        )
        if (existing) {
          navigate(`/agentes/${existing.id}`)
          return
        }
      } catch {
        // Falha do GET é silenciosa — caímos no fluxo de POST + tratamento
        // do 409 (caminho legacy) pra não bloquear o user.
      }

      const draft = await createEditDraft(agentId)
      navigate(`/agentes/${draft.id}`)
    } catch (err) {
      // Race condition: outro tab criou o draft entre o GET e o POST. Tenta
      // recuperar como antes.
      if (err instanceof ApiError && err.status === 409) {
        try {
          const all = await listAgentDrafts()
          const existing = all.find(
            (d) => d.isEditDraft && d.baseAgentId === agentId,
          )
          if (existing) {
            navigate(`/agentes/${existing.id}`)
            return
          }
        } catch {
          // Fallback pra mensagem original quando o lookup falha.
        }
        setForkError(
          'Já existe um rascunho de edição em aberto para este agente. Procure-o na aba Rascunhos.',
        )
        return
      }
      setForkError(friendlyError(err, 'Não foi possível abrir o agente para edição.'))
    } finally {
      setForkingId(null)
    }
  }

  const handleOpenHistory = async (agent: Agent) => {
    setHistoryAgent(agent)
    setHistory([])
    setHistoryError(null)
    setHistoryLoading(true)
    try {
      const list = await getApprovalHistory(agent.id)
      setHistory(list)
    } catch (err) {
      setHistoryError(friendlyError(err, 'Não foi possível carregar o histórico de aprovações.'))
    } finally {
      setHistoryLoading(false)
    }
  }

  const handleCloseHistory = () => {
    setHistoryAgent(null)
    setHistory([])
    setHistoryError(null)
  }

  const handleOpenToggle = (agent: Agent) => {
    setToggleAgent(agent)
    setToggleReason('')
    setToggleError(null)
  }

  const handleCloseToggle = () => {
    if (toggling) return
    setToggleAgent(null)
    setToggleReason('')
    setToggleError(null)
  }

  const handleConfirmToggle = async () => {
    if (!toggleAgent) return
    const target = toggleAgent
    const nextEnabled = !(target.enabled !== false)
    setToggling(true)
    setToggleError(null)
    try {
      const updated = await updateAgentEnabled(target.id, {
        enabled: nextEnabled,
        reason: toggleReason.trim() || null,
      })
      setAgents((prev) => prev.map((a) => (a.id === target.id ? updated : a)))
      setToggleAgent(null)
      setToggleReason('')
    } catch (err) {
      setToggleError(friendlyError(err, 'Não foi possível alterar o estado do agente.'))
    } finally {
      setToggling(false)
    }
  }

  const handleTestInChat = async (agentId: string) => {
    setTestingInChatId(agentId)
    setTestingInChatError(null)
    try {
      const session = await createChatSandboxSession(agentId)
      // Workflow efêmero é Chat real — reaproveita o sandbox AG-UI já existente.
      // A continuidade do thread veio com a conversation criada pelo backend, mas
      // o ChatDeploymentSandbox cria thread própria por mount; pra V1 isso é OK
      // (cada visita = novo thread). PR futuro pode plumb conversationId via state.
      navigate(`/implantacoes/chat/${session.workflowId}/sandbox`)
    } catch (err) {
      setTestingInChatError(friendlyError(err, 'Não foi possível abrir o Chat Sandbox.'))
    } finally {
      setTestingInChatId(null)
    }
  }

  const showCreateCard = activeTab === 'drafts' && !draftsLoading && !draftsError && search.trim().length === 0

  return (
    <div className="mx-auto max-w-6xl">
      <div className="mb-8 flex items-end justify-between">
        <div>
          <h1 className="text-[28px] font-semibold tracking-tight">Agentes</h1>
          <p className="mt-2 text-sm text-fg-muted">
            Gerencie seus rascunhos e visualize os agentes publicados após aprovação.
          </p>
        </div>
        {activeTab === 'drafts' && (
          <Button leftIcon={<PlusIcon className="h-4 w-4" />} onClick={() => setModeModalOpen(true)}>
            Novo agente
          </Button>
        )}
      </div>

      <div className="mb-6 flex items-center gap-1 border-b border-border">
        <TabButton
          active={activeTab === 'drafts'}
          label="Rascunhos"
          count={draftsLoading ? null : ownDrafts.length}
          onClick={() => {
            setActiveTab('drafts')
            setSearchParams({}, { replace: true })
          }}
        />
        <TabButton
          active={activeTab === 'published'}
          label="Publicados"
          count={agentsLoading ? null : ownAgents.length}
          onClick={() => {
            setActiveTab('published')
            setSearchParams({ tab: 'published' }, { replace: true })
          }}
        />
      </div>

      <div className="mb-6 flex flex-wrap items-center gap-3">
        <div className="max-w-md flex-1">
          <Input
            placeholder={
              activeTab === 'drafts'
                ? 'Buscar rascunho por nome ou descrição…'
                : 'Buscar agente publicado por nome ou descrição…'
            }
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            leftAddon={<SearchIcon className="h-4 w-4" />}
          />
        </div>
        {activeTab === 'drafts' && myAccount && (
          <button
            type="button"
            onClick={() => setOnlyMine((v) => !v)}
            className={cn(
              'inline-flex h-9 items-center gap-2 rounded-lg border px-3 text-xs font-medium transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/30',
              onlyMine
                ? 'border-accent bg-accent-subtle text-accent'
                : 'border-border bg-surface text-fg-muted hover:text-fg',
            )}
            aria-pressed={onlyMine}
          >
            {onlyMine && <CheckIcon className="h-3.5 w-3.5" />}
            Meus rascunhos
            <span
              className={cn(
                'rounded-full px-1.5 py-0.5 text-[10px] font-semibold',
                onlyMine ? 'bg-accent text-accent-contrast' : 'bg-bg-soft text-fg-muted',
              )}
            >
              {myDraftsCount}
            </span>
          </button>
        )}
      </div>

      {flash && (
        <div
          className={cn(
            'mb-4 flex items-start justify-between gap-3 rounded-xl border px-4 py-3',
            flash.tone === 'success'
              ? 'border-success/40 bg-success/10 text-success'
              : 'border-accent/40 bg-accent-subtle text-accent',
          )}
        >
          <div className="min-w-0">
            <p className="text-sm font-semibold">{flash.title}</p>
            {flash.body && <p className="mt-0.5 text-xs opacity-90">{flash.body}</p>}
          </div>
          <button
            type="button"
            onClick={() => setFlash(null)}
            className="shrink-0 rounded-md p-1 transition hover:bg-fg/10"
            aria-label="Fechar aviso"
          >
            <CloseIcon className="h-4 w-4" />
          </button>
        </div>
      )}

      {forkError && <ErrorMessage message={forkError} className="mb-4" />}
      {testingInChatError && <ErrorMessage message={testingInChatError} className="mb-4" />}

      {activeTab === 'drafts' && (
        <DraftsTab
          loading={draftsLoading}
          error={draftsError}
          items={filteredDrafts}
          totalItems={ownDrafts.length}
          showCreateCard={showCreateCard}
          searchActive={search.trim().length > 0}
          onCreateClick={() => setModeModalOpen(true)}
          onCardClick={(id) => navigate(`/agentes/${id}`)}
        />
      )}

      {activeTab === 'published' && (
        <PublishedTab
          loading={agentsLoading}
          error={agentsError}
          items={filteredAgents}
          searchActive={search.trim().length > 0}
          forkingId={forkingId}
          testingInChatId={testingInChatId}
          onEdit={handleEdit}
          onDeploy={(id) => navigate(`/agentes/${id}/implantar`)}
          onVersions={(id) => navigate(`/agentes/${id}/versoes`)}
          onHistory={handleOpenHistory}
          onToggleEnabled={handleOpenToggle}
          onTestInChat={handleTestInChat}
        />
      )}

      <NewAgentModeModal
        open={modeModalOpen}
        onClose={() => setModeModalOpen(false)}
        onSelect={handleSelectNewAgent}
      />

      <ApprovalHistoryModal
        agent={historyAgent}
        entries={history}
        loading={historyLoading}
        error={historyError}
        onClose={handleCloseHistory}
      />

      <ToggleEnabledModal
        agent={toggleAgent}
        reason={toggleReason}
        onReasonChange={setToggleReason}
        loading={toggling}
        error={toggleError}
        onClose={handleCloseToggle}
        onConfirm={handleConfirmToggle}
      />
    </div>
  )
}

interface TabButtonProps {
  active: boolean
  label: string
  count: number | null
  onClick: () => void
}

function TabButton({ active, label, count, onClick }: TabButtonProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'relative flex items-center gap-2 px-4 py-2.5 text-sm font-medium transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/30',
        active ? 'text-fg' : 'text-fg-muted hover:text-fg',
      )}
    >
      {label}
      {count !== null && (
        <span
          className={cn(
            'rounded-full px-1.5 py-0.5 text-[10px] font-semibold',
            active ? 'bg-accent text-accent-contrast' : 'bg-bg-soft text-fg-muted',
          )}
        >
          {count}
        </span>
      )}
      {active && <span className="absolute inset-x-0 bottom-0 h-0.5 bg-accent" aria-hidden="true" />}
    </button>
  )
}

interface DraftsTabProps {
  loading: boolean
  error: string | null
  items: AgentDraft[]
  totalItems: number
  showCreateCard: boolean
  searchActive: boolean
  onCreateClick: () => void
  onCardClick: (id: string) => void
}

function DraftsTab({
  loading,
  error,
  items,
  totalItems,
  showCreateCard,
  searchActive,
  onCreateClick,
  onCardClick,
}: DraftsTabProps) {
  if (loading) {
    return (
      <Card className="flex items-center justify-center py-12">
        <Spinner className="h-6 w-6 text-fg-muted" />
      </Card>
    )
  }
  if (error) return <ErrorMessage message={error} />
  if (items.length === 0 && searchActive) {
    return (
      <Card padded className="text-center">
        <p className="text-sm text-fg-muted">Nada bate com a busca. Tente ajustar o termo.</p>
      </Card>
    )
  }
  if (totalItems === 0 && !searchActive) {
    return (
      <div className="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3">
        <CreateAgentCard onClick={onCreateClick} />
      </div>
    )
  }

  return (
    <div className="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3">
      {showCreateCard && <CreateAgentCard onClick={onCreateClick} />}
      {items.map((d) => (
        <DraftCard key={d.id} draft={d} onClick={() => onCardClick(d.id)} />
      ))}
    </div>
  )
}

interface PublishedTabProps {
  loading: boolean
  error: string | null
  items: Agent[]
  searchActive: boolean
  forkingId: string | null
  testingInChatId: string | null
  onEdit: (id: string) => void
  onDeploy: (id: string) => void
  onVersions: (id: string) => void
  onHistory: (agent: Agent) => void
  onToggleEnabled: (agent: Agent) => void
  onTestInChat: (id: string) => void
}

function PublishedTab({
  loading,
  error,
  items,
  searchActive,
  forkingId,
  testingInChatId,
  onEdit,
  onDeploy,
  onVersions,
  onHistory,
  onToggleEnabled,
  onTestInChat,
}: PublishedTabProps) {
  if (loading) {
    return (
      <Card className="flex items-center justify-center py-12">
        <Spinner className="h-6 w-6 text-fg-muted" />
      </Card>
    )
  }
  if (error) return <ErrorMessage message={error} />
  if (items.length === 0 && searchActive) {
    return (
      <Card padded className="text-center">
        <p className="text-sm text-fg-muted">Nada bate com a busca. Tente ajustar o termo.</p>
      </Card>
    )
  }
  if (items.length === 0) {
    return (
      <Card padded className="text-center">
        <p className="text-sm text-fg-muted">
          Nenhum agente publicado ainda neste projeto. Crie um rascunho e submeta para aprovação.
        </p>
      </Card>
    )
  }

  return (
    <div className="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3">
      {items.map((a) => (
        <PublishedAgentCard
          key={a.id}
          agent={a}
          forking={forkingId === a.id}
          testingInChat={testingInChatId === a.id}
          onEdit={() => onEdit(a.id)}
          onDeploy={() => onDeploy(a.id)}
          onVersions={() => onVersions(a.id)}
          onHistory={() => onHistory(a)}
          onToggleEnabled={() => onToggleEnabled(a)}
          onTestInChat={() => onTestInChat(a.id)}
        />
      ))}
    </div>
  )
}

interface CreateAgentCardProps {
  onClick: () => void
}

function CreateAgentCard({ onClick }: CreateAgentCardProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'group flex min-h-[180px] flex-col items-center justify-center gap-3 rounded-xl border-2 border-dashed border-border bg-surface px-5 py-6 text-center transition',
        'hover:border-accent hover:bg-accent-subtle/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/40',
      )}
    >
      <div className="flex h-12 w-12 items-center justify-center rounded-full bg-accent-subtle text-accent transition group-hover:scale-110">
        <PlusIcon className="h-6 w-6" />
      </div>
      <div>
        <p className="text-sm font-semibold text-fg">Criar novo agente</p>
        <p className="mt-1 text-xs text-fg-muted">Escolha entre modo básico e avançado.</p>
      </div>
    </button>
  )
}

interface DraftCardProps {
  draft: AgentDraft
  onClick: () => void
}

function DraftCard({ draft, onClick }: DraftCardProps) {
  const display = draft.name || draft.payload?.name || 'Rascunho sem nome'
  const description = draft.payload?.description ?? ''
  const slaInfo =
    draft.status === 'PendingApproval' && draft.submittedAt ? buildSlaInfo(draft.submittedAt) : null
  // Mantém a barra lateral refletindo status do draft (Draft/Pending/Rejected)
  // — não trocar pelos tons de tipo. Apenas o ícone e a badge sinalizam o tipo.
  const isRouter = draft.payload?.type === 'Router'
  const isWorker = draft.payload?.type === 'Worker'
  const isToolRunner = draft.payload?.type === 'ToolRunner'
  const isConversational = draft.payload?.type === 'Conversational'
  const iconBg = isRouter
    ? 'bg-violet-500/15 text-violet-600 dark:text-violet-400'
    : isWorker
      ? 'bg-sky-500/15 text-sky-600 dark:text-sky-400'
      : isToolRunner
        ? 'bg-amber-500/15 text-amber-600 dark:text-amber-400'
        : isConversational
          ? 'bg-rose-500/15 text-rose-600 dark:text-rose-400'
          : 'bg-accent-subtle text-accent'

  return (
    <Card
      interactive
      padded={false}
      role="button"
      tabIndex={0}
      onClick={onClick}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault()
          onClick()
        }
      }}
      className={cn(
        'group relative flex min-h-[180px] cursor-pointer flex-col gap-3 overflow-hidden p-5',
        'before:absolute before:inset-y-0 before:left-0 before:w-1',
        STATUS_ACCENT[draft.status],
      )}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="flex min-w-0 items-center gap-3">
          <div className={cn('flex h-9 w-9 shrink-0 items-center justify-center rounded-lg', iconBg)}>
            <AgentIcon className="h-5 w-5" />
          </div>
          <h3 className="min-w-0 truncate text-sm font-semibold text-fg">
            {display || <span className="italic text-fg-dim">sem nome</span>}
          </h3>
        </div>
        <Badge tone={STATUS_TONE[draft.status]}>{STATUS_LABEL[draft.status]}</Badge>
      </div>

      <p className="line-clamp-3 text-xs text-fg-muted">
        {description || <span className="italic text-fg-dim">sem descrição</span>}
      </p>

      {slaInfo && (
        <div
          className={cn(
            'flex items-center gap-1.5 rounded-md px-2 py-1 text-[11px]',
            slaInfo.overdue
              ? 'bg-warning/10 text-warning'
              : 'bg-accent-subtle text-accent',
          )}
        >
          <span className="h-1.5 w-1.5 rounded-full bg-current" aria-hidden="true" />
          {slaInfo.label}
        </div>
      )}

      <div className="mt-auto flex items-center justify-between text-[11px] text-fg-dim">
        <div className="flex items-center gap-2">
          {isRouter && (
            <span className="inline-flex items-center rounded-md border border-violet-500/40 bg-violet-500/10 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider text-violet-600 dark:text-violet-400">
              Router
            </span>
          )}
          {isWorker && (
            <span className="inline-flex items-center rounded-md border border-sky-500/40 bg-sky-500/10 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider text-sky-600 dark:text-sky-400">
              Worker
            </span>
          )}
          {isToolRunner && (
            <span className="inline-flex items-center rounded-md border border-amber-500/40 bg-amber-500/10 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider text-amber-600 dark:text-amber-400">
              Tool Runner
            </span>
          )}
          {isConversational && (
            <span className="inline-flex items-center rounded-md border border-rose-500/40 bg-rose-500/10 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider text-rose-600 dark:text-rose-400">
              Conversational
            </span>
          )}
          {draft.isEditDraft && <Badge>edição</Badge>}
          <span>atualizado {formatRelative(draft.updatedAt)}</span>
        </div>
        <span className="opacity-0 transition group-hover:opacity-100">Abrir →</span>
      </div>
    </Card>
  )
}

// SLA pós-submit. Backend não expõe filas/posição; usamos a heurística
// "2 dias úteis a partir do submittedAt" pra setar expectativa do PO.
// Quando passa do prazo, vira tom warning ("aguardando há X dias úteis").
function buildSlaInfo(submittedAtIso: string): { label: string; overdue: boolean } {
  const submitted = new Date(submittedAtIso)
  if (Number.isNaN(submitted.getTime())) return { label: 'Aguardando aprovação', overdue: false }
  const businessDays = countBusinessDays(submitted, new Date())
  if (businessDays <= 0) return { label: 'Submetido agora · SLA 2 dias úteis', overdue: false }
  if (businessDays <= 2)
    return {
      label: `Aguardando há ${businessDays} dia${businessDays === 1 ? '' : 's'} útil${businessDays === 1 ? '' : 'eis'} · SLA 2 dias úteis`,
      overdue: false,
    }
  return {
    label: `Aguardando há ${businessDays} dias úteis · acima do SLA esperado`,
    overdue: true,
  }
}

function countBusinessDays(from: Date, to: Date): number {
  if (to <= from) return 0
  let count = 0
  const cursor = new Date(from)
  cursor.setHours(0, 0, 0, 0)
  const end = new Date(to)
  end.setHours(0, 0, 0, 0)
  while (cursor < end) {
    cursor.setDate(cursor.getDate() + 1)
    const day = cursor.getDay()
    if (day !== 0 && day !== 6) count++
  }
  return count
}

interface PublishedAgentCardProps {
  agent: Agent
  forking: boolean
  testingInChat: boolean
  onEdit: () => void
  onDeploy: () => void
  onVersions: () => void
  onHistory: () => void
  onToggleEnabled: () => void
  onTestInChat: () => void
}

function PublishedAgentCard({
  agent,
  forking,
  testingInChat,
  onEdit,
  onDeploy,
  onVersions,
  onHistory,
  onToggleEnabled,
  onTestInChat,
}: PublishedAgentCardProps) {
  const description = agent.description ?? ''
  const modelLabel = agent.model?.predefinedModelId || agent.model?.deploymentName || ''
  const toolCount = agent.tools?.length ?? 0
  const enabled = agent.enabled !== false
  // Router herda o accent roxo, Worker o azul (sky), Tool Runner o âmbar,
  // Conversational o rosa — espelham os cards de tipo no NewAgentModeModal/
  // TypeStep, mantendo consistência visual entre seleção e listagem. Custom
  // segue verde do tom success (default).
  const isRouter = agent.type === 'Router'
  const isWorker = agent.type === 'Worker'
  const isToolRunner = agent.type === 'ToolRunner'
  const isConversational = agent.type === 'Conversational'
  const accentBar = !enabled
    ? 'before:bg-warning'
    : isRouter
      ? 'before:bg-violet-500'
      : isWorker
        ? 'before:bg-sky-500'
        : isToolRunner
          ? 'before:bg-amber-500'
          : isConversational
            ? 'before:bg-rose-500'
            : 'before:bg-success'
  const iconBg = isRouter
    ? 'bg-violet-500/15 text-violet-600 dark:text-violet-400'
    : isWorker
      ? 'bg-sky-500/15 text-sky-600 dark:text-sky-400'
      : isToolRunner
        ? 'bg-amber-500/15 text-amber-600 dark:text-amber-400'
        : isConversational
          ? 'bg-rose-500/15 text-rose-600 dark:text-rose-400'
          : 'bg-success/10 text-success'

  return (
    <Card
      padded={false}
      className={cn(
        'group relative flex min-h-[180px] flex-col gap-3 overflow-hidden p-5',
        'before:absolute before:inset-y-0 before:left-0 before:w-1',
        accentBar,
      )}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="flex min-w-0 items-center gap-3">
          <div className={cn('flex h-9 w-9 shrink-0 items-center justify-center rounded-lg', iconBg)}>
            <AgentIcon className="h-5 w-5" />
          </div>
          <div className="min-w-0">
            <h3 className="truncate text-sm font-semibold text-fg">
              {agent.name || <span className="italic text-fg-dim">sem nome</span>}
            </h3>
            {modelLabel && (
              <p className="mt-0.5 truncate font-mono text-[10px] uppercase tracking-wider text-fg-dim">
                {modelLabel}
              </p>
            )}
          </div>
        </div>
        <div className="flex items-center gap-2">
          <Badge tone={enabled ? 'success' : 'warning'}>
            {enabled ? 'Publicado' : 'Desabilitado'}
          </Badge>
          <EnabledSwitch enabled={enabled} onToggle={onToggleEnabled} />
        </div>
      </div>

      <p className="line-clamp-3 text-xs text-fg-muted">
        {description || <span className="italic text-fg-dim">sem descrição</span>}
      </p>

      {isConversational && <ChatSandboxValidationBadge agent={agent} />}

      <div className="mt-auto flex flex-col gap-3">
        <div className="flex flex-wrap items-center gap-2 text-[11px] text-fg-dim">
          {isRouter && (
            <span className="inline-flex items-center rounded-md border border-violet-500/40 bg-violet-500/10 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider text-violet-600 dark:text-violet-400">
              Router
            </span>
          )}
          {isWorker && (
            <span className="inline-flex items-center rounded-md border border-sky-500/40 bg-sky-500/10 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider text-sky-600 dark:text-sky-400">
              Worker
            </span>
          )}
          {isToolRunner && (
            <span className="inline-flex items-center rounded-md border border-amber-500/40 bg-amber-500/10 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider text-amber-600 dark:text-amber-400">
              Tool Runner
            </span>
          )}
          {isConversational && (
            <span className="inline-flex items-center rounded-md border border-rose-500/40 bg-rose-500/10 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider text-rose-600 dark:text-rose-400">
              Conversational
            </span>
          )}
          {toolCount > 0 && <Badge>{toolCount} ferramenta{toolCount === 1 ? '' : 's'}</Badge>}
          {agent.visibility === 'global' && <Badge tone="accent">global</Badge>}
          <span>atualizado {formatRelative(agent.updatedAt)}</span>
        </div>
        <div className="flex flex-wrap items-center justify-end gap-2">
          <Button variant="ghost" size="sm" onClick={onHistory}>
            Histórico
          </Button>
          <Button variant="ghost" size="sm" onClick={onVersions}>
            Versões
          </Button>
          <Button variant="secondary" size="sm" onClick={onEdit} loading={forking}>
            Editar
          </Button>
          {isConversational && (
            <Button
              variant="secondary"
              size="sm"
              onClick={onTestInChat}
              loading={testingInChat}
              title="Cria session de teste isolado em chat AG-UI (workflow efêmero, sem Router)."
            >
              Testar em Chat
            </Button>
          )}
          {/* O disable abaixo é hint de UX. Authority da regra
              "Conversational requer InputMode=Chat" vive no backend
              (WorkflowAgentInvariantsValidator) — qualquer tentativa via API
              direta retorna 400 com errorCode=ConversationalRequiresChat. */}
          <Button
            size="sm"
            onClick={onDeploy}
            disabled={isConversational}
            leftIcon={<BoltIcon className="h-3.5 w-3.5" />}
            title={
              isConversational
                ? 'Conversational não suporta implantação Single — use Roteamento por intent ou Pipeline.'
                : undefined
            }
          >
            Implantar
          </Button>
        </div>
      </div>
    </Card>
  )
}

interface ApprovalHistoryModalProps {
  agent: Agent | null
  entries: ApprovalHistoryEntry[]
  loading: boolean
  error: string | null
  onClose: () => void
}

const ACTION_LABEL: Record<ApprovalAction, string> = {
  Submitted: 'Submetido',
  Resubmitted: 'Reenviado',
  Approved: 'Aprovado',
  Rejected: 'Rejeitado',
  AutoApproved: 'Auto-aprovado',
  AdminOverride: 'Edição direta (admin)',
}

const ACTION_TONE: Record<ApprovalAction, 'neutral' | 'accent' | 'success' | 'warning' | 'danger'> = {
  Submitted: 'accent',
  Resubmitted: 'accent',
  Approved: 'success',
  Rejected: 'danger',
  AutoApproved: 'success',
  AdminOverride: 'warning',
}

function ApprovalHistoryModal({ agent, entries, loading, error, onClose }: ApprovalHistoryModalProps) {
  return (
    <Modal
      open={agent !== null}
      onClose={onClose}
      size="lg"
      title="Histórico de aprovações"
      description={agent ? agent.name : undefined}
    >
      {loading && (
        <div className="flex items-center justify-center py-8">
          <Spinner className="h-6 w-6 text-fg-muted" />
        </div>
      )}
      {!loading && error && <ErrorMessage message={error} />}
      {!loading && !error && entries.length === 0 && (
        <p className="py-6 text-center text-sm text-fg-muted">
          Sem eventos registrados para este agente.
        </p>
      )}
      {!loading && !error && entries.length > 0 && (
        <ol className="relative space-y-4 border-l border-border pl-5">
          {entries.map((entry) => {
            const action = entry.action as ApprovalAction
            const tone = ACTION_TONE[action] ?? 'neutral'
            const label = ACTION_LABEL[action] ?? action
            return (
              <li key={entry.id} className="relative">
                <span
                  className={cn(
                    'absolute -left-[27px] top-1.5 h-3 w-3 rounded-full ring-2 ring-surface',
                    tone === 'success' && 'bg-success',
                    tone === 'danger' && 'bg-danger',
                    tone === 'warning' && 'bg-warning',
                    tone === 'accent' && 'bg-accent',
                    tone === 'neutral' && 'bg-fg-muted',
                  )}
                  aria-hidden="true"
                />
                <div className="flex flex-wrap items-center gap-2">
                  <Badge tone={tone}>{label}</Badge>
                  {entry.tier && <Badge tone={entry.tier === 'Cosmetic' ? 'success' : 'accent'}>{entry.tier}</Badge>}
                  <span className="text-[11px] text-fg-dim">{formatAbsolute(entry.occurredAt)}</span>
                </div>
                <p className="mt-1 text-xs text-fg-muted">
                  por <span className="font-mono text-fg">{entry.actorUserId}</span>
                </p>
                {entry.feedback && (
                  <p className="mt-1.5 whitespace-pre-wrap rounded-md border border-border bg-bg-soft px-3 py-2 text-xs text-fg">
                    {entry.feedback}
                  </p>
                )}
              </li>
            )
          })}
        </ol>
      )}
    </Modal>
  )
}

interface EnabledSwitchProps {
  enabled: boolean
  onToggle: () => void
}

function EnabledSwitch({ enabled, onToggle }: EnabledSwitchProps) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={enabled}
      aria-label={enabled ? 'Desabilitar agente' : 'Habilitar agente'}
      title={enabled ? 'Desabilitar agente' : 'Habilitar agente'}
      onClick={onToggle}
      className={cn(
        'relative inline-flex h-5 w-9 shrink-0 items-center rounded-full transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/40',
        enabled ? 'bg-success' : 'bg-fg-dim/40',
      )}
    >
      <span
        aria-hidden="true"
        className={cn(
          'inline-block h-4 w-4 transform rounded-full bg-white shadow transition',
          enabled ? 'translate-x-[18px]' : 'translate-x-0.5',
        )}
      />
    </button>
  )
}

interface ToggleEnabledModalProps {
  agent: Agent | null
  reason: string
  onReasonChange: (value: string) => void
  loading: boolean
  error: string | null
  onClose: () => void
  onConfirm: () => void
}

function ToggleEnabledModal({
  agent,
  reason,
  onReasonChange,
  loading,
  error,
  onClose,
  onConfirm,
}: ToggleEnabledModalProps) {
  const enabled = agent ? agent.enabled !== false : false
  const willDisable = enabled
  const title = willDisable ? 'Desabilitar agente' : 'Habilitar agente'

  // Blast radius: workflows-implantação que referenciam o agente. Carregado
  // só quando o modal abre — evita request supérfluo no mount da lista.
  // Falha NÃO é silenciosa: setamos `workflowsError` e bloqueamos o confirm
  // — em incidente P1, silêncio é pior que ruído. Operador precisa ver que
  // a verificação de impacto não rodou antes de desabilitar/habilitar.
  const [workflows, setWorkflows] = useState<Workflow[]>([])
  const [loadingWorkflows, setLoadingWorkflows] = useState(false)
  const [workflowsError, setWorkflowsError] = useState<string | null>(null)
  const [allowToggleAnyway, setAllowToggleAnyway] = useState(false)

  useEffect(() => {
    if (!agent) {
      // Reset entre aberturas — modal reaproveita estado se o user reabrir.
      setWorkflows([])
      setWorkflowsError(null)
      setAllowToggleAnyway(false)
      return
    }
    let cancelled = false
    setLoadingWorkflows(true)
    setWorkflowsError(null)
    setAllowToggleAnyway(false)
    listWorkflows()
      .then((all) => {
        if (cancelled) return
        const affected = all
          .filter(isAgentDeployment)
          .filter((w) => deployedAgentId(w) === agent.id)
        setWorkflows(affected)
      })
      .catch((err: unknown) => {
        if (cancelled) return
        setWorkflows([])
        setWorkflowsError(
          friendlyError(err, 'Não foi possível verificar quais implantações usam este agente.'),
        )
      })
      .finally(() => {
        if (!cancelled) setLoadingWorkflows(false)
      })
    return () => {
      cancelled = true
    }
  }, [agent])

  // Confirm bloqueado quando temos erro na verificação E o user ainda não
  // marcou explicitamente "desabilitar mesmo assim". Em sucesso (mesmo com 0
  // workflows), confirm continua liberado.
  const confirmDisabled = loading || (!!workflowsError && !allowToggleAnyway)

  return (
    <Modal
      open={agent !== null}
      onClose={onClose}
      title={title}
      description={agent?.name}
      footer={
        <div className="flex items-center justify-end gap-2">
          <Button variant="ghost" onClick={onClose} disabled={loading}>
            Cancelar
          </Button>
          <Button
            variant={willDisable ? 'danger' : 'primary'}
            onClick={onConfirm}
            loading={loading}
            disabled={confirmDisabled}
          >
            {willDisable ? 'Desabilitar' : 'Habilitar'}
          </Button>
        </div>
      }
    >
      <div className="space-y-3">
        <p className="text-sm text-fg-muted">
          {willDisable
            ? 'Quando desabilitado, o agente é pulado em runtime — workflows que o referenciam continuam saváveis, mas execuções não disparam o agente. Você pode reabilitar a qualquer momento.'
            : 'Ao habilitar, o agente volta a ser invocado em runtime nos workflows que o referenciam.'}
        </p>

        {/* Blast radius: lista as implantações afetadas pra evitar incidente
            P1 onde PO desabilita "seu" agente sem saber que outros squads
            consomem ele. Visível tanto pra desabilitar quanto pra habilitar.
            Em falha de carga, bloqueia o confirm até user marcar override. */}
        {loadingWorkflows ? (
          <div className="flex items-center gap-2 rounded-md bg-bg-soft px-3 py-2 text-xs text-fg-muted">
            <Spinner className="h-3.5 w-3.5" />
            Verificando implantações que referenciam este agente…
          </div>
        ) : workflowsError ? (
          <div className="space-y-2 rounded-md border border-warning/40 bg-warning/10 px-3 py-2 text-xs text-warning">
            <p className="font-semibold">Não foi possível verificar o impacto</p>
            <p>{workflowsError}</p>
            <label className="flex items-start gap-2 pt-1 text-fg-muted">
              <input
                type="checkbox"
                className="mt-0.5 h-3.5 w-3.5 accent-warning"
                checked={allowToggleAnyway}
                onChange={(e) => setAllowToggleAnyway(e.target.checked)}
                disabled={loading}
              />
              <span>
                Continuar mesmo assim — assumo o risco de afetar implantações sem
                conferência prévia.
              </span>
            </label>
          </div>
        ) : workflows.length === 0 ? (
          <div className="rounded-md bg-bg-soft px-3 py-2 text-xs text-fg-muted">
            Nenhuma implantação ativa referencia este agente.
          </div>
        ) : (
          <div
            className={cn(
              'rounded-md border px-3 py-2 text-xs',
              willDisable
                ? 'border-warning/40 bg-warning/10 text-warning'
                : 'border-accent/40 bg-accent-subtle text-accent',
            )}
          >
            <p className="font-semibold">
              {willDisable
                ? `${workflows.length} implantação${workflows.length === 1 ? '' : 'ões'} ${
                    workflows.length === 1 ? 'será afetada' : 'serão afetadas'
                  }:`
                : `${workflows.length} implantação${workflows.length === 1 ? '' : 'ões'} ${
                    workflows.length === 1 ? 'voltará' : 'voltarão'
                  } a executar:`}
            </p>
            <ul className="mt-1 space-y-0.5">
              {workflows.map((w) => (
                <li key={w.id} className="font-mono">
                  · {w.name}{' '}
                  <span className="opacity-70">({w.id})</span>
                </li>
              ))}
            </ul>
          </div>
        )}

        <div>
          <label className="block text-xs font-medium text-fg-muted">
            Motivo (opcional)
          </label>
          <Textarea
            value={reason}
            onChange={(e) => onReasonChange(e.target.value)}
            placeholder={
              willDisable
                ? 'Ex: incidente de produção, retirada temporária para retreino…'
                : 'Ex: incidente resolvido, retomando uso…'
            }
            rows={3}
            className="mt-1"
            disabled={loading}
          />
          <p className="mt-1 text-[11px] text-fg-dim">
            O motivo fica registrado no audit log da plataforma.
          </p>
        </div>
        {error && <ErrorMessage message={error} />}
      </div>
    </Modal>
  )
}

function formatAbsolute(iso: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return iso
  return date.toLocaleString('pt-BR', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function formatRelative(iso: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return ''
  const diffMs = Date.now() - date.getTime()
  const minutes = Math.round(diffMs / 60_000)
  if (minutes < 1) return 'agora'
  if (minutes < 60) return `há ${minutes} min`
  const hours = Math.round(minutes / 60)
  if (hours < 24) return `há ${hours} h`
  const days = Math.round(hours / 24)
  if (days < 7) return `há ${days} d`
  return date.toLocaleDateString('pt-BR')
}

/**
 * Badge de "validated for chat" pra Conversational. Backend é a authority:
 * frontend só renderiza com base nos 3 campos retornados em GET /agents.
 * Sem regra de negócio aqui — gating de plug em chats reais é decidido no
 * backend (warnings no save de Chat deploy).
 */
function ChatSandboxValidationBadge({ agent }: { agent: Agent }) {
  if (!agent.lastChatSandboxValidatedAt) {
    return (
      <div className="text-[11px] text-fg-muted">
        <Badge tone="neutral">Não validado em chat</Badge>
      </div>
    )
  }
  return (
    <div className="text-[11px] text-fg-muted">
      <Badge tone="success">
        Validado em chat · {formatRelative(agent.lastChatSandboxValidatedAt)}
      </Badge>
    </div>
  )
}
