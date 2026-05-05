import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { listAgents, type Agent } from '../api/agents'
import {
  deployedAgentId,
  isAgentDeployment,
  listWorkflows,
  type Workflow,
} from '../api/workflows'
import { listEvalRunsByAgent, type EvalRunSummary } from '../api/profileEvaluation'
import { friendlyError } from '../api/client'
import { isInCurrentProject } from '../stores/projectScope'
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

export function Implantacoes() {
  const navigate = useNavigate()

  const [workflows, setWorkflows] = useState<Workflow[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [lastEvalByAgent, setLastEvalByAgent] = useState<Map<string, EvalRunSummary>>(new Map())

  const [search, setSearch] = useState('')
  const [pickerOpen, setPickerOpen] = useState(false)

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setError(null)
    listWorkflows()
      .then((all) => {
        if (cancelled) return
        // Filtra Visibility=global cross-project — visualmente só mostra deploys
        // do projeto atual (visibility continua válida pra runtime/consumo).
        const deployments = all.filter(isAgentDeployment).filter(isInCurrentProject)
        setWorkflows(deployments)

        // Carrega último run por agente em paralelo (best-effort).
        const agentIds = deployments
          .map((w) => deployedAgentId(w))
          .filter((id): id is string => !!id)
        Promise.allSettled(
          agentIds.map((aid) => listEvalRunsByAgent(aid, 1).then((runs) => ({ aid, run: runs[0] }))),
        ).then((results) => {
          if (cancelled) return
          const map = new Map<string, EvalRunSummary>()
          for (const r of results) {
            if (r.status === 'fulfilled' && r.value.run) {
              map.set(r.value.aid, r.value.run)
            }
          }
          setLastEvalByAgent(map)
        })
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

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase()
    if (!q) return workflows
    return workflows.filter((w) => {
      const name = (w.name ?? '').toLowerCase()
      const desc = (w.description ?? '').toLowerCase()
      return name.includes(q) || desc.includes(q) || w.id.toLowerCase().includes(q)
    })
  }, [workflows, search])

  return (
    <div className="mx-auto max-w-6xl">
      <div className="mb-8 flex items-end justify-between">
        <div>
          <h1 className="text-[28px] font-semibold tracking-tight">Implantações</h1>
          <p className="mt-2 text-sm text-fg-muted">
            Cada implantação cria um workflow Graph que expõe um agente publicado para consumo via API.
          </p>
        </div>
        <Button leftIcon={<PlusIcon className="h-4 w-4" />} onClick={() => setPickerOpen(true)}>
          Nova implantação
        </Button>
      </div>

      <div className="mb-6 max-w-md">
        <Input
          placeholder="Buscar por nome, descrição ou id…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          leftAddon={<SearchIcon className="h-4 w-4" />}
        />
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
          <p className="text-sm text-fg-muted">Nada bate com a busca. Tente ajustar o termo.</p>
        </Card>
      ) : (
        <div className="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3">
          {filtered.map((w) => {
            const aid = deployedAgentId(w)
            return (
              <DeploymentCard
                key={w.id}
                workflow={w}
                lastEval={aid ? lastEvalByAgent.get(aid) ?? null : null}
                onClick={() => {
                  if (aid) navigate(`/agentes/${aid}/implantar`)
                }}
              />
            )
          })}
        </div>
      )}

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

function EvalBadge({ run }: { run: EvalRunSummary }) {
  const status = run.status
  if (status === 'Pending' || status === 'Running') {
    return <Badge tone="accent" className="text-[10px]">Avaliação rodando</Badge>
  }
  if (status === 'Failed' || status === 'Cancelled') {
    return <Badge tone="danger" className="text-[10px]">Avaliação falhou</Badge>
  }
  if (status === 'Completed') {
    const passed = run.casesPassed ?? 0
    const failed = run.casesFailed ?? 0
    // Todos evaluators NotApplicable — preset não casa com o agente.
    if (run.casesTotal > 0 && passed + failed === 0) {
      return <Badge tone="warning" className="text-[10px]">Preset não aplicável</Badge>
    }
    const score = run.avgScore !== null && run.avgScore !== undefined
      ? Math.round(Number(run.avgScore) * 100)
      : null
    if (score !== null) {
      const tone = score >= 60 ? 'success' : 'warning'
      const label = score >= 60 ? `Avaliação ✓ ${score}` : `Score baixo ${score}`
      return <Badge tone={tone} className="text-[10px]">{label}</Badge>
    }
    return <Badge tone="success" className="text-[10px]">Avaliado</Badge>
  }
  return null
}

interface DeploymentCardProps {
  workflow: Workflow
  lastEval: EvalRunSummary | null
  onClick: () => void
}

function DeploymentCard({ workflow, lastEval, onClick }: DeploymentCardProps) {
  const agentId = deployedAgentId(workflow)
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
        'before:absolute before:inset-y-0 before:left-0 before:w-1 before:bg-success',
      )}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="flex min-w-0 items-center gap-3">
          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-success/10 text-success">
            <BoltIcon className="h-5 w-5" />
          </div>
          <div className="min-w-0">
            <h3 className="truncate text-sm font-semibold text-fg">{workflow.name}</h3>
            <p className="mt-0.5 truncate font-mono text-[10px] uppercase tracking-wider text-fg-dim">
              {workflow.id}
            </p>
          </div>
        </div>
        <div className="flex flex-col items-end gap-1">
          <Badge tone="success">Implantado</Badge>
          {lastEval && <EvalBadge run={lastEval} />}
        </div>
      </div>

      {workflow.description && (
        <p className="line-clamp-2 text-xs text-fg-muted">{workflow.description}</p>
      )}

      <div className="mt-auto flex items-center justify-between text-[11px] text-fg-dim">
        {agentId ? (
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
        if (!cancelled) setAgents(list.filter((a) => a.enabled !== false))
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
