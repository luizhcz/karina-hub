import { useEffect } from 'react'
import { Outlet } from 'react-router'
import { Sidebar } from './Sidebar'
import { Header } from './Header'
import { useMe } from '../stores/me'
import { getIdentity, patchIdentity, useIdentity } from '../stores/identity'

// Shell padrão pós-onboarding: sidebar fixa à esquerda + header com identidade
// e seletor de projeto + main scrollável.
export function Layout() {
  useAutoSelectProject()

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

// Sincroniza identity.projectId com me.projects sempre que:
//   - O usuário entra sem projeto selecionado (onboarding deixou vazio, ou
//     fluxo de welcome devolveu vínculo novo).
//   - O projeto salvo em identity não está mais na lista visível ao usuário
//     (ex.: admin revogou o vínculo na sessão paralela, projeto foi deletado).
//
// Sem o sync: header fica "Selecionar projeto" e chamadas que dependem de
// x-project-id falham até o usuário abrir o modal manualmente. Com o sync,
// o app entra direto em algum projeto válido — usuário pode trocar pelo
// header se quiser outro.
function useAutoSelectProject() {
  const me = useMe()
  const identity = useIdentity()

  useEffect(() => {
    if (!me || !identity) return
    const visible = me.projects
    if (visible.length === 0) return  // welcome state — não força projeto

    const currentId = identity.projectId
    const stillValid = currentId && visible.some((p) => p.id === currentId)
    if (stillValid) return

    const target = visible[0]
    // Releitura no momento do patch — reduz risco de overwrite quando o
    // user trocou o projeto em outra aba entre o effect e o commit.
    // chatDeploymentAllowed fica false (conservador): /me.projects não traz
    // o flag; quando o user abrir o ProjectSelectorModal, listProjects() é
    // chamado e o valor real é hidratado no patchIdentity de lá.
    if (getIdentity()) {
      patchIdentity({
        projectId: target.id,
        projectName: target.name,
        chatDeploymentAllowed: false,
      })
    }
  }, [me, identity])
}
