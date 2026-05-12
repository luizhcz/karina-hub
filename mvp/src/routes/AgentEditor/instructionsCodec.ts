import {
  PROFILE_FIELDS,
  PROFILE_LIST_FIELDS,
  type ProfileField,
  type ProfileFields,
  type ProfileListField,
  type ProfileTextField,
} from './types'

// Encoder/decoder da string canônica de `payload.instructions`. Tudo o que o
// wizard precisa preservar entre saves vive numa única string com markers
// Markdown — backend trata `instructions` como texto opaco. Schemas de input/
// output viajam dentro de fences ```json. Rules e constraints são listas que
// viram bullets `- ` no prompt final. A seção de ferramentas é gerada toda vez
// no save a partir do catálogo selecionado e descartada no decode (não é dado
// de fonte — só apresentação pro LLM).

export const PROFILE_HEADERS: Record<ProfileField, string> = {
  role: 'Papel',
  goal: 'Objetivo',
  backstory: 'Contexto',
  rules: 'Regras de atuação',
  constraints: 'Restrições',
}

export const INPUT_HEADER = 'Input estruturado'
export const OUTPUT_HEADER = 'Output estruturado'
export const TOOLS_HEADER = 'Ferramentas disponíveis'
export const RESPONSE_FORMAT_HEADER = 'Formato da resposta'

const ALL_HEADERS = new Set<string>([
  ...Object.values(PROFILE_HEADERS),
  INPUT_HEADER,
  OUTPUT_HEADER,
  TOOLS_HEADER,
  RESPONSE_FORMAT_HEADER,
])

const FIELD_BY_HEADER: Record<string, ProfileField> = Object.fromEntries(
  PROFILE_FIELDS.map((key) => [PROFILE_HEADERS[key], key]),
)

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
  profile: ProfileFields
  input: { description: string; schema: string }
  output: { description: string; schema: string }
  hasStructured: boolean
}

const EMPTY_PROFILE = (): ProfileFields => ({
  role: '',
  goal: '',
  backstory: '',
  rules: [],
  constraints: [],
})

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

// Tolera bullets em formatos comuns (`- `, `• `, `* `) e linhas sem bullet
// (drafts legacy ou usuários colando texto). Cada linha não-vazia vira um item.
function parseListBody(body: string): string[] {
  return body
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0)
    .map((line) => line.replace(/^[-•*]\s+/, '').trim())
    .filter((line) => line.length > 0)
}

export function decodeInstructions(
  instructions: string | null | undefined,
): DecodedInstructions {
  const empty: DecodedInstructions = {
    profile: EMPTY_PROFILE(),
    input: { description: '', schema: '' },
    output: { description: '', schema: '' },
    hasStructured: false,
  }

  if (!instructions) return empty

  const text = instructions.replace(/\r\n/g, '\n')

  // Splita em blocos delimitados por linhas que começam com "## ". `### `
  // (subheadings, ex.: nomes de ferramentas) não casa porque a regex exige
  // exatamente "##" + whitespace.
  const blockRegex = /^##\s+(.+?)\s*$/gm
  const matches = Array.from(text.matchAll(blockRegex))

  // Drafts legacy ou texto livre: tudo cai em "Papel" pra que o usuário
  // re-organize visualmente sem perder conteúdo. Cobre dois casos:
  // 1. Sem header algum (matches vazio).
  // 2. Headers presentes mas NENHUM é canônico (agentes legados com
  //    estrutura custom tipo `## Shared State`, `## Tom`, etc). Sem este
  //    fallback, o iter descartaria tudo e devolveria profile vazio —
  //    UI mostraria template em cima de instructions populadas.
  const hasKnownHeader = matches.some((m) => ALL_HEADERS.has(m[1].trim()))
  if (matches.length === 0 || !hasKnownHeader) {
    return {
      ...empty,
      profile: { ...EMPTY_PROFILE(), role: text.trim() },
    }
  }

  const profile = EMPTY_PROFILE()
  let input = { description: '', schema: '' }
  let output = { description: '', schema: '' }

  for (let i = 0; i < matches.length; i++) {
    const m = matches[i]
    const header = m[1].trim()
    const start = m.index! + m[0].length
    const end = i + 1 < matches.length ? matches[i + 1].index! : text.length
    const body = text.slice(start, end).trim()

    if (!ALL_HEADERS.has(header)) continue

    if (header === INPUT_HEADER) {
      input = splitStructuredBlock(body)
      continue
    }
    if (header === OUTPUT_HEADER) {
      output = splitStructuredBlock(body)
      continue
    }
    if (header === TOOLS_HEADER || header === RESPONSE_FORMAT_HEADER) {
      // Blocos auto-gerados a partir do estado do form (ferramentas
      // selecionadas / contrato JSON-only quando output estruturado). Descarta
      // no decode pra que o save subsequente regenere a partir do estado atual.
      continue
    }
    const field = FIELD_BY_HEADER[header]
    if (!field) continue
    if (PROFILE_LIST_FIELDS.has(field)) {
      profile[field as ProfileListField] = parseListBody(body)
    } else {
      profile[field as ProfileTextField] = body
    }
  }

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
  profile: ProfileFields,
  input: { description: string; schema: string },
  output: { description: string; schema: string },
  tools: ToolDescriptor[],
  includeStructured: boolean,
): string {
  const blocks: string[] = []

  for (const field of PROFILE_FIELDS) {
    if (PROFILE_LIST_FIELDS.has(field)) {
      const items = profile[field as ProfileListField]
        .map((item) => item.trim())
        .filter((item) => item.length > 0)
      if (items.length === 0) continue
      const bullets = items.map((item) => `- ${item}`).join('\n')
      blocks.push(`## ${PROFILE_HEADERS[field]}\n${bullets}`)
    } else {
      const value = profile[field as ProfileTextField].trim()
      if (!value) continue
      blocks.push(`## ${PROFILE_HEADERS[field]}\n${value}`)
    }
  }

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
