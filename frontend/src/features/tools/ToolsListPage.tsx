import { useState } from 'react'
import type { ColumnDef } from '@tanstack/react-table'
import { useFunctions } from '../../api/tools'
import type { FunctionToolInfo, CodeExecutorInfo, MiddlewareTypeInfo } from '../../api/tools'
import { DataTable } from '../../shared/data/DataTable'
import { Card } from '../../shared/ui/Card'
import { Badge } from '../../shared/ui/Badge'
import { Button } from '../../shared/ui/Button'
import { Modal } from '../../shared/ui/Modal'
import { JsonViewer } from '../../shared/data/JsonViewer'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { EmptyState } from '../../shared/ui/EmptyState'
import { useToolUsage } from './useToolUsage'
import { UsedByBadge } from './components/UsedByBadge'

// ── Modal que mostra schema completo + fingerprint ────────────────────────────

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

// ── Middleware card ───────────────────────────────────────────────────────────

// Cor do badge por phase. Pre/Post/Both cada um reforça semanticamente
// onde o middleware roda na pipeline LLM.
const PHASE_VARIANT: Record<string, 'blue' | 'green' | 'purple'> = {
  Pre: 'blue',
  Post: 'green',
  Both: 'purple',
}

function MiddlewareCard({
  middleware,
  usedBy,
}: {
  middleware: MiddlewareTypeInfo
  usedBy: { id: string; name: string }[]
}) {
  const phase = middleware.phase ?? 'Both'
  const settings = middleware.settings ?? []

  return (
    <Card>
      <div className="flex flex-col gap-3">
        {/* Header: nome + phase */}
        <div className="flex items-start justify-between flex-wrap gap-2">
          <div className="flex items-center gap-2">
            <span className="font-mono text-lg font-semibold text-text-primary">
              {middleware.name}
            </span>
            <Badge variant={PHASE_VARIANT[phase] ?? 'gray'}>{phase}</Badge>
          </div>
          <UsedByBadge
            usedBy={usedBy}
            hrefBase="/agents"
            resourceLabel="agents"
            modalTitle={`Agents com ${middleware.name} habilitado`}
          />
        </div>

        {/* Label — texto humano destacado */}
        {middleware.label && (
          <p className="text-sm font-medium text-text-primary">{middleware.label}</p>
        )}

        {/* Descrição detalhada */}
        {middleware.description && (
          <p className="text-xs text-text-muted leading-relaxed">{middleware.description}</p>
        )}

        {/* Settings */}
        <div>
          <p className="text-xs font-medium text-text-muted uppercase tracking-wide mb-2">
            Configurações ({settings.length})
          </p>
          {settings.length === 0 ? (
            <p className="text-xs text-text-dimmed">Sem configurações extras — middleware roda com comportamento fixo.</p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-xs">
                <thead>
                  <tr className="border-b border-border-primary">
                    <th className="text-left py-1.5 px-2 font-medium text-text-muted">Key</th>
                    <th className="text-left py-1.5 px-2 font-medium text-text-muted">Label</th>
                    <th className="text-left py-1.5 px-2 font-medium text-text-muted">Tipo</th>
                    <th className="text-left py-1.5 px-2 font-medium text-text-muted">Default / Opções</th>
                  </tr>
                </thead>
                <tbody>
                  {settings.map((s) => (
                    <tr key={s.key} className="border-b border-border-primary/50 last:border-0">
                      <td className="py-1.5 px-2 font-mono text-text-primary">{s.key}</td>
                      <td className="py-1.5 px-2 text-text-secondary">{s.label}</td>
                      <td className="py-1.5 px-2">
                        <Badge variant={s.type === 'select' ? 'purple' : 'gray'}>{s.type}</Badge>
                      </td>
                      <td className="py-1.5 px-2">
                        {s.type === 'select' && s.options ? (
                          <div className="flex flex-wrap gap-1">
                            {s.options.map((opt) => (
                              <Badge
                                key={opt.value}
                                variant={opt.value === s.defaultValue ? 'blue' : 'gray'}
                              >
                                {opt.label}
                              </Badge>
                            ))}
                          </div>
                        ) : (
                          <code className="text-xs text-text-dimmed">{s.defaultValue || '—'}</code>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>
    </Card>
  )
}

// ── Main page ─────────────────────────────────────────────────────────────────

export function ToolsListPage() {
  const { data: funcs, isLoading, error, refetch } = useFunctions()
  const usage = useToolUsage()
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
      id: 'usedBy',
      header: 'Usado por',
      cell: ({ row }) => (
        <UsedByBadge
          usedBy={usage.functions.get(row.original.name) ?? []}
          hrefBase="/agents"
          resourceLabel="agents"
          modalTitle={`Agents que usam ${row.original.name}`}
        />
      ),
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
    {
      id: 'usedBy',
      header: 'Usado por',
      cell: ({ row }) => (
        <UsedByBadge
          usedBy={usage.executors.get(row.original.name) ?? []}
          hrefBase="/workflows"
          resourceLabel="workflows"
          modalTitle={`Workflows que usam ${row.original.name}`}
        />
      ),
    },
  ]

  const total = functionTools.length + codeExecutors.length + middlewareTypes.length

  return (
    <div className="flex flex-col gap-6 p-6">
      <div>
        <h1 className="text-2xl font-bold text-text-primary">Tools</h1>
        <p className="text-sm text-text-muted mt-1">
          {total} peça(s) registrada(s) no runtime — nomes em inglês, descrições em português.
        </p>
      </div>

      {/* Function Tools */}
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

      {/* Code Executors */}
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

      {/* Middlewares — cards completos com Phase/Label/Description/Settings */}
      <div className="flex flex-col gap-2">
        <h2 className="text-sm font-semibold text-text-secondary uppercase tracking-wide">
          Middlewares ({middlewareTypes.length})
        </h2>
        {middlewareTypes.length === 0 ? (
          <Card>
            <EmptyState
              title="Nenhum middleware registrado"
              description="Registre middlewares via IAgentMiddlewareRegistry no Program.cs."
            />
          </Card>
        ) : (
          <div className="flex flex-col gap-3">
            {middlewareTypes.map((m) => (
              <MiddlewareCard
                key={m.name}
                middleware={m}
                usedBy={usage.middlewares.get(m.name) ?? []}
              />
            ))}
          </div>
        )}
      </div>

      <SchemaModal
        open={selectedFn !== null}
        onClose={() => setSelectedFn(null)}
        tool={selectedFn}
      />
    </div>
  )
}
