import { useEffect, useMemo, useRef, useState } from 'react'
import { friendlyError } from '../../api/client'
import {
  createIngestion,
  getStandaloneJob,
  isTerminal,
  type CreateIngestionInput,
  type StandaloneJobResponse,
  type StandaloneJobStatus,
} from '../../api/ingestions'
import { listWorkflows, type Workflow } from '../../api/workflows'
import { useIsAdmin } from '../../stores/me'
import {
  Badge,
  Button,
  Card,
  CardHeader,
  EmptyState,
  ErrorMessage,
  Input,
  Select,
  Spinner,
  Textarea,
} from '../../ui'

type Phase = 'idle' | 'submitting' | 'polling' | 'terminal'

interface KeyValuePair {
  key: string
  value: string
}

const POLL_INTERVAL_MS = 5000

const STEP_LABELS: Record<string, string> = {
  Downloading: 'Baixando arquivo',
  Validating: 'Validando tipo (PDF/TXT/MD)',
  Extracting: 'Extraindo conteúdo via Document Intelligence',
  ContentPersisted: 'Conteúdo extraído — disparando workflow',
  WorkflowRunning: 'Workflow em execução',
}

const ORDERED_STEPS = ['Downloading', 'Validating', 'Extracting', 'ContentPersisted', 'WorkflowRunning']

function statusTone(status: StandaloneJobStatus): 'neutral' | 'accent' | 'success' | 'danger' {
  switch (status) {
    case 'Queued':
      return 'neutral'
    case 'Running':
      return 'accent'
    case 'Completed':
      return 'success'
    case 'Failed':
    case 'Cancelled':
      return 'danger'
    default:
      return 'neutral'
  }
}

function formatDuration(fromIso: string | null, toIso: string | null): string | null {
  if (!fromIso || !toIso) return null
  try {
    const ms = new Date(toIso).getTime() - new Date(fromIso).getTime()
    if (ms < 1000) return `${ms}ms`
    if (ms < 60000) return `${(ms / 1000).toFixed(1)}s`
    return `${Math.floor(ms / 60000)}min ${Math.round((ms % 60000) / 1000)}s`
  } catch {
    return null
  }
}

