// Extrai os campos exibíveis do output canônico do Conversational
// ({ output_type, output_status, message, output? }) e do Custom com
// structuredOutput ({ response }). Durante streaming, o JSON costuma chegar
// parcial (parse falha) — nesse caso devolvemos o conteúdo cru pra o caller
// renderizar como "typing" sem quebrar a UI.

export interface ConversationalDisplay {
  /** Texto humano que vai pra bolha. Fallback: o input cru. */
  message: string
  /** Identificador da família de renderer. Null quando ausente. */
  outputType: string | null
  /** Variação de status dentro do output_type. Null quando ausente. */
  outputStatus: string | null
  /** Payload livre que o agente devolve. undefined quando ausente. */
  output: unknown
  /** Indica que reconhecemos o shape canônico — caller pode mostrar chips/details. */
  structured: boolean
}

export function extractConversationalDisplay(raw: string): ConversationalDisplay {
  const fallback: ConversationalDisplay = {
    message: raw,
    outputType: null,
    outputStatus: null,
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
  // Conversational canônico (`message`) tem prioridade sobre Custom com
  // `{ response }` — agentes migrados emitem ambos transitoriamente até
  // re-publicarem, e queremos renderizar `message`.
  const messageField =
    typeof obj.message === 'string' ? obj.message
    : typeof obj.response === 'string' ? obj.response
    : null
  const outputType = typeof obj.output_type === 'string' ? obj.output_type : null
  const outputStatus = typeof obj.output_status === 'string' ? obj.output_status : null
  const hasOutput = 'output' in obj
  // Estruturado mesmo sem `message`: agentes que devolvem só UI (ex.: card
  // sem texto) ainda merecem os chips e o details com output. Sem nada
  // disso, devolvemos o cru pra o caller renderizar como texto.
  if (messageField === null && outputType === null && outputStatus === null && !hasOutput) {
    return fallback
  }

  return {
    message: messageField ?? '',
    outputType,
    outputStatus,
    output: hasOutput ? obj.output : undefined,
    structured: true,
  }
}
