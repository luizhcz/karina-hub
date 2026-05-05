import { useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router'
import {
  Badge,
  Button,
  Card,
  ErrorMessage,
  Input,
  SearchIcon,
  SparklesIcon,
  Spinner,
  cn,
} from '../../ui'
import { friendlyError } from '../../api/client'
import {
  deployedAgentId,
  isAgentDeployment,
  listWorkflows,
} from '../../api/workflows'
import {
  type EvalRunSummary,
  listEvalRunsByAgent,
} from '../../api/profileEvaluation'
import { isInCurrentProject } from '../../stores/projectScope'
import { RunHistory } from './RunHistory'
import { RunDetailDrawer } from './RunDetailDrawer'
import { GlossaryDrawer } from './GlossaryDrawer'

interface AgentEntry {
  id: string
  name: string
  workflowId: string
  lastRun: EvalRunSummary | null
}

export function Avaliacoes() {
  const [params, setParams] = useSearchParams()
  const selectedAgentId = params.get('agent')

  const [agents, setAgents] = useState<AgentEntry[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [selectedRun, setSelectedRun] = useState<EvalRunSummary | null>(null)
  const [glossaryOpen, setGlossaryOpen] = useState(false)
  const [glossaryHighlight, setGlossaryHighlight] = useState<string[]>([])

  // Carrega lista de agentes implantados + último run em paralelo.
  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setError(null)

    listWorkflows()
      .then(async (all) => {
        // Esconde deploys de outros projetos (Visibility=global cross-project).
        const deployments = all.filter(isAgentDeployment).filter(isInCurrentProject)
        const entries: AgentEntry[] = []
        for (const w of deployments) {
          const aid = deployedAgentId(w)
          if (!aid) continue
          entries.push({ id: aid, name: w.name, workflowId: w.id, lastRun: null })
        }

        // Hidrata último run em paralelo (best-effort).
        const lastRuns = await Promise.allSettled(
          entries.map((e) => listEvalRunsByAgent(e.id, 1).then((r) => r[0] ?? null)),
        )

        if (cancelled) return
        const hydrated = entries.map((e, i) => ({
          ...e,
          lastRun: lastRuns[i].status === 'fulfilled' ? lastRuns[i].value : null,
        }))
        // Ordena: agentes com run mais recente primeiro, depois alfabético.
        hydrated.sort((a, b) => {
          const ad = a.lastRun?.createdAt ? new Date(a.lastRun.createdAt).getTime() : 0
          const bd = b.lastRun?.createdAt ? new Date(b.lastRun.createdAt).getTime() : 0
          if (bd !== ad) return bd - ad
          return a.name.localeCompare(b.name)
        })
        setAgents(hydrated)
      })
      .catch((err) => {
        if (!cancelled) setError(friendlyError(err, 'Não foi possível carregar as implantações.'))
      })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [])

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase()
    if (!q) return agents
    return agents.filter((a) => a.name.toLowerCase().includes(q) || a.id.toLowerCase().includes(q))
  }, [agents, search])

  const selected = agents.find((a) => a.id === selectedAgentId) ?? null

  const handleSelectAgent = (id: string) => {
    setParams((prev) => {
      const p = new URLSearchParams(prev)
      p.set('agent', id)
      return p
    })
    setSelectedRun(null)
  }

  const handleOpenGlossary = (highlight: string[] = []) => {
    setGlossaryHighlight(highlight)
    setGlossaryOpen(true)
  }

  return (
    <div className="mx-auto max-w-7xl">
      <div className="mb-6 flex items-end justify-between gap-4">
        <div>
          <h1 className="text-[28px] font-semibold tracking-tight">Avaliações</h1>
          <p className="mt-2 text-sm text-fg-muted">
            Histórico de avaliações automáticas por agente. Cada implantação dispara uma run no preset escolhido.
          </p>
        </div>
        <Button
          variant="secondary"
          size="sm"
          leftIcon={<SparklesIcon className="h-4 w-4" />}
          onClick={() => handleOpenGlossary([])}
        >
          O que cada métrica significa
        </Button>
      </div>

      {loading ? (
        <Card className="flex items-center justify-center py-12">
          <Spinner className="h-6 w-6 text-fg-muted" />
        </Card>
      ) : error ? (
        <ErrorMessage message={error} />
      ) : agents.length === 0 ? (
        <Card padded className="text-center">
          <h3 className="text-sm font-semibold text-fg">Nenhum agente implantado</h3>
          <p className="mt-1 text-xs text-fg-muted">
            Implante um agente em <strong>Implantações</strong> pra que as avaliações automáticas comecem a rodar.
          </p>
        </Card>
      ) : (
        <div className="grid grid-cols-1 gap-5 lg:grid-cols-[280px_1fr]">
          {/* Sidebar de agentes */}
          <aside className="space-y-3">
            <Input
              placeholder="Buscar agente…"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              leftAddon={<SearchIcon className="h-4 w-4" />}
            />
            <nav className="space-y-1.5">
              {filtered.map((a) => (
                <AgentRow
                  key={a.id}
                  entry={a}
                  active={a.id === selectedAgentId}
                  onClick={() => handleSelectAgent(a.id)}
                />
              ))}
              {filtered.length === 0 && (
                <p className="px-2 py-3 text-xs text-fg-muted">Nada bate com a busca.</p>
              )}
            </nav>
          </aside>

          {/* Área principal */}
          <main className="space-y-3">
            {selected && (
              <Card className="space-y-2" padded>
                <div className="flex items-baseline justify-between gap-3">
                  <div className="min-w-0">
                    <h2 className="truncate text-base font-semibold text-fg">{selected.name}</h2>
                    <p className="font-mono text-[10px] uppercase tracking-wider text-fg-dim">{selected.id}</p>
                  </div>
                  {selected.lastRun?.avgScore !== null && selected.lastRun?.avgScore !== undefined && (
                    <div className="text-right">
                      <div className="text-2xl font-bold text-fg">
                        {Math.round(Number(selected.lastRun.avgScore) * 100)}
                      </div>
                      <div className="text-[10px] text-fg-dim">último score /100</div>
                    </div>
                  )}
                </div>
              </Card>
            )}
            <RunHistory
              agentId={selected?.id ?? null}
              agentName={selected?.name ?? null}
              onSelect={setSelectedRun}
            />
          </main>
        </div>
      )}

      <RunDetailDrawer
        open={selectedRun !== null}
        run={selectedRun}
        onClose={() => setSelectedRun(null)}
        onOpenGlossary={(names) => handleOpenGlossary(names)}
      />

      <GlossaryDrawer
        open={glossaryOpen}
        onClose={() => setGlossaryOpen(false)}
        highlightedNames={glossaryHighlight}
      />
    </div>
  )
}

