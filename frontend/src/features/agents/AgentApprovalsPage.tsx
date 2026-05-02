import { useState } from 'react'
import { useNavigate } from 'react-router'
import { type ColumnDef } from '@tanstack/react-table'
import { DataTable } from '../../shared/data/DataTable'
import { Button } from '../../shared/ui/Button'
import { Badge } from '../../shared/ui/Badge'
import { Card } from '../../shared/ui/Card'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
import { Tabs } from '../../shared/ui/Tabs'
import { Textarea } from '../../shared/ui/Textarea'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import {
  useApprovals,
  useApproveDraft,
  useRejectDraft,
  type ApprovalListStatus,
} from '../../api/agentApprovals'
import type { AgentDraft } from '../../api/agentDrafts'
import { ApiError } from '../../api/client'
import { toast } from '../../stores/toast'

const STATUS_TABS: { key: ApprovalListStatus; label: string }[] = [
  { key: 'pending', label: 'Pendentes' },
  { key: 'rejected', label: 'Rejeitados' },
]

export function AgentApprovalsPage() {
  const navigate = useNavigate()
  const [statusTab, setStatusTab] = useState<ApprovalListStatus>('pending')
  const { data: items, isLoading, error, refetch, isFetching } = useApprovals(statusTab)

  const approveMutation = useApproveDraft()
  const rejectMutation = useRejectDraft()

  const [approveTarget, setApproveTarget] = useState<AgentDraft | null>(null)
  const [rejectTarget, setRejectTarget] = useState<AgentDraft | null>(null)
  const [rejectFeedback, setRejectFeedback] = useState('')

  const handleApprove = () => {
    if (!approveTarget) return
    approveMutation.mutate(
      { id: approveTarget.id },
      {
        onSuccess: () => {
          toast.success(`Agente "${approveTarget.name || approveTarget.id}" aprovado e publicado.`)
          setApproveTarget(null)
        },
        onError: (err) => {
          if (err instanceof ApiError && err.status === 409) {
            toast.error(err.message)
          } else if (err instanceof ApiError && err.status === 400) {
            toast.error(`Não foi possível aprovar: ${err.message}`)
          } else {
            const msg = err instanceof ApiError ? err.message : 'Erro ao aprovar.'
            toast.error(msg)
          }
          setApproveTarget(null)
        },
      },
    )
  }

  const handleReject = () => {
    if (!rejectTarget) return
    if (rejectFeedback.trim().length < 10) {
      toast.error('Feedback precisa ter pelo menos 10 caracteres.')
      return
    }
    rejectMutation.mutate(
      { id: rejectTarget.id, feedback: rejectFeedback.trim() },
      {
        onSuccess: () => {
          toast.success(`Rascunho "${rejectTarget.name || rejectTarget.id}" rejeitado.`)
          setRejectTarget(null)
          setRejectFeedback('')
        },
        onError: (err) => {
          if (err instanceof ApiError && err.status === 409) {
            toast.error(err.message)
          } else {
            const msg = err instanceof ApiError ? err.message : 'Erro ao rejeitar.'
            toast.error(msg)
          }
          setRejectTarget(null)
          setRejectFeedback('')
        },
      },
    )
  }

  const columns: ColumnDef<AgentDraft, unknown>[] = [
    {
      accessorKey: 'name',
      header: 'Agente',
      cell: ({ row }) => (
        <div className="flex flex-col gap-0.5">
          <span className="font-medium text-text-primary">
            {row.original.name || row.original.id}
          </span>
          <span className="font-mono text-[11px] text-text-dimmed">{row.original.id}</span>
        </div>
      ),
    },
    {
      accessorFn: (r) => r.payload.model?.deploymentName ?? '—',
      id: 'model',
      header: 'Model',
      cell: ({ getValue }) => {
        const v = getValue() as string
        return v === '—' ? <span className="text-text-dimmed">—</span> : <Badge variant="blue">{v}</Badge>
      },
    },
    {
      id: 'kind',
      header: 'Tipo',
      cell: ({ row }) =>
        row.original.isEditDraft ? (
          <Badge variant="purple">
            Edit · {row.original.baseAgentId}
            {row.original.baseRevision != null ? ` · r${row.original.baseRevision}` : ''}
          </Badge>
        ) : (
          <Badge variant="green">Novo</Badge>
        ),
    },
    {
      accessorKey: 'projectId',
      header: 'Projeto',
      cell: ({ getValue }) => (
        <span className="font-mono text-xs text-text-muted">{getValue() as string}</span>
      ),
    },
    {
      accessorKey: 'submittedAt',
      header: 'Enviado em',
      cell: ({ row }) => {
        const v = row.original.submittedAt ?? row.original.updatedAt
        return v ? new Date(v).toLocaleString('pt-BR') : '-'
      },
    },
    {
      id: 'actions',
      header: '',
      cell: ({ row }) => {
        if (statusTab === 'pending') {
          return (
            <div className="flex items-center gap-1" onClick={(e) => e.stopPropagation()}>
              <Button
                variant="primary"
                size="sm"
                onClick={() => setApproveTarget(row.original)}
              >
                Aprovar
              </Button>
              <Button
                variant="danger"
                size="sm"
                onClick={() => {
                  setRejectTarget(row.original)
                  setRejectFeedback('')
                }}
              >
                Rejeitar
              </Button>
            </div>
          )
        }
        // Aba Rejeitados: somente leitura.
        return (
          <span className="text-xs text-text-muted italic">
            {row.original.rejectionFeedback
              ? row.original.rejectionFeedback.slice(0, 60) + (row.original.rejectionFeedback.length > 60 ? '…' : '')
              : '—'}
          </span>
        )
      },
    },
  ]

  if (isLoading) return <PageLoader />
  if (error instanceof ApiError && error.status === 403) {
    return <ErrorCard message={error.message} onRetry={refetch} />
  }
  if (error) return <ErrorCard message="Erro ao carregar painel." onRetry={refetch} />

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Painel de aprovação</h1>
          <p className="text-sm text-text-muted mt-1">
            Revise rascunhos de agente enviados pelos projetos do tenant. Aprovar publica
            o agente; rejeitar exige feedback. Atualize a página para ver novos itens.
          </p>
        </div>
        <Button variant="secondary" onClick={() => refetch()} loading={isFetching}>
          Atualizar
        </Button>
      </div>

      <Tabs
        items={STATUS_TABS.map((t) => ({
          key: t.key,
          label: t.label,
          badge: t.key === statusTab ? items?.length ?? 0 : undefined,
        }))}
        active={statusTab}
        onChange={(k) => setStatusTab(k as ApprovalListStatus)}
      />

      <DataTable
        data={items ?? []}
        columns={columns}
        searchPlaceholder="Buscar por nome..."
        onRowClick={(row) => navigate(`/agents/drafts/${row.id}`)}
      />

      <ConfirmDialog
        open={!!approveTarget}
        onClose={() => setApproveTarget(null)}
        onConfirm={handleApprove}
        title="Aprovar rascunho"
        message={
          approveTarget
            ? `Aprovar e publicar o agente "${approveTarget.name || approveTarget.id}"? Após aprovação, fica disponível para os workflows.`
            : ''
        }
        confirmLabel="Aprovar"
        variant="primary"
        loading={approveMutation.isPending}
      />

      {rejectTarget && (
        <Card title={`Rejeitar rascunho — ${rejectTarget.name || rejectTarget.id}`}>
          <div className="flex flex-col gap-3">
            <p className="text-sm text-text-muted">
              Feedback é obrigatório (mín. 10 caracteres). O criador receberá esse texto
              inline no rascunho pra ajustar e reenviar.
            </p>
            <Textarea
              rows={5}
              value={rejectFeedback}
              onChange={(e) => setRejectFeedback(e.target.value)}
              placeholder="Descreva o motivo da rejeição..."
            />
            <div className="flex justify-end gap-2">
              <Button
                variant="ghost"
                onClick={() => {
                  setRejectTarget(null)
                  setRejectFeedback('')
                }}
              >
                Cancelar
              </Button>
              <Button
                variant="danger"
                loading={rejectMutation.isPending}
                onClick={handleReject}
              >
                Rejeitar
              </Button>
            </div>
          </div>
        </Card>
      )}
    </div>
  )
}
