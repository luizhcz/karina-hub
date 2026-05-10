import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { listAgents, type Agent } from '../api/agents'
import {
  deployedAgentId,
  deploymentKindOf,
  isAgentDeployment,
  isPipelineDeployment,
  listWorkflows,
  type DeploymentKind,
  type Workflow,
} from '../api/workflows'
import { friendlyError } from '../api/client'
import {
  AgentIcon,
  Badge,
  BoltIcon,
  Button,
  Card,
  ErrorMessage,
  Input,
  Modal,
  PlusIcon,
  SearchIcon,
  Spinner,
  cn,
} from '../ui'

type FilterKind = 'all' | DeploymentKind

const FILTER_LABELS: Record<FilterKind, string> = {
  all: 'Todas',
  single: 'Single',
  pipeline: 'Pipeline',
  routing: 'Roteamento',
}

export function Implantacoes() {
  const navigate = useNavigate()
  const [searchParams, setSearchParams] = useSearchParams()

  const [workflows, setWorkflows] = useState<Workflow[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [search, setSearch] = useState('')
  const [chooserOpen, setChooserOpen] = useState(false)
  const [pickerOpen, setPickerOpen] = useState(false)

  // Filtro persistido em query string (?type=pipeline|single|routing|all).
  // Default 'all' mantém comportamento anterior pra quem não usa o filtro.
  const filterKind: FilterKind = (() => {
    const raw = searchParams.get('type')
    return raw === 'pipeline' || raw === 'single' || raw === 'routing' ? raw : 'all'
  })()

  const setFilterKind = (next: FilterKind) => {
    const params = new URLSearchParams(searchParams)
    if (next === 'all') params.delete('type')
    else params.set('type', next)
    setSearchParams(params, { replace: true })
  }

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setError(null)
    listWorkflows('project')
      .then((all) => {
        if (cancelled) return
        // Lista mostra tanto single (legado, deploy-{agentId}) quanto pipelines
        // (deploy-pipeline-{guid}). Outros workflows criados por admin via API
        // continuam fora — só o que nasceu dos fluxos de implantação aparece.
        const deployments = all.filter((w) => isAgentDeployment(w) || isPipelineDeployment(w))
        setWorkflows(deployments)
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(friendlyError(err, 'Não foi possível carregar as implantações.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [])

  const deployedAgentIds = useMemo(
    () => new Set(workflows.map((w) => deployedAgentId(w)).filter((id): id is string => !!id)),
    [workflows],
  )

  const counts = useMemo(() => {
    let single = 0
    let pipeline = 0
    let routing = 0
    for (const w of workflows) {
      const kind = deploymentKindOf(w)
      if (kind === 'pipeline') pipeline++
      else if (kind === 'routing') routing++
      else single++
    }
    return { all: workflows.length, single, pipeline, routing }
  }, [workflows])

  const filtered = useMemo(() => {
    const byKind =
      filterKind === 'all' ? workflows : workflows.filter((w) => deploymentKindOf(w) === filterKind)
    const q = search.trim().toLowerCase()
    if (!q) return byKind
    return byKind.filter((w) => {
      const name = (w.name ?? '').toLowerCase()
      const desc = (w.description ?? '').toLowerCase()
      return name.includes(q) || desc.includes(q) || w.id.toLowerCase().includes(q)
    })
  }, [workflows, search, filterKind])

  const handleCardClick = (w: Workflow) => {
    const kind = deploymentKindOf(w)
    if (kind === 'pipeline') {
      navigate(`/implantacoes/avancada/${w.id}`)
      return
    }
    if (kind === 'routing') {
      navigate(`/implantacoes/roteamento/${w.id}`)
      return
    }
    const aid = deployedAgentId(w)
    if (aid) navigate(`/agentes/${aid}/implantar`)
  }

  return (
    <div className="mx-auto max-w-6xl">
      <div className="mb-8 flex items-end justify-between">
        <div>
          <h1 className="text-[28px] font-semibold tracking-tight">Implantações</h1>
          <p className="mt-2 text-sm text-fg-muted">
            Cada implantação expõe um agente — ou uma sequência de agentes — para consumo via API.
          </p>
        </div>
        <Button leftIcon={<PlusIcon className="h-4 w-4" />} onClick={() => setChooserOpen(true)}>
          Nova implantação
        </Button>
      </div>

      <div className="mb-6 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex rounded-lg border border-border bg-bg-soft p-1">
          {(['all', 'single', 'pipeline', 'routing'] as const).map((kind) => (
            <FilterPill
              key={kind}
              active={filterKind === kind}
              onClick={() => setFilterKind(kind)}
              label={FILTER_LABELS[kind]}
              count={counts[kind]}
            />
          ))}
        </div>
        <div className="max-w-md sm:w-80">
          <Input
            placeholder="Buscar por nome, descrição ou id…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            leftAddon={<SearchIcon className="h-4 w-4" />}
          />
        </div>
      </div>

      {loading ? (
        <Card className="flex items-center justify-center py-12">
          <Spinner className="h-6 w-6 text-fg-muted" />
        </Card>
      ) : error ? (
        <ErrorMessage message={error} />
      ) : workflows.length === 0 ? (
        <Card padded className="text-center">
          <p className="text-sm text-fg-muted">
            Nenhuma implantação ainda. Clique em <strong>Nova implantação</strong> para colocar um agente publicado em produção.
          </p>
        </Card>
      ) : filtered.length === 0 ? (
        <Card padded className="text-center">
          <p className="text-sm text-fg-muted">Nada bate com os filtros. Tente ajustar.</p>
        </Card>
      ) : (
        <div className="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3">
          {filtered.map((w) => (
            <DeploymentCard key={w.id} workflow={w} onClick={() => handleCardClick(w)} />
          ))}
        </div>
      )}

      <KindChooserModal
        open={chooserOpen}
        onClose={() => setChooserOpen(false)}
        onChoose={(kind: DeploymentKind) => {
          setChooserOpen(false)
          if (kind === 'single') setPickerOpen(true)
          else if (kind === 'routing') navigate('/implantacoes/roteamento')
          else navigate('/implantacoes/avancada')
        }}
      />

      <NewDeploymentModal
        open={pickerOpen}
        onClose={() => setPickerOpen(false)}
        deployedAgentIds={deployedAgentIds}
        onPick={(agentId) => {
          setPickerOpen(false)
          navigate(`/agentes/${agentId}/implantar`)
        }}
      />
    </div>
  )
}

interface FilterPillProps {
  active: boolean
  onClick: () => void
  label: string
  count: number
}

function FilterPill({ active, onClick, label, count }: FilterPillProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'flex items-center gap-1.5 rounded-md px-3 py-1.5 text-xs font-medium transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/30',
        active ? 'bg-surface text-fg shadow-soft' : 'text-fg-muted hover:text-fg',
      )}
    >
      {label}
      <span className={cn('rounded-full px-1.5 text-[10px]', active ? 'bg-bg-soft text-fg' : 'bg-bg/50 text-fg-dim')}>
        {count}
      </span>
    </button>
  )
}

interface DeploymentCardProps {
  workflow: Workflow
  onClick: () => void
}

function DeploymentCard({ workflow, onClick }: DeploymentCardProps) {
  const kind = deploymentKindOf(workflow)
  const agentId = deployedAgentId(workflow)
  const agentCount = workflow.agents?.length ?? 0
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
        'group relative flex min-h-[160px] cursor-pointer flex-col gap-3 overflow-hidden p-5',
        'before:absolute before:inset-y-0 before:left-0 before:w-1',
        kind === 'pipeline'
          ? 'before:bg-accent'
          : kind === 'routing'
            ? 'before:bg-violet-500'
            : 'before:bg-success',
      )}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="flex min-w-0 items-center gap-3">
          <div
            className={cn(
              'flex h-9 w-9 shrink-0 items-center justify-center rounded-lg',
              kind === 'pipeline'
                ? 'bg-accent-subtle text-accent'
                : kind === 'routing'
                  ? 'bg-violet-500/15 text-violet-600 dark:text-violet-400'
                  : 'bg-success/10 text-success',
            )}
          >
            <BoltIcon className="h-5 w-5" />
          </div>
          <div className="min-w-0">
            <h3 className="truncate text-sm font-semibold text-fg">{workflow.name}</h3>
            <p className="mt-0.5 truncate font-mono text-[10px] uppercase tracking-wider text-fg-dim">
              {workflow.id}
            </p>
          </div>
        </div>
        {kind === 'routing' ? (
          <span className="inline-flex items-center rounded-md border border-violet-500/40 bg-violet-500/10 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider text-violet-600 dark:text-violet-400">
            Roteamento
          </span>
        ) : (
          <Badge tone={kind === 'pipeline' ? 'accent' : 'success'}>
            {kind === 'pipeline' ? 'Pipeline' : 'Single'}
          </Badge>
        )}
      </div>

      {workflow.description && (
        <p className="line-clamp-2 text-xs text-fg-muted">{workflow.description}</p>
      )}

      <div className="mt-auto flex items-center justify-between text-[11px] text-fg-dim">
        {kind === 'pipeline' ? (
          <span className="flex items-center gap-1.5">
            <AgentIcon className="h-3.5 w-3.5" />
            {agentCount} {agentCount === 1 ? 'agente' : 'agentes em sequência'}
          </span>
        ) : kind === 'routing' ? (
          <span className="flex items-center gap-1.5">
            <AgentIcon className="h-3.5 w-3.5" />
            {agentCount} {agentCount === 1 ? 'agente' : 'agentes (router + branches)'}
          </span>
        ) : agentId ? (
          <span className="flex items-center gap-1.5">
            <AgentIcon className="h-3.5 w-3.5" />
            <span className="truncate font-mono">{agentId}</span>
          </span>
        ) : (
          <span />
        )}
        <span className="opacity-0 transition group-hover:opacity-100">Abrir →</span>
      </div>
    </Card>
  )
}

