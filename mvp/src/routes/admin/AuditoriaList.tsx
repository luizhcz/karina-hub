import { useEffect, useMemo, useState } from 'react'
import {
  listAuditLog,
  type AdminAuditEntry,
} from '../../api/admin/auditLog'
import { listProjects, type Project } from '../../api/projects'
import { friendlyError } from '../../api/client'
import { useIsAdmin } from '../../stores/me'
import { AuditEntryModal } from '../../components/admin/AuditEntryModal'
import {
  Badge,
  Button,
  Card,
  EmptyState,
  ErrorMessage,
  Input,
  SearchIcon,
  Select,
  Spinner,
  cn,
} from '../../ui'

const PAGE_SIZE = 50

// Resource types canônicos do backend (AdminAuditResources). Mantido em PT-BR
// pra leitura humana — o filtro envia o valor cru do enum.
const RESOURCE_TYPES: { value: string; label: string }[] = [
  { value: '', label: 'Todos os recursos' },
  { value: 'agent', label: 'Agente' },
  { value: 'workflow', label: 'Workflow' },
  { value: 'project', label: 'Projeto' },
  { value: 'user', label: 'Usuário' },
  { value: 'generic_tool', label: 'Ferramenta' },
  { value: 'mcp_server', label: 'MCP Server' },
  { value: 'router_intent', label: 'Intenção' },
  { value: 'skill', label: 'Skill' },
  { value: 'predefined_model', label: 'Modelo predefinido' },
  { value: 'model_pricing', label: 'Pricing de modelo' },
  { value: 'blocklist', label: 'Blocklist' },
  { value: 'chat_sandbox_session', label: 'Chat sandbox' },
  { value: 'agent_sandbox_session', label: 'Standalone sandbox' },
  { value: 'persona_cache', label: 'Persona cache' },
  { value: 'persona_prompt_template', label: 'Persona template' },
  { value: 'persona_prompt_experiment', label: 'Persona experimento' },
  { value: 'document_intelligence_pricing', label: 'Document intelligence pricing' },
]

const ACTION_TONE: Record<string, 'neutral' | 'accent' | 'success' | 'warning' | 'danger'> = {
  create: 'success',
  update: 'accent',
  delete: 'danger',
  read: 'neutral',
  blocklist_violation: 'warning',
}

function actionTone(action: string): 'neutral' | 'accent' | 'success' | 'warning' | 'danger' {
  if (action in ACTION_TONE) return ACTION_TONE[action]
  if (action.endsWith('.created') || action.endsWith('.approved')) return 'success'
  if (action.endsWith('.deleted') || action.endsWith('.rejected')) return 'danger'
  if (action.endsWith('.updated') || action.endsWith('.changed') || action.endsWith('.assigned')) return 'accent'
  if (action.endsWith('.violation') || action.includes('failed')) return 'warning'
  return 'neutral'
}

function formatDate(iso: string): string {
  try {
    return new Date(iso).toLocaleString('pt-BR', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    })
  } catch {
    return iso
  }
}