interface AgentRowProps {
  entry: AgentEntry
  active: boolean
  onClick: () => void
}

function AgentRow({ entry, active, onClick }: AgentRowProps) {
  const score = entry.lastRun?.avgScore !== null && entry.lastRun?.avgScore !== undefined
    ? Math.round(Number(entry.lastRun.avgScore) * 100)
    : null
  const status = entry.lastRun?.status

  return (
    <button
      onClick={onClick}
      className={cn(
        'w-full rounded-lg border px-3 py-2.5 text-left transition',
        active
          ? 'border-accent bg-accent-subtle text-fg'
          : 'border-border bg-surface text-fg hover:bg-surface-hover',
      )}
    >
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-semibold">{entry.name}</p>
          <p className="mt-0.5 truncate font-mono text-[10px] uppercase tracking-wider text-fg-dim">
            {entry.id}
          </p>
        </div>
        {score !== null && (
          <Badge
            tone={score >= 80 ? 'success' : score >= 60 ? 'neutral' : 'warning'}
            className="shrink-0 text-[10px]"
          >
            {score}
          </Badge>
        )}
      </div>
      {entry.lastRun ? (
        <p className="mt-1.5 text-[10px] text-fg-dim">
          {status === 'Running' || status === 'Pending'
            ? 'Avaliação em andamento'
            : `Última: ${relativeDate(entry.lastRun.createdAt)}`}
        </p>
      ) : (
        <p className="mt-1.5 text-[10px] text-fg-dim">Sem avaliações ainda</p>
      )}
    </button>
  )
}

function relativeDate(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime()
  const min = Math.floor(diff / 60_000)
  if (min < 1) return 'agora'
  if (min < 60) return `${min}min atrás`
  const h = Math.floor(min / 60)
  if (h < 24) return `${h}h atrás`
  const d = Math.floor(h / 24)
  if (d < 7) return `${d}d atrás`
  return new Date(iso).toLocaleDateString('pt-BR', { dateStyle: 'short' })
}
