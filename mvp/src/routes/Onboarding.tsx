import { useEffect, useMemo, useState } from 'react'
import { getIdentity, setIdentity } from '../stores/identity'
import { listProjects, type Project } from '../api/projects'
import { friendlyError } from '../api/client'
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
        projectId: '',
        projectName: '',
      })

      listProjects()
        .then((list) => {
          if (cancelled) return
          setProjects(list)
          // Auto-seleciona se houver só um projeto disponível, senão deixa
          // o user escolher conscientemente.
          setProjectId((current) => {
            if (current && list.some((p) => p.id === current)) return current
            return list.length === 1 ? list[0].id : ''
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
  }, [account, name])

  const projectOptions = useMemo<SelectOption[]>(
    () => projects.map((p) => ({ value: p.id, label: p.name })),
    [projects],
  )

  const projectPlaceholder = useMemo(() => {
    if (!account.trim()) return 'Informe a conta primeiro'
    if (loading) return 'Carregando…'
    if (projects.length === 0) return 'Nenhum projeto disponível'
    return undefined
  }, [account, loading, projects.length])

  const canSubmit = !!(name.trim() && account.trim() && projectId)

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!canSubmit) return
    const selected = projects.find((p) => p.id === projectId)
    setIdentity({
      name: name.trim(),
      account: account.trim(),
      projectId,
      projectName: selected?.name ?? '',
    })
  }

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
          <h1 className="text-2xl font-semibold tracking-tight">Bem-vindo ao AI Hub</h1>
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

            <Input
              label="Conta"
              placeholder="ex.: 12345"
              value={account}
              onChange={(e) => setAccount(e.target.value)}
              hint="Identifica seu acesso em todas as chamadas do hub."
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
              Continuar
            </Button>
          </form>
        </Card>

        {error && <ErrorMessage message={error} className="mt-4" />}

        <p className="mt-6 text-center text-[11px] text-fg-dim">
          Você pode trocar de projeto a qualquer momento pelo ícone de configurações.
        </p>
      </div>
    </div>
  )
}
