import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { listAgentDrafts, type AgentDraft, type AgentDraftStatus } from '../api/agentDrafts'
import { friendlyError } from '../api/client'
import { NewAgentModeModal } from '../components/NewAgentModeModal'
import {
  AgentIcon,
  Badge,
  Button,
  Card,
  ErrorMessage,
  Input,
  PlusIcon,
  SearchIcon,
  Spinner,
  cn,
} from '../ui'

type StatusTone = 'neutral' | 'accent' | 'warning'

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

// Faixa lateral colorida por status — diferencia rapidamente cards na grade.
const STATUS_ACCENT: Record<AgentDraftStatus, string> = {
  Draft: 'before:bg-fg-dim/30',
  PendingApproval: 'before:bg-accent',
  Rejected: 'before:bg-warning',
}

export function AgentsList() {
  const navigate = useNavigate()
  const [drafts, setDrafts] = useState<AgentDraft[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [modeModalOpen, setModeModalOpen] = useState(false)

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    listAgentDrafts()
      .then((list) => {
        if (!cancelled) setDrafts(list)
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(friendlyError(err, 'Não foi possível carregar os rascunhos.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [])

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase()
    if (!q) return drafts
    return drafts.filter((d) => {
      const name = (d.name || d.payload?.name || '').toLowerCase()
      const desc = (d.payload?.description ?? '').toLowerCase()
      return name.includes(q) || desc.includes(q)
    })
  }, [drafts, search])

  const handleSelectMode = (mode: 'basic' | 'advanced') => {
    setModeModalOpen(false)
    navigate(`/agentes/novo?mode=${mode}`)
  }

  const showCreateCard = !loading && !error && search.trim().length === 0

  return (
    <div className="mx-auto max-w-6xl">
      <div className="mb-8 flex items-end justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Agentes</h1>
          <p className="mt-1 text-sm text-fg-muted">
            Rascunhos dos agentes que você está montando. Salve o progresso a qualquer momento e
            envie para aprovação quando estiver pronto.
          </p>
        </div>
        <Button leftIcon={<PlusIcon className="h-4 w-4" />} onClick={() => setModeModalOpen(true)}>
          Novo agente
        </Button>
      </div>

      <div className="mb-6 max-w-md">
        <Input
          placeholder="Buscar por nome ou descrição…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          leftAddon={<SearchIcon className="h-4 w-4" />}
        />
      </div>

      {loading && (
        <Card className="flex items-center justify-center py-12">
          <Spinner className="h-6 w-6 text-fg-muted" />
        </Card>
      )}

      {error && <ErrorMessage message={error} />}

      {!loading && !error && filtered.length === 0 && search.trim().length > 0 && (
        <Card padded className="mb-5 text-center">
          <p className="text-sm text-fg-muted">Nada bate com a busca. Tente ajustar o termo.</p>
        </Card>
      )}

      {!loading && !error && (filtered.length > 0 || showCreateCard) && (
        <div className="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3">
          {showCreateCard && <CreateAgentCard onClick={() => setModeModalOpen(true)} />}
          {filtered.map((d) => (
            <DraftCard key={d.id} draft={d} onClick={() => navigate(`/agentes/${d.id}`)} />
          ))}
        </div>
      )}

      <NewAgentModeModal
        open={modeModalOpen}
        onClose={() => setModeModalOpen(false)}
        onSelect={handleSelectMode}
      />
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
        <p className="mt-1 text-xs text-fg-muted">
          Escolha entre modo básico e avançado.
        </p>
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
        // Faixa lateral colorida via pseudo-elemento — accent visual sutil.
        'before:absolute before:inset-y-0 before:left-0 before:w-1',
        STATUS_ACCENT[draft.status],
      )}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="flex min-w-0 items-center gap-3">
          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-accent-subtle text-accent">
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

      <div className="mt-auto flex items-center justify-between text-[11px] text-fg-dim">
        <div className="flex items-center gap-2">
          {draft.isEditDraft && <Badge>edição</Badge>}
          <span>atualizado {formatRelative(draft.updatedAt)}</span>
        </div>
        <span className="opacity-0 transition group-hover:opacity-100">Abrir →</span>
      </div>
    </Card>
  )
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
