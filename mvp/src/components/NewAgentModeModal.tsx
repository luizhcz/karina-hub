import {
  AgentIcon,
  ArrowRightIcon,
  BoltIcon,
  CheckIcon,
  Modal,
  SparklesIcon,
  cn,
} from '../ui'
import { AGENT_TEMPLATES, type TemplateKey } from '../routes/AgentEditor/templates'
import type { AgentType } from '../api/agentDrafts'

export interface NewAgentSelection {
  type: AgentType
  mode: 'basic' | 'advanced'
  template?: TemplateKey
}

interface NewAgentModeModalProps {
  open: boolean
  onClose: () => void
  onSelect: (selection: NewAgentSelection) => void
}

interface TypeChoice {
  type: AgentType
  // Mode default ao entrar via este card. Custom abre em basic (user pode trocar
  // pra advanced no wizard); Router não usa agentMode (steps são fixos pelo
  // tipo), mas mantemos basic só pra coerência da URL.
  defaultMode: 'basic' | 'advanced'
  title: string
  pitch: string
  // Espelha o stepsFor real do wizard pra cada tipo. Deve bater 1:1 com
  // index.tsx — qualquer mudança lá precisa refletir aqui pro user não ter
  // surpresa entre clique e tela.
  steps: string[]
  icon: React.ReactNode
  accent: string
  iconBg: string
}

const TYPE_CHOICES: TypeChoice[] = [
  {
    type: 'Custom',
    defaultMode: 'basic',
    title: 'Custom',
    pitch:
      'Agente livre, sem template aplicado. Cobre conversação, orquestração, agregação — qualquer caso fora dos arquétipos formais. Modo avançado (com I/O estruturado) é toggle dentro do wizard.',
    steps: ['Tipo', 'Perfil', 'Ferramentas', 'Modelo', 'Revisão'],
    icon: <BoltIcon className="h-6 w-6" />,
    accent: 'from-emerald-500/15 to-emerald-500/0 text-emerald-600 dark:text-emerald-400',
    iconBg: 'bg-emerald-500/15 text-emerald-600 dark:text-emerald-400',
  },
  {
    type: 'Router',
    defaultMode: 'basic',
    title: 'Router',
    pitch:
      'Classifier de intenções. Recebe input em texto livre e devolve uma label discreta (intent + confidence). Perfil próprio: tabela de intenções com nome, descrição e exemplo — sem prompt cru.',
    steps: ['Tipo', 'Intenções', 'Modelo', 'Revisão'],
    icon: <SparklesIcon className="h-6 w-6" />,
    accent: 'from-violet-500/15 to-violet-500/0 text-violet-600 dark:text-violet-400',
    iconBg: 'bg-violet-500/15 text-violet-600 dark:text-violet-400',
  },
  {
    type: 'Worker',
    defaultMode: 'advanced',
    title: 'Worker',
    pitch:
      'Specialist de domínio. Recebe input estruturado e produz análise rica (texto + recomendação + riscos). Step próprio de Domínio captura o escopo de análise — injetado no system prompt em runtime.',
    steps: ['Tipo', 'Domínio', 'Ferramentas', 'Segurança', 'Output', 'Modelo', 'Revisão'],
    icon: <SparklesIcon className="h-6 w-6" />,
    accent: 'from-sky-500/15 to-sky-500/0 text-sky-600 dark:text-sky-400',
    iconBg: 'bg-sky-500/15 text-sky-600 dark:text-sky-400',
  },
  {
    type: 'ToolRunner',
    defaultMode: 'advanced',
    title: 'Tool Runner',
    pitch:
      'Function-caller / executor. Decide qual tool chamar com quais argumentos pra cumprir uma tarefa que exige ação no mundo. Step próprio de Identificação captura nome e política de aprovação humana (HITL).',
    steps: ['Tipo', 'Identificação', 'Ferramentas', 'Segurança', 'Memória', 'Output', 'Modelo', 'Revisão'],
    icon: <BoltIcon className="h-6 w-6" />,
    accent: 'from-amber-500/15 to-amber-500/0 text-amber-600 dark:text-amber-400',
    iconBg: 'bg-amber-500/15 text-amber-600 dark:text-amber-400',
  },
]

