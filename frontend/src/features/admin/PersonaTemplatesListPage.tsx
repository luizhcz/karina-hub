import { useState } from 'react'
import { useNavigate } from 'react-router'
import type { ColumnDef } from '@tanstack/react-table'
import {
  useDeletePersonaTemplate,
  usePersonaTemplates,
  userTypeFromScope,
  type PersonaPromptTemplate,
} from '../../api/personaTemplates'
import { DataTable } from '../../shared/data/DataTable'
import { Card } from '../../shared/ui/Card'
import { Badge } from '../../shared/ui/Badge'
import { Button } from '../../shared/ui/Button'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { EmptyState } from '../../shared/ui/EmptyState'

export function PersonaTemplatesListPage() {
  const navigate = useNavigate()
  const { data, isLoading, error, refetch } = usePersonaTemplates()
  const deleteTpl = useDeletePersonaTemplate()
  const [deletingId, setDeletingId] = useState<number | null>(null)

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar templates de persona" onRetry={refetch} />

  const items = data?.items ?? []
  const clientPlaceholders = data?.placeholders?.client ?? []
  const adminPlaceholders = data?.placeholders?.admin ?? []

  const columns: ColumnDef<PersonaPromptTemplate, unknown>[] = [
    {
      accessorKey: 'scope',
      header: 'Scope',
      cell: ({ getValue }) => {
        const v = String(getValue())
        const isGlobal = v.startsWith('global:')
        return <Badge variant={isGlobal ? 'blue' : 'purple'}>{v}</Badge>
      },
    },
    {
      id: 'userType',
      header: 'Tipo',
      accessorFn: (row) => userTypeFromScope(row.scope),
      cell: ({ getValue }) => {
        const v = String(getValue())
        return <Badge variant={v === 'cliente' ? 'blue' : 'purple'}>{v}</Badge>
      },
    },
    {
      accessorKey: 'name',
      header: 'Nome',
      cell: ({ getValue }) => (
        <span className="font-medium text-text-primary">{String(getValue())}</span>
      ),
    },
    {
      accessorKey: 'template',
      header: 'Preview',
      cell: ({ getValue }) => {
        const v = String(getValue())
        const first = v.split('\n').find((l) => l.trim().length > 0) ?? ''
        return (
          <span className="text-xs text-text-muted font-mono truncate block max-w-md">
            {first.length > 80 ? `${first.slice(0, 80)}…` : first}
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
            {v ? new Date(v).toLocaleString('pt-BR') : '—'}
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
            onClick={() => navigate(`/admin/persona-templates/${row.original.id}`)}
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
          <h1 className="text-2xl font-bold text-text-primary">Persona Templates</h1>
          <p className="text-sm text-text-muted mt-1">
            Templates do bloco de persona injetado no <code className="font-mono">system message</code> dos agentes.
            Resolução: <Badge variant="purple">agent:{'{id}'}</Badge> → fallback{' '}
            <Badge variant="blue">global</Badge>. {items.length} template(s).
          </p>
        </div>
        <Button onClick={() => navigate('/admin/persona-templates/new')}>Novo template</Button>
      </div>

      {(clientPlaceholders.length > 0 || adminPlaceholders.length > 0) && (
        <Card title="Placeholders suportados">
          <div className="flex flex-col gap-3">
            <div>
              <div className="flex items-center gap-2 mb-2">
                <Badge variant="blue">cliente</Badge>
                <span className="text-xs text-text-muted">
                  disponível em scopes <code className="font-mono">*:cliente</code>
                </span>
              </div>
              <div className="flex flex-wrap gap-2">
                {clientPlaceholders.map((p) => (
                  <code
                    key={p}
                    className="px-2 py-1 rounded bg-bg-tertiary border border-border-secondary text-xs font-mono text-text-secondary"
                  >
                    {'{{'}{p}{'}}'}
                  </code>
                ))}
              </div>
            </div>
            <div className="border-t border-border-primary" />
            <div>
              <div className="flex items-center gap-2 mb-2">
                <Badge variant="purple">admin</Badge>
                <span className="text-xs text-text-muted">
                  disponível em scopes <code className="font-mono">*:admin</code>
                </span>
              </div>
              <div className="flex flex-wrap gap-2">
                {adminPlaceholders.map((p) => (
                  <code
                    key={p}
                    className="px-2 py-1 rounded bg-bg-tertiary border border-border-secondary text-xs font-mono text-text-secondary"
                  >
                    {'{{'}{p}{'}}'}
                  </code>
                ))}
              </div>
            </div>
          </div>
          <p className="text-xs text-text-muted mt-4">
            Listas renderizam como CSV (<code className="font-mono">"A, B"</code>);
            booleanos como <code className="font-mono">sim/não</code>. Placeholders
            desconhecidos ficam literais no output pra expor typos.
          </p>
        </Card>
      )}

      <Card padding={false}>
        {items.length === 0 ? (
          <EmptyState
            title="Nenhum template cadastrado"
            description="Crie ao menos um template com scope global. Sem template, o bloco de persona fica de fora do system message."
            action={
              <Button onClick={() => navigate('/admin/persona-templates/new')}>
                Criar global
              </Button>
            }
          />
        ) : (
          <DataTable
            data={items}
            columns={columns}
            searchPlaceholder="Buscar por scope/nome..."
            onRowClick={(row) => navigate(`/admin/persona-templates/${row.id}`)}
          />
        )}
      </Card>

      <ConfirmDialog
        open={deletingId !== null}
        onClose={() => setDeletingId(null)}
        onConfirm={() => {
          if (deletingId !== null) {
            deleteTpl.mutate(deletingId, { onSuccess: () => setDeletingId(null) })
          }
        }}
        title="Excluir template"
        message="Remove o registro. Se era o template global, agentes ficarão sem bloco de persona no system message até criar um novo."
        confirmLabel="Excluir"
        variant="danger"
        loading={deleteTpl.isPending}
      />
    </div>
  )
}
