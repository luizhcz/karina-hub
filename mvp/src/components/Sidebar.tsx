import { NavLink } from 'react-router'
import {
  AgentIcon,
  BoltIcon,
  ChartIcon,
  CheckIcon,
  LogoIcon,
  ServerIcon,
  SparklesIcon,
  ToolIcon,
  cn,
} from '../ui'
import { useIsAdmin } from '../stores/me'

interface NavItem {
  label: string
  to: string
  icon: React.ReactNode
  adminOnly?: boolean
}

const items: NavItem[] = [
  { label: 'Dashboard', to: '/dashboard', icon: <ChartIcon className="h-5 w-5" /> },
  { label: 'Agentes', to: '/agentes', icon: <AgentIcon className="h-5 w-5" /> },
  { label: 'Intenções', to: '/intencoes', icon: <SparklesIcon className="h-5 w-5" /> },
  { label: 'Aprovações', to: '/aprovacoes', icon: <CheckIcon className="h-5 w-5" /> },
  // Implantações é página de gestão (deploys reais em prod) — só admin enxerga.
  // Sandbox de agente fica acessível a todos via card de Agentes ("Testar").
  { label: 'Implantações', to: '/implantacoes', icon: <BoltIcon className="h-5 w-5" />, adminOnly: true },
  { label: 'Avaliações', to: '/avaliacoes', icon: <SparklesIcon className="h-5 w-5" /> },
  { label: 'Ferramentas', to: '/ferramentas', icon: <ToolIcon className="h-5 w-5" /> },
  { label: 'MCPs', to: '/mcps', icon: <ServerIcon className="h-5 w-5" /> },
]

export function Sidebar() {
  const isAdmin = useIsAdmin()
  // isAdmin === null = ainda checando: escondemos itens adminOnly até ter certeza
  // (evita "flash" do item desaparecendo logo depois de aparecer).
  const visibleItems = items.filter((i) => !i.adminOnly || isAdmin === true)
  return (
    <aside className="flex w-60 shrink-0 flex-col border-r border-border bg-bg-soft">
      <div className="flex items-center gap-2 px-5 py-5">
        <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-accent-subtle text-accent">
          <LogoIcon className="h-4 w-4" />
        </div>
        <div className="leading-tight">
          <div className="text-sm font-semibold tracking-tight text-fg">AI Hub</div>
          <div className="text-[10px] uppercase tracking-widest text-fg-dim">Governance Platform</div>
        </div>
      </div>

      <nav className="flex-1 px-3 py-2">
        {visibleItems.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            className={({ isActive }) =>
              cn(
                'relative flex items-center gap-3 rounded-lg px-3 py-2 text-sm transition',
                // Quick-win UX: barra lateral à esquerda no item ativo dá hierarquia
                // visual de menu enterprise (Linear/Stripe pattern).
                'before:absolute before:inset-y-1.5 before:left-0 before:w-0.5 before:rounded-r before:transition',
                isActive
                  ? 'bg-accent-subtle text-accent before:bg-accent'
                  : 'text-fg-muted before:bg-transparent hover:bg-surface-hover hover:text-fg',
              )
            }
          >
            {item.icon}
            <span>{item.label}</span>
          </NavLink>
        ))}
      </nav>
    </aside>
  )
}
