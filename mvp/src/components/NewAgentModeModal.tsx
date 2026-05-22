import {
  ArrowRightIcon,
  BoltIcon,
  CheckIcon,
  Modal,
  SparklesIcon,
  cn,
} from '../ui'
import type { AgentType } from '../api/agentDrafts'
import { useIsAdmin } from '../stores/me'

export interface NewAgentSelection {
  type: AgentType
  mode: 'basic' | 'advanced'
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
  /** Tipo aparece grayed-out e não-clicável. Usado pra MVP atual onde
   *  Router ainda não tem fluxo de criação via UI completo. */
  disabled?: boolean
}

// Worker e ToolRunner existem no domínio mas estão fora do escopo atual do
// MVP — omitidos daqui até serem habilitados via produto. Router segue
// visível mas `disabled: true` enquanto não há demanda de criar Router
// pela UI (o pool global de intents + lookup runtime cobrem o caso por ora).
const TYPE_CHOICES: TypeChoice[] = [
  {
    type: 'Custom',
    defaultMode: 'basic',
    title: 'Custom',
    pitch:
      'Agente de uso geral. Você define o perfil em texto livre — quem ele é, o que precisa entregar e em que contexto. Cobre a maioria dos casos: análise, orquestração, atendimento sem chat.',
    steps: ['Perfil', 'Ferramentas', 'Modelo', 'Revisão'],
    icon: <BoltIcon className="h-6 w-6" />,
    accent: 'from-emerald-500/15 to-emerald-500/0 text-emerald-600 dark:text-emerald-400',
    iconBg: 'bg-emerald-500/15 text-emerald-600 dark:text-emerald-400',
  },
  {
    type: 'Router',
    defaultMode: 'basic',
    title: 'Router',
    pitch:
      'Classifica a mensagem do usuário em uma intenção (ex.: “consultar cotação”, “executar ordem”). Usado pra decidir qual agente especialista vai responder.',
    steps: ['Intenções', 'Modelo', 'Revisão'],
    icon: <SparklesIcon className="h-6 w-6" />,
    accent: 'from-violet-500/15 to-violet-500/0 text-violet-600 dark:text-violet-400',
    iconBg: 'bg-violet-500/15 text-violet-600 dark:text-violet-400',
    // `disabled` é decidido em runtime via useIsAdmin() — non-admin vê
    // "Em breve" (mantém UX atual), admin vê habilitado e pode criar Router.
  },
  {
    type: 'Conversational',
    defaultMode: 'basic',
    title: 'Conversational',
    pitch:
      'Chat com memória entre mensagens. O agente conversa com o usuário em múltiplos turnos, mantém contexto e responde em formato que o front sabe renderizar (cards, listas, texto).',
    steps: ['Perfil', 'Ferramentas', 'Modelo', 'Revisão'],
    icon: <SparklesIcon className="h-6 w-6" />,
    accent: 'from-rose-500/15 to-rose-500/0 text-rose-600 dark:text-rose-400',
    iconBg: 'bg-rose-500/15 text-rose-600 dark:text-rose-400',
  },
]

export function NewAgentModeModal({ open, onClose, onSelect }: NewAgentModeModalProps) {
  const isAdmin = useIsAdmin()
  // Router é admin-only no MVP — non-admin vê o card desabilitado com badge
  // "Em breve"; admin pode clicar e abrir o fluxo de criação.
  const choices: TypeChoice[] = TYPE_CHOICES.map((c) =>
    c.type === 'Router' ? { ...c, disabled: !isAdmin } : c,
  )
  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Como você quer começar?"
      description={
        isAdmin
          ? 'Escolha o tipo formal do agente — Custom (livre), Router (classifier) ou Conversational (chat).'
          : 'Escolha o tipo formal do agente — Custom (livre) ou Conversational (chat). Router fica indisponível nesta fase do MVP.'
      }
      size="xl"
    >
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {choices.map((choice) => (
          <button
            key={choice.type}
            type="button"
            onClick={() => onSelect({ type: choice.type, mode: choice.defaultMode })}
            disabled={choice.disabled}
            title={choice.disabled ? 'Em breve — desabilitado nesta fase do MVP.' : undefined}
            className={cn(
              'group relative flex flex-col items-start gap-4 overflow-hidden rounded-xl border border-border bg-surface p-5 text-left transition',
              'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/40',
              choice.disabled
                ? 'cursor-not-allowed opacity-60'
                : 'hover:border-accent/40 hover:shadow-soft',
            )}
          >
            <div className={cn('absolute inset-x-0 top-0 h-20 bg-gradient-to-b', choice.accent)} aria-hidden="true" />

            <div className="relative flex w-full items-center justify-between gap-3">
              <div className={cn('flex h-11 w-11 items-center justify-center rounded-xl', choice.iconBg)}>
                {choice.icon}
              </div>
              {choice.disabled ? (
                <span className="rounded-full border border-border bg-bg-soft px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider text-fg-dim">
                  Em breve
                </span>
              ) : (
                <ArrowRightIcon className="h-4 w-4 -translate-x-1 text-fg-dim transition group-hover:translate-x-0 group-hover:text-accent" />
              )}
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
    </Modal>
  )
}