interface KindChooserModalProps {
  open: boolean
  onClose: () => void
  onChoose: (kind: DeploymentKind) => void
}

function KindChooserModal({ open, onClose, onChoose }: KindChooserModalProps) {
  return (
    <Modal
      open={open}
      onClose={onClose}
      size="md"
      title="Como você quer implantar?"
      description="Escolha o tipo de implantação. Você pode mudar a qualquer momento criando outra implantação."
    >
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
        <KindChooserCard
          label="Um agente"
          description="Implanta um agente publicado como workflow de chamada única."
          tone="success"
          onClick={() => onChoose('single')}
        />
        <KindChooserCard
          label="Pipeline em sequência"
          description="Vários agentes em fila — a saída de um vira a entrada do próximo, automaticamente."
          tone="accent"
          onClick={() => onChoose('pipeline')}
        />
        <KindChooserCard
          label="Roteamento por intent"
          description="Router como entry node + 1 agente por intent + fallback. Switch edge encaminha o input pra branch que a intent escolhida indica."
          tone="violet"
          onClick={() => onChoose('routing')}
        />
      </div>
    </Modal>
  )
}

interface KindChooserCardProps {
  label: string
  description: string
  tone: 'success' | 'accent' | 'violet'
  onClick: () => void
}

function KindChooserCard({ label, description, tone, onClick }: KindChooserCardProps) {
  const iconClass =
    tone === 'accent'
      ? 'bg-accent-subtle text-accent'
      : tone === 'violet'
        ? 'bg-violet-500/15 text-violet-600 dark:text-violet-400'
        : 'bg-success/10 text-success'
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'flex flex-col gap-2 rounded-xl border border-border bg-surface p-4 text-left transition',
        'hover:border-accent/60 hover:bg-accent-subtle/30 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/40',
      )}
    >
      <div className={cn('flex h-9 w-9 shrink-0 items-center justify-center rounded-lg', iconClass)}>
        <BoltIcon className="h-5 w-5" />
      </div>
      <div className="text-sm font-semibold text-fg">{label}</div>
      <p className="text-xs leading-relaxed text-fg-muted">{description}</p>
    </button>
  )
}

