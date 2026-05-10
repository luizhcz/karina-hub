import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { ApiError, friendlyError, get } from '../api/client'
import {
  analyzeRouterIntent,
  createRouterIntent,
  getRouterIntent,
  updateRouterIntent,
  type RouterIntent,
  type RouterIntentAnalyzerOutput,
  type RouterIntentQualityChecks,
} from '../api/routerIntents'
import {
  Badge,
  Button,
  Card,
  CloseIcon,
  ErrorMessage,
  IconButton,
  Input,
  PlusIcon,
  Spinner,
  Textarea,
  cn,
} from '../ui'

interface RouterIntentEditorProps {
  mode: 'create' | 'edit'
}

const DISPLAY_NAME_LIMIT = 60
const DESCRIPTION_LIMIT = 280
const ANALYSIS_MESSAGES = ['Estamos analisando…', 'Aguarde…', 'Pensando…'] as const
const ANALYSIS_ROTATION_MS = 2200
const ANALYSIS_DEADLINE_MS = 28_000
const POLL_DELAYS_MS = [400, 600, 900, 1350, 1500] as const

const QUALITY_CHECK_LABELS: Array<{
  key: keyof RouterIntentQualityChecks
  label: string
}> = [
  { key: 'contextoUsuario', label: 'Contexto do usuário' },
  { key: 'foraDeEscopo', label: 'Fora de escopo' },
  { key: 'resultadoEsperado', label: 'Resultado esperado' },
]

interface ExecutionResponse {
  status: 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Cancelled'
  output?: string | null
}

class AnalysisFailureError extends Error {
  constructor(msg = 'O analisador não conseguiu processar a solicitação.') {
    super(msg)
    this.name = 'AnalysisFailureError'
  }
}

class AnalysisTimeoutError extends Error {
  constructor() {
    super('Análise demorou demais. Tente novamente.')
    this.name = 'AnalysisTimeoutError'
  }
}

// Estados do botão primário. Em vez de um único "Salvar" que faz analyze+save,
// o user passa por dois passos explícitos: primeiro Analisar (vê resultado),
// depois Salvar. Isso evita salvar silenciosamente em casos sem conflito —
// user sempre confirma o que vai pro pool.
type ActionState =
  | { kind: 'idle' } // Sem análise — botão "Analisar intenção"
  | { kind: 'analyzing' } // Polling do executionId
  | { kind: 'analyzed'; output: RouterIntentAnalyzerOutput } // Pronto pra salvar
  | { kind: 'error'; message: string }

