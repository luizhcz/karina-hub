import { useState } from 'react'
import type { ColumnDef } from '@tanstack/react-table'
import { useFunctions } from '../../api/tools'
import type { FunctionToolInfo, CodeExecutorInfo } from '../../api/tools'
import { DataTable } from '../../shared/data/DataTable'
import { Card } from '../../shared/ui/Card'
import { Badge } from '../../shared/ui/Badge'
import { Button } from '../../shared/ui/Button'
import { Modal } from '../../shared/ui/Modal'
import { JsonViewer } from '../../shared/data/JsonViewer'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { EmptyState } from '../../shared/ui/EmptyState'

function SchemaModal({
  open,
  onClose,
  tool,
}: {
  open: boolean
  onClose: () => void
  tool: FunctionToolInfo | null
}) {
  if (!tool) return null
  return (
    <Modal open={open} onClose={onClose} title={tool.name} size="lg">
      <div className="flex flex-col gap-4">
        {tool.description && (
          <p className="text-sm text-text-secondary">{tool.description}</p>
        )}
        {tool.fingerprint && (
          <div className="flex items-center gap-2">
            <span className="text-xs text-text-muted">Fingerprint:</span>
            <code className="text-xs font-mono text-text-dimmed bg-bg-tertiary px-2 py-0.5 rounded">
              {tool.fingerprint}
            </code>
          </div>
        )}
        <div>
          <p className="text-xs font-medium text-text-muted uppercase tracking-wide mb-2">Input Schema</p>
          {tool.jsonSchema ? (
            <JsonViewer data={tool.jsonSchema} collapsed={false} maxHeight="400px" />
          ) : (
            <span className="text-xs text-text-dimmed">Schema não disponível</span>
          )}
        </div>
      </div>
    </Modal>
  )
}

export function ToolsListPage() {
  const { data: funcs, isLoading, error, refetch } = useFunctions()
  const [selectedFn, setSelectedFn] = useState<FunctionToolInfo | null>(null)

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar tools" onRetry={refetch} />

  const functionTools = funcs?.functionTools ?? []
  const codeExecutors = funcs?.codeExecutors ?? []
  const middlewareTypes = funcs?.middlewareTypes ?? []

  const fnColumns: ColumnDef<FunctionToolInfo, unknown>[] = [
    {
      accessorKey: 'name',
      header: 'Nome',
      cell: ({ getValue }) => (
        <span className="font-mono text-sm text-text-primary font-medium">{String(getValue())}</span>
      ),
    },
    {
      accessorKey: 'description',
      header: 'Descrição',
      cell: ({ getValue }) => {
        const v = getValue() as string | undefined
        return <span className="text-xs text-text-muted">{v ?? '—'}</span>
      },
    },
    {
      id: 'schema',
      header: 'Schema',
      cell: ({ row }) => (
        <Button
          variant="secondary"
          size="sm"
          onClick={(e) => {
            e.stopPropagation()
            setSelectedFn(row.original)
          }}
        >
          Ver Schema
        </Button>
      ),
    },
  ]

  const executorColumns: ColumnDef<CodeExecutorInfo, unknown>[] = [
    {
      accessorKey: 'name',
      header: 'Nome',
      cell: ({ getValue }) => (
        <span className="font-mono text-sm text-text-primary font-medium">{String(getValue())}</span>
      ),
    },
    {
      id: 'io',
      header: 'Input → Output',
      cell: ({ row }) => {
        const { inputType, outputType } = row.original
        if (!inputType && !outputType) {
          return <span className="text-xs text-text-dimmed">lambda pura</span>
        }
        return (
          <div className="flex items-center gap-1.5 text-xs font-mono">
            <Badge variant="blue">{inputType ?? '?'}</Badge>
            <span className="text-text-dimmed">→</span>
            <Badge variant="purple">{outputType ?? '?'}</Badge>
          </div>
        )
      },
    },
  ]

  const total = functionTools.length + codeExecutors.length + middlewareTypes.length

  return (
    <div className="flex flex-col gap-6 p-6">
      <div>
        <h1 className="text-2xl font-bold text-text-primary">Tools</h1>
        <p className="text-sm text-text-muted mt-1">
          {total} peça(s) registrada(s) no runtime
        </p>
      </div>

      <div className="flex flex-col gap-2">
        <h2 className="text-sm font-semibold text-text-secondary uppercase tracking-wide">
          Function Tools ({functionTools.length})
        </h2>
        <Card padding={false}>
          {functionTools.length === 0 ? (
            <EmptyState
              title="Nenhuma function tool registrada"
              description="Registre tools via IFunctionToolRegistry no Program.cs."
            />
          ) : (
            <DataTable
              data={functionTools}
              columns={fnColumns}
              searchPlaceholder="Buscar function tool..."
              onRowClick={(row) => setSelectedFn(row)}
            />
          )}
        </Card>
      </div>

      <div className="flex flex-col gap-2">
        <h2 className="text-sm font-semibold text-text-secondary uppercase tracking-wide">
          Code Executors ({codeExecutors.length})
        </h2>
        <Card padding={false}>
          {codeExecutors.length === 0 ? (
            <EmptyState
              title="Nenhum code executor registrado"
              description="Registre executores via ICodeExecutorRegistry no Program.cs."
            />
          ) : (
            <DataTable
              data={codeExecutors}
              columns={executorColumns}
              searchPlaceholder="Buscar executor..."
            />
          )}
        </Card>
      </div>

      <div className="flex flex-col gap-2">
        <h2 className="text-sm font-semibold text-text-secondary uppercase tracking-wide">
          Middlewares ({middlewareTypes.length})
        </h2>
        <Card>
          {middlewareTypes.length === 0 ? (
            <EmptyState
              title="Nenhum middleware registrado"
              description="Registre middlewares via IAgentMiddlewareRegistry no Program.cs."
            />
          ) : (
            <div className="flex flex-wrap gap-2">
              {middlewareTypes.map((m) => (
                <Badge key={m.name} variant="gray">
                  {m.name}
                </Badge>
              ))}
            </div>
          )}
        </Card>
      </div>

      <SchemaModal
        open={selectedFn !== null}
        onClose={() => setSelectedFn(null)}
        tool={selectedFn}
      />
    </div>
  )
}
