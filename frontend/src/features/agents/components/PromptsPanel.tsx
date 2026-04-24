import { useState, useEffect } from 'react'
import { Button } from '../../../shared/ui/Button'
import { Card } from '../../../shared/ui/Card'
import { Badge } from '../../../shared/ui/Badge'
import { Input } from '../../../shared/ui/Input'
import { Select } from '../../../shared/ui/Select'
import { ConfirmDialog } from '../../../shared/ui/ConfirmDialog'
import { MonacoEditor } from '../../../shared/editors/MonacoEditor'
import { DiffViewer } from '../../../shared/data/DiffViewer'
import {
  usePromptVersions,
  useSavePromptVersion,
  useSetMasterPrompt,
  useClearMasterPrompt,
  useDeletePromptVersion,
} from '../../../api/prompts'
import type { AgentPromptVersion } from '../../../api/prompts'

type Mode = 'editor' | 'compare'

interface PromptsPanelProps {
  agentId: string
  currentInstructions?: string
}

export function PromptsPanel({ agentId, currentInstructions }: PromptsPanelProps) {
  const { data: versions, isLoading, error, refetch } = usePromptVersions(agentId)
  const saveMutation = useSavePromptVersion()
  const setMasterMutation = useSetMasterPrompt()
  const clearMasterMutation = useClearMasterPrompt()
  const deleteMutation = useDeletePromptVersion()

  const [mode, setMode] = useState<Mode>('editor')
  const [editorContent, setEditorContent] = useState('')
  const [editorInitialized, setEditorInitialized] = useState(false)
  const [newVersionId, setNewVersionId] = useState('')
  const [selectedVersion, setSelectedVersion] = useState<AgentPromptVersion | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<AgentPromptVersion | null>(null)
  const [activateTarget, setActivateTarget] = useState<AgentPromptVersion | null>(null)
  const [showRestoreOriginal, setShowRestoreOriginal] = useState(false)

  // Comparison mode state
  const [checked, setChecked] = useState<Set<string>>(new Set())
  const [compareA, setCompareA] = useState('')
  const [compareB, setCompareB] = useState('')

  const ORIGINAL_ID = '__original__'

  // Auto-load: select the active version, or the original instructions
  useEffect(() => {
    if (editorInitialized || isLoading) return
    const active = versions?.find((v) => v.isActive)
    if (active) {
      setSelectedVersion(active)
      setEditorContent(active.content)
    } else if (currentInstructions) {
      setSelectedVersion({ versionId: ORIGINAL_ID, content: currentInstructions, isActive: true })
      setEditorContent(currentInstructions)
    }
    setEditorInitialized(true)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [versions, isLoading])

  const handleSave = () => {
    if (!newVersionId.trim() || !editorContent.trim()) return
    saveMutation.mutate(
      { agentId, body: { versionId: newVersionId, content: editorContent } },
      {
        onSuccess: () => {
          setNewVersionId('')
          setEditorContent('')
          setSelectedVersion(null)
        },
      },
    )
  }

  const handleSelectVersion = (version: AgentPromptVersion) => {
    setSelectedVersion(version)
    setEditorContent(version.content)
  }

  const toggleCheck = (versionId: string) => {
    setChecked((prev) => {
      const next = new Set(prev)
      if (next.has(versionId)) {
        next.delete(versionId)
      } else {
        if (next.size >= 2) {
          // Replace oldest selection
          const first = next.values().next().value as string
          next.delete(first)
        }
        next.add(versionId)
      }
      return next
    })
  }

  const enterCompare = () => {
    const ids = Array.from(checked)
    if (ids.length === 2) {
      setCompareA(ids[0])
      setCompareB(ids[1])
      setMode('compare')
    }
  }

  const exitCompare = () => {
    setMode('editor')
    setChecked(new Set())
  }

  // Build a unified list: original instructions (if any) + saved versions
  const hasOriginal = !!currentInstructions
  const activeVersion = versions?.find((v) => v.isActive)
  const noVersionsActive = !activeVersion

  const allEntries: AgentPromptVersion[] = [
    // Original instructions entry — always first
    ...(hasOriginal
      ? [{
          versionId: ORIGINAL_ID,
          content: currentInstructions!,
          isActive: noVersionsActive, // active only if no version is explicitly active
        }]
      : []),
    ...(versions ?? []),
  ]

  const versionOptions = allEntries.map((v) => ({
    value: v.versionId,
    label: v.versionId === ORIGINAL_ID
      ? `Original${noVersionsActive ? ' (Ativo)' : ''}`
      : `${v.versionId}${v.isActive ? ' (Ativo)' : ''}`,
  }))

  const findEntry = (id: string) => allEntries.find((v) => v.versionId === id)
  const versionAData = findEntry(compareA)
  const versionBData = findEntry(compareB)

  if (isLoading) {
    return <div className="flex items-center justify-center py-20 text-text-muted text-sm">Carregando prompts...</div>
  }
  if (error) {
    return (
      <div className="flex flex-col items-center justify-center py-20 gap-3">
        <p className="text-sm text-red-400">Erro ao carregar prompts.</p>
        <Button variant="secondary" size="sm" onClick={() => refetch()}>Tentar novamente</Button>
      </div>
    )
  }

  if (mode === 'compare') {
    return (
      <div className="flex flex-col gap-4">
        <div className="flex items-center justify-between">
          <h2 className="text-lg font-semibold text-text-primary">Comparar Prompts</h2>
          <Button variant="secondary" size="sm" onClick={exitCompare}>
            &larr; Voltar ao Editor
          </Button>
        </div>

        <div className="grid grid-cols-2 gap-4">
          <Select
            label="Versao A"
            options={versionOptions}
            value={compareA}
            onChange={(e) => setCompareA(e.target.value)}
            placeholder="Selecione..."
          />
          <Select
            label="Versao B"
            options={versionOptions}
            value={compareB}
            onChange={(e) => setCompareB(e.target.value)}
            placeholder="Selecione..."
          />
        </div>

        {versionAData && versionBData ? (
          <DiffViewer
            oldValue={versionAData.content}
            newValue={versionBData.content}
            oldTitle={compareA}
            newTitle={compareB}
            splitView
          />
        ) : (
          <div className="flex items-center justify-center py-16 text-sm text-text-muted border border-border-primary rounded-lg">
            Selecione duas versoes para comparar.
          </div>
        )}
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-4">
      {checked.size === 2 && (
        <div className="flex items-center gap-3 px-4 py-2.5 bg-accent-blue/10 border border-accent-blue/30 rounded-lg">
          <span className="text-sm text-accent-blue flex-1">
            {Array.from(checked).join(' e ')} selecionados para comparacao.
          </span>
          <Button size="sm" onClick={enterCompare}>Comparar</Button>
        </div>
      )}

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        <Card title="Versoes" padding={false} className="lg:col-span-1">
          <div className="divide-y divide-border-primary max-h-[500px] overflow-y-auto">
            {allEntries.length === 0 && (
              <p className="text-sm text-text-dimmed p-5">Nenhum prompt configurado.</p>
            )}
            {allEntries.map((version) => {
              const isOriginal = version.versionId === ORIGINAL_ID
              return (
                <div
                  key={version.versionId}
                  className={`flex items-center gap-2 px-3 py-2.5 cursor-pointer hover:bg-bg-tertiary/50 transition-colors ${
                    selectedVersion?.versionId === version.versionId ? 'bg-bg-tertiary' : ''
                  }`}
                  onClick={() => handleSelectVersion(version)}
                >
                  <input
                    type="checkbox"
                    checked={checked.has(version.versionId)}
                    onChange={(e) => {
                      e.stopPropagation()
                      toggleCheck(version.versionId)
                    }}
                    onClick={(e) => e.stopPropagation()}
                    className="w-3.5 h-3.5 rounded border-border-secondary accent-accent-blue shrink-0"
                  />

                  <div className="flex items-center gap-2 min-w-0 flex-1">
                    <span className="text-sm font-medium text-text-primary truncate">
                      {isOriginal ? 'Original' : version.versionId}
                    </span>
                    {version.isActive && <Badge variant="green">Ativo</Badge>}
                    {isOriginal && <Badge variant="gray">Base</Badge>}
                  </div>

                  <div className="flex items-center gap-1 shrink-0">
                    {isOriginal && !version.isActive && (
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={(e) => {
                          e.stopPropagation()
                          setShowRestoreOriginal(true)
                        }}
                      >
                        Ativar
                      </Button>
                    )}
                    {!isOriginal && !version.isActive && (
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
                    {!isOriginal && (
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
                    )}
                  </div>
                </div>
              )
            })}
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
                />
              </div>
              <Button
                onClick={handleSave}
                loading={saveMutation.isPending}
                disabled={!newVersionId.trim() || !editorContent.trim()}
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
          if (activateTarget) {
            setMasterMutation.mutate(
              { agentId, versionId: activateTarget.versionId },
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
          if (deleteTarget) {
            deleteMutation.mutate(
              { agentId, versionId: deleteTarget.versionId },
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

      <ConfirmDialog
        open={showRestoreOriginal}
        onClose={() => setShowRestoreOriginal(false)}
        onConfirm={() => {
          clearMasterMutation.mutate(
            { agentId },
            { onSuccess: () => setShowRestoreOriginal(false) },
          )
        }}
        title="Restaurar Prompt Original"
        message="Deseja desativar todas as versoes e voltar ao prompt original do agente?"
        confirmLabel="Restaurar"
        loading={clearMasterMutation.isPending}
      />
    </div>
  )
}
