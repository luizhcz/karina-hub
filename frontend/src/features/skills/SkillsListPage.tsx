import { useState } from 'react'
import { useNavigate } from 'react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useSkills, useDeleteSkill } from '../../api/skills'
import type { Skill } from '../../api/skills'
import { DataTable } from '../../shared/data/DataTable'
import { Card } from '../../shared/ui/Card'
import { Badge } from '../../shared/ui/Badge'
import { Button } from '../../shared/ui/Button'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { EmptyState } from '../../shared/ui/EmptyState'
import { ApiError } from '../../api/client'

export function SkillsListPage() {
  const navigate = useNavigate()
  const { data: skills, isLoading, error, refetch } = useSkills()
  const deleteSkill = useDeleteSkill()
  const [deletingId, setDeletingId] = useState<string | null>(null)

  if (isLoading) return <PageLoader />
  if (error instanceof ApiError && error.status === 403) {
    return <ErrorCard message={error.message} onRetry={refetch} />
  }
  if (error) return <ErrorCard message="Erro ao carregar skills" onRetry={refetch} />

  const items = skills ?? []

  const columns: ColumnDef<Skill, unknown>[] = [
    {
      accessorKey: 'name',
      header: 'Nome',
      cell: ({ getValue }) => (
        <span className="font-medium text-text-primary">{String(getValue())}</span>
      ),
    },
    {
      accessorKey: 'type',
      header: 'Tipo',
      cell: ({ getValue }) => (
        <Badge variant="blue">{String(getValue())}</Badge>
      ),
    },
    {
      accessorKey: 'description',
      header: 'Descrição',
      cell: ({ getValue }) => {
        const v = getValue() as string | undefined
        return (
          <span className="text-xs text-text-muted">
            {v ? v.slice(0, 60) + (v.length > 60 ? '…' : '') : '—'}
          </span>
        )
      },
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
            onClick={() => navigate(`/skills/${row.original.id}`)}
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
          <h1 className="text-2xl font-bold text-text-primary">Skills</h1>
          <p className="text-sm text-text-muted mt-1">{items.length} skill(s) registrada(s)</p>
        </div>
        <Button onClick={() => navigate('/skills/new')}>Nova Skill</Button>
      </div>

      <Card padding={false}>
        {items.length === 0 ? (
          <EmptyState
            title="Nenhuma skill registrada"
            description="Crie skills para estender as capacidades dos seus agentes."
            action={<Button onClick={() => navigate('/skills/new')}>Nova Skill</Button>}
          />
        ) : (
          <DataTable
            data={items}
            columns={columns}
            searchPlaceholder="Buscar skill..."
            onRowClick={(row) => navigate(`/skills/${row.id}`)}
          />
        )}
      </Card>

      <ConfirmDialog
        open={deletingId !== null}
        onClose={() => setDeletingId(null)}
        onConfirm={() => {
          if (deletingId) {
            deleteSkill.mutate(deletingId, { onSuccess: () => setDeletingId(null) })
          }
        }}
        title="Excluir Skill"
        message="Tem certeza que deseja excluir esta skill? Esta ação não pode ser desfeita."
        confirmLabel="Excluir"
        variant="danger"
        loading={deleteSkill.isPending}
      />
    </div>
  )
}
