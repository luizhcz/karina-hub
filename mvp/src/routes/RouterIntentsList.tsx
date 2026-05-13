import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { ApiError, friendlyError } from '../api/client'
import { listProjects, type Project } from '../api/projects'
import { useIsAdmin } from '../stores/me'
import {
  deleteRouterIntent,
  getRouterIntentUsage,
  listRouterIntents,
  type RouterIntent,
  type RouterIntentUsage,
} from '../api/routerIntents'
import {
  Badge,
  Button,
  Card,
  EmptyState,
  ErrorMessage,
  Input,
  Modal,
  PlusIcon,
  Select,
  SearchIcon,
  SparklesIcon,
  Spinner,
  cn,
} from '../ui'

const PAGE_SIZE = 20

export function RouterIntentsList() {
  const navigate = useNavigate()
  const [searchParams, setSearchParams] = useSearchParams()
  const isAdmin = useIsAdmin()

  const [intents, setIntents] = useState<RouterIntent[]>([])
  const [projects, setProjects] = useState<Project[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const projectFilter = searchParams.get('projeto') ?? ''
  const [page, setPage] = useState(1)

  const [pendingDelete, setPendingDelete] = useState<RouterIntent | null>(null)
  const [pendingDeleteUsage, setPendingDeleteUsage] = useState<RouterIntentUsage[]>([])
  const [deleting, setDeleting] = useState(false)
  const [deleteError, setDeleteError] = useState<string | null>(null)

  useEffect(() => {
    // Short-circuit pra non-admin: evita os 2 GETs que retornariam 403 (lista
    // de intents e lista de projetos pra lookup de categoria). Backend ainda
    // enforça em qualquer operação real (delete/edit).
    if (isAdmin === false) {
      setLoading(false)
      setError(
        'Esta tela é restrita a administradores. Se você precisa criar ou editar intenções, fale com o time de governança.',
      )
      setIntents([])
      setProjects([])
      return
    }
    if (isAdmin === null) return
    let cancelled = false
    setLoading(true)
    Promise.all([listRouterIntents(), listProjects()])
      .then(([list, projs]) => {
        if (cancelled) return
        setIntents(list)
        setProjects(projs)
        setError(null)
      })
      .catch((err) => {
        if (cancelled) return
        // Espelha o tratamento da tela de Aprovações: 403 é restrição
        // administrativa (o pool global de intents só edita via admin),
        // então deixamos uma mensagem clara em vez do friendlyError genérico.
        if (err instanceof ApiError && err.status === 403) {
          setError(
            'Esta tela é restrita a administradores. Se você precisa criar ou editar intenções, fale com o time de governança.',
          )
          setIntents([])
          setProjects([])
          return
        }
        setError(friendlyError(err, 'Não foi possível carregar as intenções.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [isAdmin])

  const projectNameById = useMemo(() => {
    const map = new Map<string, string>()
    for (const p of projects) map.set(p.id, p.name)
    return map
  }, [projects])

  const projectName = (id: string) => projectNameById.get(id) ?? id

  // 4 cards top-projetos: agrega counts por projectId, ordena desc, pega top 4.
  // Quando há menos de 4 projetos representados, completa com slot vazio
  // pra preservar a estrutura visual de "4 cards no topo".
  const topProjectCards = useMemo(() => {
    const counts = new Map<string, number>()
    for (const i of intents) counts.set(i.projectId, (counts.get(i.projectId) ?? 0) + 1)
    const sorted = [...counts.entries()].sort((a, b) => b[1] - a[1])
    const cards: Array<{ projectId: string | null; name: string; count: number }> = []
    for (let idx = 0; idx < 4; idx++) {
      if (idx < sorted.length) {
        const [projectId, count] = sorted[idx]
        cards.push({ projectId, name: projectName(projectId), count })
      } else {
        cards.push({ projectId: null, name: 'Sem intenções', count: 0 })
      }
    }
    return cards
  }, [intents, projectNameById])

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase()
    return intents.filter((i) => {
      if (projectFilter && i.projectId !== projectFilter) return false
      if (!q) return true
      return (
        i.name.toLowerCase().includes(q)
        || i.description.toLowerCase().includes(q)
        || projectName(i.projectId).toLowerCase().includes(q)
      )
    })
  }, [intents, search, projectFilter, projectNameById])

  // Reset page quando filtros mudam pra evitar ficar numa página inexistente.
  useEffect(() => {
    setPage(1)
  }, [search, projectFilter])

  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE))
  const pageStart = (page - 1) * PAGE_SIZE
  const pageEnd = Math.min(filtered.length, pageStart + PAGE_SIZE)
  const visible = filtered.slice(pageStart, pageEnd)

  const handleProjectFilter = (next: string) => {
    const params = new URLSearchParams(searchParams)
    if (next) params.set('projeto', next)
    else params.delete('projeto')
    setSearchParams(params, { replace: true })
  }

  const openDeleteModal = async (intent: RouterIntent) => {
    setPendingDelete(intent)
    setDeleteError(null)
    setPendingDeleteUsage([])
    try {
      const usage = await getRouterIntentUsage(intent.id)
      setPendingDeleteUsage(usage)
    } catch (err) {
      setDeleteError(friendlyError(err, 'Não foi possível verificar uso.'))
    }
  }

  const confirmDelete = async () => {
    if (!pendingDelete) return
    setDeleting(true)
    setDeleteError(null)
    try {
      await deleteRouterIntent(pendingDelete.id)
      setIntents((prev) => prev.filter((i) => i.id !== pendingDelete.id))
      setPendingDelete(null)
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        setDeleteError('Intent em uso por algum Router. Edite o Router e remova a intent do set antes de deletar.')
      } else {
        setDeleteError(friendlyError(err, 'Falha ao deletar.'))
      }
    } finally {
      setDeleting(false)
    }
  }

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <header className="mb-2 flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight text-fg">Intenções do roteador</h1>
          <p className="mt-1 max-w-2xl text-sm text-fg-muted">
            Pool global de intenções do tenant — qualquer Router do tenant pode referenciar.
            Edição de uma intenção propaga pros Routers que a usam (lookup em runtime); criação
            não propaga (edite o Router pra adicionar a intent nova ao set dele).
          </p>
        </div>
        <Button leftIcon={<PlusIcon className="h-4 w-4" />} onClick={() => navigate('/intencoes/nova')}>
          Nova intenção
        </Button>
      </header>

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {topProjectCards.map((card, idx) => (
          <Card key={`${card.projectId ?? 'empty'}-${idx}`} padded>
            <p className="text-[11px] font-semibold uppercase tracking-wider text-fg-dim">
              {card.projectId ? `Top ${idx + 1} · ${card.name}` : 'Sem intenções'}
            </p>
            <p className="mt-1 text-2xl font-semibold tracking-tight text-fg">{card.count}</p>
            <p className="mt-1 text-xs text-fg-muted">
              {card.count === 0 ? 'Nenhuma intenção neste projeto.' : `${card.count} intenções na categoria`}
            </p>
          </Card>
        ))}
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <div className="flex w-full max-w-md items-center gap-2 rounded-lg border border-border bg-surface px-3 py-2">
          <SearchIcon className="h-4 w-4 shrink-0 text-fg-dim" />
          <Input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Buscar por nome, descrição ou projeto…"
            className="border-none bg-transparent p-0 text-sm focus:ring-0"
          />
        </div>
        <div className="min-w-[200px]">
          <Select
            value={projectFilter}
            onChange={(e) => handleProjectFilter(e.target.value)}
            options={[
              { value: '', label: 'Todos os projetos' },
              ...projects.map((p) => ({ value: p.id, label: p.name })),
            ]}
          />
        </div>
        {projectFilter && (
          <Button variant="secondary" size="sm" onClick={() => handleProjectFilter('')}>
            Limpar filtro
          </Button>
        )}
      </div>

      {error && <ErrorMessage message={error} />}

      {loading ? (
        <div className="flex items-center justify-center py-12">
          <Spinner className="h-6 w-6 text-fg-muted" />
        </div>
      ) : intents.length === 0 ? (
        <EmptyState
          icon={<SparklesIcon className="h-6 w-6" />}
          title="Nenhuma intenção cadastrada"
          description="Cadastre intenções no pool pra que Routers do tenant possam referenciar."
          action={
            <Button leftIcon={<PlusIcon className="h-4 w-4" />} onClick={() => navigate('/intencoes/nova')}>
              Cadastrar a primeira
            </Button>
          }
        />
      ) : filtered.length === 0 ? (
        <Card padded>
          <p className="text-sm text-fg-muted">Nenhuma intenção bate com o filtro atual.</p>
        </Card>
      ) : (
        <>
          <Card>
            <ul className="divide-y divide-border">
              {visible.map((intent) => (
                <li
                  key={intent.id}
                  className="flex flex-wrap items-start gap-3 px-4 py-3 transition hover:bg-bg-soft"
                >
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-2">
                      <Link
                        to={`/intencoes/${intent.id}`}
                        className="text-sm font-semibold text-fg hover:text-accent"
                      >
                        {intent.displayName?.trim() || intent.name}
                      </Link>
                      <code className="rounded bg-bg-soft px-1.5 py-0.5 font-mono text-[11px] text-fg-dim">
                        {intent.name}
                      </code>
                      <button
                        type="button"
                        onClick={() => handleProjectFilter(intent.projectId)}
                        className="cursor-pointer"
                        title="Filtrar por este projeto"
                      >
                        <Badge tone="accent">{projectName(intent.projectId)}</Badge>
                      </button>
                    </div>
                    <p className="mt-1 text-xs leading-relaxed text-fg-muted line-clamp-2">
                      {intent.description}
                    </p>
                  </div>
                  <div className="flex shrink-0 items-center gap-2">
                    <Button
                      variant="secondary"
                      size="sm"
                      onClick={() => navigate(`/intencoes/${intent.id}`)}
                    >
                      Editar
                    </Button>
                    <Button
                      variant="secondary"
                      size="sm"
                      onClick={() => openDeleteModal(intent)}
                    >
                      Deletar
                    </Button>
                  </div>
                </li>
              ))}
            </ul>
          </Card>

          <div className="flex items-center justify-between text-xs text-fg-muted">
            <span>
              Mostrando {pageStart + 1}–{pageEnd} de {filtered.length}
            </span>
            <div className="flex items-center gap-2">
              <Button
                variant="secondary"
                size="sm"
                disabled={page <= 1}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
              >
                Anterior
              </Button>
              <span>
                Página {page} de {totalPages}
              </span>
              <Button
                variant="secondary"
                size="sm"
                disabled={page >= totalPages}
                onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
              >
                Próxima
              </Button>
            </div>
          </div>
        </>
      )}

      <Modal
        open={!!pendingDelete}
        onClose={() => (deleting ? null : setPendingDelete(null))}
        title="Deletar intenção"
        description={pendingDelete ? `Confirma remover "${pendingDelete.name}" do pool?` : ''}
      >
        {pendingDelete && (
          <div className="space-y-3 text-sm text-fg-muted">
            {pendingDeleteUsage.length > 0 ? (
              <div className="rounded-lg border border-warning/40 bg-warning/10 p-3">
                <p className="font-medium text-warning">
                  Esta intenção é usada por {pendingDeleteUsage.length} Router
                  {pendingDeleteUsage.length === 1 ? '' : 's'} —{' '}
                  delete será bloqueado pelo backend até remover a intent do set deles.
                </p>
                <ul className="mt-2 space-y-1 text-xs">
                  {pendingDeleteUsage.map((u) => (
                    <li key={u.agentId} className="flex items-center gap-2">
                      <Badge tone="neutral">{u.projectId}</Badge>
                      <span className="font-mono">{u.agentId}</span>
                      <span className="text-fg-dim">— {u.agentName}</span>
                    </li>
                  ))}
                </ul>
              </div>
            ) : (
              <p>Nenhum Router referencia essa intenção. Delete seguro.</p>
            )}

            {deleteError && <ErrorMessage message={deleteError} />}

            <div className="flex justify-end gap-2 pt-2">
              <Button variant="secondary" onClick={() => setPendingDelete(null)} disabled={deleting}>
                Cancelar
              </Button>
              <Button
                onClick={confirmDelete}
                disabled={deleting || pendingDeleteUsage.length > 0}
                className={cn(deleting && 'opacity-60')}
              >
                {deleting ? 'Deletando…' : 'Deletar'}
              </Button>
            </div>
          </div>
        )}
      </Modal>
    </div>
  )
}
