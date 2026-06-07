// Tela "Document Intelligence" — uso + custo do Azure DI.
// Admin-only no backend (rota /admin/document-intelligence). Cross-projeto:
// hoje a tabela document_extraction_jobs não carrega project_id direto —
// pra ficar project-scoped precisa adicionar a coluna ou JOIN com
// conversations. Por enquanto a tela mostra TUDO do tenant e o header
// avisa explicitamente.
//
// Pricing é separado de execuções (DI cobra por PÁGINA, não por token).
// A tabela de pricing aparece em card próprio embaixo pra contextualizar.

import { useCallback, useMemo } from 'react'
import {
  Card,
  CardHeader,
  EmptyState,
  ErrorState,
  Stat,
  StatusBadge,
  Table,
  type Column,
} from '../components/ui'
import { TimeSeriesChart, type ChartSeries } from '../components/charts/TimeSeriesChart'
import { BarChart } from '../components/charts/BarChart'
import { DateRangePicker } from '../components/filters/DateRangePicker'
import { useApi } from '../hooks/useApi'
import { useDateRange } from '../hooks/useDateRange'
import {
  getDocumentIntelligenceJobs,
  getDocumentIntelligencePricing,
  getDocumentIntelligenceUsage,
  type DiJobSummary,
  type DiPricing,
  type DiUsageByDay,
} from '../api/documentIntelligence'
import {
  formatBucketLabel,
  formatCurrency,
  formatInt,
  formatLatencyMs,
  toIsoUtc,
} from '../utils/format'

// `byDay` vem como YYYY-MM-DD do backend (DateOnly). TimeSeriesChart espera
// chave `bucket` ISO-8601. Normalizamos no client antes de plotar.
interface DayBucket {
  bucket: string
  jobCount: number
  pages: number
  costUsd: number
}

function normalizeByDay(rows: DiUsageByDay[]): DayBucket[] {
  return rows.map((r) => ({
    bucket: `${r.day}T00:00:00Z`,
    jobCount: r.jobCount,
    pages: r.pages,
    costUsd: r.costUsd,
  }))
}

export function DocumentIntelligence() {
  const { range, setPreset, setCustom } = useDateRange()
  const fromIso = useMemo(() => toIsoUtc(range.from), [range.from])
  const toIso = useMemo(() => toIsoUtc(range.to), [range.to])

  const usageState = useApi(
    useCallback(
      (signal) => getDocumentIntelligenceUsage(fromIso, toIso, { signal }),
      [fromIso, toIso],
    ),
    [fromIso, toIso],
  )

  const jobsState = useApi(
    useCallback(
      (signal) => getDocumentIntelligenceJobs(fromIso, toIso, 100, { signal }),
      [fromIso, toIso],
    ),
    [fromIso, toIso],
  )

  const pricingState = useApi(
    useCallback((signal) => getDocumentIntelligencePricing({ signal }), []),
    [],
  )

  const summary = usageState.data?.summary
  const successRate =
    summary && summary.totalJobs > 0
      ? (summary.succeededJobs + summary.cachedJobs) / summary.totalJobs
      : null
  const cacheHitRate =
    summary && summary.totalJobs > 0 ? summary.cachedJobs / summary.totalJobs : null

  const daySeries: ChartSeries<DayBucket>[] = [
    { dataKey: 'costUsd', label: 'Custo (USD)', format: 'currency', yAxisId: 'left' },
    { dataKey: 'jobCount', label: 'Jobs', format: 'integer', yAxisId: 'right' },
  ]

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-end justify-between gap-4 rounded-2xl border border-border bg-surface p-4">
        <div className="flex flex-col gap-1">
          <span className="text-[10px] uppercase tracking-widest text-fg-dim">
            Escopo
          </span>
          <span className="text-sm font-medium text-fg">Cross-projeto</span>
          <span className="text-[11px] text-fg-muted">
            Document Intelligence ainda não é project-scoped no banco. Métricas refletem todo o tenant.
          </span>
        </div>
        <DateRangePicker range={range} onPresetChange={setPreset} onCustomChange={setCustom} />
      </div>

      <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
        <Card>
          {usageState.error ? (
            <ErrorState error={usageState.error} onRetry={usageState.refetch} />
          ) : (
            <Stat
              label="Custo no período"
              value={usageState.loading ? null : formatCurrency(summary?.totalCostUsd ?? 0)}
              hint={`${formatInt(summary?.totalPages ?? 0)} páginas processadas`}
            />
          )}
        </Card>
        <Card>
          {usageState.error ? (
            <ErrorState error={usageState.error} onRetry={usageState.refetch} />
          ) : (
            <Stat
              label="Jobs"
              value={usageState.loading ? null : formatInt(summary?.totalJobs ?? 0)}
              hint={
                summary && summary.totalJobs > 0
                  ? `${formatInt(summary.succeededJobs)} OK · ${formatInt(summary.failedJobs)} falhas`
                  : 'sem jobs no período'
              }
            />
          )}
        </Card>
        <Card>
          {usageState.error ? (
            <ErrorState error={usageState.error} onRetry={usageState.refetch} />
          ) : (
            <Stat
              label="Cache hit rate"
              value={
                usageState.loading
                  ? null
                  : cacheHitRate == null
                    ? '—'
                    : `${(cacheHitRate * 100).toFixed(1).replace('.', ',')}%`
              }
              hint={
                summary && summary.totalJobs > 0
                  ? `${formatInt(summary.cachedJobs)} cache hits`
                  : 'sem amostras'
              }
            />
          )}
        </Card>
        <Card>
          {usageState.error ? (
            <ErrorState error={usageState.error} onRetry={usageState.refetch} />
          ) : (
            <Stat
              label="Success rate"
              value={
                usageState.loading
                  ? null
                  : successRate == null
                    ? '—'
                    : `${(successRate * 100).toFixed(1).replace('.', ',')}%`
              }
              hint="success + cache / total"
            />
          )}
        </Card>
      </div>

      <div className="grid grid-cols-1 gap-4 xl:grid-cols-3">
        <Card className="xl:col-span-2">
          <CardHeader
            title="Custo e jobs por dia"
            description="Esquerda: custo USD. Direita: contagem de jobs."
          />
          <div className="mt-4">
            {usageState.error ? (
              <ErrorState error={usageState.error} onRetry={usageState.refetch} />
            ) : usageState.loading ? (
              <div className="h-60 animate-pulse rounded-lg bg-surface-hover" />
            ) : (
              <TimeSeriesChart
                data={normalizeByDay(usageState.data?.byDay ?? [])}
                series={daySeries}
                granularity="day"
                dualAxis
                variant="area"
                height={260}
                emptyTitle="Sem jobs DI no período"
              />
            )}
          </div>
        </Card>

        <Card>
          <CardHeader
            title="Páginas por modelo"
            description="Cada modelo DI cobra preço diferente — top consumidores."
          />
          <div className="mt-4">
            {usageState.error ? (
              <ErrorState error={usageState.error} onRetry={usageState.refetch} />
            ) : usageState.loading ? (
              <div className="h-60 animate-pulse rounded-lg bg-surface-hover" />
            ) : (
              <BarChart
                data={usageState.data?.byModel ?? []}
                dataKey="pages"
                labelKey="model"
                format="integer"
                layout="horizontal"
                height={260}
                emptyTitle="Sem uso por modelo"
              />
            )}
          </div>
        </Card>
      </div>

      <Card padded={false}>
        <div className="p-5">
          <CardHeader
            title="Jobs recentes"
            description="Top 100 do período. Inclui cache hits e falhas."
          />
        </div>
        {jobsState.error ? (
          <div className="px-5 pb-5">
            <ErrorState error={jobsState.error} onRetry={jobsState.refetch} />
          </div>
        ) : (
          <Table
            columns={jobColumns}
            rows={jobsState.data?.items ?? null}
            loading={jobsState.loading}
            keyOf={(row) => row.jobId}
            empty={{
              title: 'Nenhum job DI no período',
              description: 'Ingestões via /api/aihub/ingestions com PDF disparam jobs DI.',
            }}
          />
        )}
      </Card>

      <Card padded={false}>
        <div className="p-5">
          <CardHeader
            title="Tabela de pricing"
            description="Configurada em admin/model-pricing. Aplicada no cálculo de custo dos jobs."
          />
        </div>
        {pricingState.error ? (
          <div className="px-5 pb-5">
            <ErrorState error={pricingState.error} onRetry={pricingState.refetch} />
          </div>
        ) : (pricingState.data?.items.length ?? 0) === 0 && !pricingState.loading ? (
          <div className="px-5 pb-5">
            <EmptyState
              title="Nenhum pricing cadastrado"
              description="Sem isso o cost_usd dos jobs fica null."
            />
          </div>
        ) : (
          <Table
            columns={pricingColumns}
            rows={pricingState.data?.items ?? null}
            loading={pricingState.loading}
            keyOf={(row) => row.id}
          />
        )}
      </Card>
    </div>
  )
}