export function Ingestoes() {
  const isAdmin = useIsAdmin()

  // Form state
  const [workflowId, setWorkflowId] = useState('')
  const [url, setUrl] = useState('')
  const [idempotencyKey, setIdempotencyKey] = useState('')
  const [advancedOpen, setAdvancedOpen] = useState(false)
  const [downloadHeaders, setDownloadHeaders] = useState<KeyValuePair[]>([])
  const [metadata, setMetadata] = useState<KeyValuePair[]>([])
  const [callbackUrl, setCallbackUrl] = useState('')
  const [callbackHmac, setCallbackHmac] = useState('')

  // Workflows dropdown (carrega assíncrono, filtra Standalone)
  const [workflows, setWorkflows] = useState<Workflow[]>([])
  const [workflowsLoading, setWorkflowsLoading] = useState(false)
  const [workflowsError, setWorkflowsError] = useState<string | null>(null)

  // Job lifecycle
  const [phase, setPhase] = useState<Phase>('idle')
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [job, setJob] = useState<StandaloneJobResponse | null>(null)
  const pollerRef = useRef<number | null>(null)

  // ── Carrega lista de workflows standalone do projeto atual ──────────────
  useEffect(() => {
    if (isAdmin !== true) return
    let cancelled = false
    setWorkflowsLoading(true)
    setWorkflowsError(null)
    listWorkflows()
      .then((all) => {
        if (cancelled) return
        const standalone = all.filter((w) => w.configuration?.inputMode === 'Standalone')
        setWorkflows(standalone)
      })
      .catch((err: unknown) => {
        if (!cancelled) setWorkflowsError(friendlyError(err, 'Falha ao carregar workflows.'))
      })
      .finally(() => {
        if (!cancelled) setWorkflowsLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [isAdmin])

  // ── Polling do job ──────────────────────────────────────────────────────
  useEffect(() => {
    if (phase !== 'polling' || !job?.jobId) return
    let cancelled = false

    async function tick(jobId: string) {
      try {
        const next = await getStandaloneJob(jobId)
        if (cancelled) return
        setJob(next)
        if (isTerminal(next.status)) {
          setPhase('terminal')
          stopPoller()
        }
      } catch (err) {
        if (cancelled) return
        setSubmitError(friendlyError(err, 'Falha ao consultar o estado do job.'))
        // Mantém em polling — próximo tick tenta de novo.
      }
    }

    // Polling: usa window.setInterval (5s) + stop on terminal/unmount.
    const jobId = job.jobId
    pollerRef.current = window.setInterval(() => {
      void tick(jobId)
    }, POLL_INTERVAL_MS)

    return () => {
      cancelled = true
      stopPoller()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [phase, job?.jobId])

  function stopPoller() {
    if (pollerRef.current !== null) {
      window.clearInterval(pollerRef.current)
      pollerRef.current = null
    }
  }

  // Cleanup global no unmount.
  useEffect(() => {
    return () => stopPoller()
  }, [])

  const selectedWorkflow = useMemo(
    () => workflows.find((w) => w.id === workflowId) ?? null,
    [workflows, workflowId],
  )

  const canSubmit =
    phase === 'idle' &&
    workflowId.trim().length > 0 &&
    url.trim().length > 0

  function resetForNew() {
    setPhase('idle')
    setJob(null)
    setSubmitError(null)
    // Mantém form preenchido — operador costuma re-disparar com pequenos ajustes.
  }

  async function handleSubmit() {
    if (!canSubmit) return
    setSubmitError(null)
    setPhase('submitting')

    const body: CreateIngestionInput = {
      workflowId: workflowId.trim(),
      source: {
        type: 'url',
        url: url.trim(),
        headers: pairsToObject(downloadHeaders),
      },
      metadata: pairsToObject(metadata),
      idempotencyKey: idempotencyKey.trim() || null,
    }

    if (callbackUrl.trim()) {
      body.callback = {
        url: callbackUrl.trim(),
        hmacSecret: callbackHmac.trim() || null,
      }
    }

    try {
      const accepted = await createIngestion(body)
      // Mapeia a primeira resposta pro shape de polling (jobId + status inicial).
      setJob({
        jobId: accepted.jobId,
        workflowId: accepted.workflowId,
        executionId: null,
        status: (accepted.status as StandaloneJobStatus) ?? 'Queued',
        step: accepted.step,
        attempt: 0,
        output: null,
        lastError: null,
        createdAt: accepted.createdAt,
        startedAt: null,
        completedAt: null,
        updatedAt: accepted.updatedAt,
        pollUrl: accepted.pollUrl,
      })
      setPhase('polling')
    } catch (err) {
      setSubmitError(friendlyError(err, 'Falha ao enfileirar a ingestão.'))
      setPhase('idle')
    }
  }

  if (isAdmin === null) return null
  if (isAdmin === false) {
    return (
      <Card>
        <EmptyState
          title="Acesso restrito"
          description="A tela de Ingestões é exclusiva para administradores."
        />
      </Card>
    )
  }

  return (
    <div className="space-y-5">
      <div>
        <h1 className="text-xl font-semibold tracking-tight">Ingestões</h1>
        <p className="mt-1 text-sm text-fg-muted">
          Dispare a ingestão de uma URL (PDF, TXT ou MD). O backend baixa, valida o tipo,
          extrai conteúdo via Document Intelligence (apenas PDF) e dispara o workflow
          standalone selecionado. Acompanhe o progresso em tempo real.
        </p>
      </div>

      <Card>
        <CardHeader title="Nova ingestão" />
        <div className="space-y-4">
          <Field label="Workflow" required>
            {workflowsLoading ? (
              <div className="flex items-center gap-2 text-sm text-fg-muted">
                <Spinner /> Carregando workflows…
              </div>
            ) : workflowsError ? (
              <ErrorMessage message={workflowsError} />
            ) : workflows.length === 0 ? (
              <div className="text-sm text-fg-muted">
                Nenhum workflow Standalone encontrado neste projeto.
              </div>
            ) : (
              <Select
                value={workflowId}
                onChange={(e) => setWorkflowId(e.target.value)}
                options={[
                  { value: '', label: 'Selecione um workflow…' },
                  ...workflows.map((w) => ({ value: w.id, label: `${w.name} · ${w.id}` })),
                ]}
                disabled={phase !== 'idle'}
              />
            )}
          </Field>

          <Field label="URL do arquivo" required>
            <Input
              type="url"
              placeholder="https://exemplo.com/arquivo.pdf"
              value={url}
              onChange={(e) => setUrl(e.target.value)}
              disabled={phase !== 'idle'}
            />
          </Field>

          <Field
            label="Idempotency-Key"
            hint="Opcional. Mesmo valor não duplica o job. Charset [A-Za-z0-9_:.-], até 128 chars."
          >
            <Input
              value={idempotencyKey}
              onChange={(e) => setIdempotencyKey(e.target.value)}
              disabled={phase !== 'idle'}
            />
          </Field>

          <div>
            <button
              type="button"
              onClick={() => setAdvancedOpen((v) => !v)}
              className="text-sm text-accent hover:underline"
            >
              {advancedOpen ? '▾ Ocultar opções avançadas' : '▸ Mostrar opções avançadas'}
            </button>
          </div>

          {advancedOpen && (
            <div className="space-y-4 rounded-lg border border-border bg-bg-soft p-4">
              <Field
                label="Headers HTTP do download"
                hint="Repassados na requisição GET do arquivo (ex.: Authorization)."
              >
                <KeyValueEditor
                  pairs={downloadHeaders}
                  onChange={setDownloadHeaders}
                  disabled={phase !== 'idle'}
                  keyPlaceholder="Authorization"
                  valuePlaceholder="Bearer …"
                />
              </Field>

              <Field
                label="Metadata"
                hint="Pares chave/valor propagados pro workflow. Até 64 chaves."
              >
                <KeyValueEditor
                  pairs={metadata}
                  onChange={setMetadata}
                  disabled={phase !== 'idle'}
                  keyPlaceholder="clientId"
                  valuePlaceholder="abc-123"
                />
              </Field>

              <Field
                label="Callback (webhook)"
                hint="Quando preenchido, o backend faz POST com HMAC-SHA256 após terminal."
              >
                <div className="space-y-2">
                  <Input
                    type="url"
                    placeholder="URL do webhook (https://...)"
                    value={callbackUrl}
                    onChange={(e) => setCallbackUrl(e.target.value)}
                    disabled={phase !== 'idle'}
                  />
                  <Input
                    placeholder="HMAC secret (opcional)"
                    value={callbackHmac}
                    onChange={(e) => setCallbackHmac(e.target.value)}
                    disabled={phase !== 'idle'}
                  />
                </div>
              </Field>
            </div>
          )}

          {submitError && <ErrorMessage message={submitError} />}

          <div className="flex items-center gap-3">
            <Button
              onClick={handleSubmit}
              disabled={!canSubmit}
            >
              {phase === 'submitting' ? (
                <span className="flex items-center gap-2">
                  <Spinner /> Enviando…
                </span>
              ) : (
                'Enviar ingestão'
              )}
            </Button>
            {phase === 'terminal' && (
              <Button variant="ghost" onClick={resetForNew}>
                Nova ingestão
              </Button>
            )}
            {selectedWorkflow && phase === 'idle' && (
              <span className="text-xs text-fg-muted">
                Workflow: {selectedWorkflow.name}
              </span>
            )}
          </div>
        </div>
      </Card>

      {job && (phase === 'polling' || phase === 'terminal') && (
        <JobTracker job={job} phase={phase} />
      )}
    </div>
  )
}

// ── Sub-componentes ────────────────────────────────────────────────────────

function Field({
  label,
  hint,
  required,
  children,
}: {
  label: string
  hint?: string
  required?: boolean
  children: React.ReactNode
}) {
  return (
    <label className="block text-sm">
      <span className="block font-medium text-fg">
        {label}
        {required && <span className="ml-0.5 text-danger">*</span>}
      </span>
      {hint && <span className="mt-0.5 block text-xs text-fg-muted">{hint}</span>}
      <div className="mt-1.5">{children}</div>
    </label>
  )
}

function KeyValueEditor({
  pairs,
  onChange,
  disabled,
  keyPlaceholder,
  valuePlaceholder,
}: {
  pairs: KeyValuePair[]
  onChange: (next: KeyValuePair[]) => void
  disabled?: boolean
  keyPlaceholder: string
  valuePlaceholder: string
}) {
  function update(i: number, patch: Partial<KeyValuePair>) {
    onChange(pairs.map((p, idx) => (idx === i ? { ...p, ...patch } : p)))
  }
  function remove(i: number) {
    onChange(pairs.filter((_, idx) => idx !== i))
  }
  function add() {
    onChange([...pairs, { key: '', value: '' }])
  }

  return (
    <div className="space-y-2">
      {pairs.length === 0 && (
        <p className="text-xs italic text-fg-dim">Nenhuma chave configurada.</p>
      )}
      {pairs.map((pair, i) => (
        <div key={i} className="flex items-center gap-2">
          <Input
            value={pair.key}
            onChange={(e) => update(i, { key: e.target.value })}
            placeholder={keyPlaceholder}
            disabled={disabled}
          />
          <Input
            value={pair.value}
            onChange={(e) => update(i, { value: e.target.value })}
            placeholder={valuePlaceholder}
            disabled={disabled}
          />
          <Button variant="ghost" onClick={() => remove(i)} disabled={disabled}>
            ✕
          </Button>
        </div>
      ))}
      <Button variant="ghost" onClick={add} disabled={disabled}>
        + Adicionar
      </Button>
    </div>
  )
}

function JobTracker({ job, phase }: { job: StandaloneJobResponse; phase: Phase }) {
  const tone = statusTone(job.status)
  const elapsed = formatDuration(job.createdAt, job.completedAt ?? new Date().toISOString())
  const stepIndex = job.step ? ORDERED_STEPS.indexOf(job.step) : -1

  return (
    <Card>
      <CardHeader
        title={
          <span className="flex items-center gap-2">
            <span>Job {job.jobId.slice(0, 12)}…</span>
            <Badge tone={tone}>{job.status}</Badge>
          </span>
        }
        description={
          job.workflowId ? (
            <>workflow: <code className="font-mono text-xs">{job.workflowId}</code></>
          ) : undefined
        }
      />
      <div className="space-y-4">
        {/* Timeline de steps */}
        <ol className="space-y-2">
          {ORDERED_STEPS.map((step, i) => {
            const isPdfOnly = step === 'Extracting'
            const isCurrent = job.step === step && job.status === 'Running'
            const isPassed = stepIndex > i || job.status === 'Completed'
            return (
              <li key={step} className="flex items-start gap-3 text-sm">
                <StepDot active={isCurrent} done={isPassed} />
                <div>
                  <div className={isCurrent ? 'font-medium text-fg' : 'text-fg-muted'}>
                    {STEP_LABELS[step] ?? step}
                    {isPdfOnly && (
                      <span className="ml-2 text-xs text-fg-dim">(apenas PDF)</span>
                    )}
                  </div>
                  {isCurrent && (
                    <div className="text-xs text-fg-dim">Atualizando a cada 5 segundos…</div>
                  )}
                </div>
              </li>
            )
          })}
        </ol>

        {/* Metadados */}
        <div className="grid grid-cols-2 gap-3 border-t border-border pt-3 text-xs text-fg-muted">
          <div>
            <span className="block text-fg-dim">Tentativa</span>
            <span className="font-mono">{job.attempt}</span>
          </div>
          <div>
            <span className="block text-fg-dim">Tempo decorrido</span>
            <span className="font-mono">{elapsed ?? '—'}</span>
          </div>
          {job.executionId && (
            <div className="col-span-2">
              <span className="block text-fg-dim">ExecutionId</span>
              <code className="font-mono text-xs">{job.executionId}</code>
            </div>
          )}
        </div>

        {/* Terminal — output ou erro */}
        {phase === 'terminal' && job.status === 'Completed' && (
          <div>
            <h3 className="mb-2 text-sm font-medium text-fg">Output</h3>
            <OutputBlock raw={job.output} />
          </div>
        )}
        {phase === 'terminal' && job.status === 'Failed' && (
          <div>
            <h3 className="mb-2 text-sm font-medium text-danger">Erro</h3>
            <Textarea
              value={job.lastError ?? ''}
              readOnly
              className="font-mono text-xs"
              rows={4}
            />
            {job.step && (
              <p className="mt-2 text-xs text-fg-muted">
                Falhou no step: <code className="font-mono">{job.step}</code>
              </p>
            )}
          </div>
        )}
      </div>
    </Card>
  )
}

function StepDot({ active, done }: { active: boolean; done: boolean }) {
  return (
    <span
      className={
        'mt-1 inline-block h-2.5 w-2.5 shrink-0 rounded-full ' +
        (done
          ? 'bg-accent'
          : active
            ? 'bg-accent ring-2 ring-accent-subtle animate-pulse'
            : 'bg-border')
      }
    />
  )
}

function OutputBlock({ raw }: { raw: string | null }) {
  const formatted = useMemo(() => {
    if (!raw) return ''
    try {
      return JSON.stringify(JSON.parse(raw), null, 2)
    } catch {
      return raw
    }
  }, [raw])

  return (
    <Textarea
      value={formatted}
      readOnly
      className="font-mono text-xs"
      rows={Math.min(20, Math.max(6, formatted.split('\n').length))}
    />
  )
}

function pairsToObject(pairs: KeyValuePair[]): Record<string, string> | null {
  const valid = pairs.filter((p) => p.key.trim().length > 0)
  if (valid.length === 0) return null
  const obj: Record<string, string> = {}
  for (const p of valid) {
    obj[p.key.trim()] = p.value
  }
  return obj
}