interface NewDeploymentModalProps {
  open: boolean
  onClose: () => void
  deployedAgentIds: Set<string>
  onPick: (agentId: string) => void
}

function NewDeploymentModal({ open, onClose, deployedAgentIds, onPick }: NewDeploymentModalProps) {
  const [agents, setAgents] = useState<Agent[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')

  useEffect(() => {
    if (!open) return
    let cancelled = false
    setLoading(true)
    setError(null)
    listAgents()
      .then((list) => {
        // Single deploy não aceita Conversational — esse tipo precisa de
        // workflow Chat (InputMode=Chat + chat-message endpoint) que o
        // AgentDeploy single não monta. Conversational vai pela rota
        // Roteamento ou Pipeline (com restrições próprias).
        if (!cancelled)
          setAgents(list.filter((a) => a.enabled !== false && a.type !== 'Conversational'))
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(friendlyError(err, 'Não foi possível carregar os agentes publicados.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [open])

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase()
    if (!q) return agents
    return agents.filter((a) => {
      const name = (a.name ?? '').toLowerCase()
      const desc = (a.description ?? '').toLowerCase()
      return name.includes(q) || desc.includes(q) || a.id.toLowerCase().includes(q)
    })
  }, [agents, search])

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="lg"
      title="Nova implantação"
      description="Selecione o agente publicado que você quer implantar."
    >
      <div className="space-y-3">
        <Input
          placeholder="Buscar por nome ou id…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          leftAddon={<SearchIcon className="h-4 w-4" />}
        />
        {loading && (
          <div className="flex items-center justify-center py-8">
            <Spinner className="h-6 w-6 text-fg-muted" />
          </div>
        )}
        {!loading && error && <ErrorMessage message={error} />}
        {!loading && !error && filtered.length === 0 && (
          <p className="py-6 text-center text-sm text-fg-muted">
            Nenhum agente publicado disponível.
          </p>
        )}
        {!loading && !error && filtered.length > 0 && (
          <ul className="max-h-[400px] space-y-1.5 overflow-y-auto">
            {filtered.map((a) => {
              const already = deployedAgentIds.has(a.id)
              return (
                <li key={a.id}>
                  <button
                    type="button"
                    onClick={() => onPick(a.id)}
                    className={cn(
                      'flex w-full items-center gap-3 rounded-lg border border-border bg-surface px-3 py-2.5 text-left transition',
                      'hover:border-accent/60 hover:bg-accent-subtle/30 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/40',
                    )}
                  >
                    <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-accent-subtle text-accent">
                      <AgentIcon className="h-4 w-4" />
                    </div>
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center gap-2">
                        <span className="truncate text-sm font-semibold text-fg">{a.name}</span>
                        {already && <Badge tone="success">já implantado</Badge>}
                      </div>
                      {a.description && (
                        <p className="mt-0.5 line-clamp-1 text-xs text-fg-muted">{a.description}</p>
                      )}
                      <p className="mt-0.5 truncate font-mono text-[10px] uppercase tracking-wider text-fg-dim">
                        {a.id}
                      </p>
                    </div>
                  </button>
                </li>
              )
            })}
          </ul>
        )}
      </div>
    </Modal>
  )
}
