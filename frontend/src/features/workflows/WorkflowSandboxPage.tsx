import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { cn } from '../../shared/utils/cn'
import { Button } from '../../shared/ui/Button'
import { Badge } from '../../shared/ui/Badge'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { useWorkflow, useSandboxWorkflow } from '../../api/workflows'
import { getExecution, KEYS as EXECUTION_KEYS } from '../../api/executions'
import type { WorkflowExecution } from '../../api/executions'

// ── Status Badge ─────────────────────────────────────────────────────────────

const STATUS_MAP: Record<WorkflowExecution['status'], { label: string; variant: 'gray' | 'blue' | 'green' | 'red' | 'yellow' }> = {
  Pending:   { label: 'Aguardando',  variant: 'gray' },
  Running:   { label: 'Executando…', variant: 'blue' },
  Completed: { label: 'Concluído',   variant: 'green' },
  Failed:    { label: 'Falhou',      variant: 'red' },
  Cancelled: { label: 'Cancelado',   variant: 'gray' },
  Paused:    { label: 'Pausado',     variant: 'yellow' },
}

function StatusBadge({ status }: { status: WorkflowExecution['status'] }) {
  const { label, variant } = STATUS_MAP[status] ?? { label: status, variant: 'gray' }
  return <Badge variant={variant}>{label}</Badge>
}

// ── Output Block ──────────────────────────────────────────────────────────────

function OutputBlock({ raw }: { raw: string }) {
  let content = raw
  try { content = JSON.stringify(JSON.parse(raw), null, 2) } catch { /* use raw */ }
  return (
    <pre className="text-xs text-text-primary font-mono whitespace-pre-wrap break-all bg-bg-primary border border-border-primary rounded-lg p-4 overflow-auto max-h-96">
      {content}
    </pre>
  )
}

// ── Main Component ────────────────────────────────────────────────────────────

