import type { AgentType } from '../../api/agentDrafts'
import { Card, CardHeader, cn } from '../../ui'
import { useIsAdmin } from '../../stores/me'
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
  /** Quando true, card aparece grayed-out e não-clicável. Usado pra tipos
   *  presentes no domínio mas indisponíveis na fase atual do MVP. */
  disabled?: boolean
}

// Cards descrevem o que cada tipo *é* e quando faz sentido escolher. O wizard
// adapta os steps subsequentes a partir desta seleção. Worker e ToolRunner
// existem no domínio mas estão fora do escopo atual do MVP — omitidos daqui
// até serem habilitados. Router fica visível mas `disabled: true` enquanto
// não há demanda de produto pra criar Router via UI.
const TYPE_CARDS: TypeCard[] = [
  {
    type: 'Custom',
    title: 'Custom',
    tagline: 'Agente de uso geral, configurado por você.',
    bestFor:
      'Quando o caso não é um chat e não é roteamento — análises, agregação de dados, atendimento single-shot ou qualquer combinação que você queira montar do zero.',
    bullets: [
      'Você descreve quem é o agente, o que ele entrega e em que contexto',
      'Sem regras fixas — escolhe livremente ferramentas e formato de saída',
      'Cobre a maioria dos casos que aparecem no dia a dia',
    ],
  },
  {
    type: 'Router',
    title: 'Router',
    tagline: 'Identifica a intenção da mensagem do usuário.',
    bestFor:
      'Quando você precisa decidir qual agente especialista vai responder a uma pergunta. O Router lê a mensagem e devolve uma intenção fechada (ex.: “consultar cotação”, “executar ordem”).',
    bullets: [
      'Você cadastra as intenções possíveis com nome e descrição',
      'A saída é sempre uma das intenções declaradas — sem texto livre',
      'Costuma ser a primeira peça num fluxo de chat com múltiplos agentes',
    ],
    disabled: true,
  },
  {
    type: 'Conversational',
    title: 'Conversational',
    tagline: 'Chat com memória entre mensagens.',
    bestFor:
      'Quando o usuário precisa conversar em múltiplos turnos com o agente — atendimento, coleta de dados passo a passo, assistente que acompanha uma tarefa.',
    bullets: [
      'O agente lembra o contexto da conversa entre mensagens',
      'Saída em formato que o front sabe renderizar (cards, listas, texto)',
      'Recomendado pra fluxos que precisam de interação humana iterativa',
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
  const isAdmin = useIsAdmin()
  // Router é admin-only no MVP — non-admin vê "Em breve" como antes;
  // admin pode selecionar e seguir o fluxo. Resto dos cards inalterado.
  const cards = TYPE_CARDS.map((c) =>
    c.type === 'Router' ? { ...c, disabled: !isAdmin } : c,
  )
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
        routerForChat: next === 'Router' ? prev.routerForChat : false,
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
          {cards.map((card) => {
            const selected = form.type === card.type
            const isDisabled = readonly || card.disabled === true
            return (
              <button
                key={card.type}
                type="button"
                onClick={() => select(card.type)}
                disabled={isDisabled}
                aria-pressed={selected}
                title={card.disabled ? 'Em breve — desabilitado nesta fase do MVP.' : undefined}
                className={cn(
                  'flex flex-col gap-3 rounded-2xl border p-4 text-left transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/30',
                  isDisabled && 'cursor-not-allowed opacity-60',
                  !isDisabled && 'hover:border-accent/40',
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
                      card.disabled
                        ? 'bg-bg-soft text-fg-dim'
                        : selected
                          ? 'bg-accent text-accent-contrast'
                          : 'bg-bg-soft text-fg-dim',
                    )}
                  >
                    {card.disabled ? 'Em breve' : selected ? 'Selecionado' : 'Selecionar'}
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
