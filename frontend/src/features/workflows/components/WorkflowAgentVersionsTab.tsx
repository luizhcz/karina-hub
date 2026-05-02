import { useState } from 'react'
import { Badge } from '../../../shared/ui/Badge'
import { Button } from '../../../shared/ui/Button'
import { Card } from '../../../shared/ui/Card'
import { ErrorCard } from '../../../shared/ui/ErrorCard'
import { Modal } from '../../../shared/ui/Modal'
import { PageLoader } from '../../../shared/ui/LoadingSpinner'
import { ApiError } from '../../../api/client'
import {
  useUpdateWorkflowAgentPin,
  useWorkflowAgentVersionStatus,
  type WorkflowAgentVersionChangeEntry,
  type WorkflowAgentVersionStatus,
} from '../../../api/workflows'
import { toast } from '../../../stores/toast'

interface WorkflowAgentVersionsTabProps {
  workflowId: string
}

export function WorkflowAgentVersionsTab({ workflowId }: WorkflowAgentVersionsTabProps) {
  const { data, isLoading, error, refetch } = useWorkflowAgentVersionStatus(workflowId)
  const [diffOpen, setDiffOpen] = useState<WorkflowAgentVersionStatus | null>(null)
  const updateMutation = useUpdateWorkflowAgentPin()

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar status de pins." onRetry={refetch} />
  if (!data) return null

  if (data.length === 0) {
    return (
      <div className="text-sm text-text-muted px-4 py-8 text-center">
        Workflow não tem agent refs.
      </div>
    )
  }

  // Atalho pra fixar versão sem precisar abrir DiffModal — usado em workflow legacy
  // (sem pin, agent só tem rev 1) ou quando pin == current mas o user quer reafirmar.
  const handleQuickPin = (status: WorkflowAgentVersionStatus) => {
    if (!status.currentVersionId) return
    updateMutation.mutate(
      {
        workflowId,
        agentId: status.agentId,
        body: { newVersionId: status.currentVersionId },
      },
      {
        onSuccess: () => toast.success(`Pin fixado em rev ${status.currentRevision}.`),
        onError: (err) => {
          const msg = err instanceof ApiError ? err.message : 'Erro ao fixar pin.'
          toast.error(msg)
        },
      },
    )
  }

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-text-secondary">
        Estado de pin de cada agent referenciado neste workflow. Pins permitem
        estabilidade contra novos publishes do owner — patches propagam
        automaticamente; breaking changes ficam presos no pin até revisão manual.
      </p>

      <div className="flex flex-col gap-3">
        {data.map((status) => (
          <AgentVersionCard
            key={status.agentId}
            status={status}
            onOpenDiff={() => setDiffOpen(status)}
            onQuickPin={() => handleQuickPin(status)}
            quickPinPending={updateMutation.isPending}
          />
        ))}
      </div>

      <DiffModal
        status={diffOpen}
        workflowId={workflowId}
        onClose={() => setDiffOpen(null)}
      />
    </div>
  )
}

interface AgentVersionCardProps {
  status: WorkflowAgentVersionStatus
  onOpenDiff: () => void
  onQuickPin: () => void
  quickPinPending: boolean
}

function AgentVersionCard({ status, onOpenDiff, onQuickPin, quickPinPending }: AgentVersionCardProps) {
  const updateBadge = renderUpdateBadge(status)
  const hasChanges = status.changes.length > 0
  const canQuickPin =
    !!status.currentVersionId && status.pinnedVersionId !== status.currentVersionId

  return (
    <Card>
      <div className="flex items-start justify-between gap-4">
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 flex-wrap">
            <h3 className="font-semibold text-text-primary truncate">
              {status.agentName ?? status.agentId}
            </h3>
            {status.isAgentDisabled && <Badge variant="red">Desabilitado</Badge>}
            {updateBadge}
          </div>
          <p className="text-xs text-text-muted mt-1 font-mono">
            ID: {status.agentId}
          </p>
          {status.isAgentDisabled && (
            <p className="text-xs text-red-400 mt-2">
              Este agente está desligado pelo owner — runtime vai pulá-lo na execução do workflow.
            </p>
          )}

          <div className="grid grid-cols-2 gap-4 mt-3 text-sm">
            <PinSummary
              label="Pin atual"
              versionId={status.pinnedVersionId}
              revision={status.pinnedRevision}
              fallback="—"
            />
            <PinSummary
              label="Current Published"
              versionId={status.currentVersionId}
              revision={status.currentRevision}
              fallback="sem versão publicada"
            />
          </div>
        </div>

        <div className="flex flex-col items-end gap-2 flex-shrink-0">
          {hasChanges && (
            <Button variant="secondary" size="sm" onClick={onOpenDiff}>
              Revisar mudanças ({status.changes.length})
            </Button>
          )}
          {canQuickPin && (
            <Button
              variant={hasChanges ? 'ghost' : 'primary'}
              size="sm"
              onClick={onQuickPin}
              disabled={quickPinPending}
            >
              {quickPinPending
                ? 'Fixando...'
                : `Fixar em rev ${status.currentRevision}`}
            </Button>
          )}
        </div>
      </div>
    </Card>
  )
}

