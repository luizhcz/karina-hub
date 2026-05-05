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

interface NewAgentModeModalProps {
  open: boolean
  onClose: () => void
  onSelect: (mode: 'basic' | 'advanced', template?: TemplateKey) => void
}

interface ModeChoice {
  mode: 'basic' | 'advanced'
  title: string
  pitch: string
  steps: string[]
  icon: React.ReactNode
  accent: string
}

const CHOICES: ModeChoice[] = [
  {
    mode: 'basic',
    title: 'Agente básico',
    pitch: 'Configuração rápida pra colocar um agente em pé com poucos cliques.',
    steps: ['Perfil', 'Ferramentas e MCPs', 'Modelo', 'Revisão'],
    icon: <BoltIcon className="h-6 w-6" />,
    accent: 'from-emerald-500/15 to-emerald-500/0 text-emerald-600 dark:text-emerald-400',
  },
  {
    mode: 'advanced',
    title: 'Agente avançado',
    pitch: 'Inclui input e output estruturados — útil quando outro sistema vai consumir o agente.',
    steps: ['Perfil', 'Ferramentas e MCPs', 'Input', 'Output', 'Modelo', 'Revisão'],
    icon: <SparklesIcon className="h-6 w-6" />,
    accent: 'from-violet-500/15 to-violet-500/0 text-violet-600 dark:text-violet-400',
  },
]

export function NewAgentModeModal({ open, onClose, onSelect }: NewAgentModeModalProps) {
  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Como você quer começar?"
      description="Comece a partir de um modelo pronto ou monte do zero — você pode editar tudo depois."
      size="lg"
    >
      {/* Templates pré-populados — atalho pro time-to-first-agent.
          Clique cai em /agentes/novo?mode=basic&template=<key> e o editor
          hidrata os campos de Profile via templates.ts. */}
      <div className="mb-5 space-y-3">
        <p className="text-[11px] font-semibold uppercase tracking-wider text-fg-dim">
          Comece de um modelo
        </p>
        <div className="grid grid-cols-1 gap-2 md:grid-cols-3">
          {AGENT_TEMPLATES.map((tpl) => (
            <button
              key={tpl.key}
              type="button"
              onClick={() => onSelect(tpl.defaultMode, tpl.key)}
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
              </div>
            </button>
          ))}
        </div>
      </div>

      <p className="mb-3 text-[11px] font-semibold uppercase tracking-wider text-fg-dim">
        Ou comece em branco
      </p>
      <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
        {CHOICES.map((choice) => (
          <button
            key={choice.mode}
            type="button"
            onClick={() => onSelect(choice.mode)}
            className={cn(
              'group relative flex flex-col items-start gap-4 overflow-hidden rounded-xl border border-border bg-surface p-5 text-left transition',
              'hover:border-accent/40 hover:shadow-soft focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/40',
            )}
          >
            <div className={cn('absolute inset-x-0 top-0 h-20 bg-gradient-to-b', choice.accent)} aria-hidden="true" />

            <div className="relative flex items-center justify-between gap-3">
              <div
                className={cn(
                  'flex h-11 w-11 items-center justify-center rounded-xl',
                  choice.mode === 'basic'
                    ? 'bg-emerald-500/15 text-emerald-600 dark:text-emerald-400'
                    : 'bg-violet-500/15 text-violet-600 dark:text-violet-400',
                )}
              >
                {choice.icon}
              </div>
              <ArrowRightIcon className="h-4 w-4 -translate-x-1 text-fg-dim transition group-hover:translate-x-0 group-hover:text-accent" />
            </div>

            <div className="relative space-y-1">
              <h3 className="text-base font-semibold text-fg">{choice.title}</h3>
              <p className="text-xs text-fg-muted">{choice.pitch}</p>
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
    </Modal>
  )
}
