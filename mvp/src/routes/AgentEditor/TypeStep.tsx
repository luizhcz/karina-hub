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

export function TypeStep({ form, setForm, readonly }: TypeStepProps) {
  const select = (next: AgentType) => {
    if (readonly || form.type === next) return
    setForm((prev) => {
      const base: FormState = { ...prev, type: next }
      if (next !== 'Worker') return base

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
