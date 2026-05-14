import { useEffect, useMemo, useState } from 'react'
import { getIdentity, setIdentity, type UserType } from '../stores/identity'
import { listProjects, type Project } from '../api/projects'
import { friendlyError } from '../api/client'
import { cn } from '../ui'
import {
  Button,
  Card,
  ErrorMessage,
  Input,
  LogoIcon,
  Select,
  ThemeToggle,
  type SelectOption,
} from '../ui'

// Debounce do fetch de projetos: evita chamada por keystroke quando o user
// digita a conta. 400ms é confortável — espera o user terminar antes de
// disparar listProjects, mas não fica lento depois do último caractere.
const FETCH_DEBOUNCE_MS = 400

export function Onboarding() {
  // Pré-popula com identidade salva: se o user já passou pela tela e voltou
  // (clicou em "Trocar identidade" ou ainda não escolheu projeto), os campos
  // já vêm preenchidos.
  const initial = getIdentity()
  const [name, setName] = useState(initial?.name ?? '')
  const [account, setAccount] = useState(initial?.account ?? '')
  const [userType, setUserType] = useState<UserType>(initial?.userType ?? 'cliente')
  const [projects, setProjects] = useState<Project[]>([])
  const [projectId, setProjectId] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Lista projetos só depois que o usuário informa a conta — backend valida
  // identidade via header `x-efs-account`. Setamos identidade provisória
  // (sem projectId) só pra que o client.ts envie o header no fetch. App.tsx
  // mantém o user nessa tela enquanto projectId estiver vazio.
  useEffect(() => {
    const trimmedAccount = account.trim()
    if (!trimmedAccount) {
      setProjects([])
      setProjectId('')
      setError(null)
      setLoading(false)
      return
    }

    let cancelled = false
    setLoading(true)
    setError(null)

    const handle = window.setTimeout(() => {
      if (cancelled) return

      setIdentity({
        name: name.trim(),
        account: trimmedAccount,
        userType,
        projectId: '',
        projectName: '',
        chatDeploymentAllowed: false,
      })

      listProjects()
        .then((list) => {
          if (cancelled) return
          setProjects(list)
          // Auto-seleciona o primeiro projeto da lista. Usuário pode trocar
          // no dropdown se quiser outro — pré-selecionar evita o passo extra
          // do "agora selecione um projeto" que ninguém quer fazer na
          // primeira entrada do app.
          setProjectId((current) => {
            if (current && list.some((p) => p.id === current)) return current
            return list.length > 0 ? list[0].id : ''
          })
        })
        .catch((err: unknown) => {
          if (cancelled) return
          setError(friendlyError(err, 'Não foi possível carregar os projetos.'))
          setProjects([])
          setProjectId('')
        })
        .finally(() => {
          if (!cancelled) setLoading(false)
        })
    }, FETCH_DEBOUNCE_MS)

    return () => {
      cancelled = true
      window.clearTimeout(handle)
    }
  }, [account, name, userType])

  const projectOptions = useMemo<SelectOption[]>(
    () => projects.map((p) => ({ value: p.id, label: p.name })),
    [projects],
  )

  const projectPlaceholder = useMemo(() => {
    if (!account.trim()) return 'Informe a conta primeiro'
    if (loading) return 'Carregando…'
    if (projects.length === 0) return 'Nenhum projeto disponível'
    // Sem placeholder, <select value=""> sem matching option exibe o primeiro
    // <option> visualmente — o user pensa que está selecionado, mas o state
    // segue vazio e o botão "Continuar" fica desabilitado. Forçar option
    // vazio resolve a divergência state ↔ display.
    if (!projectId) return 'Selecione um projeto'
    return undefined
  }, [account, loading, projects.length, projectId])

  // Quando o backend devolve lista vazia (non-admin sem nenhum vínculo),
  // permitimos continuar sem projeto — o RequireAccessOrWelcome redireciona
  // pra /bem-vindo na primeira renderização do Layout.
  const noProjectsAvailable = !loading && account.trim().length > 0 && projects.length === 0
  const canSubmit = !!(name.trim() && account.trim() && (projectId || noProjectsAvailable))

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!canSubmit) return
    const selected = projects.find((p) => p.id === projectId)
    setIdentity({
      name: name.trim(),
      account: account.trim(),
      userType,
      projectId: projectId || '',
      projectName: selected?.name ?? '',
      chatDeploymentAllowed: selected?.chatDeploymentAllowed ?? false,
    })
  }

  const accountLabel = userType === 'admin' ? 'ID de perfil' : 'Conta'
  const accountPlaceholder = userType === 'admin' ? 'ex.: 011982329' : 'ex.: 12345'
  const accountHint = userType === 'admin'
    ? 'Profile id do assessor — vai como x-efs-user-profile-id em toda chamada.'
    : 'Identifica o cliente em todas as chamadas do hub — vai como x-efs-account.'

  return (
    <div className="flex min-h-screen items-center justify-center px-6">
      <div className="absolute right-6 top-6">
        <ThemeToggle />
      </div>

      <div className="w-full max-w-md">
        <div className="mb-10 text-center">
          <div className="mx-auto mb-4 flex h-12 w-12 items-center justify-center rounded-2xl bg-accent-subtle text-accent shadow-soft">
            <LogoIcon className="h-6 w-6" />
          </div>
          <h1 className="text-[28px] font-semibold tracking-tight">Bem-vindo ao AI Hub</h1>
          <p className="mt-2 text-sm text-fg-muted">
            Identifique-se para começar a montar suas ferramentas.
          </p>
        </div>

        <Card>
          <form onSubmit={handleSubmit} className="space-y-5">
            <Input
              label="Seu nome"
              placeholder="Ex.: Ana Souza"
              value={name}
              onChange={(e) => setName(e.target.value)}
              autoFocus
            />

            <div className="flex flex-col gap-1">
              <span className="text-xs font-medium text-fg-muted">Tipo de acesso</span>
              <div className="grid grid-cols-2 gap-2">
                <UserTypeOption
                  active={userType === 'cliente'}
                  onClick={() => setUserType('cliente')}
                  title="Cliente"
                  description="Acesso pelo app do cliente — header x-efs-account."
                />
                <UserTypeOption
                  active={userType === 'admin'}
                  onClick={() => setUserType('admin')}
                  title="Assessor"
                  description="Acesso pelo portal interno — header x-efs-user-profile-id."
                />
              </div>
            </div>

            <Input
              label={accountLabel}
              placeholder={accountPlaceholder}
              value={account}
              onChange={(e) => setAccount(e.target.value)}
              hint={accountHint}
              monospace
            />

            <Select
              label="Projeto"
              options={projectOptions}
              value={projectId}
              onChange={(e) => setProjectId(e.target.value)}
              disabled={!account.trim() || loading || projects.length === 0}
              placeholder={projectPlaceholder}
              error={error ?? undefined}
            />

            <Button type="submit" disabled={!canSubmit} className="w-full">
              {noProjectsAvailable ? 'Continuar mesmo assim' : 'Continuar'}
            </Button>
          </form>
        </Card>

        {error && <ErrorMessage message={error} className="mt-4" />}

        {noProjectsAvailable ? (
          <p className="mt-6 text-center text-[11px] text-fg-dim">
            Você ainda não tem projetos vinculados. Continue para acessar a área
            de boas-vindas — solicite o vínculo a um administrador.
          </p>
        ) : (
          <p className="mt-6 text-center text-[11px] text-fg-dim">
            Você pode trocar de projeto a qualquer momento pelo ícone de configurações.
          </p>
        )}
      </div>
    </div>
  )
}

interface UserTypeOptionProps {
  active: boolean
  onClick: () => void
  title: string
  description: string
}

function UserTypeOption({ active, onClick, title, description }: UserTypeOptionProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'flex flex-col items-start gap-1 rounded-lg border p-3 text-left transition focus:outline-none focus:ring-2 focus:ring-accent/30',
        active
          ? 'border-accent bg-accent-subtle text-accent'
          : 'border-border bg-surface hover:bg-surface-hover text-fg',
      )}
      aria-pressed={active}
    >
      <span className="text-sm font-medium">{title}</span>
      <span className="text-[11px] text-fg-muted">{description}</span>
    </button>
  )
}
