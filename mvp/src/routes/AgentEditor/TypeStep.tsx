import type { AgentType } from '../../api/agentDrafts'
import { Card, CardHeader, cn } from '../../ui'
import type { FormState } from './types'

interface TypeStepProps {
  form: FormState
  setForm: (mutator: (prev: FormState) => FormState) => void
  readonly: boolean
}

interface TypeCard {
  type: AgentType
  title: string
  tagline: string
  bestFor: string
  bullets: string[]
}

// Cards descrevem o que cada tipo *é* e quando faz sentido escolher. O wizard
// adapta os steps subsequentes a partir desta seleção (Router troca Profile
// por seletor de intents; Worker adiciona step de Domínio). Custom mantém o
// fluxo livre — nenhuma validação por tipo, nenhum campo derivado.
const TYPE_CARDS: TypeCard[] = [
  {
    type: 'Custom',
    title: 'Custom',
    tagline: 'Construa do zero — sem template aplicado.',
    bestFor: 'Quando o agente não cabe num arquétipo: orquestrador, conversacional aberto, agregador, ou qualquer combinação fora dos tipos formais.',
    bullets: [
      'Sem validações por tipo no save',
      'Você define perfil, output, memória e tools livremente',
      'Cobre back-compat de agentes criados antes da tipologia',
    ],
  },
  {
    type: 'Router',
    title: 'Router',
    tagline: 'Classifica input em uma categoria fechada (intent).',
    bestFor: 'Decisão de switch no início de um workflow, triagem de intenção em chat, roteamento de canais — sempre que a saída precisa ser uma label discreta.',
    bullets: [
      'Output estruturado com property `intent` + enum (obrigatório)',
      'Modelo mini, MaxTokens baixo, sem tools — recomendados',
      'Histórico de chat via workflow `InputMode=Chat`, não memória operacional',
    ],
  },
  {
    type: 'Worker',
    title: 'Worker',
    tagline: 'Raciocínio profundo em domínio específico — análise rica e estruturada.',
    bestFor: 'Step do meio em pipeline (depois do Router) ou standalone quando a análise sozinha é a resposta. Casos: parecer técnico, outlook macro, recomendação contextualizada, descrição padronizada.',
    bullets: [
      'StructuredOutput, modelo full, MaxTokens 2000–4000 — recomendados',
      'SecurityGuardrails recomendado pra manter escopo declarado',
      'OperationalMemory off — single-shot dentro de pipeline',
    ],
  },
  {
    type: 'ToolRunner',
    title: 'Tool Runner',
    tagline: 'Function-caller / executor — decide qual tool chamar e com quais argumentos.',
    bestFor: 'Tarefas que exigem ação no mundo: criar boleta, consultar API, enviar notificação, registrar movimentação. Standalone, embutido em chat ou dentro de pipeline (Worker analisa → Tool Runner executa).',
    bullets: [
      'Tools obrigatórias — sem tools o template não faz sentido',
      'SecurityGuardrails ligado por padrão; AccountGuard recomendado',
      'Modelo full, Temperature 0–0.3, MaxTokens 2000+ — determinismo prioritário',
    ],
  },
  {
    type: 'Conversational',
    title: 'Conversational',
    tagline: 'Chat / assistant multi-turn com persona e contexto entre turns.',
    bestFor: 'Atendimento conversacional, coleta iterativa de dados (boletas, onboarding), assistentes de refinamento. Único tipo que roda em workflow `InputMode=Chat` e mantém histórico/conversationId entre turns.',
    bullets: [
      'Output estruturado fixo: { ui_component, message, output } — schema é injetado',
      'Middleware StructuredOutputState ativo: dispara STATE_DELTA via SSE',
      'SecurityGuardrails ligado por padrão; modelo balanced (latência importa)',
    ],
  },
]

