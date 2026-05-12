import { useMemo, useState } from 'react'
import { getIdentity } from '../stores/identity'
import { Card, CardHeader } from '../ui'

interface HowToConsumeWorkflowProps {
  workflowId: string
  publicBaseUrl: string | null
  /**
   * Texto da descrição do CardHeader. Default cobre os dois casos (single +
   * pipeline). Pode ser sobrescrito pra detalhar especificidades por tipo.
   */
  description?: string
}

/**
 * Card "Como consumir" reutilizado por single deploy e pipeline. Expõe os dois
 * passos do contrato assíncrono do workflow:
 *   1. POST /workflows/{id}/trigger → 202 + executionId
 *   2. polling em GET /executions/{id} até status terminal
 *
 * Substitui placeholders quando a identidade ou base URL estão indisponíveis
 * pra que o exemplo continue copiável e instrutivo mesmo no primeiro acesso.
 */
export function HowToConsumeWorkflow({
  workflowId,
  publicBaseUrl,
  description = 'O workflow é assíncrono: dispara com POST, retorna 202 + executionId, e o resultado é lido fazendo polling no GET de execução.',
}: HowToConsumeWorkflowProps) {
  const identity = useMemo(() => getIdentity(), [])
  const projectId = identity?.projectId ?? '<seu-project-id>'
  const account = identity?.account ?? '<seu-account>'
  const baseUrl = publicBaseUrl ?? '<base-url-do-backend>'
  const triggerUrl = `${baseUrl}/api/aihub/workflows/${workflowId}/trigger`
  const executionUrl = `${baseUrl}/api/aihub/executions/{executionId}`

  const headers: Array<{ key: string; value: string }> = [
    { key: 'Content-Type', value: 'application/json' },
    { key: 'x-efs-account', value: account },
    { key: 'x-project-id', value: projectId },
  ]

  const bodyExample = JSON.stringify(
    { input: 'Olá, faça uma análise sobre…', metadata: {} },
    null,
    2,
  )

  const triggerResponseExample = JSON.stringify(
    {
      executionId: '04bf1f50-763c-47ed-94e3-34ab3f47ea85',
      statusUrl: `${baseUrl}/api/aihub/executions/04bf1f50-763c-47ed-94e3-34ab3f47ea85`,
    },
    null,
    2,
  )

  const executionResponseExample = JSON.stringify(
    {
      executionId: '04bf1f50-763c-47ed-94e3-34ab3f47ea85',
      workflowId,
      status: 'Completed',
      input: 'Olá, faça uma análise sobre…',
      output: '{"resumo":"…","numeros":[…],"avisos":[…]}',
      errorMessage: null,
      startedAt: '2026-05-03T21:48:59.97Z',
      completedAt: '2026-05-03T21:49:03.56Z',
      metadata: {},
    },
    null,
    2,
  )

  return (
    <Card className="space-y-4">
      <CardHeader title="Como consumir" description={description} />
      <div className="space-y-5">
        <div className="space-y-3">
          <StepHeading number={1} title="Disparar a execução" />
          <Section label="Endpoint">
            <CodeBlock value={`POST ${triggerUrl}`} />
          </Section>
          <Section label="Headers">
            <HeadersTable headers={headers} />
          </Section>
          <Section label="Body (request)">
            <CodeBlock value={bodyExample} />
          </Section>
          <Section label="Resposta (202)">
            <CodeBlock value={triggerResponseExample} />
          </Section>
        </div>

        <div className="space-y-3 border-t border-border pt-5">
          <StepHeading number={2} title="Ler o resultado" />
          <p className="text-xs text-fg-muted">
            Faça polling neste endpoint até <code className="font-mono">status</code> virar{' '}
            <code className="font-mono">Completed</code> (ou{' '}
            <code className="font-mono">Failed</code>/
            <code className="font-mono">Cancelled</code>). O output vem como string em{' '}
            <code className="font-mono">output</code> — geralmente JSON quando o agente final tem
            schema estruturado.
          </p>
          <Section label="Endpoint">
            <CodeBlock value={`GET ${executionUrl}`} />
          </Section>
          <Section label="Headers">
            <HeadersTable headers={headers.filter((h) => h.key !== 'Content-Type')} />
          </Section>
          <Section label="Resposta (200) — exemplo quando concluída">
            <CodeBlock value={executionResponseExample} />
          </Section>
        </div>
      </div>
    </Card>
  )
}

function HeadersTable({ headers }: { headers: Array<{ key: string; value: string }> }) {
  return (
    <table className="w-full text-xs">
      <tbody>
        {headers.map((h) => (
          <tr key={h.key} className="border-b border-border last:border-b-0">
            <td className="py-1.5 pr-3 font-mono text-fg-muted">{h.key}</td>
            <td className="py-1.5 font-mono text-fg">{h.value}</td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}

function StepHeading({ number, title }: { number: number; title: string }) {
  return (
    <div className="flex items-center gap-2">
      <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-accent text-[11px] font-semibold text-accent-contrast">
        {number}
      </span>
      <h3 className="text-sm font-semibold text-fg">{title}</h3>
    </div>
  )
}

function Section({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <p className="mb-1 text-[11px] font-semibold uppercase tracking-wider text-fg-muted">{label}</p>
      {children}
    </div>
  )
}

function CodeBlock({ value }: { value: string }) {
  const [copied, setCopied] = useState(false)
  const onCopy = async () => {
    try {
      await navigator.clipboard.writeText(value)
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    } catch {
      /* noop */
    }
  }
  return (
    <div className="relative">
      <pre className="overflow-x-auto rounded-md border border-border bg-bg-soft px-3 py-2 font-mono text-[11px] leading-relaxed text-fg">
        {value}
      </pre>
      <button
        type="button"
        onClick={onCopy}
        className="absolute right-2 top-1.5 rounded-md border border-border bg-surface px-2 py-0.5 text-[10px] font-semibold text-fg-muted transition hover:text-fg"
      >
        {copied ? 'Copiado' : 'Copiar'}
      </button>
    </div>
  )
}
