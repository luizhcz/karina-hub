import { Outlet } from 'react-router'
import { Sidebar } from './Sidebar'
import { Header } from './Header'
import { useSidebarStore } from '../stores/sidebar'
import { cn } from '../shared/utils/cn'

export function AppShell() {
  const collapsed = useSidebarStore((s) => s.collapsed)

  return (
    <div className="flex h-screen bg-bg-primary">
      <Sidebar />
      <div
        className={cn(
          'flex-1 flex flex-col transition-all duration-200',
          collapsed ? 'ml-16' : 'ml-56'
        )}
      >
        <Header />
        <main className="flex-1 overflow-auto p-6">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
