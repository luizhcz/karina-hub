import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  useCreatePersonaExperiment,
  useDeletePersonaExperiment,
  useEndPersonaExperiment,
  usePersonaExperimentResults,
  usePersonaExperiments,
  type PersonaPromptExperiment,
} from '../../api/personaExperiments'
import { Card } from '../../shared/ui/Card'
import { Badge } from '../../shared/ui/Badge'
import { Button } from '../../shared/ui/Button'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { EmptyState } from '../../shared/ui/EmptyState'

/**
 * A/B testing de templates de persona. Lista + criar + encerrar +
 * dashboard de resultados por variant. Decisão: 1 experiment ativo por
 * (project, scope) — UNIQUE parcial no DB força.
 */
export function PersonaExperimentsPage() {
  const { t } = useTranslation('persona')
  const { data, isLoading, error, refetch } = usePersonaExperiments()
  const createMut = useCreatePersonaExperiment()
  const endMut = useEndPersonaExperiment()
  const deleteMut = useDeletePersonaExperiment()

  const [creating, setCreating] = useState(false)
  const [endingId, setEndingId] = useState<number | null>(null)
  const [deletingId, setDeletingId] = useState<number | null>(null)
  const [resultsId, setResultsId] = useState<number | null>(null)

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message={t('experiments.errorLoading')} onRetry={refetch} />

  const items = data ?? []
  const active = items.filter((e) => e.isActive)
  const ended = items.filter((e) => !e.isActive)

  return (
    <div className="flex flex-col gap-6 p-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-text-primary">{t('experiments.title')}</h1>
          <p className="text-sm text-text-muted mt-1">
            {t('experiments.subtitle', { code: 'userId' })}
          </p>
        </div>
        <Button onClick={() => setCreating(true)}>{t('experiments.newButton')}</Button>
      </div>

      {active.length > 0 && (
        <Card title={t('experiments.activeSection', { count: active.length })} padding={false}>
          <ExperimentsTable
            experiments={active}
            onEnd={(id) => setEndingId(id)}
            onShowResults={(id) => setResultsId(id)}
            onDelete={(id) => setDeletingId(id)}
          />
        </Card>
      )}

      {ended.length > 0 && (
        <Card title={t('experiments.endedSection', { count: ended.length })} padding={false}>
          <ExperimentsTable
            experiments={ended}
            onEnd={() => undefined}
            onShowResults={(id) => setResultsId(id)}
            onDelete={(id) => setDeletingId(id)}
          />
        </Card>
      )}

      {items.length === 0 && (
        <Card padding={false}>
          <EmptyState
            title={t('experiments.emptyTitle')}
            description={t('experiments.emptyDescription')}
          />
        </Card>
      )}

      {creating && (
        <CreateExperimentDialog
          onClose={() => setCreating(false)}
          onSubmit={(body) =>
            createMut.mutate(body, { onSuccess: () => setCreating(false) })
          }
          pending={createMut.isPending}
          error={createMut.error?.message ?? null}
        />
      )}

      <ConfirmDialog
        open={endingId !== null}
        onClose={() => setEndingId(null)}
        onConfirm={() => {
          if (endingId !== null)
            endMut.mutate(endingId, { onSuccess: () => setEndingId(null) })
        }}
        title={t('experiments.endDialog.title')}
        message={t('experiments.endDialog.message')}
        confirmLabel={t('experiments.endDialog.confirm')}
        loading={endMut.isPending}
      />

      <ConfirmDialog
        open={deletingId !== null}
        onClose={() => setDeletingId(null)}
        onConfirm={() => {
          if (deletingId !== null)
            deleteMut.mutate(deletingId, { onSuccess: () => setDeletingId(null) })
        }}
        title={t('experiments.deleteDialog.title')}
        message={t('experiments.deleteDialog.message')}
        confirmLabel={t('experiments.deleteDialog.confirm')}
        variant="danger"
        loading={deleteMut.isPending}
      />

      {resultsId !== null && (
        <ResultsDialog id={resultsId} onClose={() => setResultsId(null)} />
      )}
    </div>
  )
}


