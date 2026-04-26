import { useState } from 'react'
import type { ColumnDef } from '@tanstack/react-table'
import { useAdminAudit } from '../../api/adminAudit'
import type { AdminAuditEntry } from '../../api/adminAudit'
import { useProjects } from '../../api/projects'
import { DataTable } from '../../shared/data/DataTable'
import { Card } from '../../shared/ui/Card'
import { Badge } from '../../shared/ui/Badge'
import { Input } from '../../shared/ui/Input'
import { Select } from '../../shared/ui/Select'
import { Modal } from '../../shared/ui/Modal'
import { JsonViewer } from '../../shared/data/JsonViewer'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { EmptyState } from '../../shared/ui/EmptyState'
import { PAGE_SIZE_OPTIONS_AUDIT } from '../../constants/pagination'

// Tons consistentes com a paleta do Badge shared — 'yellow' é âmbar-400 no tailwind.
const ACTION_COLOR: Record<string, 'green' | 'yellow' | 'red'> = {
  create: 'green',
  update: 'yellow',
  delete: 'red',
}

const RESOURCE_LABEL: Record<string, string> = {
  project: 'Projeto',
  agent: 'Agente',
  workflow: 'Workflow',
  skill: 'Skill',
  model_pricing: 'Model Pricing',
}

