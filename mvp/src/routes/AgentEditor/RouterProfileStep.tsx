import { useEffect, useMemo, useState } from 'react'
import { listProjects, type Project } from '../../api/projects'
import { listRouterIntents, type RouterIntent } from '../../api/routerIntents'
import { friendlyError } from '../../api/client'
import {
  Badge,
  Button,
  Card,
  CardHeader,
  ErrorMessage,
  Input,
  Select,
  Spinner,
  cn,
} from '../../ui'
import type { FormState } from './types'

interface RouterProfileStepProps {
  form: FormState
  setForm: (mutator: (prev: FormState) => FormState) => void
  readonly: boolean
}

// Selector multi-checkbox do pool global de Router intents. Agrupa por
// projeto/categoria (cabeçalho mostra o nome do projeto resolvido). Quando
// uma intent é deletada do pool em /intencoes, ela some daqui automaticamente
// no próximo mount (lookup vivo). Edits em intents (descrição/categoria/nome)
// também propagam — selector mostra o estado atual.
//
// Não edita conteúdo de intent — só seleciona quais este Router atende.
// Botão "Gerenciar pool →" navega pra /intencoes.
export function RouterProfileStep({ form, setForm, readonly }: RouterProfileStepProps) {
  const [pool, setPool] = useState<RouterIntent[]>([])
  const [projects, setProjects] = useState<Project[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [projectFilter, setProjectFilter] = useState('')

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    Promise.all([listRouterIntents(), listProjects()])
      .then(([intents, projs]) => {
        if (cancelled) return
        setPool(intents)
        setProjects(projs)
        setError(null)
      })
      .catch((err) => {
        if (cancelled) return
        setError(friendlyError(err, 'Não foi possível carregar o pool de intenções.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [])

  const projectNameById = useMemo(() => {
    const map = new Map<string, string>()
    for (const p of projects) map.set(p.id, p.name)
    return map
  }, [projects])

  const projectName = (id: string) => projectNameById.get(id) ?? id

  const selectedSet = useMemo(() => new Set(form.routerIntentIds), [form.routerIntentIds])

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase()
    return pool.filter((i) => {
      if (projectFilter && i.projectId !== projectFilter) return false
      if (!q) return true
      return (
        i.name.toLowerCase().includes(q)
        || i.description.toLowerCase().includes(q)
        || projectName(i.projectId).toLowerCase().includes(q)
      )
    })
  }, [pool, search, projectFilter, projectNameById])

  const grouped = useMemo(() => {
    const groups = new Map<string, RouterIntent[]>()
    for (const i of filtered) {
      const key = i.projectId
      const arr = groups.get(key) ?? []
      arr.push(i)
      groups.set(key, arr)
    }
    return [...groups.entries()].sort((a, b) =>
      projectName(a[0]).localeCompare(projectName(b[0])),
    )
  }, [filtered, projectNameById])

  const toggle = (intentId: string) => {
    if (readonly) return
    setForm((prev) => {
      const set = new Set(prev.routerIntentIds)
      if (set.has(intentId)) set.delete(intentId)
      else set.add(intentId)
      return { ...prev, routerIntentIds: [...set] }
    })
  }

  const selectedCount = form.routerIntentIds.length
  const tooFew = selectedCount < 2

  return (
    <div className="space-y-5">
      <Card className="space-y-3">
        <CardHeader
          title="Identificação"
          description="Nome curto pra identificar o Router em listagens, workflows e logs. Não vai pro prompt."
        />
        <Input
          label="Nome do agente"
          value={form.name}
          onChange={(e) => setForm((prev) => ({ ...prev, name: e.target.value }))}
          placeholder="Ex: triagem-pix-chat"
          disabled={readonly}
          monospace
        />
      </Card>

      <Card className="space-y-3">
        <CardHeader
          title="Intenções atendidas"
          description="Marque quais intenções do pool global este Router classifica. Adicionar uma intenção nova ao pool não inclui automaticamente neste Router — você precisa voltar aqui e marcar."
          actions={
            <Button
              variant="secondary"
              size="sm"
              onClick={() => window.open('/intencoes', '_blank')}
            >
              Gerenciar pool →
            </Button>
          }
        />

        <div
          className={cn(
            'rounded-lg border px-3 py-2 text-xs',
            tooFew
              ? 'border-warning/40 bg-warning/10 text-warning'
              : 'border-success/40 bg-success/10 text-success',
          )}
        >
          {tooFew
            ? `Selecione pelo menos 2 intenções (${selectedCount} marcada${selectedCount === 1 ? '' : 's'}).`
            : `${selectedCount} de ${pool.length} intenções selecionadas.`}
        </div>

        {error && <ErrorMessage message={error} />}

        {loading ? (
          <div className="flex items-center justify-center py-8">
            <Spinner className="h-6 w-6 text-fg-muted" />
          </div>
        ) : pool.length === 0 ? (
          <div className="rounded-lg border border-dashed border-border px-4 py-8 text-center text-xs text-fg-muted">
            <p>O pool de intenções do tenant está vazio.</p>
            <Button
              variant="secondary"
              size="sm"
              className="mt-3"
              onClick={() => window.open('/intencoes/nova', '_blank')}
            >
              Cadastrar a primeira →
            </Button>
          </div>
        ) : (
          <>
            <div className="flex flex-wrap items-center gap-2">
              <Input
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Buscar intenção…"
                className="max-w-md"
              />
              <div className="min-w-[180px]">
                <Select
                  value={projectFilter}
                  onChange={(e) => setProjectFilter(e.target.value)}
                  options={[
                    { value: '', label: 'Todos os projetos' },
                    ...projects.map((p) => ({ value: p.id, label: p.name })),
                  ]}
                />
              </div>
            </div>

            {grouped.length === 0 ? (
              <p className="text-sm text-fg-muted">Nenhuma intenção bate com o filtro.</p>
            ) : (
              <div className="space-y-4">
                {grouped.map(([projectId, items]) => {
                  const groupSelected = items.filter((i) => selectedSet.has(i.id)).length
                  return (
                    <div key={projectId}>
                      <div className="mb-2 flex items-center justify-between">
                        <div className="flex items-center gap-2">
                          <Badge tone="accent">{projectName(projectId)}</Badge>
                          <span className="text-[11px] text-fg-dim">
                            {groupSelected} de {items.length} selecionada{items.length === 1 ? '' : 's'}
                          </span>
                        </div>
                      </div>
                      <ul className="space-y-1.5">
                        {items.map((intent) => {
                          const checked = selectedSet.has(intent.id)
                          return (
                            <li key={intent.id}>
                              <label
                                className={cn(
                                  'flex cursor-pointer items-start gap-3 rounded-lg border px-3 py-2 transition',
                                  checked
                                    ? 'border-accent/40 bg-accent-subtle/40'
                                    : 'border-border bg-surface hover:bg-bg-soft',
                                  readonly && 'cursor-not-allowed opacity-60',
                                )}
                              >
                                <input
                                  type="checkbox"
                                  className="mt-1 shrink-0"
                                  checked={checked}
                                  disabled={readonly}
                                  onChange={() => toggle(intent.id)}
                                />
                                <div className="min-w-0 flex-1">
                                  <code className="font-mono text-sm font-semibold text-fg">
                                    {intent.name}
                                  </code>
                                  <p className="mt-0.5 text-xs leading-relaxed text-fg-muted line-clamp-2">
                                    {intent.description}
                                  </p>
                                </div>
                              </label>
                            </li>
                          )
                        })}
                      </ul>
                    </div>
                  )
                })}
              </div>
            )}
          </>
        )}
      </Card>

      <Card className="space-y-2">
        <CardHeader
          title="Como o pool funciona"
          description="Pool global é compartilhado entre projetos do tenant. Edits propagam pros Routers que referenciam — só criação não."
        />
        <ul className="space-y-1.5 text-xs leading-relaxed text-fg-muted">
          <li>
            Editar uma intenção (descrição/categoria/exemplos) atualiza
            automaticamente todos os Routers que a usam — incluindo este, na
            próxima chamada do agente.
          </li>
          <li>
            Cadastrar uma intenção nova no pool <strong>não</strong> a inclui
            automaticamente neste Router. Você precisa voltar aqui e marcá-la.
          </li>
          <li>
            Deletar uma intenção é bloqueado pelo backend se algum Router (incluindo
            este) referenciar — desmarque aqui antes de deletar lá.
          </li>
        </ul>
      </Card>
    </div>
  )
}
