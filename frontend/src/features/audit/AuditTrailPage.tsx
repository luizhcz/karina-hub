import { useState } from 'react'
import { useAdminConversations } from '../../api/admin'
import type { ConversationSession } from '../../api/admin'
import { useProjects } from '../../api/projects'
import { Card } from '../../shared/ui/Card'
import { Input } from '../../shared/ui/Input'
import { Badge } from '../../shared/ui/Badge'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { EmptyState } from '../../shared/ui/EmptyState'

function typeIcon(workflowId: string): string {
  if (workflowId.includes('chat')) return '💬'
  if (workflowId.includes('exec')) return '⚡'
  if (workflowId.includes('admin')) return '🔧'
  return '📋'
}

function TimelineItem({ event }: { event: ConversationSession }) {
  return (
    <div className="flex gap-4 pb-6 relative">
      <div className="flex flex-col items-center">
        <div className="w-8 h-8 rounded-full bg-bg-tertiary border border-border-primary flex items-center justify-center text-sm z-10">
          {typeIcon(event.workflowId)}
        </div>
        <div className="w-px flex-1 bg-border-primary/40 mt-1" />
      </div>
      <div className="flex-1 pb-2">
        <div className="flex items-center gap-2 mb-1">
          <Badge variant="blue">{event.workflowId.slice(0, 20)}</Badge>
          <span className="text-xs text-text-dimmed">
            {new Date(event.createdAt).toLocaleString('pt-BR')}
          </span>
        </div>
        <p className="text-sm text-text-secondary">
          Usuário <span className="text-text-primary font-medium">{event.userId}</span>
          {event.userType ? ` (${event.userType})` : ''} iniciou conversa
        </p>
        <p className="text-xs text-text-muted mt-1">
          Última mensagem: {new Date(event.lastMessageAt).toLocaleString('pt-BR')}
        </p>
      </div>
    </div>
  )
}

export function AuditTrailPage() {
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [projectId, setProjectId] = useState('')

  const { data: projectsData } = useProjects()
  const projects = projectsData ?? []

  const { data, isLoading, error, refetch } = useAdminConversations({
    from: from || undefined,
    to: to || undefined,
    projectId: projectId || undefined,
    pageSize: 50,
  })

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar trilha de auditoria" onRetry={refetch} />

  const items = [...(data?.items ?? [])].sort(
    (a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
  )

  return (
    <div className="flex flex-col gap-6 p-6">
      <div>
        <h1 className="text-2xl font-bold text-text-primary">Audit Trail</h1>
        <p className="text-sm text-text-muted mt-1">Timeline visual de eventos de auditoria</p>
      </div>

      <Card title="Filtros">
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
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
        </div>
      </Card>

      <Card title={`Timeline (${items.length} eventos)`}>
        {items.length === 0 ? (
          <EmptyState title="Nenhum evento" description="Nenhum evento encontrado neste período." />
        ) : (
          <div className="pt-2">
            {items.map((event) => (
              <TimelineItem key={event.conversationId} event={event} />
            ))}
          </div>
        )}
      </Card>
    </div>
  )
}
