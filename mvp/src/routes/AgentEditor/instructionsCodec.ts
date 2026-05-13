// Encoder/decoder da string canônica de `payload.instructions`. O profile do
// agente é uma string markdown livre que o usuário edita no BlockNote — o
// codec NÃO decompõe em campos. Apenas separa blocos auto-gerados pelo
// próprio wizard (ferramentas, schemas de input/output, contrato de resposta)
// do markdown autoral, pra que round-trips não duplem esses blocos.

export const INPUT_HEADER = 'Input estruturado'
export const OUTPUT_HEADER = 'Output estruturado'
export const TOOLS_HEADER = 'Ferramentas disponíveis'
export const RESPONSE_FORMAT_HEADER = 'Formato da resposta'

// Headers que o codec gera no save (tools, schemas, contrato JSON-only). No
// decode são descartados do `profile` — pra preservar idempotência no
// round-trip, eles só voltam quando o save regenera a partir do estado atual.
const AUTO_GENERATED_HEADERS: ReadonlySet<string> = new Set([
  INPUT_HEADER,
  OUTPUT_HEADER,
  TOOLS_HEADER,
  RESPONSE_FORMAT_HEADER,
  // Histórico: `encodeConversationalInstructions` antigo usava H1 pra "Formato
  // de resposta". Reconhecemos a variante no decode pra drenar drafts
  // publicados antes do fix H1→H2 sem deixar o bloco duplicado.
  'Formato de resposta',
])

// Aviso fixo no topo da seção de ferramentas. Reforça que o agente deve seguir
// o fluxo mesmo quando uma tool falha — comportamento padrão pra evitar travar
// a conversa em erros de integração.
const TOOLS_DISCLAIMER =
  'Você tem acesso às ferramentas abaixo. **Se uma ferramenta falhar ou retornar erro, siga adiante sem ela** — explique ao usuário que essa informação específica não está disponível agora, ofereça uma alternativa quando possível e continue a tarefa com o que conseguir das demais.'

// Contrato fixo apendado quando o agente tem output estruturado. Reforça a
// regra "responda APENAS com JSON" — sem esse aviso modelos costumam vazar
// preâmbulo conversacional ("Claro! Aqui vai…") antes do JSON.
const OUTPUT_CONTRACT =
  'Responda **exclusivamente** com um objeto JSON válido conforme o schema definido em "Output estruturado" acima. Não inclua texto, prosa, markdown ou comentários fora do JSON. Verifique que todas as propriedades marcadas como `required` estão presentes antes de responder.'

export interface ToolDescriptor {
  name: string
  description: string
  // Bloco markdown self-contained com toda a doc (endpoint/params/body pra HTTP;
  // tools list/aprovação pra MCP). Vem com seus próprios labels — o encoder
  // não envolve em "**Input esperado:**" pra evitar bold-em-bold.
  details?: string
  // Quando preenchido, fica destacado como "Use quando" embaixo da descrição —
  // orienta o LLM sobre o gatilho de uso da tool.
  whenToUse?: string
}

export interface DecodedInstructions {
  profile: string
  input: { description: string; schema: string }
  output: { description: string; schema: string }
  hasStructured: boolean
}

const FENCE_REGEX = /```json\s*\n([\s\S]*?)\n?```/

// Extrai descrição (texto antes do fence) e schema (conteúdo do fence ```json).
// Sem fence: o bloco inteiro vira description.
function splitStructuredBlock(body: string): { description: string; schema: string } {
  const match = FENCE_REGEX.exec(body)
  if (!match) {
    return { description: body.trim(), schema: '' }
  }
  const description = body.slice(0, match.index).trim()
  const schema = match[1].trim()
  return { description, schema }
}

