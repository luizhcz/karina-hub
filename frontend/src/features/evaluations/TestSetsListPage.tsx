import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { useProjectStore } from '../../stores/project'
import { useTestSets, useCreateTestSet } from '../../api/evaluations'
import type { TestSet } from '../../api/evaluations'
import { Button } from '../../shared/ui/Button'
import { Card } from '../../shared/ui/Card'
import { Badge } from '../../shared/ui/Badge'
import { Modal } from '../../shared/ui/Modal'
import { Input } from '../../shared/ui/Input'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { EmptyState } from '../../shared/ui/EmptyState'

export function TestSetsListPage() {
  const navigate = useNavigate()
  const projectId = useProjectStore((s) => s.projectId) ?? 'default'
  const { data, isLoading, error, refetch } = useTestSets(projectId)
  const createMutation = useCreateTestSet()
  const [creating, setCreating] = useState(false)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar test sets." onRetry={refetch} />

  const items = data ?? []

  const handleCreate = async () => {
    if (!name.trim()) return
    const created = await createMutation.mutateAsync({
      projectId,
      body: { name: name.trim(), description: description.trim() || undefined, visibility: 'project' },
    })
    setCreating(false)
    setName('')
    setDescription('')
    navigate(`/evaluations/test-sets/${created.id}`)
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold text-text-primary">Test Sets de Avaliação</h1>
        <Button variant="primary" onClick={() => setCreating(true)}>+ Novo Test Set</Button>
      </div>

      {items.length === 0 ? (
        <EmptyState
          title="Nenhum test set"
          description="Crie um test set pra começar a avaliar seus agentes contra cases definidos."
        />
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {items.map((ts: TestSet) => (
            <Link key={ts.id} to={`/evaluations/test-sets/${ts.id}`}>
              <Card className="hover:border-blue-500/50 transition-colors cursor-pointer h-full">
                <div className="flex items-start justify-between mb-2">
                  <div className="font-semibold text-text-primary truncate flex-1">{ts.name}</div>
                  <Badge variant={ts.visibility === 'global' ? 'purple' : 'gray'}>
                    {ts.visibility}
                  </Badge>
                </div>
                {ts.description && (
                  <div className="text-sm text-text-muted mb-2 line-clamp-2">{ts.description}</div>
                )}
                <div className="text-xs text-text-muted flex items-center gap-2">
                  <span>v{ts.currentVersionId ? '✓' : '—'}</span>
                  <span>·</span>
                  <span>{new Date(ts.updatedAt).toLocaleDateString('pt-BR')}</span>
                </div>
              </Card>
            </Link>
          ))}
        </div>
      )}

      <Modal open={creating} onClose={() => setCreating(false)} title="Novo Test Set">
        <div className="space-y-4">
          <Input
            label="Nome"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Ex.: Cobertura básica de saudações"
            required
          />
          <Input
            label="Descrição (opcional)"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
          />
          <div className="text-xs text-text-muted leading-relaxed">
            Visibility default <strong>project</strong>. Cópia cross-project usa endpoint dedicado depois.
            Promoção a <strong>global</strong> requer permissão admin (não implementado nesta versão).
          </div>
          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={() => setCreating(false)}>Cancelar</Button>
            <Button
              variant="primary"
              onClick={handleCreate}
              loading={createMutation.isPending}
              disabled={!name.trim()}
            >
              Criar
            </Button>
          </div>
        </div>
      </Modal>
    </div>
  )
}
