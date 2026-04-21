import { Link } from 'react-router'

export function NotFound() {
  return (
    <div className="flex flex-col items-center justify-center h-full gap-4">
      <div className="text-6xl font-bold text-text-muted">404</div>
      <p className="text-text-secondary">Página não encontrada</p>
      <Link
        to="/dashboard"
        className="px-4 py-2 bg-accent-blue text-white rounded-lg text-sm hover:bg-accent-blue/80 transition-colors"
      >
        Voltar ao Dashboard
      </Link>
    </div>
  )
}