// Defaults idempotentes do template Worker. Aplicados apenas quando o
// campo correspondente ainda está no estado "default Custom" — assim, se o
// user troca pra Worker, volta pra Custom, troca pra Worker de novo, a
// customização que ele aplicou no meio é preservada.
const WORKER_DEFAULT_OUTPUT_SCHEMA = JSON.stringify(
  {
    type: 'object',
    properties: {
      analise: {
        type: 'string',
        description:
          'Análise multifator do caso recebido. Cobre os fatores relevantes do domínio declarado.',
      },
      recomendacao: {
        type: 'string',
        description:
          'Recomendação consolidada — direta, sem hedging desnecessário.',
      },
      riscos: {
        type: 'array',
        items: { type: 'string' },
        description: 'Riscos/limitações identificados na análise.',
      },
      reason: {
        type: 'string',
        description:
          'Pensamento que conduziu à recomendação — chain-of-thought visível.',
      },
    },
    required: ['analise', 'recomendacao', 'riscos', 'reason'],
    additionalProperties: false,
  },
  null,
  2,
)

const WORKER_DEFAULT_OUTPUT_DESCRIPTION =
  'Saída estruturada com análise, recomendação, riscos e reason. ' +
  'Padrão recomendado pra Worker; ajuste o schema conforme o domínio.'

// Subschema default do `output` no shape canônico do Conversational.
// O user edita este subschema livremente no OutputStep. O codec envolve
// como `properties.output` dentro do shape { ui_component, message, output }
// montado no save. Default vazio com additionalProperties=true permite
// payload livre até o user formalizar.
const CONVERSATIONAL_DEFAULT_OUTPUT_SCHEMA = JSON.stringify(
  {
    type: 'object',
    properties: {},
    additionalProperties: true,
  },
  null,
  2,
)

const CONVERSATIONAL_DEFAULT_OUTPUT_DESCRIPTION =
  'Subschema do campo `output` (payload livre). O codec envolve este schema ' +
  'dentro do shape canônico { ui_component, message, output }; o frontend ' +
  'renderer lê `output` pra renderizar conforme o `ui_component` escolhido.'

const CONVERSATIONAL_DEFAULT_UI_COMPONENTS = ['text', 'card', 'list']