export function AuditoriaList() {
  const isAdmin = useIsAdmin()

  // Filtros — só disparam fetch após apertar "Aplicar" pra não ficar
  // recarregando a cada keystroke em ResourceId/ActorUserId.
  const [draftResourceType, setDraftResourceType] = useState('')
  const [draftAction, setDraftAction] = useState('')
  const [draftActor, setDraftActor] = useState('')
  const [draftResourceId, setDraftResourceId] = useState('')
  const [draftProject, setDraftProject] = useState('')

  // Filtros aplicados (o que efetivamente vai pro backend).
  const [filters, setFilters] = useState({
    resourceType: '',
    action: '',
    actor: '',
    resourceId: '',
    projectId: '',
  })

  const [page, setPage] = useState(1)
  const [items, setItems] = useState<AdminAuditEntry[]>([])
  const [total, setTotal] = useState(0)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [projects, setProjects] = useState<Project[]>([])
  const [selectedEntry, setSelectedEntry] = useState<AdminAuditEntry | null>(null)

  useEffect(() => {
    if (isAdmin !== true) return
    let cancelled = false
    listProjects()
      .then((list) => {
        if (!cancelled) setProjects(list)
      })
      .catch(() => {
        // Sem projects o filtro de projeto vira input vazio — não é blocker.
      })
    return () => {
      cancelled = true
    }
  }, [isAdmin])

  useEffect(() => {
    if (isAdmin !== true) return
    let cancelled = false
    setLoading(true)
    setError(null)
    listAuditLog({
      resourceType: filters.resourceType || undefined,
      action: filters.action || undefined,
      actorUserId: filters.actor || undefined,
      resourceId: filters.resourceId || undefined,
      projectId: filters.projectId || undefined,
      page,
      pageSize: PAGE_SIZE,
    })
      .then((res) => {
        if (cancelled) return
        setItems(res.items)
        setTotal(res.total)
      })
      .catch((err: unknown) => {
        if (cancelled) return
        setError(friendlyError(err, 'Não foi possível carregar a auditoria.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [isAdmin, filters, page])

  const projectOptions = useMemo(
    () => [
      { value: '', label: 'Todos os projetos' },
      ...projects.map((p) => ({ value: p.id, label: p.name })),
    ],
    [projects],
  )

  const applyFilters = () => {
    setFilters({
      resourceType: draftResourceType,
      action: draftAction.trim(),
      actor: draftActor.trim(),
      resourceId: draftResourceId.trim(),
      projectId: draftProject,
    })
    setPage(1)
  }

  const clearFilters = () => {
    setDraftResourceType('')
    setDraftAction('')
    setDraftActor('')
    setDraftResourceId('')
    setDraftProject('')
    setFilters({ resourceType: '', action: '', actor: '', resourceId: '', projectId: '' })
    setPage(1)
  }

  if (isAdmin === null) return null
  if (isAdmin === false) {
    return (
      <Card>
        <EmptyState
          title="Acesso restrito"
          description="Esta área é exclusiva para administradores."
        />
      </Card>
    )
  }

  const lastPage = Math.max(1, Math.ceil(total / PAGE_SIZE))

  return (
    <div className="space-y-5">
      <div>
        <h1 className="text-xl font-semibold tracking-tight">Auditoria</h1>
        <p className="mt-1 text-sm text-fg-muted">
          Trilha de mudanças administrativas do tenant. Cada linha é uma operação
          CRUD em recurso governado — projeto, agente, workflow, usuário, vínculo
          ou ferramenta.
        </p>
      </div>

      <Card>
        <div className="grid grid-cols-1 gap-3 md:grid-cols-5">
          <Select
            label="Recurso"
            options={RESOURCE_TYPES}
            value={draftResourceType}
            onChange={(e) => setDraftResourceType(e.target.value)}
          />
          <Select
            label="Projeto"
            options={projectOptions}
            value={draftProject}
            onChange={(e) => setDraftProject(e.target.value)}
          />
          <Input
            label="Ação"
            placeholder="ex.: create, user.admin_flag_changed"
            value={draftAction}
            onChange={(e) => setDraftAction(e.target.value)}
            monospace
          />
          <Input
            label="Ator"
            placeholder="ExternalUserId"
            value={draftActor}
            onChange={(e) => setDraftActor(e.target.value)}
            monospace
          />
          <Input
            label="ID do recurso"
            placeholder="opcional"
            value={draftResourceId}
            onChange={(e) => setDraftResourceId(e.target.value)}
            leftAddon={<SearchIcon className="h-4 w-4" />}
            monospace
          />
        </div>

        <div className="mt-3 flex items-center gap-2">
          <Button onClick={applyFilters}>Aplicar filtros</Button>
          <Button variant="ghost" onClick={clearFilters}>
            Limpar
          </Button>
        </div>
      </Card>

      <Card>
        {error && <ErrorMessage message={error} className="mb-3" />}

        {loading ? (
          <div className="flex justify-center py-10">
            <Spinner className="h-6 w-6 text-fg-muted" />
          </div>
        ) : items.length === 0 ? (
          <EmptyState
            title="Nada por aqui"
            description="Nenhuma operação correspondente aos filtros atuais."
          />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-border text-left text-[11px] uppercase tracking-wider text-fg-dim">
                  <th className="py-2 pr-3 font-medium">Quando</th>
                  <th className="py-2 pr-3 font-medium">Ator</th>
                  <th className="py-2 pr-3 font-medium">Ação</th>
                  <th className="py-2 pr-3 font-medium">Recurso</th>
                  <th className="py-2 pr-3 font-medium">ID</th>
                  <th className="py-2 pr-3 font-medium">Projeto</th>
                </tr>
              </thead>
              <tbody>
                {items.map((entry) => (
                  <tr
                    key={entry.id}
                    onClick={() => setSelectedEntry(entry)}
                    className={cn(
                      'cursor-pointer border-b border-border/40 transition hover:bg-surface-hover',
                    )}
                  >
                    <td className="py-2 pr-3 whitespace-nowrap text-fg">{formatDate(entry.timestamp)}</td>
                    <td className="py-2 pr-3">
                      <div className="font-mono text-[12px] text-fg">{entry.actorUserId}</div>
                      {entry.actorUserType && (
                        <div className="text-[10px] text-fg-dim">{entry.actorUserType}</div>
                      )}
                    </td>
                    <td className="py-2 pr-3">
                      <Badge tone={actionTone(entry.action)}>{entry.action}</Badge>
                    </td>
                    <td className="py-2 pr-3 text-fg-muted">{entry.resourceType}</td>
                    <td className="py-2 pr-3 font-mono text-[11px] text-fg-muted">
                      {entry.resourceId}
                    </td>
                    <td className="py-2 pr-3 text-fg-muted">{entry.projectId ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {total > PAGE_SIZE && (
          <div className="mt-4 flex items-center justify-between text-xs text-fg-muted">
            <span>
              Página {page} de {lastPage} · {total} eventos no total
            </span>
            <div className="flex gap-2">
              <Button
                variant="ghost"
                disabled={page <= 1}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
              >
                Anterior
              </Button>
              <Button
                variant="ghost"
                disabled={page >= lastPage}
                onClick={() => setPage((p) => Math.min(lastPage, p + 1))}
              >
                Próxima
              </Button>
            </div>
          </div>
        )}
      </Card>

      {selectedEntry && (
        <AuditEntryModal
          entry={selectedEntry}
          onClose={() => setSelectedEntry(null)}
        />
      )}
    </div>
  )
}
