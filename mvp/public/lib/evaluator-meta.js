// @ts-check
/**
 * Catálogo PT-BR de evaluators usados em auto-deploy. Substitui
 * mvp/src/routes/Avaliacoes/evaluatorMeta.ts.
 */

/**
 * @typedef {'Local' | 'Meai'} EvaluatorKind
 *
 * @typedef {object} EvaluatorMeta
 * @property {string} name
 * @property {EvaluatorKind} kind
 * @property {string} label
 * @property {string} shortDescription
 * @property {string} fullDescription
 * @property {string} scoreMeaning
 * @property {'alta' | 'media' | 'baixa'} failureSeverity
 */

/** @type {EvaluatorMeta[]} */
export const EVALUATOR_CATALOG = [
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
  {
    name: 'Relevance',
    kind: 'Meai',
    label: 'Relevância',
    shortDescription: 'A resposta endereça a pergunta sem se desviar do tópico.',
    fullDescription:
      'LLM avalia se a resposta do agente é relevante ao input do test case. Detecta respostas evasivas, off-topic ou que ignoram a pergunta.',
    scoreMeaning: 'Likert 1-5 normalizado pra 0-1. Threshold padrão 0.7 (≥3.8 na escala original).',
    failureSeverity: 'alta',
  },
  {
    name: 'Coherence',
    kind: 'Meai',
    label: 'Coerência',
    shortDescription: 'A resposta tem lógica interna e flui sem contradições.',
    fullDescription:
      'LLM avalia se a resposta é internamente consistente. Detecta hallucinações estruturais e respostas desorganizadas.',
    scoreMeaning: 'Likert 1-5 normalizado. Threshold padrão 0.7.',
    failureSeverity: 'media',
  },
  {
    name: 'ToolCallAccuracy',
    kind: 'Meai',
    label: 'Acurácia de tool calls',
    shortDescription: 'As tools foram chamadas com argumentos coerentes ao contexto.',
    fullDescription:
      'LLM avalia se a tool certa foi chamada com argumentos coerentes ao pedido (ex.: ordem com ticker errado).',
    scoreMeaning: 'Likert 1-5 normalizado. Threshold padrão 0.7.',
    failureSeverity: 'alta',
  },
  {
    name: 'TaskAdherence',
    kind: 'Meai',
    label: 'Aderência à tarefa',
    shortDescription: 'A resposta cumpre as instruções e regras do agente.',
    fullDescription:
      'LLM compara a resposta contra as Instructions do agente (papel, regras, restrições).',
    scoreMeaning: 'Likert 1-5 normalizado. Threshold padrão 0.7. Falha aqui é red flag de governança.',
    failureSeverity: 'alta',
  },
  {
    name: 'Fluency',
    kind: 'Meai',
    label: 'Fluência',
    shortDescription: 'A resposta é gramaticalmente bem-construída em PT-BR.',
    fullDescription:
      'LLM avalia gramática, ortografia e naturalidade do texto.',
    scoreMeaning: 'Likert 1-5 normalizado. Threshold padrão 0.7.',
    failureSeverity: 'baixa',
  },
  {
    name: 'Completeness',
    kind: 'Meai',
    label: 'Completude',
    shortDescription: 'A resposta cobre todos os aspectos da pergunta sem omitir partes.',
    fullDescription:
      'LLM avalia se a resposta endereça todos os pontos levantados no input.',
    scoreMeaning: 'Likert 1-5 normalizado. Threshold padrão 0.7.',
    failureSeverity: 'media',
  },
  {
    name: 'Equivalence',
    kind: 'Meai',
    label: 'Equivalência ao esperado',
    shortDescription: 'A resposta é semanticamente equivalente ao expectedOutput.',
    fullDescription:
      'LLM compara resposta vs expectedOutput em nível semântico. Mais robusto que ContainsExpected.',
    scoreMeaning: 'Likert 1-5 normalizado. Threshold padrão 0.7.',
    failureSeverity: 'media',
  },
  {
    name: 'IntentResolution',
    kind: 'Meai',
    label: 'Resolução da intenção',
    shortDescription: 'A intenção do usuário foi entendida e resolvida.',
    fullDescription:
      'LLM avalia se o agente identificou corretamente o que o usuário queria e tomou ação adequada.',
    scoreMeaning: 'Likert 1-5 normalizado. Threshold padrão 0.7.',
    failureSeverity: 'media',
  },
];

const BY_NAME = new Map(EVALUATOR_CATALOG.map((e) => [e.name, e]));

/**
 * @param {string} name
 * @returns {EvaluatorMeta | null}
 */
export function getEvaluatorMeta(name) {
  return BY_NAME.get(name) ?? null;
}

/**
 * @param {string} name
 * @returns {string}
 */
export function evaluatorLabel(name) {
  return BY_NAME.get(name)?.label ?? name;
}

/** @type {Record<EvaluatorKind, string>} */
export const KIND_DESCRIPTION = {
  Local:
    'Heurísticas determinísticas executadas em-processo, sem chamada de LLM. Custo zero, latência <5ms. Bom pra validações binárias (contém/não contém, chamou tool/não chamou).',
  Meai:
    'LLM-as-judge usando o modelo do projeto. Avaliação semântica — entende paráfrases, contradições, intenção. Custo ~$0.001-0.01 por chamada, latência 1-10s.',
};
