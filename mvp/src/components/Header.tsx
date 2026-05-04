import { useEffect, useState } from 'react'
import { getIdentity, subscribeIdentity } from '../stores/identity'
import { ProjectSelectorModal } from './ProjectSelectorModal'
import { ChevronDownIcon, IconButton, SettingsIcon, ThemeToggle } from '../ui'

// Top bar: nome do projeto atual à esquerda (clicável → abre modal de
// configurações com troca de projeto e identidade), toggle de tema +
// avatar/identidade à direita.
export function Header() {
  const [identity, setLocalIdentity] = useState(() => getIdentity())
  const [openSettings, setOpenSettings] = useState(false)

  useEffect(() => subscribeIdentity(() => setLocalIdentity(getIdentity())), [])

  const initials = identity?.name
    ? identity.name
        .split(/\s+/)
        .map((p) => p[0])
        .filter(Boolean)
        .slice(0, 2)
        .join('')
        .toUpperCase()
    : '?'

  const projectLabel = identity?.projectName?.trim()
    ? identity.projectName
    : 'Selecionar projeto'

  return (
    <header className="flex items-center justify-between border-b border-border bg-bg-soft px-8 py-4">
      <div className="flex items-center gap-3">
        <div className="text-[11px] uppercase tracking-widest text-fg-dim">Projeto</div>
        <button
          type="button"
          onClick={() => setOpenSettings(true)}
          className="flex items-center gap-2 rounded-lg border border-border bg-surface px-3 py-1.5 text-sm text-fg transition hover:bg-surface-hover focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/30"
        >
          <span className="font-medium">{projectLabel}</span>
          <ChevronDownIcon className="h-3.5 w-3.5 text-fg-dim" />
        </button>
      </div>

      <div className="flex items-center gap-3">
        <ThemeToggle />
        <IconButton aria-label="Configurações" onClick={() => setOpenSettings(true)}>
          <SettingsIcon className="h-4 w-4" />
        </IconButton>
        <div className="flex items-center gap-2.5 px-1">
          <div className="flex h-8 w-8 items-center justify-center rounded-full bg-accent-subtle text-[11px] font-semibold text-accent">
            {initials}
          </div>
          <div className="leading-tight">
            <div className="text-xs font-medium text-fg">{identity?.name}</div>
            <div className="text-[10px] text-fg-dim">conta {identity?.account}</div>
          </div>
        </div>
      </div>

      <ProjectSelectorModal open={openSettings} onClose={() => setOpenSettings(false)} />
    </header>
  )
}
