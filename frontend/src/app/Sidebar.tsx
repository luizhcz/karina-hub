import { NavLink, useLocation } from 'react-router'
import { cn } from '../shared/utils/cn'
import { useSidebarStore } from '../stores/sidebar'

interface NavItem {
  label: string
  path: string
  icon: string
}

const operationItems: NavItem[] = [
  { label: 'Dashboard', path: '/dashboard', icon: '◫' },
  { label: 'Agentes', path: '/agents', icon: '⬡' },
  { label: 'Workflows', path: '/workflows', icon: '⎔' },
  { label: 'Chat', path: '/chat', icon: '◉' },
  { label: 'Execuções', path: '/executions', icon: '▶' },
  { label: 'HITL', path: '/hitl', icon: '⚑' },
  { label: 'Tools', path: '/tools', icon: '⚙' },
  { label: 'Skills', path: '/skills', icon: '✦' },
]

const observabilityItems: NavItem[] = [
  { label: 'Métricas', path: '/metrics', icon: '📊' },
  { label: 'Tracing', path: '/tracing', icon: '🔍' },
  { label: 'Auditoria', path: '/audit', icon: '📋' },
]

const adminItems: NavItem[] = [
  { label: 'Custos', path: '/costs', icon: '💰' },
  { label: 'Projetos', path: '/projects', icon: '📁' },
  { label: 'Config', path: '/config', icon: '⚙' },
  { label: 'Audit Admin', path: '/audit/admin', icon: '🛡' },
  { label: 'Background Svc', path: '/background', icon: '⏳' },
]

function NavGroup({ title, items, collapsed }: { title: string; items: NavItem[]; collapsed: boolean }) {
  const location = useLocation()

  return (
    <div className="mb-2">
      {!collapsed && (
        <div className="px-3 py-1.5 text-[10px] font-semibold uppercase tracking-wider text-text-muted">
          {title}
        </div>
      )}
      {items.map((item) => {
        const isActive = location.pathname.startsWith(item.path)
        return (
          <NavLink
            key={item.path}
            to={item.path}
            className={cn(
              'flex items-center gap-2.5 px-3 py-2 mx-2 rounded-lg text-sm transition-colors',
              isActive
                ? 'bg-accent-blue/15 text-blue-400 font-medium'
                : 'text-text-secondary hover:bg-bg-tertiary hover:text-text-primary'
            )}
            title={collapsed ? item.label : undefined}
          >
            <span className="text-base w-5 text-center flex-shrink-0">{item.icon}</span>
            {!collapsed && <span>{item.label}</span>}
          </NavLink>
        )
      })}
    </div>
  )
}

export function Sidebar() {
  const { collapsed, toggle } = useSidebarStore()

  return (
    <aside
      className={cn(
        'fixed top-0 left-0 h-full bg-bg-secondary border-r border-border-primary flex flex-col z-30 transition-all duration-200',
        collapsed ? 'w-16' : 'w-56'
      )}
    >
      {/* Logo */}
      <div className="flex items-center gap-2 px-4 h-14 border-b border-border-primary flex-shrink-0">
        <div className="w-7 h-7 rounded-lg bg-accent-blue flex items-center justify-center text-white font-bold text-xs">
          AI
        </div>
        {!collapsed && <span className="font-semibold text-text-primary text-sm">EfsAiHub</span>}
      </div>

      {/* Nav */}
      <nav className="flex-1 overflow-y-auto py-3 space-y-1">
        <NavGroup title="Operação" items={operationItems} collapsed={collapsed} />
        <div className="mx-4 border-t border-border-primary my-2" />
        <NavGroup title="Observabilidade" items={observabilityItems} collapsed={collapsed} />
        <div className="mx-4 border-t border-border-primary my-2" />
        <NavGroup title="Admin" items={adminItems} collapsed={collapsed} />
      </nav>

      {/* Collapse toggle */}
      <button
        onClick={toggle}
        className="flex items-center justify-center h-10 border-t border-border-primary text-text-muted hover:text-text-primary transition-colors"
      >
        {collapsed ? '›' : '‹'}
      </button>
    </aside>
  )
}