export function NewAgentModeModal({ open, onClose, onSelect }: NewAgentModeModalProps) {
  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Como você quer começar?"
      description="Escolha o tipo formal do agente — Custom (livre), Router (classifier), Worker (specialist) ou Tool Runner (executor). Templates abaixo aceleram quando o caso já tem um modelo pronto."
      size="lg"
    >
      <p className="mb-3 text-[11px] font-semibold uppercase tracking-wider text-fg-dim">
        Tipo do agente
      </p>
      <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
        {TYPE_CHOICES.map((choice) => (
          <button
            key={choice.type}
            type="button"
            onClick={() => onSelect({ type: choice.type, mode: choice.defaultMode })}
            className={cn(
              'group relative flex flex-col items-start gap-4 overflow-hidden rounded-xl border border-border bg-surface p-5 text-left transition',
              'hover:border-accent/40 hover:shadow-soft focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/40',
            )}
          >
            <div className={cn('absolute inset-x-0 top-0 h-20 bg-gradient-to-b', choice.accent)} aria-hidden="true" />

            <div className="relative flex w-full items-center justify-between gap-3">
              <div className={cn('flex h-11 w-11 items-center justify-center rounded-xl', choice.iconBg)}>
                {choice.icon}
              </div>
              <ArrowRightIcon className="h-4 w-4 -translate-x-1 text-fg-dim transition group-hover:translate-x-0 group-hover:text-accent" />
            </div>

            <div className="relative space-y-1">
              <h3 className="text-base font-semibold text-fg">{choice.title}</h3>
              <p className="text-xs leading-relaxed text-fg-muted">{choice.pitch}</p>
            </div>

            <div className="relative w-full space-y-2 border-t border-border pt-3">
              <p className="text-[10px] font-semibold uppercase tracking-wider text-fg-dim">
                {choice.steps.length} etapas
              </p>
              <ul className="space-y-1.5">
                {choice.steps.map((step, idx) => (
                  <li key={step} className="flex items-center gap-2 text-xs text-fg-muted">
                    <span
                      className={cn(
                        'flex h-5 w-5 shrink-0 items-center justify-center rounded-full border text-[10px] font-bold',
                        idx === 0
                          ? 'border-accent/40 bg-accent-subtle text-accent'
                          : 'border-border bg-bg-soft text-fg-dim',
                      )}
                    >
                      {idx + 1}
                    </span>
                    <span className="text-fg">{step}</span>
                    {idx === choice.steps.length - 1 && (
                      <CheckIcon className="ml-auto h-3.5 w-3.5 text-success" />
                    )}
                  </li>
                ))}
              </ul>
            </div>
          </button>
        ))}
      </div>

      {/* Templates pré-populados — atalho pro time-to-first-agent. Hidratam
          Profile/nome/descrição via templates.ts e abrem o wizard direto no
          step de Perfil (skip do step de Tipo, já que o template é Custom). */}
      <div className="mt-6 space-y-3">
        <p className="text-[11px] font-semibold uppercase tracking-wider text-fg-dim">
          Templates Custom
        </p>
        <p className="text-[11px] leading-relaxed text-fg-dim">
          Pré-fills de Perfil pra casos comuns. Todos abrem como Custom — você pode editar tudo, inclusive trocar pra modo avançado pelo toggle do wizard.
        </p>
        <div className="grid grid-cols-1 gap-2 md:grid-cols-3">
          {AGENT_TEMPLATES.map((tpl) => (
            <button
              key={tpl.key}
              type="button"
              onClick={() =>
                onSelect({ type: 'Custom', mode: tpl.defaultMode, template: tpl.key })
              }
              className={cn(
                'group flex flex-col items-start gap-2 rounded-xl border border-border bg-surface p-3 text-left transition',
                'hover:border-accent/40 hover:bg-accent-subtle/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/40',
              )}
            >
              <div className="flex h-7 w-7 items-center justify-center rounded-lg bg-accent-subtle text-accent">
                <AgentIcon className="h-4 w-4" />
              </div>
              <div className="min-w-0">
                <h4 className="text-sm font-semibold text-fg">{tpl.title}</h4>
                <p className="mt-0.5 line-clamp-2 text-[11px] text-fg-muted">{tpl.pitch}</p>
                <p className="mt-1 text-[10px] uppercase tracking-wide text-fg-dim">
                  {tpl.defaultMode === 'advanced' ? 'Custom · avançado' : 'Custom · básico'}
                </p>
              </div>
            </button>
          ))}
        </div>
      </div>
    </Modal>
  )
}
