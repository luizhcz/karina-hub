import { Outlet, useLocation, useNavigate } from 'react-router'
import { Tabs } from '../../shared/ui/Tabs'

const TABS = [
  { key: '/costs', label: 'Overview' },
  { key: '/costs/workflows', label: 'Por Workflow' },
  { key: '/costs/projects', label: 'Por Projeto' },
  { key: '/costs/pricing', label: 'Pricing LLM' },
  { key: '/costs/document-intelligence', label: 'Document Intelligence' },
  { key: '/costs/model-catalog', label: 'Catálogo de Modelos' },
]

export function CostLayout() {
  const location = useLocation()
  const navigate = useNavigate()

  const activeKey =
    TABS.find((t) =>
      t.key === '/costs'
        ? location.pathname === '/costs'
        : location.pathname.startsWith(t.key)
    )?.key ?? '/costs'

  return (
    <div className="flex flex-col">
      <div className="px-6 pt-4 bg-bg-secondary border-b border-border-primary">
        <Tabs items={TABS} active={activeKey} onChange={(key) => navigate(key)} />
      </div>
      <Outlet />
    </div>
  )
}