export function TypeStep({ form, setForm, readonly }: TypeStepProps) {
  const select = (next: AgentType) => {
    if (readonly || form.type === next) return
    setForm((prev) => {
      // Reset de flags type-específicas: ao trocar de tipo, descarta a
      // declaração HITL do Tool Runner pra evitar shadow state se o user
      // voltar pra ToolRunner depois (o save já limpa o metadata via
      // encodeToolRunnerHitlMetadata; aqui mantemos o FormState coerente).
      // Mesmo princípio pra persona/uiComponents do Conversational.
      const base: FormState = {
        ...prev,
        type: next,
        toolRunnerHitlRequired: next === 'ToolRunner' ? prev.toolRunnerHitlRequired : false,
        conversationalPersona: next === 'Conversational' ? prev.conversationalPersona : '',
        conversationalUiComponents:
          next === 'Conversational' ? prev.conversationalUiComponents : [],
      }

      if (next === 'Worker') {
        // Output structured com schema padrão é a marca registrada do Worker
        // — só aplica quando o user ainda não tocou em nada. Se ele já
        // configurou um output (text com descrição custom, ou structured com
        // schema próprio), preserva.
        const outputUntouched =
          prev.output.mode === 'text'
          && prev.output.description.trim() === ''
          && prev.output.schema.trim() === ''

        // SecurityGuardrails é recomendação do template — liga só quando
        // não está explicitamente off com toggle do user. Mesmo critério:
        // só ativa em estado virgem.
        const securityUntouched = !prev.security.enabled

        return {
          ...base,
          agentMode: outputUntouched ? 'advanced' : prev.agentMode,
          output: outputUntouched
            ? {
                mode: 'structured',
                description: WORKER_DEFAULT_OUTPUT_DESCRIPTION,
                schema: WORKER_DEFAULT_OUTPUT_SCHEMA,
              }
            : prev.output,
          security: securityUntouched ? { enabled: true } : prev.security,
        }
      }

      if (next === 'ToolRunner') {
        // Tool Runner: SecurityGuardrails on quando user não tocou —
        // recomendado pra mitigar prompt injection em executores. Modo
        // wizard fica advanced (steps Tools/Security/Memory/Output são
        // explícitos pelo template). AccountGuard é avaliado em validação
        // (warning quando off) e ativado pelo user via SecurityStep ou
        // edição manual de payload.middlewares.
        const securityUntouched = !prev.security.enabled
        return {
          ...base,
          agentMode: 'advanced',
          security: securityUntouched ? { enabled: true } : prev.security,
        }
      }

      if (next === 'Conversational') {
        // Conversational: shape canônico { ui_component, message, output }
        // é injetado pelo codec; OutputStep edita só o subschema do `output`.
        // Pré-popula o subschema (vazio com additionalProperties=true) e a
        // lista default de ui_components quando o user ainda não tocou.
        // SecurityGuardrails on (canal exposto a user externo). Memory NÃO
        // é ativada por default — user marca explicitamente quando quiser
        // continuidade entre turns.
        const outputUntouched =
          prev.output.mode === 'text'
          && prev.output.description.trim() === ''
          && prev.output.schema.trim() === ''
        const securityUntouched = !prev.security.enabled
        const uiComponentsEmpty = prev.conversationalUiComponents.length === 0

        return {
          ...base,
          agentMode: 'advanced',
          output: outputUntouched
            ? {
                mode: 'structured',
                description: CONVERSATIONAL_DEFAULT_OUTPUT_DESCRIPTION,
                schema: CONVERSATIONAL_DEFAULT_OUTPUT_SCHEMA,
              }
            : prev.output,
          security: securityUntouched ? { enabled: true } : prev.security,
          conversationalUiComponents: uiComponentsEmpty
            ? [...CONVERSATIONAL_DEFAULT_UI_COMPONENTS]
            : prev.conversationalUiComponents,
        }
      }

      return base
    })
  }

  return (
    <div className="space-y-5">
      <Card className="space-y-3">
        <CardHeader
          title="Tipo de agente"
          description="Define o template aplicado e as validações de save. Custom é o default — sem template, sem validações por tipo. Router habilita validações de classifier (intent + enum)."
        />
        <div className="grid gap-3 sm:grid-cols-2">
          {TYPE_CARDS.map((card) => {
            const selected = form.type === card.type
            return (
              <button
                key={card.type}
                type="button"
                onClick={() => select(card.type)}
                disabled={readonly}
                aria-pressed={selected}
                className={cn(
                  'flex flex-col gap-3 rounded-2xl border p-4 text-left transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/30',
                  readonly && 'cursor-not-allowed opacity-70',
                  !readonly && 'hover:border-accent/40',
                  selected
                    ? 'border-accent bg-accent/5 shadow-soft'
                    : 'border-border bg-surface',
                )}
              >
                <div className="flex items-baseline justify-between gap-2">
                  <span className="text-sm font-semibold text-fg">{card.title}</span>
                  <span
                    className={cn(
                      'rounded-full px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide',
                      selected
                        ? 'bg-accent text-accent-contrast'
                        : 'bg-bg-soft text-fg-dim',
                    )}
                  >
                    {selected ? 'Selecionado' : 'Selecionar'}
                  </span>
                </div>
                <p className="text-xs leading-relaxed text-fg-muted">{card.tagline}</p>
                <div>
                  <p className="text-[11px] font-semibold uppercase tracking-wide text-fg-dim">
                    Quando usar
                  </p>
                  <p className="mt-1 text-xs leading-relaxed text-fg-muted">{card.bestFor}</p>
                </div>
                <ul className="space-y-1.5">
                  {card.bullets.map((b) => (
                    <li key={b} className="flex gap-2 text-xs leading-relaxed text-fg-muted">
                      <span
                        className="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-accent/60"
                        aria-hidden="true"
                      />
                      <span>{b}</span>
                    </li>
                  ))}
                </ul>
              </button>
            )
          })}
        </div>
      </Card>
    </div>
  )
}