function ExperimentsTable(props: {
  experiments: PersonaPromptExperiment[]
  onEnd: (id: number) => void
  onShowResults: (id: number) => void
  onDelete: (id: number) => void
}) {
  const { t, i18n } = useTranslation('persona')
  const locale = i18n.language // 'pt-BR' ou 'en-US' pra Intl.DateTimeFormat
  return (
    <div className="divide-y divide-border-primary">
      {props.experiments.map((exp) => (
        <div key={exp.id} className="flex items-center justify-between px-5 py-4">
          <div className="flex flex-col gap-1 min-w-0">
            <div className="flex items-center gap-2 flex-wrap">
              <span className="font-medium text-text-primary">{exp.name}</span>
              <Badge variant={exp.isActive ? 'green' : 'gray'}>
                {exp.isActive ? t('experiments.statusActive') : t('experiments.statusEnded')}
              </Badge>
              <Badge variant="purple">{exp.scope}</Badge>
            </div>
            <div className="text-xs text-text-muted">
              {t('experiments.splitLabel')}{' '}
              <span className="font-mono">A {100 - exp.trafficSplitB}% / B {exp.trafficSplitB}%</span>
              {' · '}{t('experiments.metricLabel')}{' '}<code className="font-mono">{exp.metric}</code>
              {' · '}{t('experiments.startedLabel')}{' '}{new Date(exp.startedAt).toLocaleString(locale)}
              {exp.endedAt ? ` · ${t('experiments.endedLabel')} ${new Date(exp.endedAt).toLocaleString(locale)}` : ''}
            </div>
            <div className="text-xs text-text-dimmed font-mono">
              A: {exp.variantAVersionId.slice(0, 8)}… · B: {exp.variantBVersionId.slice(0, 8)}…
            </div>
          </div>
          <div className="flex items-center gap-2">
            <Button variant="secondary" size="sm" onClick={() => props.onShowResults(exp.id)}>
              {t('experiments.actions.results')}
            </Button>
            {exp.isActive && (
              <Button variant="secondary" size="sm" onClick={() => props.onEnd(exp.id)}>
                {t('experiments.actions.end')}
              </Button>
            )}
            <Button variant="danger" size="sm" onClick={() => props.onDelete(exp.id)}>
              {t('experiments.actions.delete')}
            </Button>
          </div>
        </div>
      ))}
    </div>
  )
}


function CreateExperimentDialog(props: {
  onClose: () => void
  onSubmit: (body: {
    scope: string
    name: string
    variantAVersionId: string
    variantBVersionId: string
    trafficSplitB: number
    metric: string
  }) => void
  pending: boolean
  error: string | null
}) {
  const { t } = useTranslation('persona')
  const [scope, setScope] = useState('')
  const [name, setName] = useState('')
  const [vA, setVA] = useState('')
  const [vB, setVB] = useState('')
  const [split, setSplit] = useState(50)
  const [metric, setMetric] = useState('cost_usd')

  const isValid =
    scope.trim().length > 0 &&
    name.trim().length > 0 &&
    vA.trim().length > 0 &&
    vB.trim().length > 0 &&
    vA !== vB &&
    split >= 0 &&
    split <= 100

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
      <Card title={t('experiments.create.title')} className="w-full max-w-2xl">
        <div className="flex flex-col gap-4">
          <Field label={t('experiments.create.fieldScope')} hint={t('experiments.create.fieldScopeHint')}>
            <input
              value={scope}
              onChange={(e) => setScope(e.target.value)}
              className="form-input w-full"
              placeholder={t('experiments.create.fieldScopePlaceholder')}
            />
          </Field>
          <Field label={t('experiments.create.fieldName')}>
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              className="form-input w-full"
              placeholder={t('experiments.create.fieldNamePlaceholder')}
            />
          </Field>
          <div className="grid grid-cols-2 gap-3">
            <Field label={t('experiments.create.fieldVariantA')}>
              <input
                value={vA}
                onChange={(e) => setVA(e.target.value)}
                className="form-input w-full font-mono text-xs"
                placeholder="00000000-0000-0000-0000-000000000000"
              />
            </Field>
            <Field label={t('experiments.create.fieldVariantB')}>
              <input
                value={vB}
                onChange={(e) => setVB(e.target.value)}
                className="form-input w-full font-mono text-xs"
                placeholder="00000000-0000-0000-0000-000000000000"
              />
            </Field>
          </div>
          <Field label={t('experiments.create.fieldSplit', { split, aShare: 100 - split })}>
            <input
              type="range"
              min={0}
              max={100}
              value={split}
              onChange={(e) => setSplit(Number(e.target.value))}
              className="w-full"
            />
          </Field>
          <Field label={t('experiments.create.fieldMetric')}>
            <select
              value={metric}
              onChange={(e) => setMetric(e.target.value)}
              className="form-input w-full"
            >
              <option value="cost_usd">cost_usd</option>
              <option value="total_tokens">total_tokens</option>
              <option value="hitl_approved">hitl_approved</option>
            </select>
          </Field>
          {props.error && (
            <p className="text-sm text-accent-red">{props.error}</p>
          )}
          <div className="flex items-center justify-end gap-2 pt-2">
            <Button variant="ghost" onClick={props.onClose}>
              {t('experiments.create.cancel')}
            </Button>
            <Button
              onClick={() =>
                props.onSubmit({
                  scope: scope.trim(),
                  name: name.trim(),
                  variantAVersionId: vA.trim(),
                  variantBVersionId: vB.trim(),
                  trafficSplitB: split,
                  metric,
                })
              }
              disabled={!isValid || props.pending}
            >
              {props.pending ? t('experiments.create.submitting') : t('experiments.create.submit')}
            </Button>
          </div>
        </div>
      </Card>
    </div>
  )
}

