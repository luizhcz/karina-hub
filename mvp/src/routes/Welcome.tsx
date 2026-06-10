import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import { refreshMe, useMe } from '../stores/me'
import { clearIdentity } from '../stores/identity'
import { Button, Card, EmptyState, LogoIcon, Spinner } from '../ui'

/**
 * Tela exibida quando o usuário ainda não tem nenhum projeto vinculado.
 * Caminho default pra non-admin recém-cadastrado — admin vincula projetos
 * via UI, user clica em "Atualizar" e entra. Botão de logout (clearIdentity)
 * dá saída limpa enquanto o vínculo não vem.
 */
export function Welcome() {
  const me = useMe()
  const navigate = useNavigate()
  const [refreshing, setRefreshing] = useState(false)

  // Admin entrou aqui por engano (bypass passa pelo RequireAccess) OU
  // non-admin recebeu vínculo no meio da sessão e clicou em "Atualizar".
  // Redireciona em useEffect pra evitar update durante render (React 18 warn).
  useEffect(() => {
    if (me && (me.isAdmin || me.projects.length > 0)) {
      navigate('/agentes', { replace: true })
    }
  }, [me, navigate])

  if (me && (me.isAdmin || me.projects.length > 0)) return null

  const handleRefresh = async () => {
    setRefreshing(true)
    try {
      await refreshMe()
    } finally {
      setRefreshing(false)
    }
  }

  const handleSwitchIdentity = () => {
    clearIdentity()
    // Após clearIdentity, App.tsx detecta identidade nula e redireciona pro
    // Onboarding na próxima renderização.
  }

  return (
    <div className="flex min-h-full flex-col items-center justify-center px-6 py-12">
      <div className="w-full max-w-md">
        <Card>
          <EmptyState
            icon={<LogoIcon className="h-6 w-6" />}
            title="Bem-vindo"
            description="Você ainda não tem projetos vinculados. Solicite a um administrador o vínculo aos seus projetos e clique em Atualizar quando estiver pronto."
            action={
              <div className="flex flex-col items-center gap-2">
                <Button onClick={handleRefresh} disabled={refreshing} className="min-w-[180px]">
                  {refreshing ? <Spinner /> : 'Atualizar'}
                </Button>
                <button
                  type="button"
                  onClick={handleSwitchIdentity}
                  className="text-xs text-fg-muted underline-offset-2 hover:text-fg hover:underline"
                >
                  Trocar identidade
                </button>
              </div>
            }
          />
        </Card>
      </div>
    </div>
  )
}
