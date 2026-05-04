import { NavLink } from 'react-router'
import { AgentIcon, BoltIcon, CheckIcon, LogoIcon, ServerIcon, ToolIcon, cn } from '../ui'

interface NavItem {
  label: string
  to: string
  icon: React.ReactNode
}

const items: NavItem[] = [
  { label: 'Agentes', to: '/agentes', icon: <AgentIcon className="h-5 w-5" /> },
  { label: 'Aprovações', to: '/aprovacoes', icon: <CheckIcon className="h-5 w-5" /> },
  { label: 'Implantações', to: '/implantacoes', icon: <BoltIcon className="h-5 w-5" /> },
  { label: 'Ferramentas', to: '/ferramentas', icon: <ToolIcon className="h-5 w-5" /> },
  { label: 'MCPs', to: '/mcps', icon: <ServerIcon className="h-5 w-5" /> },
]

export function Sidebar() {
  return (
    <aside className="flex w-60 shrink-0 flex-col border-r border-border bg-bg-soft">
      <div className="flex items-center gap-2 px-5 py-5">
        <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-accent-subtle text-accent">
          <LogoIcon className="h-4 w-4" />
        </div>
        <div className="leading-tight">
          <div className="text-sm font-semibold tracking-tight text-fg">AI Hub</div>
          <div className="text-[10px] uppercase tracking-widest text-fg-dim">MVP</div>
        </div>
      </div>

      <nav className="flex-1 px-3 py-2">
        {items.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            className={({ isActive }) =>
              cn(
                'flex items-center gap-3 rounded-lg px-3 py-2 text-sm transition',
                isActive
                  ? 'bg-accent-subtle text-accent'
                  : 'text-fg-muted hover:bg-surface-hover hover:text-fg',
              )
            }
          >
            {item.icon}
            <span>{item.label}</span>
          </NavLink>
        ))}
      </nav>

      <div className="border-t border-border px-5 py-4 text-[10px] text-fg-dim">
        Plataforma de agents · v0.1
      </div>
    </aside>
  )
}