const jobColumns: ReadonlyArray<Column<DiJobSummary>> = [
  {
    key: 'status',
    header: 'Status',
    cell: (r) => <StatusBadge status={r.status} />,
    sortBy: (r) => r.status,
  },
  {
    key: 'model',
    header: 'Modelo',
    cell: (r) => <span className="font-mono text-xs text-fg">{r.model}</span>,
    sortBy: (r) => r.model,
  },
  {
    key: 'pages',
    header: 'Páginas',
    align: 'right',
    cell: (r) => formatInt(r.pageCount ?? 0),
    sortBy: (r) => r.pageCount ?? 0,
  },
  {
    key: 'cost',
    header: 'Custo',
    align: 'right',
    cell: (r) => formatCurrency(r.costUsd ?? 0),
    sortBy: (r) => r.costUsd ?? 0,
  },
  {
    key: 'duration',
    header: 'Duração',
    align: 'right',
    cell: (r) => formatLatencyMs(r.durationMs ?? null),
    sortBy: (r) => r.durationMs ?? 0,
  },
  {
    key: 'createdAt',
    header: 'Quando',
    cell: (r) => <span className="text-xs text-fg-muted">{formatBucketLabel(r.createdAt, 'hour')}</span>,
    sortBy: (r) => r.createdAt,
  },
]

const pricingColumns: ReadonlyArray<Column<DiPricing>> = [
  {
    key: 'modelId',
    header: 'Modelo',
    cell: (r) => <span className="font-mono text-xs text-fg">{r.modelId}</span>,
    sortBy: (r) => r.modelId,
  },
  {
    key: 'provider',
    header: 'Provider',
    cell: (r) => <span className="text-xs text-fg-muted">{r.provider}</span>,
    sortBy: (r) => r.provider,
  },
  {
    key: 'pricePerPage',
    header: 'Preço/página',
    align: 'right',
    cell: (r) => `${r.currency} ${r.pricePerPage.toFixed(4).replace('.', ',')}`,
    sortBy: (r) => r.pricePerPage,
  },
  {
    key: 'effectiveFrom',
    header: 'Vigência',
    cell: (r) => (
      <span className="text-xs text-fg-muted">
        {formatBucketLabel(r.effectiveFrom, 'day')}
        {r.effectiveTo && ` → ${formatBucketLabel(r.effectiveTo, 'day')}`}
      </span>
    ),
    sortBy: (r) => r.effectiveFrom,
  },
]
