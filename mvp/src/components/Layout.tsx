import { Outlet } from 'react-router'
import { Sidebar } from './Sidebar'
import { Header } from './Header'

// Shell padrão pós-onboarding: sidebar fixa à esquerda + header com identidade
// e seletor de projeto + main scrollável.
export function Layout() {
  return (
    <div className="flex h-screen overflow-hidden">
      <Sidebar />
      <div className="flex flex-1 flex-col overflow-hidden">
        <Header />
        <main className="flex-1 overflow-y-auto px-8 py-8">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
