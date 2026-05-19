import { useState } from 'react'
import { getIdentity, setIdentity, type UserType } from '../stores/identity'
import { cn } from '../ui'
import {
  Button,
  Card,
  Input,
  LogoIcon,
  ThemeToggle,
} from '../ui'

export function Onboarding() {
  // Pré-popula com identidade salva: se o user já passou pela tela e voltou
  // (clicou em "Trocar identidade"), os campos já vêm preenchidos.
  const initial = getIdentity()
  const [name, setName] = useState(initial?.name ?? '')
  const [account, setAccount] = useState(initial?.account ?? '')
  const [userType, setUserType] = useState<UserType>(initial?.userType ?? 'cliente')

  const canSubmit = name.trim().length > 0 && account.trim().length > 0

  // Identity só é persistida no clique do Continuar. Persistir durante o
  // typing (via debounce + setIdentity) fazia o App.tsx renderizar o Layout
  // assim que name+account ficavam preenchidos, antes do user terminar de
  // digitar — o seletor de projeto fica a cargo do Layout (auto-select via
  // useAutoSelectProject + troca manual pelo header).
  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!canSubmit) return
    setIdentity({
      name: name.trim(),
      account: account.trim(),
      userType,
      permissions: [],
      projectId: '',
      projectName: '',
      chatDeploymentAllowed: false,
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

            <Button type="submit" disabled={!canSubmit} className="w-full">
              Continuar
            </Button>
          </form>
        </Card>

        <p className="mt-6 text-center text-[11px] text-fg-dim">
          O projeto é selecionado automaticamente após entrar — você pode trocar a qualquer momento pelo header.
        </p>
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
