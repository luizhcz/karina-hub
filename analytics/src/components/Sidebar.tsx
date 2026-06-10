/**
 * Sidebar — navegação lateral fixa (240px) agrupada por área de análise.
 *
 * Reusabilidade: domain-agnostic (grupos + items vêm de constante exportada).
 * Adicionar uma tela nova: edita o array NAV_GROUPS, sem mexer no Layout.
 *
 * Decisões:
 *   - Sidebar tem o branding no topo (em vez do header da página). Header
 *     fino fica só pra contexto do usuário (nome + projeto).
 *   - Active state via barra acentuada à esquerda (pattern Linear/Stripe).
 *     Subliminar "você está aqui" sem competir com o conteúdo principal.
 *   - Section headers em uppercase tiny, low-contrast. Estilo enterprise
 *     que respira sem virar barulho.
 *   - Ícones inline SVG (sem dep nova). Trade-off: cada um custa ~10 LoC.
 *     Vale porque temos só 9 navlinks — não justifica uma lib de 50KB.
 *
 * NÃO faz: collapse mobile (V1 desktop-first), sub-items aninhados, badges.
 */
import { NavLink } from 'react-router'
import { cn } from './ui/cn'
import { useIsAdmin } from '../stores/me'

interface NavItem {
  to: string
  label: string
  icon: React.ReactNode
}

interface NavGroup {
  title: string
  items: ReadonlyArray<NavItem>
  adminOnly?: boolean
}

const NAV_GROUPS: ReadonlyArray<NavGroup> = [
  {
    title: 'Análise',
    items: [
      { to: '/', label: 'Visão geral', icon: <DashboardIcon /> },
      { to: '/custos', label: 'Custos', icon: <CoinIcon /> },
      { to: '/ferramentas', label: 'Ferramentas', icon: <ToolIcon /> },
      { to: '/roteador', label: 'Roteador', icon: <BranchIcon /> },
      { to: '/feedback', label: 'Feedback', icon: <ThumbIcon /> },
      { to: '/document-intelligence', label: 'Document AI', icon: <DocScanIcon /> },
    ],
  },
  {
    title: 'Saúde',
    items: [
      { to: '/confiabilidade', label: 'Confiabilidade', icon: <ShieldIcon /> },
      { to: '/fila', label: 'Fila', icon: <QueueIcon /> },
      { to: '/webhooks', label: 'Webhooks', icon: <WebhookIcon /> },
    ],
  },
  {
    title: 'Operação',
    items: [
      { to: '/execucoes', label: 'Execuções', icon: <ListIcon /> },
      { to: '/workers', label: 'Workers', icon: <CpuIcon /> },
    ],
  },
  {
    title: 'Administração',
    adminOnly: true,
    items: [
      { to: '/admin/auditoria', label: 'Auditoria', icon: <AuditIcon /> },
      { to: '/admin/llm-capture', label: 'Captura LLM', icon: <CaptureIcon /> },
      { to: '/admin/llm-calls', label: 'Chamadas LLM', icon: <LlmCallsIcon /> },
    ],
  },
]

export function Sidebar() {
  const isAdmin = useIsAdmin()
  // Grupo "Administração" só aparece pra admin — as telas também gateiam via
  // useIsAdmin + backend (403). null (carregando) esconde até resolver.
  const groups = NAV_GROUPS.filter((g) => !g.adminOnly || isAdmin === true)
  return (
    <aside className="flex w-60 shrink-0 flex-col border-r border-border bg-bg-soft">
      <div className="flex items-center gap-2 px-5 py-5">
        <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-accent-subtle text-accent">
          <BarChartGlyph />
        </div>
        <div className="leading-tight">
          <div className="text-sm font-semibold tracking-tight text-fg">AI Hub</div>
          <div className="text-[10px] uppercase tracking-widest text-fg-dim">Analytics</div>
        </div>
      </div>

      <nav className="flex-1 px-3 py-2">
        {groups.map((group) => (
          <div key={group.title} className="mb-5 last:mb-0">
            <div className="px-3 pb-1.5 text-[10px] font-medium uppercase tracking-widest text-fg-dim">
              {group.title}
            </div>
            {group.items.map((item) => (
              <SidebarLink key={item.to} item={item} />
            ))}
          </div>
        ))}
      </nav>
    </aside>
  )
}

function SidebarLink({ item }: { item: NavItem }) {
  return (
    <NavLink
      to={item.to}
      end={item.to === '/'}
      className={({ isActive }) =>
        cn(
          'relative flex items-center gap-3 rounded-lg px-3 py-2 text-sm transition',
          // Barra lateral indicando ativo — padrão Linear/Stripe.
          'before:absolute before:inset-y-1.5 before:left-0 before:w-0.5 before:rounded-r before:transition',
          isActive
            ? 'bg-accent-subtle text-accent before:bg-accent'
            : 'text-fg-muted before:bg-transparent hover:bg-surface-hover hover:text-fg',
        )
      }
    >
      <span className="h-4 w-4 shrink-0">{item.icon}</span>
      <span>{item.label}</span>
    </NavLink>
  )
}

// ─── Icons inline (16x16 viewBox, currentColor, stroke 1.8) ──────────────────

function BarChartGlyph() {
  return (
    <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" className="h-4 w-4" aria-hidden>
      <path d="M3 13V8" />
      <path d="M8 13V3" />
      <path d="M13 13v-6" />
      <path d="M2 13h12" />
    </svg>
  )
}

function DashboardIcon() {
  return (
    <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" className="h-4 w-4" aria-hidden>
      <rect x="2" y="2" width="5" height="5" rx="1" />
      <rect x="9" y="2" width="5" height="3" rx="1" />
      <rect x="9" y="7" width="5" height="7" rx="1" />
      <rect x="2" y="9" width="5" height="5" rx="1" />
    </svg>
  )
}

