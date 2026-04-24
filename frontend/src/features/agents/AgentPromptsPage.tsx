import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { Button } from '../../shared/ui/Button'
import { Card } from '../../shared/ui/Card'
import { Badge } from '../../shared/ui/Badge'
import { Input } from '../../shared/ui/Input'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { MonacoEditor } from '../../shared/editors/MonacoEditor'
import { useAgent } from '../../api/agents'
import {
  usePromptVersions,
  useSavePromptVersion,
  useSetMasterPrompt,
  useDeletePromptVersion,
} from '../../api/prompts'
import type { AgentPromptVersion } from '../../api/prompts'

export function AgentPromptsPage() {
  const { id } = useParams<{ id: string }>()
  const { data: agent } = useAgent(id!, !!id)
  const { data: versions, isLoading, error, refetch } = usePromptVersions(id!, !!id)
  const saveMutation = useSavePromptVersion()
  const setMasterMutation = useSetMasterPrompt()
  const deleteMutation = useDeletePromptVersion()

  const [editorContent, setEditorContent] = useState('')
  const [newVersionId, setNewVersionId] = useState('')
  const [selectedVersion, setSelectedVersion] = useState<AgentPromptVersion | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<AgentPromptVersion | null>(null)
  const [activateTarget, setActivateTarget] = useState<AgentPromptVersion | null>(null)

  const handleSave = () => {
    if (!id || !newVersionId.trim() || !editorContent.trim()) return
    saveMutation.mutate(
      { agentId: id, body: { versionId: newVersionId, content: editorContent } },
      {
        onSuccess: () => {
          setNewVersionId('')
          setEditorContent('')
        },
      },
    )
  }

  const handleSelectVersion = (version: AgentPromptVersion) => {
    setSelectedVersion(version)
    setEditorContent(version.content)
    setNewVersionId('')
  }

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar prompts." onRetry={refetch} />

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center gap-3">
        <Link to="/agents">
          <Button variant="ghost" size="sm">
            &larr; Agentes
          </Button>
        </Link>
        <div>
          <h1 className="text-2xl font-bold text-text-primary">
            Prompts: {agent?.name ?? id}
          </h1>
          <p className="text-sm text-text-muted mt-1">
            Gerencie as versoes de prompt do agente.
          </p>
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        <Card title="Versoes" padding={false} className="lg:col-span-1">
          <div className="divide-y divide-border-primary max-h-[600px] overflow-y-auto">
            {(!versions || versions.length === 0) && (
              <p className="text-sm text-text-dimmed p-5">Nenhuma versao de prompt.</p>
            )}
            {versions?.map((version) => (
              <div
                key={version.versionId}
                className={`flex items-center justify-between px-4 py-3 cursor-pointer hover:bg-bg-tertiary/50 transition-colors ${
                  selectedVersion?.versionId === version.versionId ? 'bg-bg-tertiary' : ''
                }`}
                onClick={() => handleSelectVersion(version)}
              >
                <div className="flex items-center gap-2 min-w-0">
                  <span className="text-sm font-medium text-text-primary truncate">
                    {version.versionId}
                  </span>
                  {version.isActive && <Badge variant="green">Active</Badge>}
                </div>
                <div className="flex items-center gap-1 shrink-0">
                  {!version.isActive && (
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={(e) => {
                        e.stopPropagation()
                        setActivateTarget(version)
                      }}
                    >
                      Ativar
                    </Button>
                  )}
                  <Button
                    variant="danger"
                    size="sm"
                    onClick={(e) => {
                      e.stopPropagation()
                      setDeleteTarget(version)
                    }}
                  >
                    Del
                  </Button>
                </div>
              </div>
            ))}
          </div>
        </Card>

        <Card title="Editor" className="lg:col-span-2">
          <div className="flex flex-col gap-4">
            <div className="flex gap-3 items-end">
              <div className="flex-1">
                <Input
                  label="Version ID (para nova versao)"
                  value={newVersionId}
                  onChange={(e) => setNewVersionId(e.target.value)}
                  placeholder="v2.0"
                  disabled={!!selectedVersion}
                />
              </div>
              <Button
                onClick={handleSave}
                loading={saveMutation.isPending}
                disabled={(!newVersionId.trim() && !selectedVersion) || !editorContent.trim()}
              >
                Salvar Nova Versao
              </Button>
              {selectedVersion && (
                <Button
                  variant="secondary"
                  onClick={() => {
                    setSelectedVersion(null)
                    setEditorContent('')
                    setNewVersionId('')
                  }}
                >
                  Limpar
                </Button>
              )}
            </div>

            <MonacoEditor
              value={editorContent}
              onChange={setEditorContent}
              language="markdown"
              height="400px"
            />
          </div>
        </Card>
      </div>

      <ConfirmDialog
        open={!!activateTarget}
        onClose={() => setActivateTarget(null)}
        onConfirm={() => {
          if (activateTarget && id) {
            setMasterMutation.mutate(
              { agentId: id, versionId: activateTarget.versionId },
              { onSuccess: () => setActivateTarget(null) },
            )
          }
        }}
        title="Ativar Versao"
        message={`Deseja ativar a versao "${activateTarget?.versionId}" como master?`}
        confirmLabel="Ativar"
        loading={setMasterMutation.isPending}
      />

      <ConfirmDialog
        open={!!deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onConfirm={() => {
          if (deleteTarget && id) {
            deleteMutation.mutate(
              { agentId: id, versionId: deleteTarget.versionId },
              { onSuccess: () => setDeleteTarget(null) },
            )
          }
        }}
        title="Excluir Versao"
        message={`Tem certeza que deseja excluir a versao "${deleteTarget?.versionId}"?`}
        confirmLabel="Excluir"
        variant="danger"
        loading={deleteMutation.isPending}
      />
    </div>
  )
}