// Captura headers H1 ou H2 — o decode trata os dois iguais. H1 só sobrevive em
// drafts pré-refactor; novos saves sempre emitem H2 pra blocos auto-gerados.
const HEADER_REGEX = /^(#{1,2})\s+(.+?)\s*$/gm

export function decodeInstructions(
  instructions: string | null | undefined,
): DecodedInstructions {
  const empty: DecodedInstructions = {
    profile: '',
    input: { description: '', schema: '' },
    output: { description: '', schema: '' },
    hasStructured: false,
  }

  if (!instructions) return empty

  const text = instructions.replace(/\r\n/g, '\n')
  HEADER_REGEX.lastIndex = 0
  const matches = Array.from(text.matchAll(HEADER_REGEX))

  // Sem headers nenhum: o markdown todo é profile. Não há blocos auto-gerados
  // pra extrair.
  if (matches.length === 0) {
    return { ...empty, profile: text.trim() }
  }

  let input = { description: '', schema: '' }
  let output = { description: '', schema: '' }
  // Segmentos de profile na ordem original. Inclui prefixo antes do primeiro
  // header e qualquer bloco cujo header não esteja em AUTO_GENERATED_HEADERS.
  const profileSegments: string[] = []

  const firstStart = matches[0].index ?? 0
  if (firstStart > 0) {
    const prefix = text.slice(0, firstStart).trim()
    if (prefix) profileSegments.push(prefix)
  }

  for (let i = 0; i < matches.length; i++) {
    const m = matches[i]
    const header = m[2].trim()
    const blockStart = m.index ?? 0
    const bodyStart = blockStart + m[0].length
    const blockEnd = i + 1 < matches.length ? (matches[i + 1].index ?? text.length) : text.length
    const body = text.slice(bodyStart, blockEnd).trim()

    if (header === INPUT_HEADER) {
      input = splitStructuredBlock(body)
      continue
    }
    if (header === OUTPUT_HEADER) {
      output = splitStructuredBlock(body)
      continue
    }
    if (AUTO_GENERATED_HEADERS.has(header)) {
      // Tools / Formato da resposta — descartados; regerados no save.
      continue
    }

    // Bloco autoral: preserva header + corpo na forma original (com a marcação
    // H1/H2 que o usuário escreveu).
    const block = text.slice(blockStart, blockEnd).trim()
    if (block) profileSegments.push(block)
  }

  const profile = profileSegments.join('\n\n').trim()
  const hasStructured =
    input.description.length > 0 ||
    input.schema.length > 0 ||
    output.description.length > 0 ||
    output.schema.length > 0

  return { profile, input, output, hasStructured }
}

function encodeStructuredBlock(
  header: string,
  data: { description: string; schema: string },
): string | null {
  const description = data.description.trim()
  const schema = data.schema.trim()
  if (!description && !schema) return null

  const parts: string[] = [`## ${header}`]
  if (description) parts.push(description)
  if (schema) parts.push(`\`\`\`json\n${schema}\n\`\`\``)
  return parts.join('\n')
}

function encodeToolsBlock(tools: ToolDescriptor[]): string | null {
  if (tools.length === 0) return null

  const lines: string[] = [`## ${TOOLS_HEADER}`, '', TOOLS_DISCLAIMER]
  for (const tool of tools) {
    lines.push('')
    lines.push(`### ${tool.name}`)
    if (tool.description.trim().length > 0) {
      lines.push('')
      lines.push(tool.description.trim())
    }
    if (tool.whenToUse && tool.whenToUse.trim().length > 0) {
      lines.push('')
      lines.push(`**Use quando:** ${tool.whenToUse.trim()}`)
    }
    if (tool.details && tool.details.trim().length > 0) {
      lines.push('')
      lines.push(tool.details.trim())
    }
  }
  return lines.join('\n')
}

function hasOutputContent(output: { description: string; schema: string }): boolean {
  return output.description.trim().length > 0 || output.schema.trim().length > 0
}

export function encodeInstructions(
  profile: string,
  input: { description: string; schema: string },
  output: { description: string; schema: string },
  tools: ToolDescriptor[],
  includeStructured: boolean,
): string {
  const blocks: string[] = []

  const profileTrim = profile.trim()
  if (profileTrim) blocks.push(profileTrim)

  // Tools entram independente do modo basic/advanced — todo agente pode ter
  // tools/MCPs, então documentamos sempre que existir seleção.
  const toolsBlock = encodeToolsBlock(tools)
  if (toolsBlock) blocks.push(toolsBlock)

  if (includeStructured) {
    const inputBlock = encodeStructuredBlock(INPUT_HEADER, input)
    if (inputBlock) blocks.push(inputBlock)
    const outputBlock = encodeStructuredBlock(OUTPUT_HEADER, output)
    if (outputBlock) blocks.push(outputBlock)

    // Contrato JSON-only sempre depois do schema de output, fechando o prompt.
    // Última instrução é a que LLMs ponderam mais — ajuda a conter prosa
    // conversacional vazando antes/depois do objeto.
    if (hasOutputContent(output)) {
      blocks.push(`## ${RESPONSE_FORMAT_HEADER}\n${OUTPUT_CONTRACT}`)
    }
  }

  return blocks.join('\n\n')
}

// ───────────────────────────────────────────────────────────────────────────
// Operações de seção pro assistente "Refinar com IA". O LLM devolve só
// `secao` + `exemplo`; a UI escolhe entre Substituir e Mesclar via toggle no
// AssistantDiff. Matching de seção é exato (case-insensitive + trim), sem
// fuzzy — se o user editou pra `## Persona` mas a sugestão veio com
// `secao: 'Papel'`, criamos `## Papel` nova no fim do doc. Sem mapa de
// sinônimos.

export interface SectionLocation {
  exists: boolean
  // Índices válidos só quando exists=true.
  headerStart?: number
  contentStart?: number
  end?: number
  // Conteúdo da seção sem o header (trim aplicado).
  content: string
}

export function findSection(markdown: string, secao: string): SectionLocation {
  const target = secao.trim().toLowerCase()
  if (!target) return { exists: false, content: '' }
  const text = markdown.replace(/\r\n/g, '\n')
  HEADER_REGEX.lastIndex = 0
  const headers = Array.from(text.matchAll(HEADER_REGEX))

  for (let i = 0; i < headers.length; i++) {
    const m = headers[i]
    if (m[2].trim().toLowerCase() !== target) continue
    const headerStart = m.index ?? 0
    const contentStart = headerStart + m[0].length
    const end = i + 1 < headers.length ? (headers[i + 1].index ?? text.length) : text.length
    const content = text.slice(contentStart, end).trim()
    return { exists: true, headerStart, contentStart, end, content }
  }
  return { exists: false, content: '' }
}

export type AssistantOperacao = 'substituir' | 'mesclar'

export function applyOperationToMarkdown(
  markdown: string,
  op: AssistantOperacao,
  secao: string | null,
  conteudo: string,
): string {
  const md = markdown.replace(/\r\n/g, '\n')
  const trimmed = conteudo.trim()
  if (!trimmed) return md

  // Operação global (sem seção alvo) — apend no fim do doc como prosa solta.
  if (secao === null) {
    return md.trimEnd() + (md.trim() ? '\n\n' : '') + trimmed + '\n'
  }

  const loc = findSection(md, secao)

  if (!loc.exists) {
    // Sem seção alvo no doc atual: cria seção nova no fim. Vale tanto pra
    // 'substituir' quanto pra 'mesclar' — em ambos o user pediu pra colocar
    // o conteúdo "nessa seção", a operação só difere quando a seção já existe.
    const sep = md.trim() ? '\n\n' : ''
    return md.trimEnd() + sep + `## ${secao.trim()}\n${trimmed}\n`
  }

  const before = md.slice(0, loc.contentStart!)
  const after = md.slice(loc.end!)

  if (op === 'substituir') {
    return before + '\n' + trimmed + '\n\n' + after.replace(/^\n+/, '')
  }

  // 'mesclar': preserva conteúdo existente, anexa o novo depois com double
  // newline pra garantir separação visual. Se a seção estava vazia, vira
  // efetivamente um substituir.
  const existing = loc.content
  const merged = existing ? `${existing}\n\n${trimmed}` : trimmed
  return before + '\n' + merged + '\n\n' + after.replace(/^\n+/, '')
}