function CoinIcon() {
  return (
    <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" className="h-4 w-4" aria-hidden>
      <circle cx="8" cy="8" r="6" />
      <path d="M8 4.5v7" />
      <path d="M10 6.5c0-.8-.9-1.5-2-1.5s-2 .7-2 1.5.9 1.5 2 1.5 2 .7 2 1.5-.9 1.5-2 1.5-2-.7-2-1.5" />
    </svg>
  )
}

function ToolIcon() {
  return (
    <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" className="h-4 w-4" aria-hidden>
      <path d="M10.5 3a3 3 0 1 1 2.5 4.6L7 13.5l-2.5.5.5-2.5L10.5 3z" />
      <path d="M2 14l1-1" />
    </svg>
  )
}

function BranchIcon() {
  return (
    <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" className="h-4 w-4" aria-hidden>
      <circle cx="4" cy="3" r="1.5" />
      <circle cx="4" cy="13" r="1.5" />
      <circle cx="12" cy="8" r="1.5" />
      <path d="M4 4.5v7" />
      <path d="M4 8c0-2 1.5-3 3-3h3" />
    </svg>
  )
}

function ThumbIcon() {
  return (
    <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" className="h-4 w-4" aria-hidden>
      <path d="M5.5 7v6.5h-2A.5.5 0 0 1 3 13V7.5a.5.5 0 0 1 .5-.5h2z" />
      <path d="M5.5 7l2.5-4.5a1 1 0 0 1 1.9.5V6h3a1.2 1.2 0 0 1 1.2 1.4l-1 4.6A1.5 1.5 0 0 1 11.6 13H5.5" />
    </svg>
  )
}

function DocScanIcon() {
  return (
    <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" className="h-4 w-4" aria-hidden>
      <path d="M3 5V3.5A1.5 1.5 0 0 1 4.5 2H6" />
      <path d="M13 5V3.5A1.5 1.5 0 0 0 11.5 2H10" />
      <path d="M3 11v1.5A1.5 1.5 0 0 0 4.5 14H6" />
      <path d="M13 11v1.5A1.5 1.5 0 0 1 11.5 14H10" />
      <path d="M3 8h10" />
      <path d="M5.5 5.5h5" />
      <path d="M5.5 10.5h5" />
    </svg>
  )
}

function ShieldIcon() {
  return (
    <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" className="h-4 w-4" aria-hidden>
      <path d="M8 2l5 2v4c0 3.3-2.3 5.5-5 6-2.7-.5-5-2.7-5-6V4l5-2z" />
      <path d="M6 8l1.5 1.5L10.5 6.5" />
    </svg>
  )
}

function QueueIcon() {
  return (
    <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" className="h-4 w-4" aria-hidden>
      <rect x="2" y="3" width="12" height="2.2" rx="0.8" />
      <rect x="2" y="6.9" width="12" height="2.2" rx="0.8" />
      <rect x="2" y="10.8" width="12" height="2.2" rx="0.8" />
    </svg>
  )
}

function WebhookIcon() {
  return (
    <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" className="h-4 w-4" aria-hidden>
      <circle cx="4" cy="11" r="1.5" />
      <circle cx="12" cy="11" r="1.5" />
      <circle cx="8" cy="4" r="1.5" />
      <path d="M5.3 11h5.4" />
      <path d="M8 5.5l-2.5 4.2" />
      <path d="M8 5.5l2.5 4.2" />
    </svg>
  )
}

function ListIcon() {
  return (
    <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" className="h-4 w-4" aria-hidden>
      <path d="M5 4h9" />
      <path d="M5 8h9" />
      <path d="M5 12h9" />
      <circle cx="2.5" cy="4" r="0.5" />
      <circle cx="2.5" cy="8" r="0.5" />
      <circle cx="2.5" cy="12" r="0.5" />
    </svg>
  )
}

function CpuIcon() {
  return (
    <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" className="h-4 w-4" aria-hidden>
      <rect x="3.5" y="3.5" width="9" height="9" rx="1.2" />
      <rect x="6" y="6" width="4" height="4" />
      <path d="M3.5 6h-1" />
      <path d="M3.5 10h-1" />
      <path d="M13.5 6h1" />
      <path d="M13.5 10h1" />
      <path d="M6 3.5v-1" />
      <path d="M10 3.5v-1" />
      <path d="M6 13.5v1" />
      <path d="M10 13.5v1" />
    </svg>
  )
}

function AuditIcon() {
  return (
    <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" className="h-4 w-4" aria-hidden>
      <path d="M3 2.5h6L13 6v7.5a.5.5 0 0 1-.5.5h-9a.5.5 0 0 1-.5-.5v-11a.5.5 0 0 1 .5-.5z" />
      <path d="M9 2.5V6h4" />
      <path d="M5.5 9l1.4 1.4L10 7.3" />
    </svg>
  )
}

function CaptureIcon() {
  return (
    <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" className="h-4 w-4" aria-hidden>
      <circle cx="8" cy="8" r="2" />
      <path d="M2.5 8a5.5 5.5 0 0 1 11 0a5.5 5.5 0 0 1-11 0z" />
    </svg>
  )
}

function LlmCallsIcon() {
  return (
    <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" className="h-4 w-4" aria-hidden>
      <path d="M3 4a1 1 0 0 1 1-1h8a1 1 0 0 1 1 1v5a1 1 0 0 1-1 1H6.5L4 12v-2H4a1 1 0 0 1-1-1z" />
      <path d="M5.5 6h5" />
      <path d="M5.5 8h3" />
    </svg>
  )
}