export function RouterIntentEditor({ mode }: RouterIntentEditorProps) {
  const navigate = useNavigate()
  const { id } = useParams<{ id?: string }>()

  const [original, setOriginal] = useState<RouterIntent | null>(null)
  const [loading, setLoading] = useState(mode === 'edit')
  const [loadError, setLoadError] = useState<string | null>(null)

  const [displayName, setDisplayName] = useState('')
  const [description, setDescription] = useState('')
  const [examples, setExamples] = useState<string[]>([])
  const [exampleDraft, setExampleDraft] = useState('')

  const [action, setAction] = useState<ActionState>({ kind: 'idle' })
  const [analysisMsgIdx, setAnalysisMsgIdx] = useState(0)
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const abortRef = useRef<AbortController | null>(null)

  useEffect(() => {
    if (mode !== 'edit' || !id) return
    let cancelled = false
    setLoading(true)
    getRouterIntent(id)
      .then((intent) => {
        if (cancelled) return
        setOriginal(intent)
        setDisplayName(intent.displayName?.trim() || intent.name)
        setDescription(intent.description)
        setExamples(intent.examples ?? [])
        setLoadError(null)
      })
      .catch((err) => {
        if (cancelled) return
        if (err instanceof ApiError && err.status === 404) {
          setLoadError('Intenção não encontrada.')
        } else {
          setLoadError(friendlyError(err, 'Falha ao carregar a intenção.'))
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [mode, id])

  // Cicla as 3 mensagens enquanto o analyzer corre.
  useEffect(() => {
    if (action.kind !== 'analyzing') return
    const interval = window.setInterval(() => {
      setAnalysisMsgIdx((idx) => (idx + 1) % ANALYSIS_MESSAGES.length)
    }, ANALYSIS_ROTATION_MS)
    return () => window.clearInterval(interval)
  }, [action.kind])

  // Toda mudança em campos invalidam a análise anterior — força o user a
  // re-analisar antes de salvar (resultado pode ter mudado).
  useEffect(() => {
    if (action.kind === 'analyzed' || action.kind === 'error') {
      setAction({ kind: 'idle' })
      setSaveError(null)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [displayName, description, JSON.stringify(examples)])

  const titleHeader = mode === 'create' ? 'Cadastrar nova intenção' : 'Editar intenção'

  const displayNameLen = displayName.length
  const descriptionLen = description.length
  const displayNameOver = displayNameLen > DISPLAY_NAME_LIMIT
  const descriptionOver = descriptionLen > DESCRIPTION_LIMIT

  const trimmedDisplayName = displayName.trim()
  const trimmedDescription = description.trim()

  // Heurística client-side leve só pra feedback imediato no campo Nome enquanto
  // o user digita — verde quando começa com verbo no infinitivo. Não bloqueia
  // ação; o analyzer faz a validação real.
  const nameHintPositive = useMemo(() => {
    const first = trimmedDisplayName.split(/\s+/)[0]?.toLowerCase() ?? ''
    return /(?:ar|er|ir)$/.test(first)
  }, [trimmedDisplayName])

  const fieldsValid =
    trimmedDisplayName.length > 0
    && !displayNameOver
    && trimmedDescription.length > 0
    && !descriptionOver

  const analysis = action.kind === 'analyzed' ? action.output : null

  const handleAddExample = () => {
    const trimmed = exampleDraft.trim()
    if (!trimmed) return
    setExamples((prev) => [...prev, trimmed])
    setExampleDraft('')
  }

  const handleRemoveExample = (idx: number) => {
    setExamples((prev) => prev.filter((_, i) => i !== idx))
  }

  const cancelInFlight = () => {
    abortRef.current?.abort()
    abortRef.current = null
  }

  const runAnalyzer = async () => {
    if (!fieldsValid) return
    cancelInFlight()
    setSaveError(null)
    setAnalysisMsgIdx(0)
    setAction({ kind: 'analyzing' })

    const ctrl = new AbortController()
    abortRef.current = ctrl

    try {
      const trigger = await analyzeRouterIntent({
        description: trimmedDescription,
        examples: examples.filter((e) => e.trim().length > 0),
        displayNameHint: trimmedDisplayName,
        excludeId: mode === 'edit' ? id : undefined,
      })

      const output = await pollAnalyzerExecution(trigger.executionId, ctrl.signal)
      setAction({ kind: 'analyzed', output })
    } catch (err) {
      if (err instanceof DOMException && err.name === 'AbortError') {
        setAction({ kind: 'idle' })
        return
      }
      const message =
        err instanceof AnalysisTimeoutError ? err.message
        : err instanceof AnalysisFailureError ? err.message
        : friendlyError(err, 'Falha no analisador.')
      setAction({ kind: 'error', message })
    } finally {
      abortRef.current = null
    }
  }

  const persist = async () => {
    if (action.kind !== 'analyzed') return
    const output = action.output
    setSaving(true)
    setSaveError(null)
    try {
      const body = {
        name: output.suggestedName,
        displayName: output.suggestedDisplayName?.trim() || trimmedDisplayName,
        description: trimmedDescription,
        examples: examples.filter((e) => e.trim().length > 0),
        ...(output.suggestedProjectId ? { projectId: output.suggestedProjectId } : {}),
      }
      if (mode === 'create') {
        await createRouterIntent(body)
      } else if (id) {
        await updateRouterIntent(id, body)
      }
      navigate('/intencoes')
    } catch (err) {
      if (err instanceof ApiError && err.status === 409) {
        setSaveError('Já existe uma intenção com esse nome no tenant. Edite o nome e tente novamente.')
      } else {
        setSaveError(friendlyError(err, 'Falha ao salvar.'))
      }
    } finally {
      setSaving(false)
    }
  }

  if (loadError) {
    return (
      <div className="mx-auto max-w-3xl space-y-4">
        <ErrorMessage message={loadError} />
        <Button variant="secondary" onClick={() => navigate('/intencoes')}>
          Voltar
        </Button>
      </div>
    )
  }

  if (loading) {
    return (
      <div className="flex items-center justify-center py-16">
        <Spinner className="h-6 w-6 text-fg-muted" />
      </div>
    )
  }

  return (
    <div className="mx-auto max-w-3xl space-y-5">
      {/* Breadcrumb */}
      <nav className="flex items-center gap-1 text-xs text-fg-dim">
        <button
          type="button"
          onClick={() => navigate('/intencoes')}
          className="hover:text-fg-muted"
        >
          Intenções
        </button>
        <span>/</span>
        <span className="text-fg">
          {mode === 'create'
            ? 'Nova intenção'
            : (original?.displayName?.trim() || original?.name || 'Editar')}
        </span>
      </nav>

      <header>
        <h1 className="text-2xl font-semibold tracking-tight text-fg">{titleHeader}</h1>
        <p className="mt-1 text-sm text-fg-muted">
          Uma intenção descreve o que o usuário quer fazer. O roteador usa o nome e a
          descrição pra direcionar a conversa para o fluxo certo.
        </p>
      </header>

      <div className="rounded-xl border border-accent/30 bg-accent-subtle/40 px-4 py-3 text-sm leading-relaxed text-fg">
        Pense na descrição como o briefing que você daria a um analista de primeira viagem:
        o que o cliente quer, em que situação, e o que <em>não</em> faz parte dessa intenção.
      </div>

      <Card padded className="space-y-5">
        {/* Nome */}
        <div>
          <div className="mb-1 flex items-end justify-between">
            <label className="text-xs font-semibold text-fg">Nome da intenção</label>
            <span
              className={cn(
                'text-[11px]',
                displayNameOver ? 'text-warning' : 'text-fg-dim',
              )}
            >
              {displayNameLen} / {DISPLAY_NAME_LIMIT}
            </span>
          </div>
          <Input
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            placeholder="Ex: Solicitar segunda via de fatura"
            invalid={displayNameOver}
            maxLength={DISPLAY_NAME_LIMIT * 2}
          />
          {trimmedDisplayName.length > 0 && nameHintPositive && (
            <p className="mt-1.5 flex items-start gap-1.5 text-xs text-success">
              <span aria-hidden="true">✓</span>
              <span>Boa! Verbo de ação no início ajuda o roteador a entender melhor.</span>
            </p>
          )}
        </div>

        {/* Descrição */}
        <div>
          <div className="mb-1 flex items-end justify-between">
            <label className="text-xs font-semibold text-fg">Descrição</label>
            <span
              className={cn(
                'text-[11px]',
                descriptionOver ? 'text-warning' : 'text-fg-dim',
              )}
            >
              {descriptionLen} / {DESCRIPTION_LIMIT}
            </span>
          </div>
          <Textarea
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="Cliente já pagou ou perdeu o boleto e precisa baixar a fatura novamente. Inclui pedidos por mês de referência, mas não envolve contestação de valores."
            className="min-h-[120px]"
            maxLength={DESCRIPTION_LIMIT * 2}
          />
          <div className="mt-2 flex flex-wrap items-center gap-3">
            {QUALITY_CHECK_LABELS.map((cf) => {
              const value = analysis?.qualityChecks?.[cf.key]
              const tone = value === true ? 'success' : value === false ? 'warning' : 'idle'
              return (
                <span
                  key={cf.key}
                  className={cn(
                    'inline-flex items-center gap-1.5 rounded-md border px-2 py-1 text-[11px]',
                    tone === 'success' && 'border-success/40 bg-success/10 text-success',
                    tone === 'warning' && 'border-warning/40 bg-warning/10 text-warning',
                    tone === 'idle' && 'border-border bg-bg-soft text-fg-dim',
                  )}
                  title={
                    tone === 'idle'
                      ? 'Será avaliado quando você analisar a intenção'
                      : tone === 'success'
                        ? 'Coberto na descrição'
                        : 'Pode estar ausente — considere reforçar'
                  }
                >
                  <span aria-hidden="true">
                    {tone === 'success' ? '✓' : tone === 'warning' ? '!' : '○'}
                  </span>
                  {cf.label}
                </span>
              )
            })}
          </div>
        </div>

        {/* Exemplos */}
        <div>
          <label className="mb-1 block text-xs font-semibold text-fg">
            Exemplos de fala <span className="font-normal text-fg-dim">— opcional, mas recomendado</span>
          </label>
          <div className="rounded-xl border border-border bg-bg-soft p-3">
            <div className="flex flex-wrap items-center gap-2">
              {examples.map((ex, idx) => (
                <span
                  key={`${ex}-${idx}`}
                  className="inline-flex items-center gap-1 rounded-full border border-border bg-surface px-3 py-1 text-xs text-fg"
                >
                  <span>"{ex}"</span>
                  <IconButton
                    aria-label={`Remover exemplo ${idx + 1}`}
                    variant="ghost"
                    size="sm"
                    onClick={() => handleRemoveExample(idx)}
                    className="!h-4 !w-4 !p-0"
                  >
                    <CloseIcon className="h-3 w-3" />
                  </IconButton>
                </span>
              ))}
              <ExampleAdder
                value={exampleDraft}
                onChange={setExampleDraft}
                onCommit={handleAddExample}
              />
            </div>
          </div>
        </div>
      </Card>

      {/* Resultado/feedback do analyzer (substitui o footer fixo). Renderiza só
          depois da primeira análise — antes disso, fica oculto. */}
      {action.kind === 'analyzing' && (
        <Card padded className="flex items-center gap-3">
          <Spinner className="h-4 w-4" />
          <span className="text-sm text-fg-muted">{ANALYSIS_MESSAGES[analysisMsgIdx]}</span>
        </Card>
      )}

      {action.kind === 'error' && (
        <Card padded className="space-y-2">
          <p className="flex items-center gap-2 text-sm text-warning">
            <span aria-hidden="true">!</span>
            <span>{action.message}</span>
          </p>
          <p className="text-xs text-fg-dim">
            Você pode tentar de novo ou ajustar a descrição.
          </p>
        </Card>
      )}

      {analysis && (
        <Card padded className="space-y-3">
          {analysis.conflictFound ? (
            <>
              <div className="flex items-center gap-2">
                <span aria-hidden="true" className="text-warning">!</span>
                <h3 className="text-sm font-semibold text-warning">
                  Conflito com {analysis.conflictsWith.length === 1 ? '1 intenção' : `${analysis.conflictsWith.length} intenções`} existente{analysis.conflictsWith.length === 1 ? '' : 's'}
                </h3>
              </div>
              <div className="rounded-lg border border-warning/40 bg-warning/10 px-3 py-2">
                <p className="text-xs leading-relaxed text-fg">{analysis.details}</p>
                {analysis.conflictsWith.length > 0 && (
                  <div className="mt-2 flex flex-wrap gap-1.5">
                    {analysis.conflictsWith.map((n) => (
                      <Badge key={n} tone="warning">
                        <code className="font-mono">{n}</code>
                      </Badge>
                    ))}
                  </div>
                )}
              </div>
            </>
          ) : (
            <>
              <div className="flex items-center gap-2">
                <span aria-hidden="true" className="text-success">✓</span>
                <h3 className="text-sm font-semibold text-success">
                  Nenhum conflito com intenções existentes
                </h3>
              </div>
              <p className="text-xs leading-relaxed text-fg-muted">{analysis.details}</p>
            </>
          )}

          <div className="grid grid-cols-1 gap-3 border-t border-border pt-3 sm:grid-cols-3">
            <div>
              <p className="text-[10px] uppercase tracking-wider text-fg-dim">Nome canônico</p>
              <code className="mt-1 inline-block rounded bg-bg-soft px-2 py-1 font-mono text-xs">
                {analysis.suggestedName}
              </code>
            </div>
            <div>
              <p className="text-[10px] uppercase tracking-wider text-fg-dim">Nome humano</p>
              <p className="mt-1 text-xs text-fg">{analysis.suggestedDisplayName}</p>
            </div>
            <div>
              <p className="text-[10px] uppercase tracking-wider text-fg-dim">Categoria</p>
              <p className="mt-1 text-xs text-fg">{analysis.suggestedProjectId || '—'}</p>
            </div>
          </div>
        </Card>
      )}

      {saveError && <ErrorMessage message={saveError} />}

      {/* Botões inline (sem footer fixo). Botão primário é dinâmico:
          - antes da análise → "Analisar intenção"
          - depois da análise sem conflito → "Salvar intenção"
          - depois da análise com conflito → "Salvar mesmo assim" (warning) */}
      <div className="flex flex-wrap items-center justify-end gap-2">
        <Button
          variant="secondary"
          onClick={() => navigate('/intencoes')}
          disabled={saving || action.kind === 'analyzing'}
        >
          Cancelar
        </Button>
        {action.kind === 'analyzed' ? (
          <Button onClick={persist} disabled={saving}>
            {saving
              ? 'Salvando…'
              : action.output.conflictFound
                ? 'Salvar mesmo assim'
                : 'Salvar intenção'}
          </Button>
        ) : (
          <Button
            onClick={runAnalyzer}
            disabled={!fieldsValid || action.kind === 'analyzing'}
          >
            {action.kind === 'analyzing' ? 'Analisando…' : 'Analisar intenção'}
          </Button>
        )}
      </div>
    </div>
  )
}

interface ExampleAdderProps {
  value: string
  onChange: (value: string) => void
  onCommit: () => void
}

function ExampleAdder({ value, onChange, onCommit }: ExampleAdderProps) {
  const editing = value.length > 0
  if (editing) {
    return (
      <div className="flex items-center gap-1">
        <input
          autoFocus
          value={value}
          onChange={(e) => onChange(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') {
              e.preventDefault()
              onCommit()
            } else if (e.key === 'Escape') {
              onChange('')
            }
          }}
          onBlur={() => {
            if (value.trim()) onCommit()
            else onChange('')
          }}
          placeholder='ex: "Quero a 2ª via do boleto"'
          className="rounded-full border border-accent/40 bg-surface px-3 py-1 text-xs text-fg outline-none focus:border-accent"
        />
      </div>
    )
  }
  return (
    <button
      type="button"
      onClick={() => onChange(' ')}
      className="inline-flex items-center gap-1 rounded-full border border-dashed border-border bg-bg-soft px-3 py-1 text-xs text-fg-dim hover:border-accent/40 hover:text-fg"
    >
      <PlusIcon className="h-3 w-3" />
      Adicionar exemplo
    </button>
  )
}

async function pollAnalyzerExecution(
  executionId: string,
  signal: AbortSignal,
): Promise<RouterIntentAnalyzerOutput> {
  const start = Date.now()
  let attempt = 0
  while (true) {
    if (signal.aborted) throw new DOMException('Aborted', 'AbortError')
    if (Date.now() - start > ANALYSIS_DEADLINE_MS) throw new AnalysisTimeoutError()

    await sleep(POLL_DELAYS_MS[Math.min(attempt, POLL_DELAYS_MS.length - 1)], signal)
    attempt++

    let exec: ExecutionResponse
    try {
      exec = await get<ExecutionResponse>(`/executions/${executionId}`)
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) continue
      throw err
    }

    if (exec.status === 'Completed') {
      try {
        return JSON.parse(exec.output ?? '') as RouterIntentAnalyzerOutput
      } catch {
        throw new AnalysisFailureError('Resposta do analisador fora do formato esperado.')
      }
    }
    if (exec.status === 'Failed' || exec.status === 'Cancelled') {
      throw new AnalysisFailureError()
    }
  }
}

function sleep(ms: number, signal?: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal?.aborted) {
      reject(new DOMException('Aborted', 'AbortError'))
      return
    }
    const timer = window.setTimeout(resolve, ms)
    signal?.addEventListener(
      'abort',
      () => {
        window.clearTimeout(timer)
        reject(new DOMException('Aborted', 'AbortError'))
      },
      { once: true },
    )
  })
}
