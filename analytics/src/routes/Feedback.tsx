// Tela "Feedback" — likes/dislikes de mensagens (aihub.message_feedbacks).
// Sentiment é binário ±1. UI mostra:
//   - 4 KPIs (total, satisfação, comentários, distinct messages)
//   - Chart dual-axis (likes + dislikes ao longo do período)
//   - Lista paginada com filtro de sentiment e preview da fala avaliada
//
// PII: Comment é texto livre do USUÁRIO sobre a fala do produto. Show as-is.
// MessagePreview é a fala do assistant — sem PII de terceiros.

import { useCallback, useMemo, useState } from 'react'
import {
  Button,
  Card,
  CardHeader,
  ErrorState,
  JsonViewer,
  Stat,
  Table,
  cn,
  type Column,
} from '../components/ui'
import { TimeSeriesChart, type ChartSeries } from '../components/charts/TimeSeriesChart'
import { FilterHeader } from '../components/FilterHeader'
import { useApi } from '../hooks/useApi'
import { useDateRange } from '../hooks/useDateRange'
import { useIdentity } from '../stores/identity'
import {
  getFeedbackRecent,
  getFeedbackSummary,
  getFeedbackTimeseries,
  type FeedbackGranularity,
  type FeedbackRecentRow,
  type FeedbackTimeseriesBucket,
  type Sentiment,
} from '../api/feedbackAnalytics'
import {
  formatInt,
  formatPercent,
  formatBucketLabel,
  toIsoUtc,
} from '../utils/format'

function pickGranularity(from: Date, to: Date): FeedbackGranularity {
  const hours = (to.getTime() - from.getTime()) / 3600_000
  return hours <= 72 ? 'hour' : 'day'
}

const PAGE_SIZE = 25