function renderUpdateBadge(status: WorkflowAgentVersionStatus) {
  // Pin é obrigatório global no save; pinnedVersionId nunca é null pra workflows persistidos.
  // hasUpdate=false → pin == current ou pin > current (rollback do owner).
  if (!status.hasUpdate) return <Badge variant="green">Atualizado</Badge>
  if (status.isPinnedBlockedByBreaking) {
    return <Badge variant="yellow">Atualização bloqueada — breaking change</Badge>
  }
  return <Badge variant="green">Patch disponível (auto)</Badge>
}

interface PinSummaryProps {
  label: string
  versionId?: string | null
  revision?: number | null
  fallback: string
}

function PinSummary({ label, versionId, revision, fallback }: PinSummaryProps) {
  return (
    <div>
      <p className="text-xs text-text-muted uppercase tracking-wide mb-1">{label}</p>
      {versionId ? (
        <>
          <p className="text-sm text-text-primary">rev {revision}</p>
          <p className="text-[11px] text-text-dimmed font-mono truncate" title={versionId}>
            {versionId.slice(0, 16)}…
          </p>
        </>
      ) : (
        <p className="text-sm text-text-dimmed italic">{fallback}</p>
      )}
    </div>
  )
}

interface DiffModalProps {
  status: WorkflowAgentVersionStatus | null
  workflowId: string
  onClose: () => void
}

function DiffModal({ status, workflowId, onClose }: DiffModalProps) {
  const updateMutation = useUpdateWorkflowAgentPin()

  if (!status) return null

  const targetVersionId = status.currentVersionId
  const canMigrate = !!targetVersionId && status.hasUpdate

  const handleMigrate = () => {
    if (!targetVersionId) return
    updateMutation.mutate(
      {
        workflowId,
        agentId: status.agentId,
        body: { newVersionId: targetVersionId },
      },
      {
        onSuccess: () => {
          toast.success(`Pin migrado para rev ${status.currentRevision}.`)
          onClose()
        },
        onError: (err) => {
          const msg = err instanceof ApiError ? err.message : 'Erro ao migrar pin.'
          toast.error(msg)
        },
      },
    )
  }

  return (
    <Modal open={status !== null} onClose={onClose} title={`Mudanças: ${status.agentName ?? status.agentId}`} size="lg">
      <div className="flex flex-col gap-4">
        <div className="text-sm text-text-secondary">
          Versions publicadas entre o pin atual ({renderRevPair(status.pinnedRevision)}) e
          o current ({renderRevPair(status.currentRevision)}).
          {status.isPinnedBlockedByBreaking && (
            <span className="ml-2 text-yellow-400 font-medium">
              ⚠ Há breaking change — patches não propagam automaticamente.
            </span>
          )}
        </div>

        <div className="flex flex-col gap-2 max-h-96 overflow-y-auto">
          {status.changes.map((change) => (
            <ChangeRow key={change.agentVersionId} change={change} />
          ))}
        </div>

        <div className="flex justify-end gap-2 pt-2 border-t border-border-primary">
          <Button variant="ghost" size="sm" onClick={onClose}>
            Fechar
          </Button>
          {canMigrate && (
            <Button
              variant="primary"
              size="sm"
              onClick={handleMigrate}
              disabled={updateMutation.isPending}
            >
              {updateMutation.isPending ? 'Migrando...' : `Migrar para rev ${status.currentRevision}`}
            </Button>
          )}
        </div>
      </div>
    </Modal>
  )
}

function ChangeRow({ change }: { change: WorkflowAgentVersionChangeEntry }) {
  return (
    <div className="flex items-start gap-3 px-3 py-2 rounded-md border border-border-primary bg-bg-tertiary">
      <div className="flex-shrink-0 mt-0.5">
        {change.breakingChange ? (
          <Badge variant="yellow">breaking</Badge>
        ) : (
          <Badge variant="green">patch</Badge>
        )}
      </div>
      <div className="flex-1 min-w-0">
        <div className="flex items-center gap-2 text-xs text-text-muted">
          <span>rev {change.revision}</span>
          <span>·</span>
          <span>{new Date(change.createdAt).toLocaleString()}</span>
          {change.createdBy && (
            <>
              <span>·</span>
              <span>{change.createdBy}</span>
            </>
          )}
        </div>
        <p className="text-sm text-text-primary mt-0.5">
          {change.changeReason ?? <span className="italic text-text-dimmed">sem changeReason</span>}
        </p>
      </div>
    </div>
  )
}

function renderRevPair(rev?: number | null) {
  return rev !== null && rev !== undefined ? `rev ${rev}` : 'sem versão'
}
