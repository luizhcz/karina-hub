// Tela "Execuções" — lista paginada de workflow_executions com filtros
// (status, workflowId, período) e drawer pra detalhe individual.
//
// Project-scoping: o backend devolve cross-project; filtramos client-side
// via metadata.projectId. Trade-off conhecido: count `total` reflete o
// global do tenant, não o do projeto. Pra V1 aceito; quando o controller
// ganhar o mesmo padrão de scoping dos analytics endpoints, removo o filter
// client-side.

import { useCallback, useMemo, useState, type FormEvent } from 'react'
import {
  Button,
  Card,
  CardHeader,
  EmptyState,
  ErrorState,
  JsonViewer,
  Skeleton,
  StatusBadge,
  Table,
  cn,
  type Column,
} from '../components/ui'
import { DateRangePicker } from '../components/filters/DateRangePicker'
import { ProjectPicker } from '../components/filters/ProjectPicker'
import { useApi } from '../hooks/useApi'
import { useDateRange } from '../hooks/useDateRange'
import { useIdentity } from '../stores/identity'
import {
  executionDurationMs,
  getExecutionFull,
  listExecutions,
  type ExecutionAuditEvent,
  type ExecutionFull,
  type ExecutionItem,
  type ExecutionNode,
  type ExecutionStatus,
  type ExecutionToolCall,
} from '../api/executions'
import {
  formatInt,
  formatLatencyMs,
  formatBucketLabel,
  toIsoUtc,
} from '../utils/format'

// Status disponíveis pra filtro. Ordem reflete prioridade visual (ativos
// primeiro, terminais depois) — bate com o que o user procura olhando "fila".
const STATUS_OPTIONS: ReadonlyArray<{ value: ExecutionStatus; label: string }> = [
  { value: 'Running', label: 'Rodando' },
  { value: 'Pending', label: 'Fila' },
  { value: 'Paused', label: 'HITL' },
  { value: 'Failed', label: 'Falhou' },
  { value: 'Completed', label: 'Concluído' },
  { value: 'Cancelled', label: 'Cancelado' },
]

const PAGE_SIZE = 25

