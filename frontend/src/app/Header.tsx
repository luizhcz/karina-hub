import { useNavigate } from 'react-router'
import { useQueryClient } from '@tanstack/react-query'
import { useProjects } from '../api/projects'
import { useProjectStore } from '../stores/project'
import { useUserStore } from '../stores/user'

export function Header() {
  const navigate = useNavigate()
  const qc = useQueryClient()
  const { projectId, setProject } = useProjectStore()
  const { userId, userType, logout } = useUserStore()
  const { data: projects = [] } = useProjects()

  const handleLogout = () => {
    logout()
    navigate('/login', { replace: true })
  }

  const handleProjectChange = (newId: string) => {
    const p = projects.find((p) => p.id === newId)
    if (p) setProject(p.id, p.name)
    else setProject('default', 'Default')
    qc.invalidateQueries()
  }

  return (
    <header className="h-14 bg-bg-secondary border-b border-border-primary flex items-center justify-between px-6 flex-shrink-0">
      <div className="flex items-center gap-4">
        <div className="flex items-center gap-2">
          <label className="text-xs text-text-muted">Projeto</label>
          <select
            value={projectId}
            onChange={(e) => handleProjectChange(e.target.value)}
            className="bg-bg-tertiary border border-border-secondary rounded-md px-2.5 py-1.5 text-sm text-text-primary focus:outline-none focus:border-accent-blue"
          >
            <option value="default">Default</option>
            {projects.map((p) => (
              <option key={p.id} value={p.id}>{p.name}</option>
            ))}
          </select>
        </div>
      </div>

      <div className="flex items-center gap-3">
        {/* Badge de usuário */}
        <div className="flex items-center gap-2 text-xs">
          <div className="w-7 h-7 rounded-full bg-accent-blue/20 border border-accent-blue/40 flex items-center justify-center text-accent-blue font-semibold">
            {userId.charAt(0).toUpperCase()}
          </div>
          <div className="hidden sm:flex flex-col leading-tight">
            <span className="text-text-primary font-medium">{userId}</span>
            <span className="text-text-muted capitalize">{userType}</span>
          </div>
        </div>

        {/* Botão sair */}
        <button
          onClick={handleLogout}
          className="text-xs text-text-muted hover:text-red-400 border border-border-secondary hover:border-red-400/40 rounded-md px-2.5 py-1.5 transition-colors"
        >
          Sair
        </button>
      </div>
    </header>
  )
}
