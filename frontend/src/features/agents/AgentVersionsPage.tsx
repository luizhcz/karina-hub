import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { Button } from '../../shared/ui/Button'
import { Card } from '../../shared/ui/Card'
import { Badge } from '../../shared/ui/Badge'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { DiffViewer } from '../../shared/data/DiffViewer'
import {
  useAgent,
  useAgentVersions,
  useAgentVersion,
  useRollbackAgent,
} from '../../api/agents'
import type { AgentVersion } from '../../api/agents'
import { PublishVersionModal } from './components/PublishVersionModal'

export function AgentVersionsPage() {
  const { id } = useParams<{ id: string }>()
  const { data: agent } = useAgent(id!, !!id)
  const { data: versions, isLoading, error, refetch } = useAgentVersions(id!, !!id)
  const rollbackMutation = useRollbackAgent()

  const [rollbackTarget, setRollbackTarget] = useState<AgentVersion | null>(null)
  const [selectedA, setSelectedA] = useState<string | null>(null)
  const [selectedB, setSelectedB] = useState<string | null>(null)
  const [publishOpen, setPublishOpen] = useState(false)

  const { data: versionA } = useAgentVersion(id!, selectedA ?? '', !!id && !!selectedA)
  const { data: versionB } = useAgentVersion(id!, selectedB ?? '', !!id && !!selectedB)

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar versoes." onRetry={refetch} />

  const sorted = [...(versions ?? [])].sort(
    (a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
  )

  const handleSelectForDiff = (versionId: string) => {
    if (!selectedA) {
      setSelectedA(versionId)
    } else if (!selectedB && versionId !== selectedA) {
      setSelectedB(versionId)
    } else {
      setSelectedA(versionId)
      setSelectedB(null)
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center gap-3">
        <Link to="/agents">
          <Button variant="ghost" size="sm">
            &larr; Agentes
          </Button>
        </Link>
        <div className="flex-1">
          <h1 className="text-2xl font-bold text-text-primary">
            Versoes: {agent?.name ?? id}
          </h1>
          <p className="text-sm text-text-muted mt-1">
            Historico de versoes do agente. Selecione duas versoes para comparar.
          </p>
        </div>
        <Button variant="primary" size="sm" onClick={() => setPublishOpen(true)}>
          Publicar versão
        </Button>
      </div>

      <Card title="Historico de Versoes" padding={false}>
        <div className="divide-y divide-border-primary">
          {sorted.length === 0 && (
            <p className="text-sm text-text-dimmed p-5">Nenhuma versao encontrada.</p>
          )}
          {sorted.map((version, idx) => (
            <div
              key={version.versionId}
              className="flex items-center justify-between px-5 py-4 hover:bg-bg-tertiary/50 transition-colors"
            >
              <div className="flex items-center gap-4">
                <div className="flex flex-col items-center">
                  <div className={`w-3 h-3 rounded-full border-2 ${
                    idx === 0 ? 'bg-accent-blue border-accent-blue' : 'bg-bg-tertiary border-border-secondary'
                  }`} />
                </div>
                <div>
                  <div className="flex items-center gap-2">
                    <span className="text-sm font-medium text-text-primary">
                      {version.versionId}
                    </span>
                    {idx === 0 && <Badge variant="green">Current</Badge>}
                    {(selectedA === version.versionId || selectedB === version.versionId) && (
                      <Badge variant="blue">Selected</Badge>
                    )}
                  </div>
                  <p className="text-xs text-text-muted mt-0.5">
                    {new Date(version.createdAt).toLocaleString('pt-BR')}
                    {version.description && ` — ${version.description}`}
                  </p>
                </div>
              </div>
              <div className="flex items-center gap-2">
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => handleSelectForDiff(version.versionId)}
                >
                  Compare
                </Button>
                {idx > 0 && (
                  <Button
                    variant="secondary"
                    size="sm"
                    onClick={() => setRollbackTarget(version)}
                  >
                    Rollback
                  </Button>
                )}
              </div>
            </div>
          ))}
        </div>
      </Card>

      {selectedA && selectedB && versionA && versionB && (
        <Card title={`Diff: ${selectedA} vs ${selectedB}`}>
          <DiffViewer
            oldValue={JSON.stringify(versionA, null, 2)}
            newValue={JSON.stringify(versionB, null, 2)}
            oldTitle={selectedA}
            newTitle={selectedB}
            splitView
          />
        </Card>
      )}

      <ConfirmDialog
        open={!!rollbackTarget}
        onClose={() => setRollbackTarget(null)}
        onConfirm={() => {
          if (rollbackTarget) {
            rollbackMutation.mutate(
              { id: id!, body: { versionId: rollbackTarget.versionId } },
              { onSuccess: () => setRollbackTarget(null) },
            )
          }
        }}
        title="Rollback de Versao"
        message={`Deseja restaurar o agente para a versao "${rollbackTarget?.versionId}"?`}
        confirmLabel="Restaurar"
        loading={rollbackMutation.isPending}
      />

      <PublishVersionModal
        agentId={id!}
        agentName={agent?.name}
        open={publishOpen}
        onClose={() => setPublishOpen(false)}
      />
    </div>
  )
}
