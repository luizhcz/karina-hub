import { cn } from '../ui'

/**
 * Indicador de "agente digitando" usado enquanto o stream chega vazio (sem
 * conteúdo nem tool calls). Três bolinhas que pulsam em sequência — comum
 * em chats. Centralizado aqui pra ser reusado no AgentSandbox e no
 * ChatDeploymentSandbox sem duplicação.
 */
export function TypingDots() {
  return (
    <span className="flex h-5 items-center gap-1">
      <span className="h-1.5 w-1.5 rounded-full bg-fg-dim animate-bounce [animation-delay:0ms]" />
      <span className="h-1.5 w-1.5 rounded-full bg-fg-dim animate-bounce [animation-delay:150ms]" />
      <span className="h-1.5 w-1.5 rounded-full bg-fg-dim animate-bounce [animation-delay:300ms]" />
    </span>
  )
}

/**
 * Chip compacto que exibe o renderer escolhido pelo Conversational
 * (campo <c>ui_component</c>). Aparece no header da bolha de chat — sinaliza
 * pro user que o agente decidiu uma forma específica de exibição, mesmo
 * quando a UI ainda cai no fallback de texto.
 */
export function UiComponentChip({ value, className }: { value: string; className?: string }) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 rounded-full border border-border bg-bg-soft px-2 py-0.5 text-[10px] font-medium text-fg-muted',
        className,
      )}
      title="ui_component sugerido pelo agente"
    >
      <span aria-hidden="true">▦</span>
      <code className="font-mono text-[10px]">{value}</code>
    </span>
  )
}

/**
 * Bloco colapsável com o payload <c>output</c> completo formatado em JSON.
 * Default fechado — não polui a leitura da mensagem. Render de pretty-print
 * é via <c>JSON.stringify(..., 2)</c>; se o output for primitivo, ainda
 * funciona (vira "valor").
 */
export function OutputDetails({ value, className }: { value: unknown; className?: string }) {
  if (value === undefined) return null
  let pretty: string
  try {
    pretty = JSON.stringify(value, null, 2)
  } catch {
    pretty = String(value)
  }
  return (
    <details
      className={cn(
        'mt-1.5 rounded-md border border-border bg-bg-soft text-[11px]',
        className,
      )}
    >
      <summary className="cursor-pointer select-none px-2.5 py-1 font-medium text-fg-muted">
        Payload completo (output)
      </summary>
      <pre className="m-0 max-h-64 overflow-auto px-2.5 py-1.5 font-mono text-[10.5px] leading-snug text-fg">
        {pretty}
      </pre>
    </details>
  )
}
