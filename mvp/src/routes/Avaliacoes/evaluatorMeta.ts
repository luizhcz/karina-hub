/**
 * Catálogo PT-BR de evaluators usados em auto-deploy. Banking-friendly: descrições
 * escritas pra PM/PO entender sem jargão de ML. Mantido aqui (não no backend) pra
 * que mudança de tom/linguagem seja release de produto via PR, não de tenant.
 */

export type EvaluatorKind = 'Local' | 'Meai'

export interface EvaluatorMeta {
  name: string
  kind: EvaluatorKind
  label: string
  shortDescription: string
  fullDescription: string
  scoreMeaning: string
  /** Quando o score baixo é alarme (ex.: tool errada → bug) vs aceitável (ex.: fluência baixa em PT-BR). */
  failureSeverity: 'alta' | 'media' | 'baixa'
}

export const EVALUATOR_CATALOG: EvaluatorMeta[] = [
  // ── Local: heurísticas determinísticas, custo zero ───────────────────────
  {
    name: 'ContainsExpected',
    kind: 'Local',
    label: 'Compatibilidade textual',
    shortDescription: 'A resposta contém o output esperado do test case (substring match, sem case).',
    fullDescription:
      'Verifica se a saída do agente contém literalmente o expectedOutput definido no test case (case-insensitive). Heurística determinística sem chamada de LLM. Útil pra confirmar que o agente cita um número, nome ou trecho específico.',
    scoreMeaning: '1.0 quando a substring está presente, 0.0 quando ausente. Sem nuances.',
    failureSeverity: 'media',
  },
  {
    name: 'KeywordCheck',
    kind: 'Local',
    label: 'Palavras-chave',
    shortDescription: 'A resposta contém keywords obrigatórias (configuráveis nos params).',
    fullDescription:
      'Heurística determinística que verifica presença de uma lista de keywords no output. Aceita modos all (todas) ou any (pelo menos uma). Útil pra validar disclaimers obrigatórios, citações regulatórias, ou termos de domínio bancário.',
    scoreMeaning: 'Proporção de keywords presentes (0.0 a 1.0). Threshold de aprovação depende do matchMode.',
    failureSeverity: 'alta',
  },
  {
    name: 'ToolCalledCheck',
    kind: 'Local',
    label: 'Chamada de tool correta',
    shortDescription: 'O agente invocou a tool esperada do test case.',
    fullDescription:
      'Confirma que o agente chamou a tool listada em expectedToolCalls do test case. Para agentes de boleta/atendimento que precisam executar ações externas (consulta de posição, envio de ordem), esta métrica detecta quando o LLM responde sem invocar a operação real.',
    scoreMeaning: '1.0 quando todas as tools esperadas foram invocadas, 0.0 caso contrário.',
    failureSeverity: 'alta',
  },

  // ── MEAI: LLM-as-judge usando o modelo do projeto ────────────────────────
  {
    name: 'Relevance',
    kind: 'Meai',
    label: 'Relevância',
    shortDescription: 'A resposta endereça a pergunta sem se desviar do tópico.',
    fullDescription:
      'LLM avalia se a resposta do agente é relevante ao input do test case. Detecta respostas evasivas, off-topic ou que ignoram a pergunta. Pondera tópico, intenção e completude da cobertura.',
    scoreMeaning: 'Likert 1-5 normalizado pra 0-1. Threshold padrão 0.7 (≥3.8 na escala original).',
    failureSeverity: 'alta',
  },
  {
    name: 'Coherence',
    kind: 'Meai',
    label: 'Coerência',
    shortDescription: 'A resposta tem lógica interna e flui sem contradições.',
    fullDescription:
      'LLM avalia se a resposta é internamente consistente — frases não se contradizem, raciocínio segue um fio condutor, conclusões batem com premissas apresentadas. Detecta hallucinações estruturais e respostas desorganizadas.',
    scoreMeaning: 'Likert 1-5 normalizado. Threshold padrão 0.7.',
    failureSeverity: 'media',
  },
  {
    name: 'ToolCallAccuracy',
    kind: 'Meai',
    label: 'Acurácia de tool calls',
    shortDescription: 'As tools foram chamadas com argumentos coerentes ao contexto.',
    fullDescription:
      'LLM avalia não só se a tool certa foi chamada (igual ao Local ToolCalledCheck), mas se os argumentos passados fazem sentido pro pedido. Por exemplo, ordem de compra com ticker errado ou quantidade negativa cai aqui.',
    scoreMeaning: 'Likert 1-5 normalizado. Threshold padrão 0.7.',
    failureSeverity: 'alta',
  },
  {
    name: 'TaskAdherence',
    kind: 'Meai',
    label: 'Aderência à tarefa',
    shortDescription: 'A resposta cumpre as instruções e regras do agente.',
    fullDescription:
      'LLM compara a resposta contra as Instructions do agente (papel, regras, restrições). Detecta quando o agente quebra constraints declarados (ex.: dá recomendação de investimento quando proibido) ou ignora seu papel definido.',
    scoreMeaning: 'Likert 1-5 normalizado. Threshold padrão 0.7. Falha aqui é red flag de governança.',
    failureSeverity: 'alta',
  },
  {
    name: 'Fluency',
    kind: 'Meai',
    label: 'Fluência',
    shortDescription: 'A resposta é gramaticalmente bem-construída em PT-BR.',
    fullDescription:
      'LLM avalia gramática, ortografia e naturalidade do texto. Não diz nada sobre conteúdo ou correção factual — apenas se está bem-escrito. Útil pra catch de respostas com ruído de tradução automática ou tokens corrompidos.',
    scoreMeaning: 'Likert 1-5 normalizado. Threshold padrão 0.7.',
    failureSeverity: 'baixa',
  },
  {
    name: 'Completeness',
    kind: 'Meai',
    label: 'Completude',
    shortDescription: 'A resposta cobre todos os aspectos da pergunta sem omitir partes.',
    fullDescription:
      'LLM avalia se a resposta endereça todos os pontos levantados no input. Pergunta com 3 partes que recebe resposta cobrindo só 1 falha aqui. Complementa Relevance — uma pode estar ok e a outra fraca.',
    scoreMeaning: 'Likert 1-5 normalizado. Threshold padrão 0.7.',
    failureSeverity: 'media',
  },
  {
    name: 'Equivalence',
    kind: 'Meai',
    label: 'Equivalência ao esperado',
    shortDescription: 'A resposta é semanticamente equivalente ao expectedOutput, mesmo sem palavras idênticas.',
    fullDescription:
      'LLM compara resposta vs expectedOutput em nível semântico — duas frases podem ter palavras diferentes mas significar o mesmo. Mais robusto que ContainsExpected, que falha em paráfrases. Custo: 1 chamada de LLM por case.',
    scoreMeaning: 'Likert 1-5 normalizado. Threshold padrão 0.7.',
    failureSeverity: 'media',
  },
  {
    name: 'IntentResolution',
    kind: 'Meai',
    label: 'Resolução da intenção',
    shortDescription: 'A intenção do usuário foi entendida e resolvida.',
    fullDescription:
      'LLM avalia se o agente identificou corretamente o que o usuário queria e tomou ação adequada (resposta direta, escalamento, recusa fundamentada). Diferente de Relevance — a resposta pode ser relevante ao tópico mas não resolver o problema real.',
    scoreMeaning: 'Likert 1-5 normalizado. Threshold padrão 0.7.',
    failureSeverity: 'media',
  },
]

const BY_NAME = new Map(EVALUATOR_CATALOG.map((e) => [e.name, e]))

export function getEvaluatorMeta(name: string): EvaluatorMeta | null {
  return BY_NAME.get(name) ?? null
}

export function evaluatorLabel(name: string): string {
  return BY_NAME.get(name)?.label ?? name
}

export function evaluatorShort(name: string): string {
  return BY_NAME.get(name)?.shortDescription ?? 'Evaluator desconhecido — ver glossário.'
}