export function Feedback() {
  const identity = useIdentity()
  const projectId = identity?.projectId ?? ''
  const { range, setPreset, setCustom } = useDateRange()
  const fromIso = useMemo(() => toIsoUtc(range.from), [range.from])
  const toIso = useMemo(() => toIsoUtc(range.to), [range.to])
  const granularity = useMemo(() => pickGranularity(range.from, range.to), [range])

  const [sentimentFilter, setSentimentFilter] = useState<Sentiment | null>(null)
  const [page, setPage] = useState(1)
  const [selectedId, setSelectedId] = useState<string | null>(null)

  const skip = !projectId

  const summaryState = useApi(
    useCallback(
      (signal) => getFeedbackSummary(projectId, fromIso, toIso, { signal }),
      [projectId, fromIso, toIso],
    ),
    [projectId, fromIso, toIso],
    { skip },
  )

  const timeseriesState = useApi(
    useCallback(
      (signal) => getFeedbackTimeseries(projectId, granularity, fromIso, toIso, { signal }),
      [projectId, granularity, fromIso, toIso],
    ),
    [projectId, granularity, fromIso, toIso],
    { skip },
  )

  const recentState = useApi(
    useCallback(
      (signal) =>
        getFeedbackRecent(
          projectId,
          fromIso,
          toIso,
          { sentiment: sentimentFilter, page, pageSize: PAGE_SIZE },
          { signal },
        ),
      [projectId, fromIso, toIso, sentimentFilter, page],
    ),
    [projectId, fromIso, toIso, sentimentFilter, page],
    { skip },
  )

  if (!projectId) {
    return (
      <div className="flex flex-col gap-4">
        <FilterHeader range={range} onPresetChange={setPreset} onCustomChange={setCustom} />
        <Card>
          <CardHeader title="Selecione um projeto" description="Use o seletor acima pra ver feedback." />
        </Card>
      </div>
    )
  }

  const summary = summaryState.data
  const ratingResolved = summary != null && summary.total > 0

  const series: ChartSeries<FeedbackTimeseriesBucket>[] = [
    { dataKey: 'positives', label: 'Likes', format: 'integer', yAxisId: 'left' },
    { dataKey: 'negatives', label: 'Dislikes', format: 'integer', yAxisId: 'right' },
  ]

  const recent = recentState.data
  const selectedRow = recent?.items.find((r) => r.feedbackId === selectedId) ?? null

  return (
    <div className="flex flex-col gap-6">
      <FilterHeader range={range} onPresetChange={setPreset} onCustomChange={setCustom} />

      <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
        <Card>
          {summaryState.error ? (
            <ErrorState error={summaryState.error} onRetry={summaryState.refetch} />
          ) : (
            <Stat
              label="Feedbacks no período"
              value={summaryState.loading ? null : formatInt(summary?.total ?? 0)}
              hint={`👍 ${formatInt(summary?.positives ?? 0)} · 👎 ${formatInt(summary?.negatives ?? 0)}`}
            />
          )}
        </Card>
        <Card>
          {summaryState.error ? (
            <ErrorState error={summaryState.error} onRetry={summaryState.refetch} />
          ) : (
            <Stat
              label="Satisfação"
              value={
                summaryState.loading
                  ? null
                  : ratingResolved
                    ? formatPercent(summary?.satisfactionRate ?? 0)
                    : '—'
              }
              hint={ratingResolved ? 'positives / total' : 'Sem feedback no período'}
            />
          )}
        </Card>
        <Card>
          {summaryState.error ? (
            <ErrorState error={summaryState.error} onRetry={summaryState.refetch} />
          ) : (
            <Stat
              label="Comentários"
              value={summaryState.loading ? null : formatInt(summary?.withCommentCount ?? 0)}
              hint={`${formatInt(summary?.distinctUsers ?? 0)} usuário${(summary?.distinctUsers ?? 0) === 1 ? '' : 's'}`}
            />
          )}
        </Card>
        <Card>
          {summaryState.error ? (
            <ErrorState error={summaryState.error} onRetry={summaryState.refetch} />
          ) : (
            <Stat
              label="Mensagens avaliadas"
              value={summaryState.loading ? null : formatInt(summary?.distinctMessages ?? 0)}
              hint="distintas no período"
            />
          )}
        </Card>
      </div>

      <Card>
        <CardHeader
          title="Volume de likes e dislikes"
          description="Eixo esquerdo: likes. Eixo direito: dislikes."
        />
        <div className="mt-4">
          {timeseriesState.error ? (
            <ErrorState error={timeseriesState.error} onRetry={timeseriesState.refetch} />
          ) : timeseriesState.loading ? (
            <div className="h-60 animate-pulse rounded-lg bg-surface-hover" />
          ) : (
            <TimeSeriesChart
              data={timeseriesState.data ?? []}
              series={series}
              granularity={granularity}
              dualAxis
              variant="line"
              height={260}
            />
          )}
        </div>
      </Card>

      <Card padded={false}>
        <div className="p-5">
          <CardHeader
            title="Feedbacks recentes"
            description={
              recent
                ? `Página ${recent.page} de ${Math.max(1, Math.ceil(recent.total / recent.pageSize))} · ${formatInt(recent.total)} total`
                : 'Lista detalhada com comentário e mensagem avaliada.'
            }
          />
          <div className="mt-3 flex flex-wrap items-center gap-2">
            <span className="text-xs font-medium text-fg-muted">Sentiment:</span>
            <SentimentChip
              active={sentimentFilter === null}
              onClick={() => {
                setSentimentFilter(null)
                setPage(1)
              }}
            >
              Todos
            </SentimentChip>
            <SentimentChip
              active={sentimentFilter === 1}
              onClick={() => {
                setSentimentFilter(1)
                setPage(1)
              }}
            >
              👍 Likes
            </SentimentChip>
            <SentimentChip
              active={sentimentFilter === -1}
              onClick={() => {
                setSentimentFilter(-1)
                setPage(1)
              }}
            >
              👎 Dislikes
            </SentimentChip>
          </div>
        </div>
        {recentState.error ? (
          <div className="px-5 pb-5">
            <ErrorState error={recentState.error} onRetry={recentState.refetch} />
          </div>
        ) : (
          <Table
            columns={feedbackColumns}
            rows={recent?.items ?? null}
            loading={recentState.loading}
            keyOf={(row) => row.feedbackId}
            onRowClick={(row) => setSelectedId(row.feedbackId)}
            empty={{
              title: sentimentFilter === null ? 'Nenhum feedback' : 'Nenhum nesse sentiment',
              description: 'Tente outro período ou filtro.',
            }}
          />
        )}
        {recent && recent.total > recent.pageSize && (
          <div className="flex items-center justify-between border-t border-border px-5 py-3">
            <span className="text-xs text-fg-dim">
              {formatInt(recent.items.length)} mostrando · {formatInt(recent.total)} total
            </span>
            <div className="flex items-center gap-2">
              <Button
                variant="ghost"
                className="h-7 px-2 text-xs"
                disabled={page <= 1 || recentState.loading}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
              >
                ← Anterior
              </Button>
              <span className="text-xs text-fg-muted">Página {page}</span>
              <Button
                variant="ghost"
                className="h-7 px-2 text-xs"
                disabled={recentState.loading || page * recent.pageSize >= recent.total}
                onClick={() => setPage((p) => p + 1)}
              >
                Próxima →
              </Button>
            </div>
          </div>
        )}
      </Card>

      {selectedRow && (
        <FeedbackDrawer row={selectedRow} onClose={() => setSelectedId(null)} />
      )}
    </div>
  )
}

function SentimentChip({
  active,
  onClick,
  children,
}: {
  active: boolean
  onClick: () => void
  children: React.ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'rounded-md px-2.5 py-1 text-xs transition',
        active ? 'bg-accent-subtle text-accent' : 'bg-surface-hover text-fg-muted hover:text-fg',
      )}
    >
      {children}
    </button>
  )
}

