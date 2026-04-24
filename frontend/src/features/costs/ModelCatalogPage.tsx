import { useState, useMemo } from 'react'
import { useModelCatalog, useUpsertModelCatalog, useDeactivateModel } from '../../api/modelCatalog'
import type { ModelCatalog, UpsertModelCatalogRequest } from '../../api/modelCatalog'
import { Card } from '../../shared/ui/Card'
import { Button } from '../../shared/ui/Button'
import { Input } from '../../shared/ui/Input'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { PROVIDER_TYPES } from '../../constants/providers'
import { PAGE_SIZES } from '../../constants/pagination'

function Badge({ text, color }: { text: string; color: string }) {
  return (
    <span className={`px-1.5 py-0.5 rounded text-[10px] font-medium ${color}`}>{text}</span>
  )
}

function ModelForm({
  initial,
  onSave,
  onCancel,
  saving,
}: {
  initial?: Partial<UpsertModelCatalogRequest>
  onSave: (r: UpsertModelCatalogRequest) => void
  onCancel: () => void
  saving: boolean
}) {
  const [form, setForm] = useState<UpsertModelCatalogRequest>({
    id: initial?.id ?? '',
    provider: initial?.provider ?? 'OPENAI',
    displayName: initial?.displayName ?? '',
    description: initial?.description ?? '',
    contextWindow: initial?.contextWindow,
    capabilities: initial?.capabilities ?? ['chat'],
    isActive: initial?.isActive ?? true,
  })

  const setField = <K extends keyof UpsertModelCatalogRequest>(key: K, value: UpsertModelCatalogRequest[K]) =>
    setForm((prev) => ({ ...prev, [key]: value }))

  const toggleCap = (cap: string) => {
    const caps = form.capabilities ?? []
    setField('capabilities', caps.includes(cap) ? caps.filter((c) => c !== cap) : [...caps, cap])
  }

  const CAPS = ['chat', 'vision', 'function_calling', 'reasoning']

  return (
    <div className="flex flex-col gap-4 p-4 bg-bg-secondary border border-border-primary rounded-lg">
      <div className="flex gap-3">
        <div className="flex-1">
          <Input label="ID do modelo *" value={form.id} onChange={(e) => setField('id', e.target.value)} placeholder="gpt-4o" />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-xs text-text-muted">Provider *</label>
          <select
            value={form.provider}
            onChange={(e) => setField('provider', e.target.value)}
            className="bg-bg-tertiary border border-border-secondary rounded-md px-2.5 py-1.5 text-sm text-text-primary focus:outline-none focus:border-accent-blue"
          >
            {PROVIDER_TYPES.map((p) => <option key={p}>{p}</option>)}
          </select>
        </div>
      </div>
      <Input label="Nome exibido *" value={form.displayName} onChange={(e) => setField('displayName', e.target.value)} placeholder="GPT-4o" />
      <Input label="Descrição" value={form.description ?? ''} onChange={(e) => setField('description', e.target.value)} placeholder="Descrição opcional" />
      <Input
        label="Context window (tokens)"
        type="number"
        value={form.contextWindow ?? ''}
        onChange={(e) => setField('contextWindow', e.target.value ? Number(e.target.value) : undefined)}
      />
      <div className="flex flex-col gap-1">
        <label className="text-xs text-text-muted">Capacidades</label>
        <div className="flex gap-2 flex-wrap">
          {CAPS.map((cap) => (
            <button
              key={cap}
              type="button"
              onClick={() => toggleCap(cap)}
              className={`px-2 py-0.5 rounded text-xs border transition-colors ${
                (form.capabilities ?? []).includes(cap)
                  ? 'bg-accent-blue/20 border-accent-blue text-accent-blue'
                  : 'border-border-secondary text-text-muted hover:border-accent-blue'
              }`}
            >
              {cap}
            </button>
          ))}
        </div>
      </div>
      <div className="flex items-center gap-2">
        <input
          type="checkbox"
          id="isActive"
          checked={form.isActive ?? true}
          onChange={(e) => setField('isActive', e.target.checked)}
          className="w-4 h-4 accent-accent-blue"
        />
        <label htmlFor="isActive" className="text-sm text-text-secondary">Ativo</label>
      </div>
      <div className="flex gap-2 justify-end">
        <Button variant="ghost" size="sm" onClick={onCancel}>Cancelar</Button>
        <Button
          size="sm"
          loading={saving}
          onClick={() => onSave(form)}
          disabled={!form.id || !form.displayName}
        >
          Salvar
        </Button>
      </div>
    </div>
  )
}

