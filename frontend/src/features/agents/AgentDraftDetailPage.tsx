import { useMemo, useRef, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { Button } from '../../shared/ui/Button'
import { Badge } from '../../shared/ui/Badge'
import { Card } from '../../shared/ui/Card'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { ApiError } from '../../api/client'
import {
  useAgentDraft,
  useUpdateAgentDraft,
  useDeleteAgentDraft,
  useSubmitAgentDraft,
} from '../../api/agentDrafts'
import { useApprovalHistory } from '../../api/agentApprovals'
import { AgentForm } from './components/AgentForm'
import { formToRequest } from './formToRequest'
import { requestToDraftPayload, draftToAgentDef } from './draftConverters'
import { toast } from '../../stores/toast'
import type { AgentFormValues } from './types'

const STATUS_BADGE: Record<string, { label: string; variant: 'yellow' | 'blue' | 'red' | 'green' }> = {
  Draft: { label: 'Rascunho', variant: 'yellow' },
  PendingApproval: { label: 'Em aprovação', variant: 'blue' },
  Rejected: { label: 'Rejeitado', variant: 'red' },
}

export function AgentDraftDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { data: draft, isLoading, error, refetch } = useAgentDraft(id!, !!id)

  const updateMutation = useUpdateAgentDraft()
  const deleteMutation = useDeleteAgentDraft()
  const submitMutation = useSubmitAgentDraft()
  const { data: history } = useApprovalHistory(id ?? '', !!id)

  // Ref evita closure stale entre o click e o handler do react-hook-form (async resolver).
  const modeRef = useRef<'save' | 'submit'>('save')

  const [expectedUpdatedAt, setExpectedUpdatedAt] = useState<string | null>(null)
  const effectiveUpdatedAt = expectedUpdatedAt ?? draft?.updatedAt ?? null

  const [discardOpen, setDiscardOpen] = useState(false)

  const initialValues = useMemo(
    () => (draft ? draftToAgentDef(draft) : undefined),
    [draft],
  )

  if (isLoading) return <PageLoader />
  if (error instanceof ApiError && error.status === 404) {
    return <ErrorCard message="Rascunho não encontrado." onRetry={refetch} />
  }
  if (error || !draft) return <ErrorCard message="Erro ao carregar rascunho." onRetry={refetch} />

  const isPending = draft.status === 'PendingApproval'
  const isRejected = draft.status === 'Rejected'
  const statusBadge = STATUS_BADGE[draft.status] ?? STATUS_BADGE.Draft

  const handleSubmit = (values: AgentFormValues) => {
    const result = formToRequest(values)
    if (!result.ok) {
      toast.error(result.error)
      return
    }

    if (!effectiveUpdatedAt) {
      toast.error('Estado do rascunho indisponível. Recarregue a página.')
      return
    }

    if (modeRef.current === 'submit') {
      // Sequência: salva o estado atual do form (caso não tenha clicado Save antes),
      // depois envia ao painel. Save antes garante que UpdatedAt é fresh — se
      // submit falhar (status inválido, race), o draft preserva o estado mais recente.
      // Em PendingApproval, a edição é proibida no backend → pulamos o save.
      const performSubmit = () => {
        submitMutation.mutate(
          { id: draft.id },
          {
            onSuccess: () => {
              toast.success('Rascunho enviado ao painel de aprovação.')
              navigate('/agents')
            },
            onError: (err) => {
              if (err instanceof ApiError && err.status === 409) {
                toast.error(err.message)
                return
              }
              const msg = err instanceof ApiError ? err.message : 'Erro ao enviar pra aprovação.'
              toast.error(msg)
            },
          },
        )
      }

      if (isPending) {
        performSubmit()
        return
      }

      updateMutation.mutate(
        {
          id: draft.id,
          payload: requestToDraftPayload(result.body),
          expectedUpdatedAt: effectiveUpdatedAt,
        },
        {
          onSuccess: (savedDraft) => {
            setExpectedUpdatedAt(savedDraft.updatedAt)
            performSubmit()
          },
          onError: (err) => {
            if (err instanceof ApiError && err.status === 412) {
              toast.error('Rascunho foi modificado em outra aba. Recarregue.')
              return
            }
            const msg = err instanceof ApiError ? err.message : 'Erro ao salvar antes de enviar.'
            toast.error(msg)
          },
        },
      )
      return
    }

    updateMutation.mutate(
      {
        id: draft.id,
        payload: requestToDraftPayload(result.body),
        expectedUpdatedAt: effectiveUpdatedAt,
      },
      {
        onSuccess: (savedDraft) => {
          setExpectedUpdatedAt(savedDraft.updatedAt)
          toast.success('Rascunho salvo.')
        },
        onError: (err) => {
          if (err instanceof ApiError && err.status === 412) {
            toast.error('Rascunho foi modificado em outra aba. Recarregue.')
            return
          }
          if (err instanceof ApiError && err.status === 409) {
            toast.error('Rascunho está em aprovação. Cancele a submissão antes de editar.')
            return
          }
          const msg = err instanceof ApiError ? err.message : 'Erro ao salvar rascunho.'
          toast.error(msg)
        },
      },
    )
  }

  const loading = updateMutation.isPending || submitMutation.isPending

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <Link to="/agents">
            <Button variant="ghost" size="sm">
              &larr; Agentes
            </Button>
          </Link>
          <div>
            <div className="flex items-center gap-2">
              <h1 className="text-2xl font-bold text-text-primary">
                {draft.name || draft.id}
              </h1>
              <Badge variant={statusBadge.variant}>{statusBadge.label}</Badge>
              {draft.isEditDraft && (
                <Badge variant="purple">
                  Edit · base ver. {draft.baseRevision ?? 'n/a'}
                </Badge>
              )}
            </div>
            <p className="text-sm text-text-muted mt-1">
              {draft.isEditDraft
                ? `Editando rascunho do agent publicado "${draft.baseAgentId}". O original continua ativo até a aprovação.`
                : 'Rascunho de criação. Envie ao painel de aprovação pra promover ao catálogo de agents.'}
            </p>
          </div>
        </div>

        <Button variant="danger" size="sm" onClick={() => setDiscardOpen(true)}>
          Descartar
        </Button>
      </div>

      {isRejected && draft.rejectionFeedback && (
        <Card title="Feedback do aprovador">
          <div className="whitespace-pre-wrap text-sm text-text-primary bg-red-500/10 border border-red-500/30 rounded p-3">
            {draft.rejectionFeedback}
          </div>
          <p className="text-xs text-text-muted mt-2">
            Edite o conteúdo abaixo e envie novamente, ou clique "Reenviar" pra submeter sem mudar.
          </p>
        </Card>
      )}

      {isPending && (
        <Card title="Em aprovação">
          <p className="text-sm text-text-muted">
            Este rascunho está aguardando revisão no painel de aprovação. Edição
            está bloqueada até aprovação ou rejeição.
          </p>
        </Card>
      )}

      {history && history.length > 0 && (
        <Card title="Histórico de aprovação">
          <ol className="flex flex-col gap-2">
            {history.map((entry) => (
              <li
                key={entry.id}
                className="flex flex-col gap-1 border-l-2 border-border-primary pl-3 py-1"
              >
                <div className="flex items-center gap-2 text-sm">
                  <Badge
                    variant={
                      entry.action === 'Approved'
                        ? 'green'
                        : entry.action === 'Rejected'
                        ? 'red'
                        : 'blue'
                    }
                  >
                    {entry.action === 'Approved'
                      ? 'Aprovado'
                      : entry.action === 'Rejected'
                      ? 'Rejeitado'
                      : entry.action === 'Resubmitted'
                      ? 'Reenviado'
                      : 'Enviado'}
                  </Badge>
                  <span className="text-text-muted">
                    {new Date(entry.occurredAt).toLocaleString('pt-BR')}
                  </span>
                  <span className="text-xs text-text-dimmed font-mono">
                    {entry.actorUserId}
                  </span>
                </div>
                {entry.feedback && (
                  <p className="text-xs text-text-muted whitespace-pre-wrap pl-1">
                    {entry.feedback}
                  </p>
                )}
              </li>
            ))}
          </ol>
        </Card>
      )}

      <Card title="Conteúdo do rascunho">
        <AgentForm
          initialValues={initialValues}
          onSubmit={handleSubmit}
          loading={loading}
          disabled={isPending}
          submitOverride={
            <div className="flex justify-end gap-2">
              <Button
                type="submit"
                variant="secondary"
                loading={updateMutation.isPending && !submitMutation.isPending}
                disabled={isPending}
                onClick={() => { modeRef.current = 'save' }}
              >
                Salvar rascunho
              </Button>
              <Button
                type="submit"
                loading={submitMutation.isPending}
                onClick={() => { modeRef.current = 'submit' }}
              >
                {isRejected ? 'Reenviar para aprovação' : 'Enviar para aprovação'}
              </Button>
            </div>
          }
        />
      </Card>

      <ConfirmDialog
        open={discardOpen}
        onClose={() => setDiscardOpen(false)}
        onConfirm={() => {
          deleteMutation.mutate(draft.id, {
            onSuccess: () => {
              toast.success('Rascunho descartado.')
              navigate('/agents')
            },
            onError: (err) => {
              const msg = err instanceof ApiError ? err.message : 'Erro ao descartar.'
              toast.error(msg)
            },
          })
        }}
        title="Descartar rascunho"
        message={`Tem certeza que deseja descartar o rascunho "${draft.name || draft.id}"?`}
        confirmLabel="Descartar"
        variant="danger"
        loading={deleteMutation.isPending}
      />
    </div>
  )
}
