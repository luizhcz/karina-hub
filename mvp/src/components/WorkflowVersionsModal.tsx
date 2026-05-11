import { useEffect, useState } from 'react'
import { friendlyError } from '../api/client'
import {
  listWorkflowVersions,
  rollbackWorkflow,
  type Workflow,
  type WorkflowVersion,
} from '../api/workflows'
import { Badge, Button, ErrorMessage, Modal, Spinner, cn } from '../ui'

// Modal compartilhado pra timeline de versões + rollback de workflows.
// Funciona pra qualquer deploy kind (single, pipeline, routing, chat) — só
// precisa do Workflow carregado e de um callback pra propagar o pós-rollback.

interface WorkflowVersionsModalProps {
  open: boolean
  workflow: Workflow | null
  onClose: () => void
  onRolledBack: (workflow: Workflow) => void
}

export function WorkflowVersionsModal({
  open,
  workflow,
  onClose,
  onRolledBack,
}: WorkflowVersionsModalProps) {
  const [versions, setVersions] = useState<WorkflowVersion[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [rollingBackId, setRollingBackId] = useState<string | null>(null)
  const [confirm, setConfirm] = useState<WorkflowVersion | null>(null)

  useEffect(() => {
    if (!open || !workflow) return
    let cancelled = false
    setLoading(true)
    setError(null)
    setVersions([])
    listWorkflowVersions(workflow.id)
      .then((list) => {
        if (!cancelled) setVersions(list)
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(friendlyError(err, 'Não foi possível carregar as versões.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [open, workflow])

  const performRollback = async (target: WorkflowVersion) => {
    if (!workflow) return
    setRollingBackId(target.workflowVersionId)
    setError(null)
    try {
      const wf = await rollbackWorkflow(workflow.id, target.workflowVersionId)
      onRolledBack(wf)
      // Recarrega timeline (rollback gerou nova revision com mesmo conteúdo).
      const refreshed = await listWorkflowVersions(workflow.id)
      setVersions(refreshed)
      setConfirm(null)
    } catch (err) {
      setError(friendlyError(err, 'Não foi possível restaurar essa versão.'))
    } finally {
      setRollingBackId(null)
    }
  }

  return (
    <>
      <Modal
        open={open}
        onClose={onClose}
        size="lg"
        title="Versões do workflow"
        description={workflow?.name}
      >
        <p className="mb-4 text-xs text-fg-muted">
          Timeline da definição do <strong>workflow</strong> (independente da timeline dos agentes
          que ele referencia). Cada edição cria uma nova revisão; <strong>Restaurar</strong> volta o
          estado em runtime para o snapshot escolhido — o histórico permanece intacto.
        </p>
        {loading && (
          <div className="flex items-center justify-center py-8">
            <Spinner className="h-6 w-6 text-fg-muted" />
          </div>
        )}
        {!loading && error && <ErrorMessage message={error} />}
        {!loading && !error && versions.length === 0 && (
          <p className="py-6 text-center text-sm text-fg-muted">Nenhuma versão registrada.</p>
        )}
        {!loading && !error && versions.length > 0 && (
          <ol className="relative space-y-3 border-l border-border pl-5">
            {versions.map((v) => {
              const isCurrent = workflow?.currentVersionId === v.workflowVersionId
              return (
                <li key={v.workflowVersionId} className="relative">
                  <span
                    className={cn(
                      'absolute -left-[27px] top-1.5 h-3 w-3 rounded-full ring-2 ring-surface',
                      isCurrent ? 'bg-success' : 'bg-fg-muted',
                    )}
                    aria-hidden="true"
                  />
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="font-mono text-sm font-semibold text-fg">r{v.revision}</span>
                    {isCurrent && <Badge tone="success">atual</Badge>}
                    <span className="font-mono text-[10px] uppercase tracking-wider text-fg-dim">
                      {v.contentHash.slice(0, 12)}
                    </span>
                    <span className="text-[11px] text-fg-dim">{formatAbsolute(v.createdAt)}</span>
                  </div>
                  {(v.createdBy || v.changeReason) && (
                    <p className="mt-1 text-xs text-fg-muted">
                      {v.createdBy && (
                        <>
                          por <span className="font-mono text-fg">{v.createdBy}</span>
                        </>
                      )}
                      {v.changeReason && (
                        <>
                          {v.createdBy && ' · '}
                          {v.changeReason}
                        </>
                      )}
                    </p>
                  )}
                  {!isCurrent && (
                    <div className="mt-2">
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => setConfirm(v)}
                        loading={rollingBackId === v.workflowVersionId}
                      >
                        Restaurar
                      </Button>
                    </div>
                  )}
                </li>
              )
            })}
          </ol>
        )}
      </Modal>

      <Modal
        open={confirm !== null}
        onClose={() => rollingBackId === null && setConfirm(null)}
        title="Restaurar versão"
        description={confirm ? `Voltar pra r${confirm.revision}` : undefined}
        footer={
          <div className="flex items-center justify-end gap-2">
            <Button
              variant="ghost"
              onClick={() => setConfirm(null)}
              disabled={rollingBackId !== null}
            >
              Cancelar
            </Button>
            <Button
              onClick={() => confirm && performRollback(confirm)}
              loading={rollingBackId !== null}
            >
              Restaurar r{confirm?.revision}
            </Button>
          </div>
        }
      >
        <p className="text-sm text-fg-muted">
          O rollback é <strong>append-only</strong> — não apaga as revisões intermediárias. Cria uma
          nova revisão com o conteúdo idêntico à versão alvo, que passa a ser a atual em runtime.
        </p>
      </Modal>
    </>
  )
}

function formatAbsolute(iso: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return iso
  return date.toLocaleString('pt-BR', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}
