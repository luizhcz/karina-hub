import { useEffect } from 'react'
import { Outlet } from 'react-router'
import { getIdentity, patchIdentity, useIdentity } from '../stores/identity'
import { useMe } from '../stores/me'
import { Sidebar } from './Sidebar'

// Shell padrão: Sidebar 240px fixa à esquerda + topbar fino com identity
// à direita + Outlet do conteúdo. Branding mora no sidebar (não duplica).
// Nav cresceu pra 9 telas e ficou apertado horizontalmente — sidebar deixa
// espaço pra adicionar mais sem refluir o conteúdo.

/**
 * Sincroniza identity.projectId com a lista de projetos visíveis do /me.
 * Mirror do mvp/Layout.useAutoSelectProject. Dois cenários cobertos:
 *   1. projectId vazio → escolhe o primeiro projeto visível. Sem isso, todo
 *      useApi(getProject*) dispara com path `/projects//*` e devolve 404 até
 *      o user clicar manualmente no picker.
 *   2. projectId vigente NÃO está mais na lista (admin revogou vínculo em
 *      sessão paralela, projeto foi deletado) → re-escolhe o primeiro válido.
 * Não força quando o user tem 0 projetos visíveis — a UI mostra estado vazio
 * e o user vai pro /bem-vindo (a implementar futuramente).
 */
function useAutoSelectProject() {
  const me = useMe()
  const identity = useIdentity()

  useEffect(() => {
    if (!me || !identity) return
    const visible = me.projects
    if (visible.length === 0) return

    const currentId = identity.projectId
    const stillValid = currentId && visible.some((p) => p.id === currentId)
    if (stillValid) return

    const target = visible[0]
    // Releitura imediata antes do patch — reduz risco de overwrite quando o
    // user trocou em outra aba entre o effect e o commit.
    if (getIdentity()) {
      patchIdentity({ projectId: target.id, projectName: target.name })
    }
  }, [me, identity])
}

export function Layout() {
  useAutoSelectProject()
  const identity = useIdentity()
  return (
    <div className="flex min-h-screen">
      <Sidebar />
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="border-b border-border bg-surface/80 backdrop-blur">
          <div className="flex items-center justify-end gap-4 px-6 py-3">
            {identity && (
              <div className="text-xs text-fg-muted">
                <span className="text-fg">{identity.name}</span>
                {identity.projectName && (
                  <span className="ml-2 text-fg-dim">/ {identity.projectName}</span>
                )}
              </div>
            )}
          </div>
        </header>
        <main className="w-full flex-1 px-6 py-6">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
