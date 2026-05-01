import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { Button } from '../../shared/ui/Button'
import { Badge } from '../../shared/ui/Badge'
import { Card } from '../../shared/ui/Card'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { useWorkflow, useUpdateWorkflow, useUpdateWorkflowVisibility } from '../../api/workflows'
import type { WorkflowVisibility } from '../../api/workflows'
import { ApiError } from '../../api/client'
import { toast } from '../../stores/toast'
import { useProjectStore } from '../../stores/project'
import { WorkflowForm } from './components/WorkflowForm'
import { formValuesToWorkflowRequest } from './formMapping'
import type { WorkflowFormValues } from './types'

export function WorkflowEditPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { data: workflow, isLoading, error, refetch } = useWorkflow(id!, !!id)
  const updateMutation = useUpdateWorkflow()
  const visibilityMutation = useUpdateWorkflowVisibility()
  const [pendingVisibility, setPendingVisibility] = useState<WorkflowVisibility | null>(null)

  if (isLoading) return <PageLoader />
  if (error || !workflow)
    return <ErrorCard message="Erro ao carregar workflow." onRetry={refetch} />

  const currentVisibility: WorkflowVisibility = workflow.visibility ?? 'project'
  const isGlobal = currentVisibility === 'global'
  const currentProjectId = useProjectStore.getState().projectId
  // Toggle só faz sentido pro project owner. Quando workflow é global de outro
  // projeto sendo visualizado pelo consumer, o backend retornaria 403 — UI já
  // bloqueia pra evitar afford disabled.
  const isOwnedByCurrentProject = !workflow.originProjectId
    || workflow.originProjectId === currentProjectId

  const handleSubmit = (values: WorkflowFormValues) => {
    const body = formValuesToWorkflowRequest(values)
    updateMutation.mutate(
      { id: id!, body },
      {
        onSuccess: () => navigate('/workflows'),
        onError: (err) => {
          const msg = err instanceof ApiError ? err.message : 'Erro ao salvar workflow.'
          toast.error(msg)
        },
      },
    )
  }

  const confirmVisibilityChange = () => {
    if (!pendingVisibility) return
    visibilityMutation.mutate(
      { id: id!, visibility: pendingVisibility },
      {
        onSuccess: () => {
          toast.success(
            pendingVisibility === 'global'
              ? 'Workflow agora visível em todos os projetos do tenant.'
              : 'Workflow agora restrito ao projeto dono.',
          )
          setPendingVisibility(null)
        },
        onError: (err) => {
          const msg = err instanceof ApiError ? err.message : 'Erro ao alterar visibility.'
          toast.error(msg)
          setPendingVisibility(null)
        },
      },
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

      <Card title="Compartilhamento">
        <div className="flex items-start justify-between gap-4">
          <div className="flex-1">
            <div className="flex items-center gap-2 flex-wrap">
              {isGlobal ? (
                <Badge variant="purple">🌐 Global</Badge>
              ) : (
                <Badge variant="gray">🔒 Privado</Badge>
              )}
              {!isOwnedByCurrentProject && (
                <Badge variant="yellow">Somente leitura — projeto {workflow.originProjectId}</Badge>
              )}
              <p className="text-sm text-text-secondary">
                {isGlobal
                  ? 'Visível em todos os projetos do tenant.'
                  : 'Visível apenas no projeto dono.'}
              </p>
            </div>
            <p className="text-xs text-text-dimmed mt-2 leading-relaxed">
              {!isOwnedByCurrentProject
                ? 'Você está vendo um workflow compartilhado de outro projeto. Apenas o projeto dono pode alterar visibility ou conteúdo.'
                : isGlobal
                  ? 'Outros projetos podem listar e clonar este workflow. A definição continua sendo editável apenas pelo projeto dono.'
                  : 'Tornar global permite que outros projetos do mesmo tenant vejam e usem este workflow no catálogo. Tenant boundary é estrito (cross-tenant nunca é permitido).'}
            </p>
          </div>
          <Button
            variant="secondary"
            size="sm"
            onClick={() => setPendingVisibility(isGlobal ? 'project' : 'global')}
            disabled={visibilityMutation.isPending || !isOwnedByCurrentProject}
          >
            {isGlobal ? 'Tornar privado' : 'Compartilhar globalmente'}
          </Button>
        </div>
      </Card>

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

      <ConfirmDialog
        open={pendingVisibility !== null}
        onClose={() => setPendingVisibility(null)}
        onConfirm={confirmVisibilityChange}
        title={pendingVisibility === 'global' ? 'Tornar global?' : 'Tornar privado?'}
        message={
          pendingVisibility === 'global'
            ? 'Outros projetos do tenant passarão a ver este workflow no catálogo. Você pode reverter a qualquer momento.'
            : 'O workflow deixará de aparecer em outros projetos. Workflows que já tenham sido clonados permanecem inalterados.'
        }
        confirmLabel={pendingVisibility === 'global' ? 'Compartilhar' : 'Tornar privado'}
        loading={visibilityMutation.isPending}
      />
    </div>
  )
}