export function Execucoes() {
  const identity = useIdentity()
  const projectId = identity?.projectId ?? ''
  const { range, setPreset, setCustom } = useDateRange()
  const fromIso = useMemo(() => toIsoUtc(range.from), [range.from])
  const toIso = useMemo(() => toIsoUtc(range.to), [range.to])

  const [statusFilter, setStatusFilter] = useState<ExecutionStatus | ''>('')
  const [workflowFilter, setWorkflowFilter] = useState('')
  const [workflowDraft, setWorkflowDraft] = useState('')
  const [page, setPage] = useState(1)
  const [selectedId, setSelectedId] = useState<string | null>(null)

  const skip = !projectId

  // Lista o backend SEM filtro de projeto (não é scoped); filtramos client-side.
  // pageSize aumentado pra compensar parte das rows descartadas pelo filter
  // local (heurística: ~50% das execuções tendem a estar no projeto atual no
  // dev; em prod com mais projetos esse balanço muda). Follow-up: scoping
  // backend.
  const listState = useApi(
    useCallback(
      (signal) =>
        listExecutions(
          {
            status: statusFilter || undefined,
            workflowId: workflowFilter || undefined,
            from: fromIso,
            to: toIso,
            page,
            pageSize: PAGE_SIZE * 2,
          },
          { signal },
        ),
      [statusFilter, workflowFilter, fromIso, toIso, page],
    ),
    [statusFilter, workflowFilter, fromIso, toIso, page],
    { skip },
  )

  // Drawer puxa via /full porque /executions/{id} retorna steps=[] sempre —
  // a model não materializa essa coleção. /full traz nodes (com output,
  // tokens) + tools + events, que é o que faz a tela útil pra análise.
  const detailState = useApi(
    useCallback(
      (signal) => getExecutionFull(selectedId ?? '', { signal }),
      [selectedId],
    ),
    [selectedId],
    { skip: !selectedId },
  )

  const filteredItems = useMemo(() => {
    if (!listState.data) return []
    if (!projectId) return []
    return listState.data.items.filter((e) => e.metadata?.projectId === projectId)
  }, [listState.data, projectId])

  function applyWorkflowFilter(e: FormEvent) {
    e.preventDefault()
    setPage(1)
    setWorkflowFilter(workflowDraft.trim())
  }

  function clearFilters() {
    setStatusFilter('')
    setWorkflowFilter('')
    setWorkflowDraft('')
    setPage(1)
  }

  if (!projectId) {
    return (
      <div className="flex flex-col gap-4">
        <FilterRow
          range={range}
          onPresetChange={setPreset}
          onCustomChange={setCustom}
        />
        <Card>
          <CardHeader title="Selecione um projeto" description="Use o seletor acima pra ver execuções." />
        </Card>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-6">
      <FilterRow
        range={range}
        onPresetChange={setPreset}
        onCustomChange={setCustom}
      />

      <Card>
        <div className="flex flex-wrap items-center gap-3">
          <span className="text-xs font-medium text-fg-muted">Status:</span>
          <button
            type="button"
            onClick={() => {
              setStatusFilter('')
              setPage(1)
            }}
            className={cn(
              'rounded-md px-2.5 py-1 text-xs transition',
              statusFilter === ''
                ? 'bg-accent-subtle text-accent'
                : 'bg-surface-hover text-fg-muted hover:text-fg',
            )}
          >
            Todos
          </button>
          {STATUS_OPTIONS.map((opt) => (
            <button
              key={opt.value}
              type="button"
              onClick={() => {
                setStatusFilter(opt.value)
                setPage(1)
              }}
              className={cn(
                'rounded-md px-2.5 py-1 text-xs transition',
                statusFilter === opt.value
                  ? 'bg-accent-subtle text-accent'
                  : 'bg-surface-hover text-fg-muted hover:text-fg',
              )}
            >
              {opt.label}
            </button>
          ))}

          <span className="mx-2 h-4 w-px bg-border" />

          <form onSubmit={applyWorkflowFilter} className="flex items-center gap-2">
            <label className="text-xs font-medium text-fg-muted" htmlFor="workflow-filter">
              WorkflowId:
            </label>
            <input
              id="workflow-filter"
              type="text"
              value={workflowDraft}
              onChange={(e) => setWorkflowDraft(e.target.value)}
              placeholder="ex.: deploy-chat-..."
              className="h-7 w-56 rounded-md border border-border bg-surface px-2 text-xs text-fg focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/30"
            />
            {workflowDraft !== workflowFilter && (
              <Button type="submit" variant="ghost" className="h-7 px-2 text-xs">
                Aplicar
              </Button>
            )}
          </form>

          {(statusFilter || workflowFilter) && (
            <Button type="button" variant="ghost" className="h-7 px-2 text-xs" onClick={clearFilters}>
              Limpar
            </Button>
          )}
        </div>
      </Card>

      <Card padded={false}>
        <div className="p-5">
          <CardHeader
            title="Execuções"
            description={`Página ${page} · ${filteredItems.length} no projeto · ${formatInt(listState.data?.total ?? 0)} total no tenant`}
          />
        </div>
        {listState.error ? (
          <div className="px-5 pb-5">
            <ErrorState error={listState.error} onRetry={listState.refetch} />
          </div>
        ) : (
          <Table
            columns={executionColumns}
            rows={filteredItems}
            loading={listState.loading}
            keyOf={(row) => row.executionId}
            onRowClick={(row) => setSelectedId(row.executionId)}
            empty={{
              title: 'Nenhuma execução no filtro',
              description: 'Tente outro status ou ampliar o período.',
            }}
          />
        )}
        <div className="flex items-center justify-between gap-2 border-t border-border px-5 py-3">
          <span className="text-xs text-fg-dim">
            {filteredItems.length === PAGE_SIZE * 2
              ? 'Página cheia; pode haver mais nesta página'
              : `${filteredItems.length} item${filteredItems.length === 1 ? '' : 's'}`}
          </span>
          <div className="flex items-center gap-2">
            <Button
              variant="ghost"
              className="h-7 px-2 text-xs"
              disabled={page <= 1 || listState.loading}
              onClick={() => setPage((p) => Math.max(1, p - 1))}
            >
              ← Anterior
            </Button>
            <span className="text-xs text-fg-muted">Página {page}</span>
            <Button
              variant="ghost"
              className="h-7 px-2 text-xs"
              disabled={listState.loading || (listState.data?.items.length ?? 0) < PAGE_SIZE * 2}
              onClick={() => setPage((p) => p + 1)}
            >
              Próxima →
            </Button>
          </div>
        </div>
      </Card>

      {selectedId && (
        <ExecutionDrawer
          executionId={selectedId}
          state={detailState}
          onClose={() => setSelectedId(null)}
        />
      )}
    </div>
  )
}

const executionColumns: ReadonlyArray<Column<ExecutionItem>> = [
  {
    key: 'status',
    header: 'Status',
    cell: (r) => <StatusBadge status={r.status} />,
    sortBy: (r) => r.status,
  },
  {
    key: 'workflow',
    header: 'Workflow',
    cell: (r) => <span className="font-mono text-xs text-fg">{r.workflowId}</span>,
    sortBy: (r) => r.workflowId,
  },
  {
    key: 'execId',
    header: 'Execution',
    cell: (r) => (
      <span className="font-mono text-[11px] text-fg-dim" title={r.executionId}>
        {r.executionId.slice(0, 8)}…
      </span>
    ),
    sortBy: (r) => r.executionId,
  },
  {
    key: 'startedAt',
    header: 'Iniciou',
    cell: (r) => <span className="text-xs text-fg-muted">{formatBucketLabel(r.startedAt, 'hour')}</span>,
    sortBy: (r) => r.startedAt,
  },
  {
    key: 'duration',
    header: 'Duração',
    align: 'right',
    cell: (r) => formatLatencyMs(executionDurationMs(r)),
    sortBy: (r) => executionDurationMs(r) ?? 0,
  },
]

interface FilterRowProps {
  range: ReturnType<typeof useDateRange>['range']
  onPresetChange: (preset: Parameters<ReturnType<typeof useDateRange>['setPreset']>[0]) => void
  onCustomChange: (from: Date, to: Date) => void
}

// FilterHeader normal embute ProjectPicker + DateRangePicker; nessa tela quero
// só isso também — reuso direto pra coerência visual.
function FilterRow({ range, onPresetChange, onCustomChange }: FilterRowProps) {
  return (
    <div className="flex flex-wrap items-end gap-4 rounded-2xl border border-border bg-surface p-4">
      <ProjectPicker />
      <DateRangePicker range={range} onPresetChange={onPresetChange} onCustomChange={onCustomChange} />
    </div>
  )
}

interface ExecutionDrawerProps {
  executionId: string
  state: ReturnType<typeof useApi<ExecutionFull>>
  onClose: () => void
}

function ExecutionDrawer({ executionId, state, onClose }: ExecutionDrawerProps) {
  const full = state.data
  const exec = full?.execution
  return (
    <div
      className="fixed inset-0 z-40 flex justify-end bg-black/40 backdrop-blur-sm"
      role="dialog"
      onClick={onClose}
    >
      <div
        className="flex h-full w-full max-w-3xl flex-col border-l border-border bg-bg shadow-xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start justify-between border-b border-border p-5">
          <div className="min-w-0 flex-1">
            <div className="text-xs uppercase tracking-wide text-fg-dim">Execução</div>
            <div className="mt-1 truncate font-mono text-sm text-fg" title={executionId}>
              {executionId}
            </div>
            {exec && (
              <div className="mt-2 flex items-center gap-2">
                <StatusBadge status={exec.status} />
                <span className="font-mono text-[11px] text-fg-muted">{exec.workflowId}</span>
              </div>
            )}
          </div>
          <div className="flex items-center gap-2">
            <Button
              variant="ghost"
              className="h-7 px-2 text-xs"
              onClick={state.refetch}
              disabled={state.loading}
            >
              ↻ Recarregar
            </Button>
            <Button variant="ghost" className="-mr-2 px-2" onClick={onClose} aria-label="Fechar">
              ×
            </Button>
          </div>
        </div>

        <div className="flex-1 overflow-y-auto p-5">
          {state.error ? (
            <ErrorState error={state.error} onRetry={state.refetch} />
          ) : state.loading || !full ? (
            <div className="space-y-3">
              <Skeleton className="h-4 w-1/2" />
              <Skeleton className="h-4 w-1/3" />
              <Skeleton className="h-24 w-full" />
            </div>
          ) : (
            <ExecutionFullBody full={full} />
          )}
        </div>
      </div>
    </div>
  )
}

function ExecutionFullBody({ full }: { full: ExecutionFull }) {
  const exec = full.execution
  const duration = executionDurationMs(exec)
  const totalTokens = full.nodes.reduce((acc, n) => acc + (n.tokensUsed || 0), 0)
  const toolCount = full.tools.length
  const successfulTools = full.tools.filter((t) => t.success).length

  return (
    <div className="flex flex-col gap-6 text-sm">
      {/* KPI strip */}
      <section className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        <KpiCell label="Duração" value={formatLatencyMs(duration)} />
        <KpiCell label="Tokens" value={formatInt(totalTokens)} hint={`${full.nodes.length} agente${full.nodes.length === 1 ? '' : 's'}`} />
        <KpiCell label="Tools" value={formatInt(toolCount)} hint={toolCount > 0 ? `${successfulTools} OK` : 'nenhuma'} />
        <KpiCell label="Eventos" value={formatInt(full.events.length)} hint="audit log" />
      </section>

      {/* Tempos compactos */}
      <section>
        <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-fg-muted">Tempos</h3>
        <dl className="grid grid-cols-[max-content_1fr] gap-x-3 gap-y-1 text-xs">
          <dt className="text-fg-muted">Iniciou</dt>
          <dd className="text-fg">{formatBucketLabel(exec.startedAt, 'hour')}</dd>
          <dt className="text-fg-muted">Terminou</dt>
          <dd className="text-fg">{exec.completedAt ? formatBucketLabel(exec.completedAt, 'hour') : '—'}</dd>
          {exec.workflowVersionId && (
            <>
              <dt className="text-fg-muted">Versão</dt>
              <dd className="font-mono text-fg">{exec.workflowVersionId}</dd>
            </>
          )}
        </dl>
      </section>

      {/* Input — só renderiza quando há conteúdo */}
      {exec.input && exec.input.trim().length > 0 && (
        <section>
          <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-fg-muted">Input</h3>
          <JsonViewer value={exec.input} ariaLabel="input da execução" showMeta />
        </section>
      )}

      {/* Output — sempre renderiza pra Completed; pra Failed o erro é mais útil */}
      {exec.output && exec.output.trim().length > 0 && (
        <section>
          <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-fg-muted">Output</h3>
          <JsonViewer value={exec.output} ariaLabel="output da execução" showMeta />
        </section>
      )}

      {exec.errorMessage && (
        <section>
          <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-warning">Erro</h3>
          <pre className="max-h-64 overflow-auto rounded-md border border-warning/40 bg-warning/5 p-3 text-[11px] text-fg whitespace-pre-wrap break-words">
            {exec.errorMessage}
          </pre>
        </section>
      )}

      {/* Nodes — agentes invocados, com output expandível */}
      <section>
        <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-fg-muted">
          Agentes invocados {full.nodes.length > 0 && `(${full.nodes.length})`}
        </h3>
        {full.nodes.length === 0 ? (
          <EmptyState title="Sem agentes" description="A execução não chegou a invocar nenhum agente." />
        ) : (
          <ol className="space-y-2">
            {full.nodes.map((n, i) => (
              <NodeCard key={n.nodeId + i} node={n} index={i} />
            ))}
          </ol>
        )}
      </section>

      {/* Tools — opcional */}
      {full.tools.length > 0 && (
        <section>
          <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-fg-muted">
            Chamadas de tool ({full.tools.length})
          </h3>
          <ol className="space-y-2">
            {full.tools.map((t, i) => (
              <ToolCard key={String(t.id ?? i)} tool={t} />
            ))}
          </ol>
        </section>
      )}

      {/* Eventos — timeline compacta */}
      {full.events.length > 0 && (
        <section>
          <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-fg-muted">
            Eventos ({full.events.length})
          </h3>
          <EventsTimeline events={full.events} />
        </section>
      )}

      {Object.keys(exec.metadata ?? {}).length > 0 && (
        <section>
          <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-fg-muted">Metadata</h3>
          <dl className="grid grid-cols-[max-content_1fr] gap-x-3 gap-y-1 text-xs">
            {Object.entries(exec.metadata).map(([k, v]) => (
              <div key={k} className="contents">
                <dt className="text-fg-muted">{k}</dt>
                <dd className="break-all font-mono text-fg">{v}</dd>
              </div>
            ))}
          </dl>
        </section>
      )}
    </div>
  )
}

function KpiCell({ label, value, hint }: { label: string; value: string; hint?: string }) {
  return (
    <div className="rounded-lg border border-border bg-surface p-3">
      <div className="text-[10px] uppercase tracking-wider text-fg-dim">{label}</div>
      <div className="mt-0.5 text-base font-semibold text-fg">{value}</div>
      {hint && <div className="mt-0.5 text-[11px] text-fg-muted">{hint}</div>}
    </div>
  )
}

function NodeCard({ node, index }: { node: ExecutionNode; index: number }) {
  const [open, setOpen] = useState(false)
  const duration =
    node.completedAt && node.startedAt
      ? new Date(node.completedAt).getTime() - new Date(node.startedAt).getTime()
      : null
  const hasOutput = !!(node.output && node.output.trim().length > 0)

  return (
    <li className="rounded-md border border-border bg-surface text-xs">
      <button
        type="button"
        onClick={() => hasOutput && setOpen((v) => !v)}
        disabled={!hasOutput}
        className={cn(
          'flex w-full items-center gap-3 px-3 py-2.5 text-left transition',
          hasOutput && 'cursor-pointer hover:bg-surface-hover',
          !hasOutput && 'cursor-default',
        )}
      >
        <span className="font-mono text-fg-dim">#{index + 1}</span>
        <StatusBadge status={node.status} className="capitalize" />
        <div className="flex min-w-0 flex-1 flex-col">
          <span className="truncate font-medium text-fg">{node.nodeId}</span>
          <span className="text-[11px] text-fg-dim">
            {node.nodeType}
            {node.iteration != null && ` · iter ${node.iteration}`}
          </span>
        </div>
        <div className="flex shrink-0 items-center gap-3 text-fg-dim">
          <span>{formatInt(node.tokensUsed)} tokens</span>
          <span>{formatLatencyMs(duration)}</span>
          {hasOutput && <span className="text-fg-muted">{open ? '▲' : '▼'}</span>}
        </div>
      </button>
      {open && hasOutput && (
        <div className="border-t border-border p-3">
          <JsonViewer
            value={node.output}
            ariaLabel={`output do agente ${node.nodeId}`}
            maxHeightClass="max-h-80"
            showMeta
          />
          {node.outputTruncated && (
            <p className="mt-2 text-[11px] text-warning">
              ⚠ Output truncado pelo backend. Veja a execução completa via API.
            </p>
          )}
        </div>
      )}
    </li>
  )
}

function ToolCard({ tool }: { tool: ExecutionToolCall }) {
  const [open, setOpen] = useState(false)
  const hasBody = !!(tool.result || tool.arguments || tool.errorMessage)
  return (
    <li className="rounded-md border border-border bg-surface text-xs">
      <button
        type="button"
        onClick={() => hasBody && setOpen((v) => !v)}
        disabled={!hasBody}
        className={cn(
          'flex w-full items-center gap-3 px-3 py-2.5 text-left transition',
          hasBody && 'cursor-pointer hover:bg-surface-hover',
        )}
      >
        <StatusBadge status={tool.success ? 'Completed' : 'Failed'} />
        <div className="flex min-w-0 flex-1 flex-col">
          <span className="truncate font-mono text-fg">{tool.toolName}</span>
          <span className="text-[11px] text-fg-dim">{tool.agentId}</span>
        </div>
        <div className="flex shrink-0 items-center gap-3 text-fg-dim">
          <span>{formatLatencyMs(tool.durationMs)}</span>
          {hasBody && <span className="text-fg-muted">{open ? '▲' : '▼'}</span>}
        </div>
      </button>
      {open && hasBody && (
        <div className="space-y-3 border-t border-border p-3">
          {tool.arguments != null && (
            <div>
              <div className="mb-1 text-[10px] uppercase tracking-wider text-fg-dim">Argumentos</div>
              <JsonViewer
                value={
                  typeof tool.arguments === 'string'
                    ? tool.arguments
                    : JSON.stringify(tool.arguments, null, 2)
                }
                ariaLabel="arguments da tool"
                maxHeightClass="max-h-60"
              />
            </div>
          )}
          {tool.result && (
            <div>
              <div className="mb-1 text-[10px] uppercase tracking-wider text-fg-dim">Resultado</div>
              <JsonViewer value={tool.result} ariaLabel="result da tool" maxHeightClass="max-h-60" />
            </div>
          )}
          {tool.errorMessage && (
            <div>
              <div className="mb-1 text-[10px] uppercase tracking-wider text-warning">Erro</div>
              <pre className="overflow-auto rounded-md border border-warning/40 bg-warning/5 p-3 text-[11px] text-fg whitespace-pre-wrap">
                {tool.errorMessage}
              </pre>
            </div>
          )}
        </div>
      )}
    </li>
  )
}

function EventsTimeline({ events }: { events: ExecutionAuditEvent[] }) {
  // Pra timeline: ordena cronologicamente e mostra tipo + horário. Payload
  // fica expandido só quando o user clica (evita parede de JSON na cara).
  const ordered = useMemo(
    () =>
      [...events].sort((a, b) => {
        const ta = new Date(a.timestamp).getTime()
        const tb = new Date(b.timestamp).getTime()
        return ta - tb
      }),
    [events],
  )

  return (
    <ol className="space-y-1 border-l-2 border-border pl-4">
      {ordered.map((e, i) => (
        <EventCard key={(e.sequenceId ?? i) + '-' + e.eventType} event={e} />
      ))}
    </ol>
  )
}

function EventCard({ event }: { event: ExecutionAuditEvent }) {
  const [open, setOpen] = useState(false)
  const hasPayload = event.payload != null && event.payload !== ''
  return (
    <li className="relative">
      <span className="absolute -left-[1.4rem] top-2 h-2 w-2 -translate-x-1/2 rounded-full bg-accent/60" />
      <button
        type="button"
        onClick={() => hasPayload && setOpen((v) => !v)}
        disabled={!hasPayload}
        className={cn(
          'flex w-full items-center justify-between gap-3 rounded-md py-1.5 pr-2 text-left text-xs transition',
          hasPayload && 'cursor-pointer hover:bg-surface-hover',
        )}
      >
        <span className="font-mono text-fg">{event.eventType}</span>
        <span className="text-[11px] text-fg-dim">{formatBucketLabel(event.timestamp, 'hour')}</span>
      </button>
      {open && hasPayload && (
        <div className="mb-1 pl-1">
          <JsonViewer
            value={
              typeof event.payload === 'string'
                ? event.payload
                : JSON.stringify(event.payload, null, 2)
            }
            ariaLabel={`payload do evento ${event.eventType}`}
            maxHeightClass="max-h-48"
          />
        </div>
      )}
    </li>
  )
}
