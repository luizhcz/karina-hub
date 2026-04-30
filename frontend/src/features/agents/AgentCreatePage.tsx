import { Link, useNavigate } from 'react-router'
import { Button } from '../../shared/ui/Button'
import { useCreateAgent, useAgents } from '../../api/agents'
import { ApiError } from '../../api/client'
import { AgentForm } from './components/AgentForm'
import { formToRequest } from './formToRequest'
import { toast } from '../../stores/toast'
import type { AgentFormValues } from './types'

export function AgentCreatePage() {
  const navigate = useNavigate()
  const createMutation = useCreateAgent()
  const { data: existingAgents } = useAgents()
  const existingIds = new Set((existingAgents ?? []).map((a) => a.id))

  const handleSubmit = (values: AgentFormValues) => {
    const result = formToRequest(values)
    if (!result.ok) {
      toast.error(result.error)
      return
    }
    createMutation.mutate(result.body, {
      onSuccess: () => navigate('/agents'),
      onError: (err) => {
        const msg = err instanceof ApiError ? err.message : 'Erro ao criar agente.'
        toast.error(msg)
      },
    })
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
          <h1 className="text-2xl font-bold text-text-primary">Novo Agente</h1>
          <p className="text-sm text-text-muted mt-1">Preencha os campos para criar um novo agente.</p>
        </div>
      </div>

      <AgentForm
        onSubmit={handleSubmit}
        loading={createMutation.isPending}
        existingIds={existingIds}
      />
    </div>
  )
}