export function AdminAuditPage() {
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [pageSize, setPageSize] = useState('25')
  const [projectId, setProjectId] = useState('')
  const [resourceType, setResourceType] = useState('')
  const [actorUserId, setActorUserId] = useState('')
  const [selected, setSelected] = useState<AdminAuditEntry | null>(null)

  const { data: projectsData } = useProjects()
  const projects = projectsData ?? []

  const { data, isLoading, error, refetch } = useAdminAudit({
    from: from || undefined,
    to: to || undefined,
    projectId: projectId || undefined,
    resourceType: resourceType || undefined,
    actorUserId: actorUserId || undefined,
    pageSize: Number(pageSize),
  })

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar audit log" onRetry={refetch} />

  const items = data?.items ?? []

  const columns: ColumnDef<AdminAuditEntry, unknown>[] = [
    {
      accessorKey: 'timestamp',
      header: 'Quando',
      cell: ({ getValue }) => (
        <span className="text-xs text-text-muted">{new Date(String(getValue())).toLocaleString('pt-BR')}</span>
      ),
    },
    {
      accessorKey: 'actorUserId',
      header: 'Actor',
      cell: ({ row }) => (
        <div className="flex flex-col">
          <span className="text-sm text-text-primary">{row.original.actorUserId}</span>
          {row.original.actorUserType && (
            <span className="text-xs text-text-muted">{row.original.actorUserType}</span>
          )}
        </div>
      ),
    },
    {
      accessorKey: 'action',
      header: 'Ação',
      cell: ({ getValue }) => {
        const a = String(getValue())
        return <Badge variant={ACTION_COLOR[a] ?? 'gray'}>{a}</Badge>
      },
    },
    {
      accessorKey: 'resourceType',
      header: 'Recurso',
      cell: ({ getValue }) => (
        <Badge variant="blue">{RESOURCE_LABEL[String(getValue())] ?? String(getValue())}</Badge>
      ),
    },
    {
      accessorKey: 'resourceId',
      header: 'ID',
      cell: ({ getValue }) => (
        <span className="font-mono text-xs text-text-secondary">{String(getValue())}</span>
      ),
    },
    {
      accessorKey: 'projectId',
      header: 'Projeto',
      cell: ({ getValue }) => {
        const v = getValue() as string | undefined
        return v ? <span className="text-xs text-text-muted">{v}</span> : <span className="text-text-dimmed">—</span>
      },
    },
  ]

  return (
    <div className="flex flex-col gap-6 p-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Admin Audit Log</h1>
          <p className="text-sm text-text-muted mt-1">
            Trilha de mudanças CRUD em projetos, agentes, workflows, skills e model pricing.
          </p>
        </div>
      </div>

      <Card title="Filtros">
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
          <Input
            label="De"
            type="datetime-local"
            value={from}
            onChange={(e) => setFrom(e.target.value)}
          />
          <Input
            label="Até"
            type="datetime-local"
            value={to}
            onChange={(e) => setTo(e.target.value)}
          />
          <div className="flex flex-col gap-1">
            <label className="text-xs text-text-muted">Projeto</label>
            <select
              value={projectId}
              onChange={(e) => setProjectId(e.target.value)}
              className="bg-bg-tertiary border border-border-secondary rounded-md px-2.5 py-1.5 text-sm text-text-primary focus:outline-none focus:border-accent-blue"
            >
              <option value="">Todos os projetos</option>
              {projects.map((p) => (
                <option key={p.id} value={p.id}>{p.name}</option>
              ))}
            </select>
          </div>
          <div className="flex flex-col gap-1">
            <label className="text-xs text-text-muted">Recurso</label>
            <select
              value={resourceType}
              onChange={(e) => setResourceType(e.target.value)}
              className="bg-bg-tertiary border border-border-secondary rounded-md px-2.5 py-1.5 text-sm text-text-primary focus:outline-none focus:border-accent-blue"
            >
              <option value="">Todos</option>
              {Object.entries(RESOURCE_LABEL).map(([value, label]) => (
                <option key={value} value={value}>{label}</option>
              ))}
            </select>
          </div>
          <Input
            label="Actor (UserId)"
            type="text"
            placeholder="ex: 123456789"
            value={actorUserId}
            onChange={(e) => setActorUserId(e.target.value)}
          />
          <Select
            label="Itens por página"
            value={pageSize}
            onChange={(e) => setPageSize(e.target.value)}
            options={PAGE_SIZE_OPTIONS_AUDIT}
          />
        </div>
      </Card>

      <Card title={`Entradas (${data?.total ?? 0} total · exibindo ${items.length})`} padding={false}>
        {items.length === 0 ? (
          <EmptyState
            title="Nenhuma entrada"
            description="Nenhuma mudança administrativa registrada no filtro selecionado."
          />
        ) : (
          <DataTable
            data={items}
            columns={columns}
            searchPlaceholder="Buscar entrada..."
            onRowClick={(row) => setSelected(row)}
          />
        )}
      </Card>

      <Modal
        open={selected !== null}
        onClose={() => setSelected(null)}
        title="Detalhes da Entrada"
        size="lg"
      >
        {selected && (
          <div className="flex flex-col gap-4">
            <div className="grid grid-cols-2 gap-3 text-sm">
              <Field label="Quando" value={new Date(selected.timestamp).toLocaleString('pt-BR')} />
              <Field label="Actor" value={`${selected.actorUserId}${selected.actorUserType ? ` (${selected.actorUserType})` : ''}`} />
              <Field label="Ação" value={selected.action} />
              <Field label="Recurso" value={`${RESOURCE_LABEL[selected.resourceType] ?? selected.resourceType} · ${selected.resourceId}`} />
              <Field label="Tenant" value={selected.tenantId ?? '—'} />
              <Field label="Projeto" value={selected.projectId ?? '—'} />
            </div>
            {selected.payloadBefore !== undefined && selected.payloadBefore !== null && (
              <div>
                <p className="text-sm text-text-muted mb-2">Payload antes:</p>
                <JsonViewer data={selected.payloadBefore} collapsed={false} />
              </div>
            )}
            {selected.payloadAfter !== undefined && selected.payloadAfter !== null && (
              <div>
                <p className="text-sm text-text-muted mb-2">Payload depois:</p>
                <JsonViewer data={selected.payloadAfter} collapsed={false} />
              </div>
            )}
          </div>
        )}
      </Modal>
    </div>
  )
}

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <span className="text-text-muted">{label}:</span>
      <p className="text-text-primary mt-1 break-words">{value}</p>
    </div>
  )
}
