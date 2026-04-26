import { Link, useNavigate } from 'react-router'
import { Button } from '../../shared/ui/Button'
import { useCreateWorkflow } from '../../api/workflows'
import { ApiError } from '../../api/client'
import { WorkflowForm } from './components/WorkflowForm'
import { formValuesToWorkflowRequest } from './formMapping'
import type { WorkflowFormValues } from './types'

export function WorkflowCreatePage() {
  const navigate = useNavigate()
  const createMutation = useCreateWorkflow()

  const handleSubmit = (values: WorkflowFormValues) => {
    const body = formValuesToWorkflowRequest(values)
    createMutation.mutate(body, {
      onSuccess: () => navigate('/workflows'),
    })
  }

  const apiError = createMutation.error
  const invariantErrors = apiError instanceof ApiError ? apiError.invariantErrors : undefined

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center gap-3">
        <Link to="/workflows">
          <Button variant="ghost" size="sm">
            &larr; Workflows
          </Button>
        </Link>
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Novo Workflow</h1>
          <p className="text-sm text-text-muted mt-1">
            Configure um novo workflow de orquestração de agentes.
          </p>
        </div>
      </div>

      <WorkflowForm
        onSubmit={handleSubmit}
        loading={createMutation.isPending}
        apiErrors={invariantErrors}
      />

      {apiError && !invariantErrors && (
        <div className="bg-red-500/10 border border-red-500/30 rounded-lg px-4 py-3 text-sm text-red-400">
          Erro ao criar workflow: {(apiError as Error).message}
        </div>
      )}
    </div>
  )
}