export function WorkflowSandboxPage() {
  const { id } = useParams<{ id: string }>()
  const { data: workflow, isLoading } = useWorkflow(id!, !!id)
  const sandboxMutation = useSandboxWorkflow()

  const [input, setInput] = useState('')
  const [executionId, setExecutionId] = useState<string | null>(null)

  const isChatMode = (workflow?.configuration?.inputMode ?? 'Standalone').toLowerCase() === 'chat'
  const isSubmitting = sandboxMutation.isPending

  // Poll execution until terminal state
  const { data: execution } = useQuery({
    queryKey: EXECUTION_KEYS.detail(executionId ?? ''),
    queryFn: () => getExecution(executionId!),
    enabled: !!executionId,
    refetchInterval: (query) => {
      const status = (query.state.data as WorkflowExecution | undefined)?.status
      if (!status) return 2000
      return ['Pending', 'Running'].includes(status) ? 2000 : false
    },
  })

  const isDone = execution && !['Pending', 'Running'].includes(execution.status)

  const handleRun = () => {
    if (!input.trim() || !id || isSubmitting) return
    setExecutionId(null)
    sandboxMutation.mutate(
      { id, body: { input: input.trim() } },
      { onSuccess: (data) => setExecutionId(data.executionId) },
    )
  }

  const handleReset = () => {
    setExecutionId(null)
    sandboxMutation.reset()
  }

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && e.ctrlKey) {
      e.preventDefault()
      handleRun()
    }
  }

  if (isLoading) return <PageLoader />

  // ── Chat Mode → Redirect ──────────────────────────────────────────────────

  if (isChatMode) {
    return (
      <div className="flex flex-col h-[calc(100vh-8rem)]">
        <div className="flex items-center gap-3 flex-wrap mb-4">
          <Link to={`/workflows/${id}`}>
            <Button variant="ghost" size="sm">← Editar</Button>
          </Link>
          <Link to="/workflows">
            <Button variant="ghost" size="sm">Workflows</Button>
          </Link>
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2 flex-wrap">
              <h1 className="text-xl font-bold text-text-primary">
                Sandbox: {workflow?.name ?? id}
              </h1>
              <Badge variant="blue">CHAT</Badge>
            </div>
          </div>
        </div>
        <div className="flex-1 flex flex-col items-center justify-center gap-4 text-center bg-bg-secondary border border-border-primary rounded-xl p-8">
          <div className="text-5xl">💬</div>
          <h2 className="text-lg font-semibold text-text-primary">Workflow em Modo Chat</h2>
          <p className="text-sm text-text-muted max-w-sm leading-relaxed">
            Este workflow usa <strong className="text-text-primary">Chat</strong> como modo de entrada.
            Para testá-lo, acesse a aba Chat e selecione este workflow ao criar uma nova conversa.
          </p>
          <Link to="/chat">
            <Button variant="primary">→ Abrir Chat</Button>
          </Link>
        </div>
      </div>
    )
  }

  // ── Standalone Sandbox ────────────────────────────────────────────────────

  return (
    <div className="flex flex-col gap-6">
      {/* Header */}
      <div className="flex items-center gap-3 flex-wrap">
        <Link to={`/workflows/${id}`}>
          <Button variant="ghost" size="sm">← Editar</Button>
        </Link>
        <Link to="/workflows">
          <Button variant="ghost" size="sm">Workflows</Button>
        </Link>
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 flex-wrap">
            <h1 className="text-xl font-bold text-text-primary">
              Sandbox: {workflow?.name ?? id}
            </h1>
            <Badge variant="blue">STANDALONE</Badge>
          </div>
          <p className="text-xs text-text-dimmed mt-0.5">Ctrl+Enter para executar</p>
        </div>
        {executionId && (
          <Button variant="secondary" size="sm" onClick={handleReset} disabled={isSubmitting}>
            ↺ Nova Execução
          </Button>
        )}
      </div>

      {/* Input */}
      <div className="flex flex-col gap-3 bg-bg-secondary border border-border-primary rounded-xl p-4">
        <label className="text-sm font-medium text-text-primary">Input</label>
        <textarea
          value={input}
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder="Digite o input para o workflow... (Ctrl+Enter para executar)"
          rows={6}
          disabled={isSubmitting || (!!executionId && !isDone)}
          className={cn(
            'w-full bg-bg-tertiary border border-border-primary rounded-lg px-3 py-2 text-sm text-text-primary font-mono',
            'placeholder:text-text-muted focus:outline-none focus:border-accent-blue resize-y transition-colors',
            (isSubmitting || (!!executionId && !isDone)) && 'opacity-50 cursor-not-allowed',
          )}
        />
        <div className="flex justify-end">
          <Button
            variant="primary"
            onClick={handleRun}
            loading={isSubmitting}
            disabled={!input.trim() || (!!executionId && !isDone)}
          >
            ▷ Executar
          </Button>
        </div>
      </div>

      {/* Result */}
      {executionId && (
        <div className="flex flex-col gap-3 bg-bg-secondary border border-border-primary rounded-xl p-4">
          <div className="flex items-center justify-between gap-2 flex-wrap">
            <span className="text-sm font-medium text-text-primary">Resultado</span>
            <div className="flex items-center gap-2">
              {execution && <StatusBadge status={execution.status} />}
              <span className="text-[10px] text-text-dimmed font-mono truncate max-w-[240px]">
                {executionId}
              </span>
            </div>
          </div>

          {!isDone && (
            <div className="flex items-center justify-center gap-2 py-8 text-sm text-text-muted">
              <span className="w-2 h-2 rounded-full bg-accent-blue animate-pulse flex-shrink-0" />
              {execution?.status === 'Running' ? 'Executando…' : 'Aguardando início…'}
            </div>
          )}

          {isDone && execution.status === 'Completed' && execution.output && (
            <OutputBlock raw={execution.output} />
          )}

          {isDone && execution.status === 'Completed' && !execution.output && (
            <p className="text-sm text-text-muted text-center py-4">Execução concluída sem output.</p>
          )}

          {isDone && execution.status === 'Failed' && (
            <div className="px-4 py-3 rounded-lg text-sm bg-red-500/10 border border-red-500/30 text-red-400">
              {execution.errorMessage ?? 'Execução falhou sem mensagem de erro.'}
            </div>
          )}

          {isDone && execution.status === 'Cancelled' && (
            <p className="text-sm text-text-muted text-center py-4">Execução cancelada.</p>
          )}
        </div>
      )}

      {/* Mutation error */}
      {sandboxMutation.isError && (
        <div className="px-4 py-3 rounded-lg text-sm bg-red-500/10 border border-red-500/30 text-red-400">
          Erro ao iniciar execução: {(sandboxMutation.error as Error).message}
        </div>
      )}
    </div>
  )
}
