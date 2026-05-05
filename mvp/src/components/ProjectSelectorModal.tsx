import { useEffect, useState } from 'react'
import { listProjects, type Project } from '../api/projects'
import { ApiError, friendlyError } from '../api/client'
import { clearIdentity, getIdentity, patchIdentity } from '../stores/identity'
import {
  Badge,
  Button,
  CheckIcon,
  ErrorMessage,
  Modal,
  Spinner,
  cn,
} from '../ui'

interface Props {
  open: boolean
  onClose: () => void
}

// Modal de configurações: troca de projeto (lista vinda de /api/aihub/projects, já
// filtrada pelo backend pra esconder projetos admin) + opção de "trocar
// identidade" pra voltar ao onboarding.
export function ProjectSelectorModal({ open, onClose }: Props) {
  const [projects, setProjects] = useState<Project[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Refetch toda vez que abre o modal pra refletir alterações (ex.: novo
  // projeto cadastrado por outro PM).
  useEffect(() => {
    if (!open) return
    let cancelled = false
    setLoading(true)
    setError(null)
    listProjects()
      .then((list) => {
        if (!cancelled) setProjects(list)
      })
      .catch((err: unknown) => {
        if (cancelled) return
        // Em caso de 403 ou mensagem técnica, exibe genérico — PM/PO não
        // precisa saber sobre admin gates.
        if (err instanceof ApiError && err.status === 403) {
          setError('Não foi possível carregar os projetos disponíveis.')
        } else {
          setError(friendlyError(err, 'Não foi possível carregar os projetos.'))
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [open])

  const current = getIdentity()

  const handlePick = (project: Project) => {
    patchIdentity({ projectId: project.id, projectName: project.name })
    onClose()
  }

  const handleSignOut = () => {
    clearIdentity()
    onClose()
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Configurações"
      description="Escolha o projeto em que vai trabalhar."
      footer={
        <div className="flex items-center justify-between text-[11px] text-fg-dim">
          <div>
            <span className="block">{current?.name}</span>
            <span className="block font-mono">conta {current?.account}</span>
          </div>
          <Button variant="ghost" size="sm" onClick={handleSignOut}>
            Trocar identidade
          </Button>
        </div>
      }
    >
      {loading && (
        <div className="flex items-center justify-center py-6 text-fg-muted">
          <Spinner className="h-5 w-5" />
        </div>
      )}

      {error && <ErrorMessage message={error} />}

      {!loading && !error && (
        <div className="max-h-72 space-y-2 overflow-y-auto pr-1">
          {projects.length === 0 && (
            <div className="rounded-lg border border-dashed border-border px-4 py-6 text-center text-sm text-fg-muted">
              Nenhum projeto disponível.
            </div>
          )}
          {projects.map((p) => {
            const active = p.id === current?.projectId
            return (
              <button
                key={p.id}
                type="button"
                onClick={() => handlePick(p)}
                className={cn(
                  'flex w-full items-center justify-between rounded-lg border px-3 py-3 text-left transition',
                  active
                    ? 'border-accent/60 bg-accent-subtle'
                    : 'border-border bg-surface hover:border-border-strong hover:bg-surface-hover',
                )}
              >
                <div className="min-w-0">
                  <div className="text-sm font-medium text-fg">{p.name}</div>
                  {p.description && (
                    <div className="mt-0.5 truncate text-[11px] text-fg-muted">
                      {p.description}
                    </div>
                  )}
                </div>
                {active && (
                  <Badge tone="accent" className="shrink-0">
                    <CheckIcon className="h-3 w-3" />
                    Atual
                  </Badge>
                )}
              </button>
            )
          })}
        </div>
      )}
    </Modal>
  )
}
