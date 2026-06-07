// Drill-down de custos: breakdown por workflow + por agente + throughput
// horário cross-projeto. Workflow/projetos summary do /token-usage NÃO tem
// `costUsd`, só tokens — refletido nas colunas. ProjectAnalytics/agents
// continua sendo a fonte com custo por agente do projeto.

import { useCallback, useMemo } from 'react'
import { Card, CardHeader, ErrorState, Table, type Column } from '../components/ui'
import { TimeSeriesChart, type ChartSeries } from '../components/charts/TimeSeriesChart'
import { FilterHeader } from '../components/FilterHeader'
import { useApi } from '../hooks/useApi'
import { useDateRange } from '../hooks/useDateRange'
import { useIdentity } from '../stores/identity'
import {
  getProjectAgents,
  type ProjectAgentBreakdown,
} from '../api/analytics'
import {
  getThroughput,
  getWorkflowsSummary,
  type ThroughputBucket,
  type WorkflowTokenSummary,
} from '../api/tokenUsage'
import {
  formatCurrency,
  formatInt,
  formatLatencyMs,
  toIsoUtc,
} from '../utils/format'

export function Custos() {
  const identity = useIdentity()
  const { range, setPreset, setCustom } = useDateRange()
  const projectId = identity?.projectId ?? ''
  const fromIso = useMemo(() => toIsoUtc(range.from), [range.from])
  const toIso = useMemo(() => toIsoUtc(range.to), [range.to])

  // skip enquanto projectId não estiver populado — apenas o agentsState depende
  // dele; os outros 2 (workflows/throughput) são cross-project, mas pra evitar
  // que a tela acenda meia (com dados globais e card de agente vazio) só
  // habilitamos os fetches quando o projeto está selecionado.
  const skip = !projectId

  // Workflows summary é global (cross-project) — mantemos no Custos porque a
  // tela já mostra "onde estão meus gastos". Quando o backend ganhar variante
  // project-scoped, filtramos localmente até lá.
  const workflowsState = useApi(
    useCallback(
      (signal) => getWorkflowsSummary(fromIso, toIso, { signal }),
      [fromIso, toIso],
    ),
    [fromIso, toIso],
    { skip },
  )

  const agentsState = useApi(
    useCallback(
      (signal) => getProjectAgents(projectId, 20, fromIso, toIso, false, { signal }),
      [projectId, fromIso, toIso],
    ),
    [projectId, fromIso, toIso],
    { skip },
  )

  // Throughput global é sempre horário pelo backend; usamos a granularidade
  // do gráfico = 'hour' mesmo em ranges maiores. Pra muitos dias o eixo X
  // ainda fica legível porque minTickGap controla densidade.
  const throughputState = useApi(
    useCallback((signal) => getThroughput(fromIso, toIso, { signal }), [fromIso, toIso]),
    [fromIso, toIso],
    { skip },
  )

  if (!projectId) {
    return (
      <div className="flex flex-col gap-4">
        <FilterHeader range={range} onPresetChange={setPreset} onCustomChange={setCustom} />
        <Card>
          <CardHeader title="Selecione um projeto" description="Use o seletor acima pra ver os custos." />
        </Card>
      </div>
    )
  }

  const throughputSeries: ChartSeries<ThroughputBucket>[] = [
    { dataKey: 'executions', label: 'Execuções', format: 'integer', yAxisId: 'left' },
    { dataKey: 'tokens', label: 'Tokens', format: 'integer', yAxisId: 'right' },
    { dataKey: 'llmCalls', label: 'Chamadas LLM', format: 'integer', yAxisId: 'left' },
  ]

  return (
    <div className="flex flex-col gap-6">
      <FilterHeader range={range} onPresetChange={setPreset} onCustomChange={setCustom} />

      <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
        <Card padded={false}>
          <div className="p-5">
            <CardHeader
              title="Custo por agente"
              description="Agentes deste projeto. Custo agregado no período selecionado."
            />
          </div>
          {agentsState.error ? (
            <div className="px-5 pb-5">
              <ErrorState error={agentsState.error} onRetry={agentsState.refetch} />
            </div>
          ) : (
            <Table
              columns={agentColumns}
              rows={agentsState.data}
              loading={agentsState.loading}
              keyOf={(row) => row.agentId}
              empty={{ title: 'Nenhum agente no período' }}
            />
          )}
        </Card>

        <Card padded={false}>
          <div className="p-5">
            <CardHeader
              title="Tokens por workflow"
              description="Backend não devolve custo por workflow — exibimos volume de tokens."
            />
          </div>
          {workflowsState.error ? (
            <div className="px-5 pb-5">
              <ErrorState error={workflowsState.error} onRetry={workflowsState.refetch} />
            </div>
          ) : (
            <Table
              columns={workflowColumns}
              rows={workflowsState.data}
              loading={workflowsState.loading}
              keyOf={(row, i) => `${row.workflowId}-${row.modelId}-${i}`}
              empty={{ title: 'Nenhum workflow no período' }}
            />
          )}
        </Card>
      </div>

      <Card>
        <CardHeader
          title="Throughput por hora"
          description="Execuções, tokens e chamadas LLM agregados por hora (global)."
        />
        <div className="mt-4">
          {throughputState.error ? (
            <ErrorState error={throughputState.error} onRetry={throughputState.refetch} />
          ) : throughputState.loading ? (
            <div className="h-60 animate-pulse rounded-lg bg-surface-hover" />
          ) : (
            <TimeSeriesChart
              data={throughputState.data?.buckets ?? []}
              series={throughputSeries}
              granularity="hour"
              dualAxis
              variant="line"
              height={260}
              emptyTitle="Sem throughput no período"
            />
          )}
        </div>
      </Card>
    </div>
  )
}

