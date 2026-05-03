import { useState } from 'react'
import { useNavigate } from 'react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useAdminPresets, useDeletePreset } from '../../api/predefinedModels'
import type { PredefinedModel } from '../../api/predefinedModels'
import { DataTable } from '../../shared/data/DataTable'
import { Card } from '../../shared/ui/Card'
import { Badge } from '../../shared/ui/Badge'
import { Button } from '../../shared/ui/Button'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { EmptyState } from '../../shared/ui/EmptyState'
import { ApiError } from '../../api/client'

export function PredefinedModelsListPage() {
  const navigate = useNavigate()
  const { data: presets, isLoading, error, refetch } = useAdminPresets(true)
  const deletePreset = useDeletePreset()
  const [deletingId, setDeletingId] = useState<string | null>(null)

  if (isLoading) return <PageLoader />
  if (error instanceof ApiError && error.status === 403) {
    return <ErrorCard message={error.message} onRetry={refetch} />
  }
  if (error) return <ErrorCard message="Erro ao carregar modelos pré-definidos" onRetry={refetch} />

  const items = presets ?? []

  const columns: ColumnDef<PredefinedModel, unknown>[] = [
    {
      accessorKey: 'displayName',
      header: 'Nome',
      cell: ({ getValue, row }) => (
        <div className="flex flex-col">
          <span className="font-medium text-text-primary">{String(getValue())}</span>
          <span className="text-[10px] text-text-dimmed font-mono">{row.original.id}</span>
        </div>
      ),
    },
    {
      accessorKey: 'provider',
      header: 'Provider',
      cell: ({ getValue }) => (
        <Badge variant="blue">{String(getValue())}</Badge>
      ),
    },
    {
      accessorKey: 'deploymentName',
      header: 'Deployment',
      cell: ({ getValue }) => (
        <code className="text-xs text-text-muted font-mono">{String(getValue())}</code>
      ),
    },
    {
      accessorKey: 'description',
      header: 'Descrição',
      cell: ({ getValue }) => {
        const v = getValue() as string
        return (
          <span className="text-xs text-text-muted">
            {v ? v.slice(0, 80) + (v.length > 80 ? '…' : '') : '—'}
          </span>
        )
      },
    },
    {
      accessorKey: 'enabled',
      header: 'Status',
      cell: ({ getValue }) => (
        <Badge variant={getValue() ? 'green' : 'red'}>
          {getValue() ? 'Ativo' : 'Inativo'}
        </Badge>
      ),
    },
    {
      accessorKey: 'updatedAt',
      header: 'Atualizado',
      cell: ({ getValue }) => {
        const v = getValue() as string | undefined
        return (
          <span className="text-xs text-text-muted">
            {v ? new Date(v).toLocaleDateString('pt-BR') : '—'}
          </span>
        )
      },
    },
    {
      id: 'actions',
      header: 'Ações',
      cell: ({ row }) => (
        <div className="flex items-center gap-2" onClick={(e) => e.stopPropagation()}>
          <Button
            variant="secondary"
            size="sm"
            onClick={() => navigate(`/admin/predefined-models/${row.original.id}`)}
          >
            Editar
          </Button>
          <Button
            variant="danger"
            size="sm"
            onClick={() => setDeletingId(row.original.id)}
          >
            Excluir
          </Button>
        </div>
      ),
    },
  ]

  return (
    <div className="flex flex-col gap-6 p-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Modelos pré-definidos</h1>
          <p className="text-sm text-text-muted mt-1">
            Catálogo global de presets de modelo. PMs escolhem por descrição no AgentForm —
            backend resolve provider/deployment em runtime.
          </p>
        </div>
        <Button onClick={() => navigate('/admin/predefined-models/new')}>Novo preset</Button>
      </div>

      <Card padding={false}>
        {items.length === 0 ? (
          <EmptyState
            title="Nenhum preset cadastrado"
            description="Cadastre receitas de provider+deployment+defaults pra que PMs possam escolher modelos por descrição friendly."
            action={<Button onClick={() => navigate('/admin/predefined-models/new')}>Novo preset</Button>}
          />
        ) : (
          <DataTable
            data={items}
            columns={columns}
            searchPlaceholder="Buscar preset..."
            onRowClick={(row) => navigate(`/admin/predefined-models/${row.id}`)}
          />
        )}
      </Card>

      <ConfirmDialog
        open={deletingId !== null}
        onClose={() => setDeletingId(null)}
        onConfirm={() => {
          if (deletingId) {
            deletePreset.mutate(deletingId, { onSuccess: () => setDeletingId(null) })
          }
        }}
        title="Excluir preset"
        message="Tem certeza? Agents que referenciam esse preset falharão ao invocar até serem reapontados ou re-seedados."
        confirmLabel="Excluir"
        variant="danger"
        loading={deletePreset.isPending}
      />
    </div>
  )
}
