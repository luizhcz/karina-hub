/**
 * Onboarding — entrada manual de identidade quando o app roda em dev local
 * sem proxy/SSO. Mirror funcional do `mvp/src/routes/Onboarding.tsx` (mesma
 * semântica: persistir name + account + userType no identity store antes do
 * primeiro request bater no backend).
 *
 * Quando NÃO aparece:
 *   - App acessado com `?access_token=...&app_origin=...` (fluxo via proxy):
 *     `bootstrapAuthFromUrl` persiste o token, App.tsx mostra splash
 *     "Carregando sessão…" enquanto /me hidrata e syncIdentityFromMe popula
 *     o identity store.
 *   - Identity já presente em localStorage (refresh ou retorno após logout
 *     com identity preservada).
 *
 * Por que existe (e é diferente do MVP em escopo, não em fluxo):
 *   - Analytics ecoa as mesmas convenções do MVP de cabeçalhos `x-efs-*`.
 *     Sem identity, o backend trata como anônimo e os endpoints retornam
 *     escopo de projeto vazio — o dashboard fica sem dados úteis e o usuário
 *     vê um dead-end. Onboarding mata o dead-end em dev.
 */
import { useState, type FormEvent } from 'react'
import { Button, Card, CardHeader, cn } from '../components/ui'
import { getIdentity, setIdentity, type UserType } from '../stores/identity'

export function Onboarding() {
  const initial = getIdentity()
  const [name, setName] = useState(initial?.name ?? '')
  const [account, setAccount] = useState(initial?.account ?? '')
  const [userType, setUserType] = useState<UserType>(initial?.userType ?? 'cliente')

  const canSubmit = name.trim().length > 0 && account.trim().length > 0

  function handleSubmit(e: FormEvent) {
    e.preventDefault()
    if (!canSubmit) return
    // Identity é persistida só no clique do "Continuar" — assim que ela
    // entra no store, o App.tsx re-renderiza e dispara /me, que sobrescreve
    // permissions/projects/tenantId via syncIdentityFromMe. projectId fica
    // vazio até o ProjectPicker auto-selecionar (1 projeto) ou o user
    // escolher.
    setIdentity({
      name: name.trim(),
      account: account.trim(),
      userType,
      permissions: [],
      projectId: '',
      projectName: '',
    })
  }

  const accountLabel = userType === 'admin' ? 'ID de perfil' : 'Conta'
  const accountPlaceholder = userType === 'admin' ? 'ex.: 011982329' : 'ex.: 12345'
  const accountHint =
    userType === 'admin'
      ? 'Profile id do assessor — vai como x-efs-user-profile-id em toda chamada.'
      : 'Identifica o cliente em todas as chamadas — vai como x-efs-account.'

  return (
    <div className="flex min-h-screen items-center justify-center px-6">
      <div className="w-full max-w-md">
        <div className="mb-8 text-center">
          <div className="mx-auto mb-4 flex h-12 w-12 items-center justify-center rounded-2xl bg-accent-subtle text-accent">
            <BarChartGlyph className="h-6 w-6" />
          </div>
          <h1 className="text-2xl font-semibold tracking-tight text-fg">Analytics</h1>
          <p className="mt-2 text-sm text-fg-muted">
            Identifique-se para começar a explorar métricas e custos.
          </p>
        </div>

        <Card>
          <CardHeader title="Entrada de identidade" description="Necessário em dev local sem proxy/SSO." />
          <form onSubmit={handleSubmit} className="mt-5 space-y-5">
            <Field
              label="Seu nome"
              placeholder="Ex.: Ana Souza"
              value={name}
              onChange={setName}
              autoFocus
            />

            <div className="flex flex-col gap-1">
              <span className="text-xs font-medium text-fg-muted">Tipo de acesso</span>
              <div className="grid grid-cols-2 gap-2">
                <UserTypeOption
                  active={userType === 'cliente'}
                  onClick={() => setUserType('cliente')}
                  title="Cliente"
                  description="Header x-efs-account."
                />
                <UserTypeOption
                  active={userType === 'admin'}
                  onClick={() => setUserType('admin')}
                  title="Admin / Assessor"
                  description="Header x-efs-user-profile-id."
                />
              </div>
            </div>

            <Field
              label={accountLabel}
              placeholder={accountPlaceholder}
              hint={accountHint}
              value={account}
              onChange={setAccount}
              monospace
            />

            <Button type="submit" disabled={!canSubmit} className="w-full">
              Continuar
            </Button>
          </form>
        </Card>

        <p className="mt-6 text-center text-[11px] text-fg-dim">
          Em prod, o proxy corporativo injeta a identidade via headers — essa tela
          fica inacessível porque o splash de boot já hidrata via /me.
        </p>
      </div>
    </div>
  )
}

interface FieldProps {
  label: string
  placeholder?: string
  hint?: string
  value: string
  onChange: (value: string) => void
  autoFocus?: boolean
  monospace?: boolean
}

function Field({ label, placeholder, hint, value, onChange, autoFocus, monospace }: FieldProps) {
  return (
    <label className="flex flex-col gap-1.5 text-sm">
      <span className="text-xs font-medium text-fg-muted">{label}</span>
      <input
        type="text"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        autoFocus={autoFocus}
        className={cn(
          'h-10 rounded-lg border border-border bg-surface px-3 text-sm text-fg placeholder:text-fg-dim',
          'focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/30',
          monospace && 'font-mono',
        )}
      />
      {hint && <span className="text-[11px] text-fg-dim">{hint}</span>}
    </label>
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
          : 'border-border bg-surface text-fg hover:bg-surface-hover',
      )}
      aria-pressed={active}
    >
      <span className="text-sm font-medium">{title}</span>
      <span className="text-[11px] text-fg-muted">{description}</span>
    </button>
  )
}

function BarChartGlyph({ className }: { className?: string }) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.8}
      strokeLinecap="round"
      strokeLinejoin="round"
      className={className}
      aria-hidden
    >
      <path d="M4 20V10" />
      <path d="M10 20V4" />
      <path d="M16 20v-7" />
      <path d="M22 20H2" />
    </svg>
  )
}
