import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { type ColumnDef } from '@tanstack/react-table'
import { Button } from '../../shared/ui/Button'
import { Card } from '../../shared/ui/Card'
import { Badge } from '../../shared/ui/Badge'
import { Textarea } from '../../shared/ui/Textarea'
import { DataTable } from '../../shared/data/DataTable'
import { StatusPill } from '../../shared/data/StatusPill'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { useWorkflow, useTriggerWorkflow, useWorkflowExecutions } from '../../api/workflows'
import type { WorkflowExecution } from '../../api/workflows'


const EXAMPLE_INPUT = JSON.stringify({ message: 'Hello, workflow!', context: {} }, null, 2)

function isValidJson(str: string): boolean {
  try {
    JSON.parse(str)
    return true
  } catch {
    return false
  }
}


export function WorkflowTriggerPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { data: workflow, isLoading: workflowLoading } = useWorkflow(id!, !!id)
  const { data: executions, isLoading: executionsLoading, refetch: refetchExecutions } =
    useWorkflowExecutions(id!, { pageSize: 20 })
  const triggerMutation = useTriggerWorkflow()

  const [input, setInput] = useState(EXAMPLE_INPUT)
  const [lastExecutionId, setLastExecutionId] = useState<string | null>(null)

  const handleTrigger = () => {
    if (!id || !isValidJson(input)) return

    triggerMutation.mutate(
      { id, body: { input } },
      {
        onSuccess: (data) => {
          setLastExecutionId(data.executionId)
          refetchExecutions()
        },
      },
    )
  }

  const columns: ColumnDef<WorkflowExecution, unknown>[] = [
    {
      accessorKey: 'executionId',
      header: 'Execution ID',
      cell: ({ getValue }) => (
        <code className="text-xs font-mono text-text-secondary">
          {(getValue() as 'Running' | 'Completed' | 'Failed' | 'Paused' | 'Cancelled' | 'Pending').slice(0, 8)}...
        </code>
      ),
    },
    {
      accessorKey: 'status',
      header: 'Status',
      cell: ({ getValue }) => <StatusPill status={getValue() as 'Running' | 'Completed' | 'Failed' | 'Paused' | 'Cancelled' | 'Pending'} />,
    },
    {
      accessorKey: 'startedAt',
      header: 'Started At',
      cell: ({ getValue }) => {
        const v = getValue() as 'Running' | 'Completed' | 'Failed' | 'Paused' | 'Cancelled' | 'Pending'
        return <span className="text-sm text-text-muted">{new Date(v).toLocaleString('pt-BR')}</span>
      },
    },
    {
      accessorKey: 'completedAt',
      header: 'Completed At',
      cell: ({ getValue }) => {
        const v = getValue() as 'Running' | 'Completed' | 'Failed' | 'Paused' | 'Cancelled' | 'Pending' | undefined
        return (
          <span className="text-sm text-text-muted">
            {v ? new Date(v).toLocaleString('pt-BR') : '—'}
          </span>
        )
      },
    },
    {
      id: 'duration',
      header: 'Duration',
      cell: ({ row }) => {
        const start = new Date(row.original.startedAt).getTime()
        const end = row.original.completedAt
          ? new Date(row.original.completedAt).getTime()
          : null
        if (!end) return <span className="text-text-dimmed">—</span>
        const ms = end - start
        const s = Math.floor(ms / 1000)
        return (
          <span className="text-sm text-text-secondary">
            {s < 60 ? `${s}s` : `${Math.floor(s / 60)}m ${s % 60}s`}
          </span>
        )
      },
    },
    {
      id: 'actions',
      header: '',
      cell: ({ row }) => (
        <Button
          variant="ghost"
          size="sm"
          onClick={(e) => {
            e.stopPropagation()
            navigate(`/executions/${row.original.executionId}`)
          }}
        >
          Ver →
        </Button>
      ),
    },
  ]

  if (workflowLoading) return <PageLoader />

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center gap-3 flex-wrap">
        <Link to={`/workflows/${id}`}>
          <Button variant="ghost" size="sm">
            &larr; Editar
          </Button>
        </Link>
        <Link to="/workflows">
          <Button variant="ghost" size="sm">
            Workflows
          </Button>
        </Link>
        <div className="flex-1">
          <h1 className="text-2xl font-bold text-text-primary">
            Trigger: {workflow?.name ?? id}
          </h1>
          <p className="text-sm text-text-muted mt-1">
            Execute o workflow manualmente fornecendo um input JSON.
          </p>
        </div>
      </div>

      <Card
        title="Executar Workflow"
        actions={
          workflow?.trigger && (
            <Badge variant={workflow.trigger.enabled ? 'green' : 'red'}>
              Trigger {workflow.trigger.enabled ? 'Ativo' : 'Inativo'}
            </Badge>
          )
        }
      >
        <div className="flex flex-col gap-4">
          <div className="flex flex-col gap-2">
            <label className="text-xs font-medium text-text-muted">Input JSON</label>
            <div className="font-mono">
              <Textarea
                value={input}
                onChange={(e) => setInput(e.target.value)}
                rows={8}
                placeholder={EXAMPLE_INPUT}
              />
            </div>
            {input.trim() && !isValidJson(input) && (
              <span className="text-xs text-red-400">JSON inválido. Verifique a sintaxe.</span>
            )}
          </div>

          <div className="flex items-center justify-between">
            <button
              type="button"
              className="text-xs text-accent-blue hover:underline"
              onClick={() => setInput(EXAMPLE_INPUT)}
            >
              Usar exemplo
            </button>
            <Button
              onClick={handleTrigger}
              loading={triggerMutation.isPending}
              disabled={!input.trim() || !isValidJson(input)}
            >
              ▶ Executar
            </Button>
          </div>
        </div>
      </Card>

      {lastExecutionId && !triggerMutation.isPending && (
        <div className="bg-emerald-500/10 border border-emerald-500/30 rounded-lg px-4 py-3 flex items-center justify-between">
          <div>
            <p className="text-sm font-medium text-emerald-400">Workflow executado com sucesso!</p>
            <p className="text-xs text-emerald-400/70 mt-0.5">
              Execution ID:{' '}
              <code className="font-mono">{lastExecutionId}</code>
            </p>
          </div>
          <Button
            variant="secondary"
            size="sm"
            onClick={() => navigate(`/executions/${lastExecutionId}`)}
          >
            Ver Execucao →
          </Button>
        </div>
      )}

      {triggerMutation.error && (
        <div className="bg-red-500/10 border border-red-500/30 rounded-lg px-4 py-3 text-sm text-red-400">
          Erro ao executar workflow: {(triggerMutation.error as Error).message}
        </div>
      )}

      <Card title="Ultimas Execucoes" padding={false}>
        {executionsLoading ? (
          <div className="p-6 text-center text-sm text-text-muted">Carregando execucoes...</div>
        ) : (executions ?? []).length === 0 ? (
          <div className="p-6 text-center">
            <p className="text-sm text-text-muted">Nenhuma execucao encontrada.</p>
            <p className="text-xs text-text-dimmed mt-1">
              Execute o workflow acima para criar a primeira execucao.
            </p>
          </div>
        ) : (
          <DataTable
            data={executions ?? []}
            columns={columns}
            onRowClick={(row) => navigate(`/executions/${row.executionId}`)}
          />
        )}
      </Card>
    </div>
  )
}