const feedbackColumns: ReadonlyArray<Column<FeedbackRecentRow>> = [
  {
    key: 'sentiment',
    header: 'Sent.',
    cell: (r) => <SentimentIcon value={r.sentiment} />,
    sortBy: (r) => r.sentiment,
    width: '70px',
  },
  {
    key: 'message',
    header: 'Mensagem avaliada',
    cell: (r) => (
      <div className="flex flex-col gap-0.5">
        <span className="line-clamp-2 text-xs text-fg">
          {r.messagePreview ?? <span className="italic text-fg-dim">(mensagem indisponível)</span>}
        </span>
        {r.comment && (
          <span className="line-clamp-1 text-[11px] text-fg-muted">
            💬 {r.comment}
          </span>
        )}
      </div>
    ),
    sortBy: (r) => r.messagePreview ?? '',
  },
  {
    key: 'user',
    header: 'Usuário',
    cell: (r) => <span className="font-mono text-[11px] text-fg-dim">{r.userId ?? '—'}</span>,
    sortBy: (r) => r.userId ?? '',
  },
  {
    key: 'createdAt',
    header: 'Quando',
    cell: (r) => <span className="text-xs text-fg-muted">{formatBucketLabel(r.createdAt, 'hour')}</span>,
    sortBy: (r) => r.createdAt,
  },
]

function SentimentIcon({ value }: { value: number }) {
  if (value === 1) {
    return (
      <span className="inline-flex items-center gap-1 rounded-md bg-success/15 px-2 py-0.5 text-xs text-success">
        👍
      </span>
    )
  }
  if (value === -1) {
    return (
      <span className="inline-flex items-center gap-1 rounded-md bg-warning/15 px-2 py-0.5 text-xs text-warning">
        👎
      </span>
    )
  }
  return <span className="text-fg-dim">—</span>
}

function FeedbackDrawer({
  row,
  onClose,
}: {
  row: FeedbackRecentRow
  onClose: () => void
}) {
  return (
    <div
      className="fixed inset-0 z-40 flex justify-end bg-black/40 backdrop-blur-sm"
      role="dialog"
      onClick={onClose}
    >
      <div
        className="flex h-full w-full max-w-2xl flex-col border-l border-border bg-bg shadow-xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start justify-between border-b border-border p-5">
          <div className="min-w-0 flex-1">
            <div className="text-xs uppercase tracking-wide text-fg-dim">Feedback</div>
            <div className="mt-1 flex items-center gap-2">
              <SentimentIcon value={row.sentiment} />
              <span className="text-sm text-fg">
                {row.sentiment === 1 ? 'Like' : 'Dislike'} de{' '}
                <span className="font-mono">{row.userId ?? 'anônimo'}</span>
              </span>
            </div>
            <div className="mt-1 text-[11px] text-fg-dim">{formatBucketLabel(row.createdAt, 'hour')}</div>
          </div>
          <Button variant="ghost" className="-mr-2 px-2" onClick={onClose} aria-label="Fechar">
            ×
          </Button>
        </div>
        <div className="flex-1 overflow-y-auto p-5 space-y-5 text-sm">
          {row.comment && (
            <section>
              <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-fg-muted">
                Comentário do usuário
              </h3>
              <pre className="whitespace-pre-wrap rounded-md border border-border bg-surface-hover p-3 text-xs text-fg">
                {row.comment}
              </pre>
            </section>
          )}

          <section>
            <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-fg-muted">
              Mensagem avaliada
            </h3>
            {row.messagePreview ? (
              <JsonViewer
                value={row.messagePreview}
                ariaLabel="conteúdo da mensagem avaliada"
                maxHeightClass="max-h-72"
                showMeta
              />
            ) : (
              <div className="rounded-md border border-dashed border-border px-3 py-4 text-center text-xs text-fg-dim">
                Mensagem indisponível (provavelmente foi expirada/deletada do chat).
              </div>
            )}
          </section>

          <section>
            <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-fg-muted">
              IDs de referência
            </h3>
            <dl className="grid grid-cols-[max-content_1fr] gap-x-3 gap-y-1 text-xs">
              <dt className="text-fg-muted">Feedback</dt>
              <dd className="break-all font-mono text-fg">{row.feedbackId}</dd>
              <dt className="text-fg-muted">Mensagem</dt>
              <dd className="break-all font-mono text-fg">{row.messageId}</dd>
              {row.conversationId && (
                <>
                  <dt className="text-fg-muted">Conversa</dt>
                  <dd className="break-all font-mono text-fg">{row.conversationId}</dd>
                </>
              )}
              {row.executionId && (
                <>
                  <dt className="text-fg-muted">Execução</dt>
                  <dd className="break-all font-mono text-fg">{row.executionId}</dd>
                </>
              )}
            </dl>
          </section>
        </div>
      </div>
    </div>
  )
}

