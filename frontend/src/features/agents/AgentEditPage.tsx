import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { Button } from '../../shared/ui/Button'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { useAgent, useUpdateAgent, type CreateAgentRequest } from '../../api/agents'
import { ApiError } from '../../api/client'
import { AgentForm } from './components/AgentForm'
import { UpdateAgentReasonModal } from './components/UpdateAgentReasonModal'
import { formToRequest } from './formToRequest'
import { toast } from '../../stores/toast'
import type { AgentFormValues } from './types'

export function AgentEditPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { data: agent, isLoading, error, refetch } = useAgent(id!, !!id)
  const updateMutation = useUpdateAgent()

  // Backend exige changeReason (min 10 chars) — toda update vira AdminOverride
  // no audit. Coletamos via modal antes do submit.
  const [pendingBody, setPendingBody] = useState<CreateAgentRequest | null>(null)

  if (isLoading) return <PageLoader />
  if (error || !agent) return <ErrorCard message="Erro ao carregar agente." onRetry={refetch} />

  const handleSubmit = (values: AgentFormValues) => {
    const result = formToRequest(values)
    if (!result.ok) {
      toast.error(result.error)
      return
    }
    setPendingBody(result.body)
  }

  const handleConfirm = (changeReason: string, breakingChange: boolean) => {
    if (!pendingBody) return
    updateMutation.mutate(
      { id: id!, body: { ...pendingBody, changeReason, breakingChange } },
      {
        onSuccess: () => {
          setPendingBody(null)
          navigate('/agents')
        },
        onError: (err) => {
          const msg = err instanceof ApiError ? err.message : 'Erro ao salvar agente.'
          toast.error(msg)
        },
      },
    )
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center gap-3">
        <Link to="/agents">
          <Button variant="ghost" size="sm">
            &larr; Agentes
          </Button>
        </Link>
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Editar: {agent.name}</h1>
          <p className="text-sm text-text-muted mt-1">Altere os campos e salve.</p>
        </div>
      </div>

      <AgentForm
        initialValues={agent}
        onSubmit={handleSubmit}
        loading={updateMutation.isPending}
      />

      <UpdateAgentReasonModal
        open={pendingBody !== null}
        loading={updateMutation.isPending}
        onClose={() => setPendingBody(null)}
        onConfirm={handleConfirm}
      />
    </div>
  )
}
