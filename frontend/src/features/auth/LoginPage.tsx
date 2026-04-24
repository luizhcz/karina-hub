import { useState } from 'react'
import { useNavigate } from 'react-router'
import { useUserStore } from '../../stores/user'
import { USER_TYPE_OPTIONS, type UserType } from '../../constants/identity'

export function LoginPage() {
  const navigate = useNavigate()
  const { setUser } = useUserStore()

  const [account, setAccount] = useState('')
  const [userType, setUserType] = useState<UserType>('cliente')
  const [error, setError] = useState('')

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    const trimmed = account.trim()
    if (!trimmed) {
      setError('Informe a conta.')
      return
    }
    setUser(trimmed, userType)
    navigate('/dashboard', { replace: true })
  }

  return (
    <div className="flex h-screen items-center justify-center bg-bg-primary">
      <div className="w-full max-w-sm">
        <div className="mb-8 text-center">
          <h1 className="text-2xl font-bold text-text-primary">EFS AI Hub</h1>
          <p className="text-sm text-text-muted mt-1">Informe sua conta para continuar</p>
        </div>

        <form
          onSubmit={handleSubmit}
          className="bg-bg-secondary border border-border-primary rounded-2xl p-8 flex flex-col gap-5 shadow-lg"
        >
          <div className="flex flex-col gap-1.5">
            <label className="text-xs font-medium text-text-secondary uppercase tracking-wide">
              Conta
            </label>
            <input
              type="text"
              value={account}
              onChange={(e) => { setAccount(e.target.value); setError('') }}
              placeholder="Ex: 011982329"
              autoFocus
              className="bg-bg-tertiary border border-border-secondary rounded-lg px-3 py-2.5 text-sm text-text-primary placeholder:text-text-muted focus:outline-none focus:border-accent-blue transition-colors"
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <label className="text-xs font-medium text-text-secondary uppercase tracking-wide">
              Tipo de usuário
            </label>
            <select
              value={userType}
              onChange={(e) => setUserType(e.target.value as UserType)}
              className="bg-bg-tertiary border border-border-secondary rounded-lg px-3 py-2.5 text-sm text-text-primary focus:outline-none focus:border-accent-blue transition-colors"
            >
              {USER_TYPE_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>{o.label}</option>
              ))}
            </select>
          </div>

          {error && (
            <p className="text-xs text-red-400">{error}</p>
          )}

          <button
            type="submit"
            className="mt-1 w-full bg-accent-blue hover:bg-accent-blue/90 text-white font-medium rounded-lg py-2.5 text-sm transition-colors"
          >
            Entrar
          </button>
        </form>
      </div>
    </div>
  )
}
