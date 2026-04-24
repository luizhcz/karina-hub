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
  usePersonaTemplateVersions,
  useRollbackPersonaTemplate,
} from '../../api/personaTemplates'
import type { PersonaPromptTemplateVersion } from '../../api/personaTemplates'

/**
 * Histórico append-only de versions de um template + rollback.
 * Replica pattern de AgentVersionsPage. Rollback cria nova version com
 * conteúdo da alvo (não pula ponteiro).
 */
export function PersonaTemplateVersionsPage() {
  const { id } = useParams<{ id: string }>()
  const templateId = id ? Number(id) : undefined
  const { data, isLoading, error, refetch } = usePersonaTemplateVersions(templateId)
  const rollbackMutation = useRollbackPersonaTemplate()

  const [rollbackTarget, setRollbackTarget] = useState<PersonaPromptTemplateVersion | null>(null)
  const [selectedA, setSelectedA] = useState<string | null>(null)
  const [selectedB, setSelectedB] = useState<string | null>(null)

  if (isLoading) return <PageLoader />
  if (error || !data) return <ErrorCard message="Erro ao carregar versões." onRetry={refetch} />

  const { template, versions, activeVersionId } = data

  const handleSelectForDiff = (versionId: string) => {
    if (!selectedA) setSelectedA(versionId)
    else if (!selectedB && versionId !== selectedA) setSelectedB(versionId)
    else {
      setSelectedA(versionId)
      setSelectedB(null)
    }
  }

  const versionA = versions.find((v) => v.versionId === selectedA) ?? null
  const versionB = versions.find((v) => v.versionId === selectedB) ?? null

  return (
    <div className="flex flex-col gap-6 p-6">
      <div className="flex items-center gap-3">
        <Link to={`/admin/persona-templates/${template.id}`}>
          <Button variant="ghost" size="sm">
            &larr; {template.name}
          </Button>
        </Link>
        <div>
          <h1 className="text-2xl font-bold text-text-primary">
            Versões: {template.name}
          </h1>
          <p className="text-sm text-text-muted mt-1">
            Histórico append-only. Rollback cria nova version com conteúdo da alvo —
            histórico fica linear e auditável. Selecione duas versões pra comparar.
          </p>
        </div>
      </div>

      <Card title={`Histórico (${versions.length} versões)`} padding={false}>
        <div className="divide-y divide-border-primary">
          {versions.length === 0 && (
            <p className="text-sm text-text-dimmed p-5">Nenhuma versão encontrada.</p>
          )}
          {versions.map((version) => {
            const isActive = version.versionId === activeVersionId
            return (
              <div
                key={version.versionId}
                className="flex items-center justify-between px-5 py-4 hover:bg-bg-tertiary/50 transition-colors"
              >
                <div className="flex items-center gap-4">
                  <div className="flex flex-col items-center">
                    <div
                      className={`w-3 h-3 rounded-full border-2 ${
                        isActive
                          ? 'bg-accent-blue border-accent-blue'
                          : 'bg-bg-tertiary border-border-secondary'
                      }`}
                    />
                  </div>
                  <div>
                    <div className="flex items-center gap-2">
                      <span className="text-sm font-mono text-text-primary">
                        {version.versionId.slice(0, 8)}…
                      </span>
                      {isActive && <Badge variant="green">Ativa</Badge>}
                      {(selectedA === version.versionId || selectedB === version.versionId) && (
                        <Badge variant="blue">Selecionada</Badge>
                      )}
                    </div>
                    <p className="text-xs text-text-muted mt-0.5">
                      {new Date(version.createdAt).toLocaleString('pt-BR')}
                      {version.changeReason && ` — ${version.changeReason}`}
                      {version.createdBy && ` · by ${version.createdBy}`}
                    </p>
                  </div>
                </div>
                <div className="flex items-center gap-2">
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => handleSelectForDiff(version.versionId)}
                  >
                    Comparar
                  </Button>
                  {!isActive && (
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

      {versionA && versionB && (
        <Card title={`Diff: ${versionA.versionId.slice(0, 8)}… vs ${versionB.versionId.slice(0, 8)}…`}>
          <DiffViewer
            oldValue={versionA.template}
            newValue={versionB.template}
            oldTitle={versionA.versionId.slice(0, 8)}
            newTitle={versionB.versionId.slice(0, 8)}
            splitView
          />
        </Card>
      )}

      <ConfirmDialog
        open={!!rollbackTarget}
        onClose={() => setRollbackTarget(null)}
        onConfirm={() => {
          if (rollbackTarget && templateId !== undefined) {
            rollbackMutation.mutate(
              { id: templateId, versionId: rollbackTarget.versionId },
              { onSuccess: () => setRollbackTarget(null) },
            )
          }
        }}
        title="Rollback de versão"
        message={`Criar nova version com o conteúdo de "${rollbackTarget?.versionId.slice(0, 8)}…"? A versão atual fica no histórico.`}
        confirmLabel="Restaurar"
        loading={rollbackMutation.isPending}
      />
    </div>
  )
}
