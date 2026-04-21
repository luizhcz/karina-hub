import { useState } from 'react'
import { useNavigate, Link } from 'react-router'
import { Card } from '../../shared/ui/Card'
import { Button } from '../../shared/ui/Button'
import { Select } from '../../shared/ui/Select'
import { EmptyState } from '../../shared/ui/EmptyState'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { useWorkflows } from '../../api/workflows'
import {
  useCreateConversation,
  useUserConversations,
} from '../../api/chat'
import { useUserStore } from '../../stores/user'

export function ChatPage() {
  const navigate = useNavigate()
  const { userId } = useUserStore()

  const [workflowId, setWorkflowId] = useState('')

  const { data: workflows, isLoading: workflowsLoading } = useWorkflows()
  const { data: userConversations, isLoading: convsLoading, error: convsError, refetch } = useUserConversations(userId, !!userId)
  const createConversation = useCreateConversation()

  const workflowOptions = (workflows ?? [])
    .filter((w) => w.configuration?.inputMode?.toLowerCase() === 'chat')
    .map((w) => ({ label: w.name, value: w.id }))

  const handleStart = async () => {
    if (!workflowId) return
    const result = await createConversation.mutateAsync({ workflowId })
    navigate(`/chat/${result.conversationId}`)
  }

  return (
    <div className="flex flex-col gap-8 max-w-2xl mx-auto">
      {/* Header */}
      <div>
        <h1 className="text-2xl font-bold text-text-primary">Nova Conversa</h1>
        <p className="text-sm text-text-muted mt-1">
          Configure e inicie uma nova conversa com um workflow.
        </p>
      </div>

      {/* Form */}
      <Card title="Configurar Conversa">
        <div className="flex flex-col gap-4">
          <Select
            label="Workflow (modo Chat)"
            value={workflowId}
            onChange={(e) => setWorkflowId(e.target.value)}
            options={workflowOptions}
            placeholder={
              workflowsLoading
                ? 'Carregando...'
                : workflowOptions.length === 0
                ? 'Nenhum workflow em modo Chat disponível'
                : 'Selecione um workflow'
            }
            disabled={workflowsLoading || workflowOptions.length === 0}
          />

          <div className="pt-2">
            <Button
              variant="primary"
              onClick={handleStart}
              loading={createConversation.isPending}
              disabled={!workflowId}
            >
              Iniciar Conversa
            </Button>
          </div>

          {createConversation.isError && (
            <p className="text-sm text-red-400">
              Erro ao criar conversa. Tente novamente.
            </p>
          )}
        </div>
      </Card>

      {/* Recent conversations */}
      <div className="flex flex-col gap-4">
        <h2 className="text-lg font-semibold text-text-primary">Conversas Recentes</h2>

        {convsLoading && <PageLoader />}
        {convsError && (
          <ErrorCard message="Erro ao carregar conversas." onRetry={refetch} />
        )}

        {!convsLoading && !convsError && (!userConversations || userConversations.length === 0) && (
          <EmptyState
            title="Nenhuma conversa encontrada"
            description="Inicie uma nova conversa acima."
          />
        )}

        {!convsLoading && userConversations && userConversations.length > 0 && (
          <div className="flex flex-col gap-3">
            {userConversations.map((conv) => (
              <Card key={conv.conversationId} className="hover:border-accent-blue/40 transition-colors">
                <div className="flex items-center justify-between">
                  <div className="flex flex-col gap-0.5 min-w-0">
                    <p className="text-sm font-medium text-text-primary truncate">
                      {conv.title ?? conv.conversationId}
                    </p>
                    <p className="text-xs text-text-muted">
                      Workflow: <span className="text-text-secondary">{conv.workflowId}</span>
                      {' · '}
                      {new Date(conv.lastMessageAt).toLocaleString('pt-BR')}
                    </p>
                  </div>
                  <Link to={`/chat/${conv.conversationId}`}>
                    <Button variant="secondary" size="sm">
                      Continuar
                    </Button>
                  </Link>
                </div>
              </Card>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