const agentColumns: ReadonlyArray<Column<ProjectAgentBreakdown>> = [
  {
    key: 'agent',
    header: 'Agente',
    cell: (r) => (
      <div className="flex flex-col">
        <span className="font-medium text-fg">{r.agentName ?? r.agentId}</span>
        {r.modelId && <span className="text-xs text-fg-dim">{r.modelId}</span>}
      </div>
    ),
    sortBy: (r) => r.agentName ?? r.agentId,
  },
  {
    key: 'cost',
    header: 'Custo',
    align: 'right',
    cell: (r) => formatCurrency(r.costUsd),
    sortBy: (r) => r.costUsd,
  },
  {
    key: 'tokens',
    header: 'Tokens',
    align: 'right',
    cell: (r) => formatInt(r.totalTokens),
    sortBy: (r) => r.totalTokens,
  },
  {
    key: 'calls',
    header: 'Chamadas',
    align: 'right',
    cell: (r) => formatInt(r.calls),
    sortBy: (r) => r.calls,
  },
  {
    key: 'p95',
    header: 'p95',
    align: 'right',
    cell: (r) => formatLatencyMs(r.p95DurationMs),
    sortBy: (r) => r.p95DurationMs,
  },
]

const workflowColumns: ReadonlyArray<Column<WorkflowTokenSummary>> = [
  {
    key: 'workflow',
    header: 'Workflow',
    cell: (r) => (
      <div className="flex flex-col">
        <span className="font-medium text-fg">{r.workflowId}</span>
        <span className="text-xs text-fg-dim">{r.modelId}</span>
      </div>
    ),
    sortBy: (r) => r.workflowId,
  },
  {
    key: 'tokens',
    header: 'Tokens',
    align: 'right',
    cell: (r) => formatInt(r.totalTokens),
    sortBy: (r) => r.totalTokens,
  },
  {
    key: 'calls',
    header: 'Chamadas',
    align: 'right',
    cell: (r) => formatInt(r.callCount),
    sortBy: (r) => r.callCount,
  },
  {
    key: 'avgDur',
    header: 'Duração média',
    align: 'right',
    cell: (r) => formatLatencyMs(r.avgDurationMs),
    sortBy: (r) => r.avgDurationMs,
  },
]
