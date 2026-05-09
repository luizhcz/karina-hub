import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { listAgents, type Agent } from '../api/agents'
import {
  deployedAgentId,
  getWorkflow,
  isPipelineDeployment,
  sandboxWorkflow,
  type NodeCompletedPayload,
  type NodeStartedPayload,
  type Workflow,
  type WorkflowCompletedPayload,
  type WorkflowEvent,
  type WorkflowFailedPayload,
} from '../api/workflows'
import { friendlyError } from '../api/client'
import { useExecutionStream } from '../hooks/useExecutionStream'
import {
  ArrowLeftIcon,
  ArrowRightIcon,
  Badge,
  Button,
  Card,
  ErrorMessage,
  Spinner,
  cn,
} from '../ui'

type StepStatus = 'pending' | 'running' | 'completed' | 'failed'

interface StepRow {
  agentId: string
  name: string
  status: StepStatus
  input?: string
  output?: string
  errorMessage?: string
}

const STATUS_LABEL: Record<StepStatus, string> = {
  pending: 'aguardando',
  running: 'executando',
  completed: 'concluído',
  failed: 'falhou',
}

const STATUS_TONE: Record<StepStatus, 'neutral' | 'accent' | 'success' | 'danger'> = {
  pending: 'neutral',
  running: 'accent',
  completed: 'success',
  failed: 'danger',
}

