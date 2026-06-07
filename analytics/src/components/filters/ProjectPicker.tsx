/**
 * ProjectPicker — dropdown dos projetos visíveis ao usuário (`/me`).
 *
 * Reusabilidade: domain-agnostic — exibição. Lê de `useProjects()` (cache
 * singleton) e escreve via `patchIdentity` no identity store. Todo componente
 * que precisa de "qual projeto vou consultar?" subscreve `useIdentity()` e
 * recebe o update automaticamente.
 *
 * Auto-select: quando o usuário tem exatamente 1 projeto e ainda não escolheu,
 * grava silenciosamente. Evita o teto de "selecione um projeto" eternamente.
 *
 * NÃO faz: criar projeto, listar tenant (escopo do admin).
 *
 * Exemplo: <ProjectPicker />
 *          Reusado por: header de qualquer tela analytics.
 */
import { useEffect } from 'react'
import { useProjects } from '../../stores/me'
import { patchIdentity, useIdentity } from '../../stores/identity'

export function ProjectPicker() {
  const projects = useProjects()
  const identity = useIdentity()

  // Auto-select quando o usuário só tem 1 projeto — evita pedir clique
  // pra escolher entre "default" único.
  useEffect(() => {
    if (!projects || projects.length === 0) return
    if (identity?.projectId) return
    if (projects.length === 1) {
      patchIdentity({ projectId: projects[0].id, projectName: projects[0].name })
    }
  }, [projects, identity?.projectId])

  function handleChange(e: React.ChangeEvent<HTMLSelectElement>) {
    const next = projects?.find((p) => p.id === e.target.value)
    if (!next) return
    patchIdentity({ projectId: next.id, projectName: next.name })
  }

  if (!projects) {
    return (
      <div className="flex flex-col gap-1">
        <span className="text-xs font-medium text-fg-muted">Projeto</span>
        <div className="h-9 w-48 animate-pulse rounded-lg bg-surface-hover" />
      </div>
    )
  }

  if (projects.length === 0) {
    return (
      <div className="flex flex-col gap-1">
        <span className="text-xs font-medium text-fg-muted">Projeto</span>
        <span className="text-xs text-fg-dim">Sem projetos visíveis</span>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-1">
      <label className="text-xs font-medium text-fg-muted" htmlFor="project-picker">
        Projeto
      </label>
      <select
        id="project-picker"
        value={identity?.projectId ?? ''}
        onChange={handleChange}
        className="h-9 min-w-[12rem] rounded-lg border border-border bg-surface px-3 text-sm text-fg focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/30"
      >
        {!identity?.projectId && <option value="">Selecione…</option>}
        {projects.map((p) => (
          <option key={p.id} value={p.id}>
            {p.name}
          </option>
        ))}
      </select>
    </div>
  )
}
