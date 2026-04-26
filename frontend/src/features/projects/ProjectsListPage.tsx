import { useState } from 'react'
import { useNavigate } from 'react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useProjects, useDeleteProject } from '../../api/projects'
import type { Project } from '../../api/projects'
import { DataTable } from '../../shared/data/DataTable'
import { Card } from '../../shared/ui/Card'
import { Button } from '../../shared/ui/Button'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { EmptyState } from '../../shared/ui/EmptyState'

export function ProjectsListPage() {
  const navigate = useNavigate()
  const { data: projects, isLoading, error, refetch } = useProjects()
  const deleteProject = useDeleteProject()
  const [deletingId, setDeletingId] = useState<string | null>(null)

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar projetos" onRetry={refetch} />

  const items = projects ?? []

  const columns: ColumnDef<Project, unknown>[] = [
    {
      accessorKey: 'name',
      header: 'Nome',
      cell: ({ getValue }) => (
        <span className="font-medium text-text-primary">{String(getValue())}</span>
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
      accessorKey: 'id',
      header: 'ID',
      cell: ({ getValue }) => (
        <span className="font-mono text-xs text-text-dimmed">{String(getValue()).slice(0, 16)}…</span>
      ),
    },
    {
      accessorKey: 'createdAt',
      header: 'Criado em',
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
            onClick={() => navigate(`/projects/${row.original.id}`)}
          >
            Editar
          </Button>
          <Button
            variant="secondary"
            size="sm"
            onClick={() => navigate(`/projects/${row.original.id}/stats`)}
          >
            Stats
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
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Projetos</h1>
          <p className="text-sm text-text-muted mt-1">{items.length} projeto(s) registrado(s)</p>
        </div>
        <Button onClick={() => navigate('/projects/new')}>Novo Projeto</Button>
      </div>

      <Card padding={false}>
        {items.length === 0 ? (
          <EmptyState
            title="Nenhum projeto registrado"
            description="Crie projetos para organizar seus agentes e workflows."
            action={<Button onClick={() => navigate('/projects/new')}>Novo Projeto</Button>}
          />
        ) : (
          <DataTable
            data={items}
            columns={columns}
            searchPlaceholder="Buscar projeto..."
            onRowClick={(row) => navigate(`/projects/${row.id}`)}
          />
        )}
      </Card>

      <ConfirmDialog
        open={deletingId !== null}
        onClose={() => setDeletingId(null)}
        onConfirm={() => {
          if (deletingId) {
            deleteProject.mutate(deletingId, { onSuccess: () => setDeletingId(null) })
          }
        }}
        title="Excluir Projeto"
        message="Tem certeza que deseja excluir este projeto? Esta ação não pode ser desfeita."
        confirmLabel="Excluir"
        variant="danger"
        loading={deleteProject.isPending}
      />
    </div>
  )
}