export function DeploymentSandbox() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()

  const [workflow, setWorkflow] = useState<Workflow | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)

  const [input, setInput] = useState('')
  const [steps, setSteps] = useState<StepRow[]>([])
  const [executionId, setExecutionId] = useState<string | null>(null)
  const [running, setRunning] = useState(false)
  const [finalOutput, setFinalOutput] = useState<string | null>(null)
  const [runError, setRunError] = useState<string | null>(null)

  const inputRef = useRef<HTMLTextAreaElement | null>(null)

  // Auto-grow do textarea com cap em 5 linhas. Mesmo padrão do AgentSandbox.
  useLayoutEffect(() => {
    const ta = inputRef.current
    if (!ta) return
    ta.style.height = 'auto'
    const cs = window.getComputedStyle(ta)
    const lh = parseFloat(cs.lineHeight) || 20
    const pt = parseFloat(cs.paddingTop) || 0
    const pb = parseFloat(cs.paddingBottom) || 0
    const maxH = lh * 5 + pt + pb
    const desired = ta.scrollHeight
    ta.style.height = `${Math.min(desired, maxH)}px`
    ta.style.overflowY = desired > maxH ? 'auto' : 'hidden'
  }, [input])

  // Carrega o workflow + nomes dos agentes pra render. Steps inicializam vazios
  // até o primeiro trigger.
  useEffect(() => {
    if (!id) return
    let cancelled = false
    setLoading(true)
    setLoadError(null)
    Promise.all([getWorkflow(id), listAgents()])
      .then(([wf, list]) => {
        if (cancelled) return
        const map = new Map(list.map((a) => [a.id, a]))
        setWorkflow(wf)
        setSteps(initialStepsFromWorkflow(wf, map))
      })
      .catch((err: unknown) => {
        if (!cancelled) setLoadError(friendlyError(err, 'Não foi possível carregar o pipeline.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [id])

  const handleEvent = useCallback((event: WorkflowEvent) => {
    if (event.type === 'workflow_started') {
      // Reset visual já é feito ao iniciar o trigger; nada extra aqui.
      return
    }
    if (event.type === 'node_started') {
      const p = event.payload as NodeStartedPayload
      const ref = p.nodeId ?? p.agentId
      if (!ref) return
      setSteps((prev) =>
        prev.map((s) => (matchesStep(ref, s.agentId) ? { ...s, status: 'running' } : s)),
      )
      return
    }
    if (event.type === 'node_completed') {
      const p = event.payload as NodeCompletedPayload
      const ref = p.nodeId ?? p.agentId
      if (!ref) return
      setSteps((prev) =>
        prev.map((s) =>
          matchesStep(ref, s.agentId)
            ? { ...s, status: 'completed', output: p.output ?? s.output }
            : s,
        ),
      )
      return
    }
    if (event.type === 'workflow_completed') {
      const p = event.payload as WorkflowCompletedPayload
      setRunning(false)
      setFinalOutput(p.output ?? extractLastStepOutput(steps))
      // Marca steps que ainda estavam "running" como completed (caso o
      // backend não emita node_completed pro último).
      setSteps((prev) => prev.map((s) => (s.status === 'running' ? { ...s, status: 'completed' } : s)))
      return
    }
    if (event.type === 'workflow_failed' || event.type === 'workflow_cancelled' || event.type === 'error') {
      const p = event.payload as WorkflowFailedPayload
      const message = p.message ?? p.error ?? 'A execução falhou.'
      setRunning(false)
      setRunError(message)
      // Marca último step running como failed; resto fica como estava.
      setSteps((prev) => {
        const idxRunning = prev.findIndex((s) => s.status === 'running')
        if (idxRunning < 0) return prev
        const next = [...prev]
        next[idxRunning] = { ...next[idxRunning], status: 'failed', errorMessage: message }
        return next
      })
    }
  }, [steps])

  useExecutionStream({
    executionId: running ? executionId : null,
    onEvent: handleEvent,
    onError: (err) => {
      // Só log; reconnect é via reload da página. Backend encerra o stream
      // em terminal event então onError aqui é raro.
      console.warn('[PipelineSandbox] SSE error', err)
    },
  })

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      void handleSubmit()
    }
  }

  const handleSubmit = async () => {
    if (!workflow || !input.trim()) return
    setRunError(null)
    setFinalOutput(null)
    setRunning(true)
    // Reset visual: zerar input/output dos steps e marcar todos pendentes.
    setSteps((prev) => prev.map((s) => ({ agentId: s.agentId, name: s.name, status: 'pending' as const })))
    try {
      const { executionId: execId } = await sandboxWorkflow(workflow.id, {
        input: input.trim(),
        metadata: {},
      })
      setExecutionId(execId)
      // Marca o primeiro step como running com o input do user (o backend
      // emite node_started logo, mas isso dá feedback imediato).
      setSteps((prev) => {
        if (prev.length === 0) return prev
        const next = [...prev]
        next[0] = { ...next[0], status: 'running', input: input.trim() }
        return next
      })
    } catch (err) {
      setRunError(friendlyError(err, 'Não foi possível disparar a implantação.'))
      setRunning(false)
    }
  }

  if (loading) {
    return (
      <Card className="mx-auto max-w-4xl flex items-center justify-center py-12">
        <Spinner className="h-6 w-6 text-fg-muted" />
      </Card>
    )
  }
  if (loadError) {
    return <ErrorMessage message={loadError} className="mx-auto max-w-4xl" />
  }
  if (!workflow) return null

  // Pipeline = 2+ agentes. Single = 1 agente. Layout é o mesmo (timeline com
  // cards numerados); só muda label e a rota de "voltar".
  const isPipeline = isPipelineDeployment(workflow)
  const agentCount = workflow.agents?.length ?? 0
  const backRoute = isPipeline
    ? `/implantacoes/avancada/${workflow.id}`
    : (() => {
        const aid = deployedAgentId(workflow)
        return aid ? `/agentes/${aid}/implantar` : '/implantacoes'
      })()

  return (
    <div className="mx-auto flex h-[calc(100vh-9rem)] max-w-4xl flex-col">
      <div className="mb-4 flex items-center gap-3">
        <Button
          variant="ghost"
          size="sm"
          leftIcon={<ArrowLeftIcon className="h-4 w-4" />}
          onClick={() => navigate(backRoute)}
        >
          Voltar
        </Button>
        <div className="min-w-0 flex-1">
          <h1 className="truncate text-2xl font-semibold tracking-tight">
            Testar: {workflow.name}
          </h1>
          <p className="mt-1 text-xs text-fg-muted">
            Execução standalone (sem histórico) em modo sandbox. Em produção o caller recebe só a resposta final.
          </p>
        </div>
      </div>

      {agentCount === 0 && (
        <ErrorMessage
          message="Essa implantação não tem nenhum agente. Edite antes de testar."
          className="mb-4"
        />
      )}

      <Card padded={false} className="flex flex-1 flex-col overflow-hidden">
        <div className="flex-1 space-y-4 overflow-y-auto px-5 py-5">
          <div>
            <p className="text-xs font-semibold uppercase tracking-wider text-fg-dim">Execução</p>
            <p className="mt-0.5 text-xs text-fg-muted">
              {isPipeline
                ? 'Cada card mostra o agente, o input recebido e a resposta produzida em ordem.'
                : 'O agente recebe o input e produz a resposta — execução isolada por turn.'}
            </p>
          </div>
          <ol className="space-y-0">
            {steps.map((step, index) => (
              <li key={`${step.agentId}-${index}`}>
                <StepCard step={step} index={index} />
                {index < steps.length - 1 && <Connector />}
              </li>
            ))}
          </ol>

          {finalOutput !== null && (
            <div className="space-y-2 rounded-xl border border-success/30 bg-success/5 p-4">
              <p className="text-xs font-semibold uppercase tracking-wider text-success">
                Resposta final
              </p>
              <PreText text={finalOutput} />
            </div>
          )}

          {runError && <ErrorMessage message={runError} />}
        </div>

        <div className="border-t border-border bg-bg-soft/50 px-4 py-3">
          <div className="flex items-end gap-2">
            <textarea
              ref={inputRef}
              value={input}
              onChange={(e) => setInput(e.target.value)}
              onKeyDown={handleKeyDown}
              placeholder={isPipeline ? 'Digite uma mensagem pra testar o pipeline…' : 'Digite uma mensagem pra testar o agente…'}
              rows={1}
              disabled={running || agentCount === 0}
              className={cn(
                'block flex-1 resize-none rounded-lg border border-border bg-surface px-3 py-2 text-sm leading-5 text-fg placeholder:text-fg-dim',
                'focus:outline-none focus:ring-2 focus:ring-accent/30 focus:border-accent',
                'disabled:cursor-not-allowed disabled:opacity-60',
              )}
            />
            <Button
              aria-label="Disparar execução"
              onClick={handleSubmit}
              loading={running}
              disabled={!input.trim() || running || agentCount === 0}
              rightIcon={!running ? <ArrowRightIcon className="h-4 w-4" /> : undefined}
              className="shrink-0"
            >
              Disparar
            </Button>
          </div>
          <p className="mt-1.5 text-[11px] text-fg-dim">
            Enter pra disparar · Shift+Enter pra quebrar linha
          </p>
        </div>
      </Card>
    </div>
  )
}

// Backend identifica o nó de runtime como `{Role}_{agentId}` quando o agente
// tem role declarada (ex: `Triagem_triage`). O agentId puro também aparece em
// alguns payloads. Match permissivo cobre as duas formas.
function matchesStep(nodeRef: string, agentId: string): boolean {
  if (nodeRef === agentId) return true
  if (nodeRef.endsWith(`_${agentId}`)) return true
  return false
}

function initialStepsFromWorkflow(workflow: Workflow, agents: Map<string, Agent>): StepRow[] {
  return (workflow.agents ?? []).map((ref) => {
    const a = agents.get(ref.agentId)
    return {
      agentId: ref.agentId,
      name: a?.name ?? ref.agentId,
      status: 'pending' as StepStatus,
    }
  })
}

function extractLastStepOutput(steps: StepRow[]): string {
  for (let i = steps.length - 1; i >= 0; i--) {
    if (steps[i].output) return steps[i].output ?? ''
  }
  return ''
}

interface StepCardProps {
  step: StepRow
  index: number
}

function StepCard({ step, index }: StepCardProps) {
  const [expanded, setExpanded] = useState(step.status === 'running' || step.status === 'failed')
  const tone = STATUS_TONE[step.status]
  return (
    <div className="flex items-start gap-3 rounded-xl border border-border bg-surface p-4">
      <span
        className={cn(
          'flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-sm font-semibold',
          step.status === 'failed' ? 'bg-danger text-white' : 'bg-accent text-accent-contrast',
        )}
      >
        {index + 1}
      </span>
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-sm font-semibold text-fg">{step.name}</span>
          <Badge tone={tone}>
            {step.status === 'running' && <Spinner className="mr-1 h-3 w-3" />}
            {STATUS_LABEL[step.status]}
          </Badge>
          {(step.input || step.output || step.errorMessage) && (
            <button
              type="button"
              onClick={() => setExpanded((v) => !v)}
              className="ml-auto text-[11px] font-medium text-fg-muted hover:text-fg"
            >
              {expanded ? 'esconder' : 'mostrar detalhes'}
            </button>
          )}
        </div>
        <p className="mt-0.5 truncate font-mono text-[10px] uppercase tracking-wider text-fg-dim">
          {step.agentId}
        </p>
        {expanded && (
          <div className="mt-3 space-y-2">
            {step.input && (
              <Section label="Recebeu">
                <PreText text={step.input} />
              </Section>
            )}
            {step.output && (
              <Section label="Respondeu">
                <PreText text={step.output} />
              </Section>
            )}
            {step.errorMessage && (
              <Section label="Erro">
                <p className="text-xs text-danger">{step.errorMessage}</p>
              </Section>
            )}
          </div>
        )}
      </div>
    </div>
  )
}

function Connector() {
  return (
    <div className="flex justify-center py-1" aria-hidden="true">
      <div className="flex flex-col items-center gap-0">
        <span className="h-3 w-px bg-border" />
        <span className="text-[10px] text-fg-dim">▼</span>
        <span className="h-3 w-px bg-border" />
      </div>
    </div>
  )
}

function Section({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <p className="mb-1 text-[10px] font-semibold uppercase tracking-wider text-fg-dim">{label}</p>
      {children}
    </div>
  )
}

function PreText({ text }: { text: string }) {
  // Truncagem leve em ~800 chars com toggle expandir. Em sandbox stepwise é
  // comum aparecer JSON grande em alguns turnos.
  const [showAll, setShowAll] = useState(false)
  const truncated = !showAll && text.length > 800
  const visible = truncated ? text.slice(0, 800) : text
  return (
    <div className="space-y-1">
      <pre className="overflow-x-auto rounded-md border border-border bg-bg-soft px-3 py-2 font-mono text-[11px] leading-relaxed text-fg whitespace-pre-wrap">
        {visible}
        {truncated && '…'}
      </pre>
      {text.length > 800 && (
        <button
          type="button"
          onClick={() => setShowAll((v) => !v)}
          className="text-[11px] font-medium text-fg-muted hover:text-fg"
        >
          {showAll ? 'reduzir' : `mostrar tudo (${text.length} caracteres)`}
        </button>
      )}
    </div>
  )
}

