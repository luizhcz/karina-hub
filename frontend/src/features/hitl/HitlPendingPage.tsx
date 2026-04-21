import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router'
import { Badge } from '../../shared/ui/Badge'
import { Button } from '../../shared/ui/Button'
import { Card } from '../../shared/ui/Card'
import { Modal } from '../../shared/ui/Modal'
import { Input } from '../../shared/ui/Input'
import { Textarea } from '../../shared/ui/Textarea'
import { EmptyState } from '../../shared/ui/EmptyState'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
import {
  usePendingInteractions,
  useResolveInteraction,
  type HumanInteraction,
} from '../../api/interactions'
import { useUserStore } from '../../stores/user'

// ── Resolve Modal ─────────────────────────────────────────────────────────────

interface ResolveModalProps {
  interaction: HumanInteraction | null
  onClose: () => void
  onResolved: () => void
}

function ResolveModal({ interaction, onClose, onResolved }: ResolveModalProps) {
  const { userId } = useUserStore()
  const [response, setResponse] = useState('')
  const [resolvedBy, setResolvedBy] = useState(userId)
  const [approved, setApproved] = useState(true)
  const resolveInteraction = useResolveInteraction()

  useEffect(() => {
    if (interaction) {
      setResponse('')
      setResolvedBy(userId)
      setApproved(true)
    }
  }, [interaction, userId])

  const handleConfirm = () => {
    if (!interaction || !response.trim()) return
    resolveInteraction.mutate(
      {
        id: interaction.interactionId,
        body: { response: response.trim(), approved },
      },
      {
        onSuccess: () => {
          onResolved()
          onClose()
        },
      }
    )
  }

  return (
    <Modal
      open={!!interaction}
      onClose={onClose}
      title="Resolver Interação"
      size="md"
    >
      {interaction && (
        <div className="flex flex-col gap-4">
          <div className="bg-bg-tertiary rounded-lg p-3">
            <p className="text-xs text-text-muted mb-1">Prompt</p>
            <p className="text-sm text-text-primary">{interaction.prompt}</p>
          </div>

          <Textarea
            label="Resposta"
            value={response}
            onChange={(e) => setResponse(e.target.value)}
            rows={3}
            placeholder="Digite sua resposta..."
          />

          <Input
            label="Resolvido por"
            value={resolvedBy}
            onChange={(e) => setResolvedBy(e.target.value)}
            placeholder="ID do resolvedor"
          />

          <div className="flex items-center gap-3">
            <span className="text-sm text-text-muted">Decisão:</span>
            <button
              type="button"
              onClick={() => setApproved(true)}
              className={`px-3 py-1.5 rounded-lg text-sm font-medium transition-colors ${
                approved
                  ? 'bg-green-500/20 text-green-400 border border-green-500/30'
                  : 'bg-bg-tertiary text-text-muted border border-border-primary hover:border-green-500/30'
              }`}
            >
              Aprovar
            </button>
            <button
              type="button"
              onClick={() => setApproved(false)}
              className={`px-3 py-1.5 rounded-lg text-sm font-medium transition-colors ${
                !approved
                  ? 'bg-red-500/20 text-red-400 border border-red-500/30'
                  : 'bg-bg-tertiary text-text-muted border border-border-primary hover:border-red-500/30'
              }`}
            >
              Rejeitar
            </button>
          </div>

          {resolveInteraction.isError && (
            <p className="text-sm text-red-400">Erro ao resolver interação. Tente novamente.</p>
          )}

          <div className="flex gap-2 justify-end pt-2">
            <Button variant="secondary" size="sm" onClick={onClose}>
              Cancelar
            </Button>
            <Button
              variant="primary"
              size="sm"
              onClick={handleConfirm}
              loading={resolveInteraction.isPending}
              disabled={!response.trim()}
            >
              Confirmar Resolução
            </Button>
          </div>
        </div>
      )}
    </Modal>
  )
}

// ── Interaction Card ──────────────────────────────────────────────────────────

interface InteractionCardProps {
  interaction: HumanInteraction
  onResolve: (interaction: HumanInteraction) => void
  onCancel: (interaction: HumanInteraction) => void
}

