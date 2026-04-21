import { useParams, useNavigate } from 'react-router'
import { useProject, useProjectStats } from '../../api/projects'
import { Card } from '../../shared/ui/Card'
import { MetricCard } from '../../shared/data/MetricCard'
import { Button } from '../../shared/ui/Button'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { formatNumber } from '../../shared/utils/formatters'

export function ProjectStatsPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const project = useProject(id ?? '', !!id)
  const stats = useProjectStats(id ?? '', !!id)

  if (project.isLoading || stats.isLoading) return <PageLoader />
  if (project.error) return <ErrorCard message="Projeto não encontrado" onRetry={project.refetch} />

  const p = project.data
  const s = stats.data

  return (
    <div className="flex flex-col gap-6 p-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="sm" onClick={() => navigate('/projects')}>← Voltar</Button>
        <div>
          <h1 className="text-2xl font-bold text-text-primary">{p?.name ?? 'Projeto'}</h1>
          {p?.description && <p className="text-sm text-text-muted mt-1">{p.description}</p>}
        </div>
      </div>

      <div className="grid grid-cols-2 md:grid-cols-3 gap-4">
        <MetricCard
          label="Total Execuções"
          value={formatNumber(s?.executionCount ?? 0)}
          sub="workflows executados"
        />
        <MetricCard
          label="Agentes"
          value={formatNumber(s?.agentCount ?? 0)}
          sub="agentes no projeto"
        />
        <MetricCard
          label="Workflows"
          value={formatNumber(s?.workflowCount ?? 0)}
          sub="workflows configurados"
        />
      </div>


      <Card title="Informações">
        <div className="grid grid-cols-2 gap-4 text-sm">
          <div>
            <span className="text-text-muted">ID:</span>
            <p className="font-mono text-xs text-text-secondary mt-1">{p?.id}</p>
          </div>
          <div>
            <span className="text-text-muted">Criado em:</span>
            <p className="text-text-secondary mt-1">
              {p?.createdAt ? new Date(p.createdAt).toLocaleString('pt-BR') : '—'}
            </p>
          </div>
          <div>
            <span className="text-text-muted">Atualizado em:</span>
            <p className="text-text-secondary mt-1">
              {p?.updatedAt ? new Date(p.updatedAt).toLocaleString('pt-BR') : '—'}
            </p>
          </div>
        </div>
      </Card>
    </div>
  )
}
