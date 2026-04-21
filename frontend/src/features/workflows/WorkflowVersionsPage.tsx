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
  useWorkflow,
  useWorkflowVersions,
  useWorkflowVersion,
  useRollbackWorkflow,
} from '../../api/workflows'
import type { WorkflowVersion } from '../../api/workflows'

export function WorkflowVersionsPage() {
  const { id } = useParams<{ id: string }>()
  const { data: workflow } = useWorkflow(id!, !!id)
  const { data: versions, isLoading, error, refetch } = useWorkflowVersions(id!, !!id)
  const rollbackMutation = useRollbackWorkflow()

  const [rollbackTarget, setRollbackTarget] = useState<WorkflowVersion | null>(null)
  const [selectedA, setSelectedA] = useState<string | null>(null)
  const [selectedB, setSelectedB] = useState<string | null>(null)

  // Fetch selected version snapshots for diff
  const { data: versionA } = useWorkflowVersion(id!, selectedA ?? '', !!id && !!selectedA)
  const { data: versionB } = useWorkflowVersion(id!, selectedB ?? '', !!id && !!selectedB)

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar versoes." onRetry={refetch} />

  const sorted = [...(versions ?? [])].sort(
    (a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime(),
  )

  const handleSelectForDiff = (versionId: string) => {
    if (!selectedA) {
      setSelectedA(versionId)
    } else if (!selectedB && versionId !== selectedA) {
      setSelectedB(versionId)
    } else {
      // Reset selection and start over
      setSelectedA(versionId)
      setSelectedB(null)
    }
  }

  const clearDiff = () => {
    setSelectedA(null)
    setSelectedB(null)
  }

  return (
    <div className="flex flex-col gap-6">
      {/* Header */}
      <div className="flex items-center gap-3 flex-wrap">
        <Link to={`/workflows/${id}`}>
          <Button variant="ghost" size="sm">
            &larr; Editar
          </Button>
        </Link>
        <Link to="/workflows">
          <Button variant="ghost" size="sm">
            Workflows
          </Button>
        </Link>
        <div className="flex-1">
          <h1 className="text-2xl font-bold text-text-primary">
            Versoes: {workflow?.name ?? id}
          </h1>
          <p className="text-sm text-text-muted mt-1">
            Historico de versoes do workflow. Selecione duas versoes para comparar.
          </p>
        </div>
        {(selectedA || selectedB) && (
          <Button variant="ghost" size="sm" onClick={clearDiff}>
            Limpar selecao
          </Button>
        )}
      </div>

      {/* Diff help banner */}
      {selectedA && !selectedB && (
        <div className="bg-accent-blue/10 border border-accent-blue/30 rounded-lg px-4 py-2 text-sm text-accent-blue">
          Versao <strong>{selectedA}</strong> selecionada. Escolha outra versao para comparar.
        </div>
      )}

      {/* Timeline */}
      <Card title={`Historico de Versoes (${sorted.length})`} padding={false}>
        <div className="divide-y divide-border-primary">
          {sorted.length === 0 && (
            <p className="text-sm text-text-dimmed p-6 text-center">
              Nenhuma versao encontrada para este workflow.
            </p>
          )}
          {sorted.map((version, idx) => {
            const isCurrentSelected =
              selectedA === version.versionId || selectedB === version.versionId
            const isCurrent = idx === 0

            return (
              <div
                key={version.versionId}
                className={`
                  flex items-center justify-between px-5 py-4 transition-colors
                  ${isCurrentSelected ? 'bg-accent-blue/5' : 'hover:bg-bg-tertiary/50'}
                `}
              >
                <div className="flex items-center gap-4">
                  {/* Timeline dot */}
                  <div className="flex flex-col items-center self-stretch justify-center">
                    <div
                      className={`w-3 h-3 rounded-full border-2 flex-shrink-0 ${
                        isCurrent
                          ? 'bg-accent-blue border-accent-blue'
                          : isCurrentSelected
                            ? 'bg-blue-400 border-blue-400'
                            : 'bg-bg-tertiary border-border-secondary'
                      }`}
                    />
                  </div>

                  {/* Version info */}
                  <div>
                    <div className="flex items-center gap-2 flex-wrap">
                      <span className="text-sm font-mono font-medium text-text-primary">
                        {version.versionId}
                      </span>
                      {isCurrent && <Badge variant="green">Current</Badge>}
                      {selectedA === version.versionId && (
                        <Badge variant="blue">A</Badge>
                      )}
                      {selectedB === version.versionId && (
                        <Badge variant="purple">B</Badge>
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
                    variant={isCurrentSelected ? 'secondary' : 'ghost'}
                    size="sm"
                    onClick={() => handleSelectForDiff(version.versionId)}
                  >
                    {isCurrentSelected ? 'Selecionado' : 'Comparar'}
                  </Button>
                  {!isCurrent && (
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
            )
          })}
        </div>
      </Card>

      {/* Diff Viewer */}
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

      {selectedA && selectedB && (!versionA || !versionB) && (
        <div className="bg-bg-secondary border border-border-primary rounded-xl p-6 text-center text-sm text-text-muted">
          Carregando snapshot das versoes para comparacao...
        </div>
      )}

      {/* Rollback confirmation */}
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
        message={`Deseja restaurar o workflow para a versao "${rollbackTarget?.versionId}"? A versao atual sera substituida.`}
        confirmLabel="Restaurar"
        loading={rollbackMutation.isPending}
      />
    </div>
  )
}