function Field(props: {
  label: string
  hint?: string
  children: React.ReactNode
}) {
  return (
    <label className="flex flex-col gap-1">
      <span className="text-sm font-medium text-text-secondary">{props.label}</span>
      {props.children}
      {props.hint && <span className="text-xs text-text-dimmed">{props.hint}</span>}
    </label>
  )
}


function ResultsDialog(props: { id: number; onClose: () => void }) {
  const { t, i18n } = useTranslation('persona')
  const locale = i18n.language
  const { data, isLoading, error } = usePersonaExperimentResults(props.id)

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
      <Card title={t('experiments.results.title')} className="w-full max-w-3xl">
        <div className="flex flex-col gap-4">
          {isLoading && <p className="text-sm text-text-muted">{t('experiments.results.loading')}</p>}
          {error && <p className="text-sm text-accent-red">{t('experiments.results.error')}</p>}
          {data && (
            <>
              <div className="text-sm text-text-muted">
                <strong>{data.experiment.name}</strong> · {t('experiments.results.scopePrefix')}{' '}
                <code className="font-mono">{data.experiment.scope}</code>
                {' · '}{t('experiments.results.metricPrefix')}{' '}<code className="font-mono">{data.experiment.metric}</code>
              </div>
              {data.results.length === 0 ? (
                <p className="text-sm text-text-dimmed italic">
                  {t('experiments.results.empty')}
                </p>
              ) : (
                <table className="w-full text-sm">
                  <thead className="text-left text-text-muted">
                    <tr>
                      <th className="py-2">{t('experiments.results.colVariant')}</th>
                      <th className="py-2 text-right">{t('experiments.results.colCalls')}</th>
                      <th className="py-2 text-right">{t('experiments.results.colTotalTokens')}</th>
                      <th className="py-2 text-right">{t('experiments.results.colCached')}</th>
                      <th className="py-2 text-right">{t('experiments.results.colAvgTokens')}</th>
                      <th className="py-2 text-right">{t('experiments.results.colLatency')}</th>
                    </tr>
                  </thead>
                  <tbody className="font-mono">
                    {data.results.map((r) => (
                      <tr key={r.variant} className="border-t border-border-primary">
                        <td className="py-2">
                          <Badge variant={r.variant === 'A' ? 'blue' : 'purple'}>{r.variant}</Badge>
                        </td>
                        <td className="py-2 text-right">{r.sampleCount}</td>
                        <td className="py-2 text-right">{r.totalTokens.toLocaleString(locale)}</td>
                        <td className="py-2 text-right">{r.cachedTokens.toLocaleString(locale)}</td>
                        <td className="py-2 text-right">{r.avgTotalTokens.toFixed(0)}</td>
                        <td className="py-2 text-right">{r.avgDurationMs.toFixed(0)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </>
          )}
          <div className="flex items-center justify-end pt-2">
            <Button onClick={props.onClose}>{t('experiments.results.close')}</Button>
          </div>
        </div>
      </Card>
    </div>
  )
}
