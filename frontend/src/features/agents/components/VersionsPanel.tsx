import { useState } from 'react'
import { Button } from '../../../shared/ui/Button'
import { Card } from '../../../shared/ui/Card'
import { Badge } from '../../../shared/ui/Badge'
import { ConfirmDialog } from '../../../shared/ui/ConfirmDialog'
import { DiffViewer } from '../../../shared/data/DiffViewer'
import {
  useAgentVersions,
  useAgentVersion,
  useRollbackAgent,
} from '../../../api/agents'
import type { AgentVersion } from '../../../api/agents'

interface VersionsPanelProps {
  agentId: string
}

export function VersionsPanel({ agentId }: VersionsPanelProps) {
  const { data: versions, isLoading, error, refetch } = useAgentVersions(agentId)
  const rollbackMutation = useRollbackAgent()

  const [rollbackTarget, setRollbackTarget] = useState<AgentVersion | null>(null)
  const [selectedA, setSelectedA] = useState<string | null>(null)
  const [selectedB, setSelectedB] = useState<string | null>(null)

  const { data: versionA } = useAgentVersion(agentId, selectedA ?? '', !!selectedA)
  const { data: versionB } = useAgentVersion(agentId, selectedB ?? '', !!selectedB)

  if (isLoading) {
    return <div className="flex items-center justify-center py-20 text-text-muted text-sm">Carregando versoes...</div>
  }
  if (error) {
    return (
      <div className="flex flex-col items-center justify-center py-20 gap-3">
        <p className="text-sm text-red-400">Erro ao carregar versoes.</p>
        <Button variant="secondary" size="sm" onClick={() => refetch()}>Tentar novamente</Button>
      </div>
    )
  }

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
      {/* Timeline */}
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

      {/* Rollback confirmation */}
      <ConfirmDialog
        open={!!rollbackTarget}
        onClose={() => setRollbackTarget(null)}
        onConfirm={() => {
          if (rollbackTarget) {
            rollbackMutation.mutate(
              { id: agentId, body: { versionId: rollbackTarget.versionId } },
              { onSuccess: () => setRollbackTarget(null) },
            )
          }
        }}
        title="Rollback de Versao"
        message={`Deseja restaurar o agente para a versao "${rollbackTarget?.versionId}"?`}
        confirmLabel="Restaurar"
        loading={rollbackMutation.isPending}
      />
    </div>
  )
}
