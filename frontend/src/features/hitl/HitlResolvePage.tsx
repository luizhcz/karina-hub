import { useState } from 'react'
import { useParams, useNavigate, Link } from 'react-router'
import { Badge } from '../../shared/ui/Badge'
import { Button } from '../../shared/ui/Button'
import { Card } from '../../shared/ui/Card'
import { Input } from '../../shared/ui/Input'
import { Textarea } from '../../shared/ui/Textarea'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import {
  useInteraction,
  useResolveInteraction,
  isHitlAlreadyResolvedError,
  type HumanInteraction,
} from '../../api/interactions'
import { ApiError } from '../../api/client'
import { useUserStore } from '../../stores/user'


function statusVariant(status: HumanInteraction['status']): 'yellow' | 'green' | 'red' {
  switch (status) {
    case 'Pending': return 'yellow'
    case 'Resolved': return 'green'
    case 'Rejected': return 'red'
  }
}


export function HitlResolvePage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { userId } = useUserStore()

  const [response, setResponse] = useState('')
  const [resolvedBy, setResolvedBy] = useState(userId)
  const [approved, setApproved] = useState(true)

  const { data: interaction, isLoading, error, refetch } = useInteraction(id ?? '', !!id)
  const resolveInteraction = useResolveInteraction()

  const handleResolve = () => {
    if (!id || !response.trim()) return
    resolveInteraction.mutate(
      {
        id,
        body: { resolution: response.trim(), approved },
      },
      {
        onSuccess: () => {
          refetch()
          navigate('/hitl')
        },
        onError: (err) => {
          // 404 = CAS perdido (outro pod/caller já resolveu) — UI já refetchou via
          // useResolveInteraction.onError; basta atualizar esta página para refletir
          // o novo status (Resolved/Rejected) sem navegar embora.
          if (isHitlAlreadyResolvedError(err)) {
            refetch()
          }
        },
      }
    )
  }

  /**
   * Mensagem amigável derivada do erro da mutation. Prioriza:
   * - 404 → texto específico sobre race/concorrência
   * - Outras ApiError → message do backend (já extraída pelo client.ts)
   * - Erro desconhecido → fallback genérico
   */
  const resolveErrorMessage = resolveInteraction.error
    ? isHitlAlreadyResolvedError(resolveInteraction.error)
      ? 'Esta interação já foi resolvida por outro operador ou a execução expirou. Recarregando...'
      : resolveInteraction.error instanceof ApiError
        ? resolveInteraction.error.message
        : 'Erro ao resolver interação. Tente novamente.'
    : null

  if (!id) return <ErrorCard message="ID da interação não encontrado." />
  if (isLoading) return <PageLoader />
  if (error || !interaction) return <ErrorCard message="Erro ao carregar interação." onRetry={refetch} />

  const isPending = interaction.status === 'Pending'

  return (
    <div className="flex flex-col gap-6 max-w-2xl mx-auto">
      <button
        onClick={() => navigate(-1)}
        className="text-xs text-text-muted hover:text-text-secondary flex items-center gap-1 self-start"
      >
        ← Voltar
      </button>

      <div className="flex items-start justify-between gap-4">
        <div className="flex flex-col gap-2">
          <h1 className="text-xl font-bold text-text-primary">Interação HITL</h1>
          <div className="flex items-center gap-2">
            <span className="font-mono text-xs text-text-muted">{interaction.interactionId}</span>
            <Badge variant={statusVariant(interaction.status)}>{interaction.status}</Badge>
          </div>
        </div>
      </div>

      <Card title="Informações">
        <div className="grid grid-cols-2 gap-4">
          <div>
            <p className="text-xs text-text-muted">Interaction ID</p>
            <p className="text-xs font-mono text-text-secondary mt-0.5 break-all">
              {interaction.interactionId}
            </p>
          </div>
          <div>
            <p className="text-xs text-text-muted">Execução</p>
            <Link
              to={`/executions/${interaction.executionId}`}
              className="text-xs text-accent-blue hover:underline font-mono mt-0.5 block"
            >
              {interaction.executionId}
            </Link>
          </div>
          <div>
            <p className="text-xs text-text-muted">Workflow</p>
            <p className="text-xs text-text-secondary mt-0.5">{interaction.workflowId}</p>
          </div>
          <div>
            <p className="text-xs text-text-muted">Criado em</p>
            <p className="text-xs text-text-secondary mt-0.5">
              {new Date(interaction.createdAt).toLocaleString('pt-BR')}
            </p>
          </div>
          {interaction.resolvedAt && (
            <div>
              <p className="text-xs text-text-muted">Resolvido em</p>
              <p className="text-xs text-text-secondary mt-0.5">
                {new Date(interaction.resolvedAt).toLocaleString('pt-BR')}
              </p>
            </div>
          )}
        </div>
      </Card>

      <Card title="Prompt">
        <p className="text-sm text-text-primary leading-relaxed">{interaction.prompt}</p>
      </Card>

      {interaction.context && (
        <Card title="Contexto">
          <p className="text-sm text-text-secondary leading-relaxed">{interaction.context}</p>
        </Card>
      )}

      {isPending ? (
        <Card title="Resolver Interação">
          <div className="flex flex-col gap-4">
            <div className="flex flex-col gap-2">
              <p className="text-xs text-text-muted">Decisão</p>
              <div className="flex gap-2">
                <button
                  type="button"
                  onClick={() => setApproved(true)}
                  className={`px-4 py-2 rounded-lg text-sm font-medium transition-colors ${
                    approved
                      ? 'bg-green-500/20 text-green-400 border border-green-500/40'
                      : 'bg-bg-tertiary text-text-muted border border-border-primary hover:border-green-500/30'
                  }`}
                >
                  Aprovar
                </button>
                <button
                  type="button"
                  onClick={() => setApproved(false)}
                  className={`px-4 py-2 rounded-lg text-sm font-medium transition-colors ${
                    !approved
                      ? 'bg-red-500/20 text-red-400 border border-red-500/40'
                      : 'bg-bg-tertiary text-text-muted border border-border-primary hover:border-red-500/30'
                  }`}
                >
                  Rejeitar
                </button>
              </div>
            </div>

            <Textarea
              label="Resposta"
              value={response}
              onChange={(e) => setResponse(e.target.value)}
              rows={4}
              placeholder="Descreva sua decisão ou forneça informações adicionais..."
            />

            <Input
              label="Resolvido por"
              value={resolvedBy}
              onChange={(e) => setResolvedBy(e.target.value)}
              placeholder="ID do operador"
            />

            {resolveErrorMessage && (
              <p
                className={`text-sm ${
                  isHitlAlreadyResolvedError(resolveInteraction.error)
                    ? 'text-amber-400'
                    : 'text-red-400'
                }`}
              >
                {resolveErrorMessage}
              </p>
            )}

            <div className="flex gap-2 pt-2">
              <Button
                variant="primary"
                onClick={handleResolve}
                loading={resolveInteraction.isPending}
                disabled={!response.trim()}
              >
                Confirmar Resolução
              </Button>
              <Button variant="secondary" onClick={() => navigate(-1)}>
                Cancelar
              </Button>
            </div>
          </div>
        </Card>
      ) : (
        <Card title="Resolução">
          <div className="flex flex-col gap-3">
            <div className="flex items-center gap-2">
              <Badge variant={statusVariant(interaction.status)}>{interaction.status}</Badge>
              {interaction.resolvedAt && (
                <span className="text-xs text-text-muted">
                  {new Date(interaction.resolvedAt).toLocaleString('pt-BR')}
                </span>
              )}
            </div>
            {interaction.resolvedBy && (
              <div className="flex items-center gap-2">
                <p className="text-xs text-text-muted">Resolvido por:</p>
                <p className="text-xs font-mono text-text-secondary">
                  {interaction.resolvedBy === 'system:timeout'
                    ? '⏱ sistema (timeout automático)'
                    : interaction.resolvedBy}
                </p>
              </div>
            )}
            {interaction.resolution && (
              <div className="bg-bg-tertiary rounded-lg p-3">
                <p className="text-xs text-text-muted mb-1">Resposta</p>
                <p className="text-sm text-text-primary">{interaction.resolution}</p>
              </div>
            )}
          </div>
        </Card>
      )}
    </div>
  )
}
