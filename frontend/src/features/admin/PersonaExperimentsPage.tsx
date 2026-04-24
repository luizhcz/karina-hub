import { useState } from 'react'
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
 * F6 — A/B testing de templates de persona. Lista + criar + encerrar +
 * dashboard de resultados por variant. Decisão: 1 experiment ativo por
 * (project, scope) — UNIQUE parcial no DB força.
 */
export function PersonaExperimentsPage() {
  const { data, isLoading, error, refetch } = usePersonaExperiments()
  const createMut = useCreatePersonaExperiment()
  const endMut = useEndPersonaExperiment()
  const deleteMut = useDeletePersonaExperiment()

  const [creating, setCreating] = useState(false)
  const [endingId, setEndingId] = useState<number | null>(null)
  const [deletingId, setDeletingId] = useState<number | null>(null)
  const [resultsId, setResultsId] = useState<number | null>(null)

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar experiments" onRetry={refetch} />

  const items = data ?? []
  const active = items.filter((e) => e.isActive)
  const ended = items.filter((e) => !e.isActive)

  return (
    <div className="flex flex-col gap-6 p-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Experiments A/B</h1>
          <p className="text-sm text-text-muted mt-1">
            Compara duas versions de um template com bucketing determinístico por{' '}
            <code className="font-mono">userId</code>. Um experiment ativo por scope
            (regra do DB).
          </p>
        </div>
        <Button onClick={() => setCreating(true)}>Novo experiment</Button>
      </div>

      {active.length > 0 && (
        <Card title={`Ativos (${active.length})`} padding={false}>
          <ExperimentsTable
            experiments={active}
            onEnd={(id) => setEndingId(id)}
            onShowResults={(id) => setResultsId(id)}
            onDelete={(id) => setDeletingId(id)}
          />
        </Card>
      )}

      {ended.length > 0 && (
        <Card title={`Encerrados (${ended.length})`} padding={false}>
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
            title="Nenhum experiment cadastrado"
            description="Experiments precisam de pelo menos duas versions de um template. Cadastre um template e edite-o pra gerar versions antes de criar um experiment."
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
        title="Encerrar experiment"
        message="Para o bucketing e libera o scope pra um novo experiment. Operação idempotente. Dados ficam preservados no histórico."
        confirmLabel="Encerrar"
        loading={endMut.isPending}
      />

      <ConfirmDialog
        open={deletingId !== null}
        onClose={() => setDeletingId(null)}
        onConfirm={() => {
          if (deletingId !== null)
            deleteMut.mutate(deletingId, { onSuccess: () => setDeletingId(null) })
        }}
        title="Deletar experiment"
        message="Remove o registro do experiment. Rows em llm_token_usage com ExperimentId deste experiment ficam órfãs (sem cascade por design — preserva analytics histórico)."
        confirmLabel="Deletar"
        variant="danger"
        loading={deleteMut.isPending}
      />

      {resultsId !== null && (
        <ResultsDialog id={resultsId} onClose={() => setResultsId(null)} />
      )}
    </div>
  )
}

// ── Tabela ───────────────────────────────────────────────────────────────────

function ExperimentsTable(props: {
  experiments: PersonaPromptExperiment[]
  onEnd: (id: number) => void
  onShowResults: (id: number) => void
  onDelete: (id: number) => void
}) {
  return (
    <div className="divide-y divide-border-primary">
      {props.experiments.map((exp) => (
        <div key={exp.id} className="flex items-center justify-between px-5 py-4">
          <div className="flex flex-col gap-1 min-w-0">
            <div className="flex items-center gap-2 flex-wrap">
              <span className="font-medium text-text-primary">{exp.name}</span>
              <Badge variant={exp.isActive ? 'green' : 'gray'}>
                {exp.isActive ? 'ativo' : 'encerrado'}
              </Badge>
              <Badge variant="purple">{exp.scope}</Badge>
            </div>
            <div className="text-xs text-text-muted">
              Split: <span className="font-mono">A {100 - exp.trafficSplitB}% / B {exp.trafficSplitB}%</span>
              {' · '}Métrica: <code className="font-mono">{exp.metric}</code>
              {' · '}Iniciou: {new Date(exp.startedAt).toLocaleString('pt-BR')}
              {exp.endedAt ? ` · Encerrou: ${new Date(exp.endedAt).toLocaleString('pt-BR')}` : ''}
            </div>
            <div className="text-xs text-text-dimmed font-mono">
              A: {exp.variantAVersionId.slice(0, 8)}… · B: {exp.variantBVersionId.slice(0, 8)}…
            </div>
          </div>
          <div className="flex items-center gap-2">
            <Button variant="secondary" size="sm" onClick={() => props.onShowResults(exp.id)}>
              Resultados
            </Button>
            {exp.isActive && (
              <Button variant="secondary" size="sm" onClick={() => props.onEnd(exp.id)}>
                Encerrar
              </Button>
            )}
            <Button variant="danger" size="sm" onClick={() => props.onDelete(exp.id)}>
              Deletar
            </Button>
          </div>
        </div>
      ))}
    </div>
  )
}

// ── Dialog de criação ────────────────────────────────────────────────────────

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
      <Card title="Novo experiment" className="w-full max-w-2xl">
        <div className="flex flex-col gap-4">
          <Field label="Scope" hint="Ex: project:p1:cliente ou agent:trading:admin">
            <input
              value={scope}
              onChange={(e) => setScope(e.target.value)}
              className="form-input w-full"
              placeholder="project:p1:cliente"
            />
          </Field>
          <Field label="Nome">
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              className="form-input w-full"
              placeholder="Ex: novo tom de suitability v2"
            />
          </Field>
          <div className="grid grid-cols-2 gap-3">
            <Field label="Variant A (VersionId UUID)">
              <input
                value={vA}
                onChange={(e) => setVA(e.target.value)}
                className="form-input w-full font-mono text-xs"
                placeholder="00000000-0000-0000-0000-000000000000"
              />
            </Field>
            <Field label="Variant B (VersionId UUID)">
              <input
                value={vB}
                onChange={(e) => setVB(e.target.value)}
                className="form-input w-full font-mono text-xs"
                placeholder="00000000-0000-0000-0000-000000000000"
              />
            </Field>
          </div>
          <Field label={`Split B: ${split}% (A recebe ${100 - split}%)`}>
            <input
              type="range"
              min={0}
              max={100}
              value={split}
              onChange={(e) => setSplit(Number(e.target.value))}
              className="w-full"
            />
          </Field>
          <Field label="Métrica de interesse">
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
              Cancelar
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
              {props.pending ? 'Criando…' : 'Criar'}
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

// ── Dialog de resultados ─────────────────────────────────────────────────────

function ResultsDialog(props: { id: number; onClose: () => void }) {
  const { data, isLoading, error } = usePersonaExperimentResults(props.id)

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
      <Card title="Resultados por variant" className="w-full max-w-3xl">
        <div className="flex flex-col gap-4">
          {isLoading && <p className="text-sm text-text-muted">Carregando…</p>}
          {error && <p className="text-sm text-accent-red">Erro ao carregar.</p>}
          {data && (
            <>
              <div className="text-sm text-text-muted">
                <strong>{data.experiment.name}</strong> · scope{' '}
                <code className="font-mono">{data.experiment.scope}</code>
                {' · '}métrica <code className="font-mono">{data.experiment.metric}</code>
              </div>
              {data.results.length === 0 ? (
                <p className="text-sm text-text-dimmed italic">
                  Nenhuma LLM call registrada sob esse experiment ainda.
                </p>
              ) : (
                <table className="w-full text-sm">
                  <thead className="text-left text-text-muted">
                    <tr>
                      <th className="py-2">Variant</th>
                      <th className="py-2 text-right">Calls</th>
                      <th className="py-2 text-right">Total tokens</th>
                      <th className="py-2 text-right">Cached</th>
                      <th className="py-2 text-right">Média tokens</th>
                      <th className="py-2 text-right">Latência (ms)</th>
                    </tr>
                  </thead>
                  <tbody className="font-mono">
                    {data.results.map((r) => (
                      <tr key={r.variant} className="border-t border-border-primary">
                        <td className="py-2">
                          <Badge variant={r.variant === 'A' ? 'blue' : 'purple'}>{r.variant}</Badge>
                        </td>
                        <td className="py-2 text-right">{r.sampleCount}</td>
                        <td className="py-2 text-right">{r.totalTokens.toLocaleString('pt-BR')}</td>
                        <td className="py-2 text-right">{r.cachedTokens.toLocaleString('pt-BR')}</td>
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
            <Button onClick={props.onClose}>Fechar</Button>
          </div>
        </div>
      </Card>
    </div>
  )
}
