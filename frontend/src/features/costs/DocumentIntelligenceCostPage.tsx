import { useState, useMemo } from 'react'
import { Link } from 'react-router'
import {
  useDocumentIntelligenceUsage,
  useDocumentIntelligenceJobs,
} from '../../api/documentIntelligence'
import { Card } from '../../shared/ui/Card'
import { Badge } from '../../shared/ui/Badge'
import { Button } from '../../shared/ui/Button'
import { MetricCard } from '../../shared/data/MetricCard'
import { TimeRangeSelector } from '../../shared/data/TimeRangeSelector'
import { TimeseriesChart } from '../../shared/charts/TimeseriesChart'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { EmptyState } from '../../shared/ui/EmptyState'
import { formatNumber } from '../../shared/utils/formatters'
import { getFromDate, type TimeRange } from '../../shared/utils/date'

const STATUS_BADGE: Record<string, 'green' | 'blue' | 'yellow' | 'red' | 'gray'> = {
  succeeded: 'green',
  cached: 'blue',
  running: 'yellow',
  failed: 'red',
  created: 'gray',
}

export function DocumentIntelligenceCostPage() {
  const [range, setRange] = useState<TimeRange>('30d')
  const from = useMemo(() => getFromDate(range), [range])

  const usage = useDocumentIntelligenceUsage(from)
  const jobs = useDocumentIntelligenceJobs(from, undefined, 30)

  if (usage.isLoading) return <PageLoader />
  if (usage.error) {
    return <ErrorCard message="Erro ao carregar dados do Document Intelligence" onRetry={usage.refetch} />
  }

  const summary = usage.data?.summary
  const byDay = usage.data?.byDay ?? []
  const byModel = usage.data?.byModel ?? []
  const jobsList = jobs.data?.items ?? []

  const chartData = byDay.map((d) => ({
    bucket: d.day,
    cost: Number(d.costUsd),
    pages: d.pages,
  }))

  return (
    <div className="flex flex-col gap-6 p-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Document Intelligence — Custo</h1>
          <p className="text-sm text-text-muted mt-1">
            Azure AI Document Intelligence — cobrança por página processada (layout, read, invoice)
          </p>
        </div>
        <div className="flex items-center gap-3">
          <TimeRangeSelector value={range} onChange={setRange} />
          <Link to="/costs/document-intelligence/pricing">
            <Button variant="secondary" size="sm">Gerenciar Pricing</Button>
          </Link>
        </div>
      </div>

      <div className="grid grid-cols-2 md:grid-cols-5 gap-4">
        <MetricCard
          label="Custo Total"
          value={`$${(summary?.totalCostUsd ?? 0).toFixed(4)}`}
          sub={`${summary?.totalJobs ?? 0} jobs`}
        />
        <MetricCard label="Páginas" value={formatNumber(summary?.totalPages ?? 0)} />
        <MetricCard label="Sucesso" value={formatNumber(summary?.succeededJobs ?? 0)} />
        <MetricCard label="Cache hits" value={formatNumber(summary?.cachedJobs ?? 0)} sub="sem custo" />
        <MetricCard label="Falhas" value={formatNumber(summary?.failedJobs ?? 0)} />
      </div>

      <Card title="Custo diário">
        {chartData.length === 0 ? (
          <EmptyState
            title="Sem dados"
            description="Nenhuma extração realizada neste período."
          />
        ) : (
          <TimeseriesChart
            data={chartData as Record<string, unknown>[]}
            xKey="bucket"
            series={[
              { key: 'cost', label: 'Custo (USD)', color: '#0057E0' },
              { key: 'pages', label: 'Páginas', color: '#10B981' },
            ]}
            height={260}
          />
        )}
      </Card>

      <Card title="Por modelo">
        {byModel.length === 0 ? (
          <EmptyState title="Sem dados" description="Nenhum modelo usado no período." />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="text-left text-text-muted border-b border-border-primary">
                <tr>
                  <th className="py-2 pr-4">Modelo</th>
                  <th className="py-2 pr-4 text-right">Jobs</th>
                  <th className="py-2 pr-4 text-right">Páginas</th>
                  <th className="py-2 text-right">Custo (USD)</th>
                </tr>
              </thead>
              <tbody>
                {byModel.map((m) => (
                  <tr key={m.model} className="border-b border-border-primary/50">
                    <td className="py-2 pr-4 font-mono text-text-primary">{m.model}</td>
                    <td className="py-2 pr-4 text-right">{formatNumber(m.jobCount)}</td>
                    <td className="py-2 pr-4 text-right">{formatNumber(m.pages)}</td>
                    <td className="py-2 text-right">${Number(m.costUsd).toFixed(4)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <Card title="Jobs recentes">
        {jobsList.length === 0 ? (
          <EmptyState title="Sem jobs" description="Nenhum job de extração no período." />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="text-left text-text-muted border-b border-border-primary">
                <tr>
                  <th className="py-2 pr-4">Quando</th>
                  <th className="py-2 pr-4">Status</th>
                  <th className="py-2 pr-4">Modelo</th>
                  <th className="py-2 pr-4">Usuário</th>
                  <th className="py-2 pr-4 text-right">Pág.</th>
                  <th className="py-2 pr-4 text-right">Custo</th>
                  <th className="py-2 text-right">Duração</th>
                </tr>
              </thead>
              <tbody>
                {jobsList.map((j) => (
                  <tr key={j.jobId} className="border-b border-border-primary/50">
                    <td className="py-2 pr-4 font-mono text-xs text-text-muted">
                      {new Date(j.createdAt).toLocaleString('pt-BR')}
                    </td>
                    <td className="py-2 pr-4">
                      <Badge variant={STATUS_BADGE[j.status] ?? 'gray'}>{j.status}</Badge>
                    </td>
                    <td className="py-2 pr-4 font-mono text-xs">{j.model}</td>
                    <td className="py-2 pr-4 font-mono text-xs text-text-muted">{j.userId || '—'}</td>
                    <td className="py-2 pr-4 text-right">{j.pageCount ?? '—'}</td>
                    <td className="py-2 pr-4 text-right">
                      {j.costUsd != null ? `$${Number(j.costUsd).toFixed(4)}` : '—'}
                    </td>
                    <td className="py-2 text-right text-text-muted">
                      {j.durationMs != null ? `${j.durationMs}ms` : '—'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </div>
  )
}