function InteractionCard({ interaction, onResolve, onCancel }: InteractionCardProps) {
  return (
    <Card className="border-yellow-500/20 hover:border-yellow-500/40 transition-colors">
      <div className="flex flex-col gap-3">
        {/* Header */}
        <div className="flex items-start justify-between gap-2">
          <div className="flex flex-col gap-1">
            <div className="flex items-center gap-2">
              <span className="font-mono text-xs text-text-muted">
                {interaction.interactionId.slice(0, 12)}…
              </span>
              <Badge variant="yellow">Pending</Badge>
            </div>
            <div className="flex items-center gap-2 text-xs text-text-muted">
              <span>Execução:</span>
              <Link
                to={`/executions/${interaction.executionId}`}
                className="text-accent-blue hover:underline font-mono"
                onClick={(e) => e.stopPropagation()}
              >
                {interaction.executionId.slice(0, 12)}…
              </Link>
            </div>
          </div>
          <span className="text-xs text-text-muted whitespace-nowrap">
            {new Date(interaction.createdAt).toLocaleString('pt-BR')}
          </span>
        </div>

        {/* Prompt */}
        <div className="bg-bg-tertiary rounded-lg p-3">
          <p className="text-xs text-text-muted mb-1">Prompt</p>
          <p className="text-sm text-text-primary leading-relaxed">{interaction.prompt}</p>
        </div>

        {/* Context */}
        {interaction.context && (
          <div className="bg-bg-tertiary rounded-lg p-3">
            <p className="text-xs text-text-muted mb-1">Contexto</p>
            <p className="text-sm text-text-secondary">{interaction.context}</p>
          </div>
        )}

        {/* Actions */}
        <div className="flex gap-2 pt-1">
          <Button variant="primary" size="sm" onClick={() => onResolve(interaction)}>
            Resolver
          </Button>
          <Button variant="danger" size="sm" onClick={() => onCancel(interaction)}>
            Cancelar
          </Button>
          <Link to={`/hitl/${interaction.interactionId}`}>
            <Button variant="ghost" size="sm">
              Ver Detalhes
            </Button>
          </Link>
        </div>
      </div>
    </Card>
  )
}

// ── Main Component ────────────────────────────────────────────────────────────

export function HitlPendingPage() {
  const { data: interactions, isLoading, error, refetch } = usePendingInteractions()
  const resolveInteraction = useResolveInteraction()

  const [resolveTarget, setResolveTarget] = useState<HumanInteraction | null>(null)
  const [cancelTarget, setCancelTarget] = useState<HumanInteraction | null>(null)

  const pendingCount = interactions?.length ?? 0

  // Auto-refresh every 15s
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null)
  useEffect(() => {
    intervalRef.current = setInterval(() => refetch(), 15_000)
    return () => {
      if (intervalRef.current) clearInterval(intervalRef.current)
    }
  }, [refetch])

  const handleCancelConfirm = () => {
    if (!cancelTarget) return
    // Cancel by resolving with rejected=false and empty response
    resolveInteraction.mutate(
      {
        id: cancelTarget.interactionId,
        body: { response: 'Cancelado pelo operador', approved: false },
      },
      {
        onSuccess: () => {
          setCancelTarget(null)
          refetch()
        },
      }
    )
  }

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar interações pendentes." onRetry={refetch} />

  return (
    <div className="flex flex-col gap-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <h1 className="text-2xl font-bold text-text-primary">Interações Pendentes</h1>
          {pendingCount > 0 && (
            <span className="flex items-center justify-center w-6 h-6 bg-red-500 text-white text-xs font-bold rounded-full">
              {pendingCount > 99 ? '99+' : pendingCount}
            </span>
          )}
        </div>
        <Button variant="secondary" size="sm" onClick={() => refetch()}>
          Atualizar
        </Button>
      </div>

      {/* Empty state */}
      {pendingCount === 0 && (
        <EmptyState
          title="Nenhuma interação pendente"
          description="Todas as interações foram resolvidas. O sistema está atualizado automaticamente a cada 15 segundos."
        />
      )}

      {/* Interaction cards */}
      {pendingCount > 0 && (
        <div className="flex flex-col gap-4">
          {interactions!.map((interaction) => (
            <InteractionCard
              key={interaction.interactionId}
              interaction={interaction}
              onResolve={setResolveTarget}
              onCancel={setCancelTarget}
            />
          ))}
        </div>
      )}

      {/* Resolve modal */}
      <ResolveModal
        interaction={resolveTarget}
        onClose={() => setResolveTarget(null)}
        onResolved={() => refetch()}
      />

      {/* Cancel confirm */}
      <ConfirmDialog
        open={!!cancelTarget}
        onClose={() => setCancelTarget(null)}
        onConfirm={handleCancelConfirm}
        title="Cancelar Interação"
        message={`Tem certeza que deseja cancelar a interação "${cancelTarget?.interactionId.slice(0, 12)}..."?`}
        confirmLabel="Cancelar Interação"
        variant="danger"
        loading={resolveInteraction.isPending}
      />
    </div>
  )
}
