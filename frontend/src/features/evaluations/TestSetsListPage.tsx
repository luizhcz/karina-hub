import { Link, useNavigate } from 'react-router'
import { useProjectStore } from '../../stores/project'
import { useTestSets } from '../../api/evaluations'
import type { TestSet } from '../../api/evaluations'
import { Button } from '../../shared/ui/Button'
import { Card } from '../../shared/ui/Card'
import { Badge } from '../../shared/ui/Badge'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { EmptyState } from '../../shared/ui/EmptyState'

export function TestSetsListPage() {
  const navigate = useNavigate()
  const projectId = useProjectStore((s) => s.projectId) ?? 'default'
  const { data, isLoading, error, refetch } = useTestSets(projectId)

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar test sets." onRetry={refetch} />

  const items = data ?? []

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold text-text-primary">Test Sets de Avaliação</h1>
        <Button variant="primary" onClick={() => navigate('/evaluations/test-sets/new')}>
          + Novo Test Set
        </Button>
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
                  <span>
                    {ts.caseCount ?? 0} case{(ts.caseCount ?? 0) === 1 ? '' : 's'}
                    {ts.currentRevision ? ` · v${ts.currentRevision}` : ' · sem versão'}
                  </span>
                  <span>·</span>
                  <span>{new Date(ts.updatedAt).toLocaleDateString('pt-BR')}</span>
                </div>
              </Card>
            </Link>
          ))}
        </div>
      )}

    </div>
  )
}