export function ModelCatalogPage() {
  const [provider, setProvider] = useState<string>('')
  const [showInactive, setShowInactive] = useState(false)
  const [adding, setAdding] = useState(false)
  const [editing, setEditing] = useState<ModelCatalog | null>(null)
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(0)

  const { data: models = [], isLoading, error, refetch } = useModelCatalog(provider || undefined, !showInactive)
  const upsert = useUpsertModelCatalog()
  const deactivate = useDeactivateModel()

  const handleSave = async (req: UpsertModelCatalogRequest) => {
    await upsert.mutateAsync(req)
    setAdding(false)
    setEditing(null)
  }

  const filtered = useMemo(() => {
    const q = search.toLowerCase()
    if (!q) return models
    return models.filter(
      (m) =>
        m.id.toLowerCase().includes(q) ||
        m.displayName.toLowerCase().includes(q) ||
        m.provider.toLowerCase().includes(q)
    )
  }, [models, search])

  const totalPages = Math.ceil(filtered.length / PAGE_SIZES.small)
  const paged = filtered.slice(page * PAGE_SIZES.small, (page + 1) * PAGE_SIZES.small)

  const handleSearch = (value: string) => {
    setSearch(value)
    setPage(0)
  }

  const handleProviderChange = (value: string) => {
    setProvider(value)
    setPage(0)
  }

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar catálogo de modelos" onRetry={refetch} />

  return (
    <div className="flex flex-col gap-6 p-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Catálogo de Modelos</h1>
          <p className="text-sm text-text-muted mt-1">Modelos disponíveis por provider para uso nos projetos</p>
        </div>
        <Button onClick={() => { setAdding(true); setEditing(null) }}>+ Adicionar</Button>
      </div>

      <div className="flex items-center gap-4 flex-wrap">
        <input
          type="text"
          placeholder="Buscar modelo..."
          value={search}
          onChange={(e) => handleSearch(e.target.value)}
          className="bg-bg-tertiary border border-border-secondary rounded-md px-3 py-1.5 text-sm text-text-primary placeholder-text-muted focus:outline-none focus:border-accent-blue w-56"
        />
        <select
          value={provider}
          onChange={(e) => handleProviderChange(e.target.value)}
          className="bg-bg-tertiary border border-border-secondary rounded-md px-2.5 py-1.5 text-sm text-text-primary focus:outline-none focus:border-accent-blue"
        >
          <option value="">Todos os providers</option>
          {PROVIDER_TYPES.map((p) => <option key={p} value={p}>{p}</option>)}
        </select>
        <label className="flex items-center gap-2 text-sm text-text-muted cursor-pointer">
          <input
            type="checkbox"
            checked={showInactive}
            onChange={(e) => { setShowInactive(e.target.checked); setPage(0) }}
            className="accent-accent-blue"
          />
          Mostrar inativos
        </label>
        <span className="text-xs text-text-muted ml-auto">
          {filtered.length} modelo(s)
        </span>
      </div>

      {adding && (
        <ModelForm saving={upsert.isPending} onSave={handleSave} onCancel={() => setAdding(false)} />
      )}

      <Card padding={false}>
        <div className="flex flex-col divide-y divide-border-primary">
          {paged.length === 0 && (
            <p className="text-sm text-text-muted py-8 text-center">Nenhum modelo encontrado.</p>
          )}
          {paged.map((m) => (
            <div key={`${m.provider}/${m.id}`}>
              {editing?.id === m.id && editing?.provider === m.provider ? (
                <div className="p-4">
                  <ModelForm
                    initial={m}
                    saving={upsert.isPending}
                    onSave={handleSave}
                    onCancel={() => setEditing(null)}
                  />
                </div>
              ) : (
                <div className="flex items-center gap-4 py-3 px-4">
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2 flex-wrap">
                      <span className="text-sm font-medium text-text-primary">{m.displayName}</span>
                      <Badge text={m.provider} color="bg-accent-blue/20 text-accent-blue" />
                      {!m.isActive && <Badge text="inativo" color="bg-red-500/20 text-red-400" />}
                    </div>
                    <div className="text-xs text-text-muted font-mono mt-0.5">{m.id}</div>
                    {m.contextWindow && (
                      <div className="text-xs text-text-dimmed mt-0.5">
                        {(m.contextWindow / 1000).toFixed(0)}k context
                      </div>
                    )}
                    <div className="flex gap-1 mt-1 flex-wrap">
                      {m.capabilities.map((c) => (
                        <Badge key={c} text={c} color="bg-bg-tertiary text-text-secondary" />
                      ))}
                    </div>
                  </div>
                  <div className="flex gap-2 flex-shrink-0">
                    <Button variant="secondary" size="sm" onClick={() => setEditing(m)}>Editar</Button>
                    {m.isActive && (
                      <Button
                        variant="danger"
                        size="sm"
                        loading={deactivate.isPending}
                        onClick={() => deactivate.mutate({ provider: m.provider, id: m.id })}
                      >
                        Desativar
                      </Button>
                    )}
                  </div>
                </div>
              )}
            </div>
          ))}
        </div>

        {totalPages > 1 && (
          <div className="flex items-center justify-between px-4 py-3 border-t border-border-primary">
            <span className="text-xs text-text-muted">
              Página {page + 1} de {totalPages}
            </span>
            <div className="flex gap-2">
              <button
                onClick={() => setPage((p) => p - 1)}
                disabled={page === 0}
                className="px-3 py-1 text-xs rounded border border-border-secondary text-text-secondary hover:bg-bg-tertiary disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                Anterior
              </button>
              <button
                onClick={() => setPage((p) => p + 1)}
                disabled={page >= totalPages - 1}
                className="px-3 py-1 text-xs rounded border border-border-secondary text-text-secondary hover:bg-bg-tertiary disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                Próxima
              </button>
            </div>
          </div>
        )}
      </Card>
    </div>
  )
}
