// Extrai os campos exibíveis do output canônico do Conversational
// ({ ui_component, message, output }) e do shape legacy de Custom com
// structuredOutput ({ response }). Durante streaming, o JSON costuma chegar
// parcial (parse falha) — nesse caso devolvemos o conteúdo cru pra o caller
// renderizar como "typing" sem quebrar a UI.

export interface ConversationalDisplay {
  /** Texto humano que vai pra bolha. Fallback: o input cru. */
  message: string
  /** Identificador do renderer (chip, card, etc.). Null quando ausente. */
  uiComponent: string | null
  /** Payload livre que o agente devolve. undefined quando ausente. */
  output: unknown
  /** Indica que reconhecemos o shape canônico/legacy — caller pode mostrar chips/details. */
  structured: boolean
}

export function extractConversationalDisplay(raw: string): ConversationalDisplay {
  const fallback: ConversationalDisplay = {
    message: raw,
    uiComponent: null,
    output: undefined,
    structured: false,
  }
  const trimmed = raw.trim()
  if (!trimmed.startsWith('{')) return fallback

  let parsed: unknown
  try {
    parsed = JSON.parse(trimmed)
  } catch {
    // chunk parcial durante stream ou JSON malformado — devolve cru
    return fallback
  }
  if (parsed === null || typeof parsed !== 'object') return fallback

  const obj = parsed as Record<string, unknown>
  // Conversational canônico tem prioridade sobre o legacy `{ response }`
  // — agentes que migraram do template antigo emitiriam ambos os campos
  // transitoriamente, queremos renderizar o `message`.
  const messageField =
    typeof obj.message === 'string' ? obj.message
    : typeof obj.response === 'string' ? obj.response
    : null
  const uiComponent = typeof obj.ui_component === 'string' ? obj.ui_component : null
  const hasOutput = 'output' in obj
  // Estruturado mesmo sem `message`: agentes que devolvem só UI (ex.: card
  // sem texto) ainda merecem o chip do ui_component e o details com output.
  // Sem nada disso, devolvemos o cru pra o caller renderizar como texto.
  if (messageField === null && uiComponent === null && !hasOutput) return fallback

  return {
    message: messageField ?? '',
    uiComponent,
    output: hasOutput ? obj.output : undefined,
    structured: true,
  }
}
