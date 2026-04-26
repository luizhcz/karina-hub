import { Link, useNavigate, useParams } from 'react-router'
import { Button } from '../../shared/ui/Button'
import { Badge } from '../../shared/ui/Badge'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { useWorkflow, useUpdateWorkflow } from '../../api/workflows'
import { ApiError } from '../../api/client'
import { WorkflowForm } from './components/WorkflowForm'
import { formValuesToWorkflowRequest } from './formMapping'
import type { WorkflowFormValues } from './types'

export function WorkflowEditPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { data: workflow, isLoading, error, refetch } = useWorkflow(id!, !!id)
  const updateMutation = useUpdateWorkflow()

  if (isLoading) return <PageLoader />
  if (error || !workflow)
    return <ErrorCard message="Erro ao carregar workflow." onRetry={refetch} />

  const handleSubmit = (values: WorkflowFormValues) => {
    const body = formValuesToWorkflowRequest(values)
    updateMutation.mutate(
      { id: id!, body },
      { onSuccess: () => navigate('/workflows') },
    )
  }

  const apiError = updateMutation.error
  const invariantErrors = apiError instanceof ApiError ? apiError.invariantErrors : undefined

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center gap-3 flex-wrap">
        <Link to="/workflows">
          <Button variant="ghost" size="sm">
            &larr; Workflows
          </Button>
        </Link>
        <div className="flex-1">
          <div className="flex items-center gap-2 flex-wrap">
            <h1 className="text-2xl font-bold text-text-primary">
              Editar: {workflow.name}
            </h1>
            <Badge variant="blue">{workflow.orchestrationMode}</Badge>
            {workflow.version && (
              <Badge variant="gray">v{workflow.version}</Badge>
            )}
          </div>
          <p className="text-sm text-text-muted mt-1">
            ID: <code className="font-mono text-xs text-text-secondary">{workflow.id}</code>
          </p>
        </div>

        <div className="flex items-center gap-2">
          <Button
            variant="secondary"
            size="sm"
            onClick={() => navigate(`/workflows/${id}/diagram`)}
          >
            Diagram
          </Button>
          <Button
            variant="secondary"
            size="sm"
            onClick={() => navigate(`/workflows/${id}/versions`)}
          >
            Versions
          </Button>
          <Button
            variant="secondary"
            size="sm"
            onClick={() => navigate(`/workflows/${id}/trigger`)}
          >
            Trigger
          </Button>
          {workflow.configuration?.inputMode?.toLowerCase() === 'chat' ? (
            <Button
              variant="secondary"
              size="sm"
              onClick={() => navigate('/chat')}
            >
              💬 Testar no Chat
            </Button>
          ) : (
            <Button
              variant="secondary"
              size="sm"
              onClick={() => navigate(`/workflows/${id}/sandbox`)}
            >
              Sandbox
            </Button>
          )}
        </div>
      </div>

      <WorkflowForm
        initialValues={workflow}
        onSubmit={handleSubmit}
        loading={updateMutation.isPending}
        isEdit
        apiErrors={invariantErrors}
      />

      {apiError && !invariantErrors && (
        <div className="bg-red-500/10 border border-red-500/30 rounded-lg px-4 py-3 text-sm text-red-400">
          Erro ao salvar workflow: {(apiError as Error).message}
        </div>
      )}
    </div>
  )
}
