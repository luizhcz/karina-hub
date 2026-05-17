import { Badge, cn } from '../../../ui'
import type {
  EnrichedHttpToolDescriptor,
  EnrichedMcpToolDescriptor,
  EnrichedToolDescriptor,
} from './toolDescriptors'

interface Props {
  tools: EnrichedToolDescriptor[]
}

const TOOLTIP =
  'As tools não ficam dentro do texto do prompt. Elas são entregues ao modelo como uma lista separada, e o modelo escolhe sozinho quando usar cada uma.'

/**
 * Bloco "Ferramentas disponíveis" da revisão. Empty state quando nenhuma
 * ferramenta foi anexada — a ausência é informação útil pro PM, não algo a
 * ser escondido. Cada tool fica num <details> colapsável pra não poluir a
 * tela quando há muitas seleções.
 */
export function ToolsPreview({ tools }: Props) {
  return (
    <section
      aria-label="Ferramentas anexadas ao agente"
      className="rounded-lg border-l-4 border-l-accent/60 border border-border bg-accent/[0.04] px-4 py-3"
    >
      <header className="mb-2 flex items-center gap-2">
        <span className="text-base">🛠️</span>
        <h3 className="text-sm font-semibold text-fg">Ferramentas disponíveis</h3>
        <Badge tone="accent">{tools.length}</Badge>
        <button
          type="button"
          title={TOOLTIP}
          aria-label="O que isso significa?"
          className="ml-auto h-5 w-5 rounded-full border border-border text-[10px] font-bold text-fg-muted transition hover:bg-surface-hover hover:text-fg"
        >
          ?
        </button>
      </header>
      <p className="mb-3 text-[11px] text-fg-muted">{TOOLTIP}</p>

      {tools.length === 0 ? (
        <p className="rounded-md border border-dashed border-border bg-surface px-3 py-3 text-xs text-fg-muted">
          Nenhuma ferramenta anexada — o agente responde apenas com texto.
        </p>
      ) : (
        <ul className="space-y-2">
          {tools.map((tool) => (
            <li key={`${tool.source}:${tool.id}`}>
              <ToolCard tool={tool} />
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

function ToolCard({ tool }: { tool: EnrichedToolDescriptor }) {
  return (
    <details className="group rounded-md border border-border bg-surface text-xs [&_summary]:list-none">
      <summary
        className={cn(
          'flex cursor-pointer select-none items-center gap-2 px-3 py-2 transition',
          'hover:bg-surface-hover',
        )}
      >
        {/* Caret manual: substitui o marker default do <details> (que varia
            entre Chrome/Firefox/Safari) por chevron consistente que rotaciona
            quando aberto. list-none acima esconde o marker nativo. */}
        <span
          aria-hidden="true"
          className="text-[10px] text-fg-dim transition-transform group-open:rotate-90"
        >
          ▶
        </span>
        <Badge tone={tool.source === 'http' ? 'accent' : 'success'}>
          {tool.source === 'http' ? 'HTTP' : 'MCP'}
        </Badge>
        <span className="font-mono text-[12px] font-medium text-fg">{tool.name}</span>
        {tool.source === 'http' && (
          <span className="ml-2 truncate text-[11px] text-fg-dim">
            {tool.httpMethod} {tool.urlTemplate}
          </span>
        )}
      </summary>
      <div className="space-y-2 border-t border-border px-3 py-2.5">
        {tool.description && (
          <p className="text-xs text-fg-muted">{tool.description}</p>
        )}
        {tool.source === 'http' ? <HttpToolBody tool={tool} /> : <McpToolBody tool={tool} />}
      </div>
    </details>
  )
}

function HttpToolBody({ tool }: { tool: EnrichedHttpToolDescriptor }) {
  return (
    <>
      {tool.whenToUse && (
        <p className="text-xs">
          <span className="font-medium text-fg">Use quando:</span>{' '}
          <span className="text-fg-muted">{tool.whenToUse}</span>
        </p>
      )}
      <SchemaBlock title="Input schema" value={tool.inputSchema} fallback={`Content-Type: ${tool.inputContentType}`} />
      <SchemaBlock title="Output schema" value={tool.outputSchema} fallback={`Content-Type: ${tool.outputContentType}`} />
    </>
  )
}

function McpToolBody({ tool }: { tool: EnrichedMcpToolDescriptor }) {
  return (
    <div className="space-y-1.5 text-xs">
      <p className="text-fg-muted">
        <span className="font-medium text-fg">Servidor:</span>{' '}
        <code className="font-mono">{tool.serverLabel}</code>
      </p>
      {tool.allowedTools.length > 0 ? (
        <p>
          <span className="font-medium text-fg">Tools liberadas:</span>{' '}
          <span className="font-mono text-fg-muted">{tool.allowedTools.join(', ')}</span>
        </p>
      ) : (
        <p className="text-fg-muted">
          Tools descobertas dinamicamente — schemas só conhecidos em runtime.
        </p>
      )}
      {tool.requiresApprovalAlways && (
        <p className="rounded border border-warning/30 bg-warning/[0.08] px-2 py-1 text-[11px] text-fg">
          ⚠️ Requer aprovação humana antes de cada execução.
        </p>
      )}
    </div>
  )
}

function SchemaBlock({
  title,
  value,
  fallback,
}: {
  title: string
  value: unknown
  fallback: string
}) {
  if (!value) {
    return (
      <div>
        <p className="mb-1 text-[10px] font-semibold uppercase tracking-wider text-fg-dim">{title}</p>
        <p className="text-[11px] italic text-fg-muted">{fallback}</p>
      </div>
    )
  }
  let pretty: string
  try {
    pretty = JSON.stringify(value, null, 2)
  } catch {
    pretty = String(value)
  }
  return (
    <div>
      <p className="mb-1 text-[10px] font-semibold uppercase tracking-wider text-fg-dim">{title}</p>
      <pre className="m-0 max-h-48 overflow-auto rounded border border-border bg-bg-soft px-2 py-1.5 font-mono text-[10.5px] leading-snug text-fg">
        {pretty}
      </pre>
    </div>
  )
}
