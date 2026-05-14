import { useEffect, useMemo, useState } from 'react'
import {
  getAdminUser,
  patchAdminUser,
  setUserProjects,
  type AdminUserDetail,
} from '../../api/admin/users'
import { listProjects, type Project } from '../../api/projects'
import { friendlyError } from '../../api/client'
import { useMe } from '../../stores/me'
import {
  Badge,
  Button,
  CheckIcon,
  ErrorMessage,
  Input,
  Modal,
  Spinner,
  cn,
} from '../../ui'

interface Props {
  userId: string
  onClose: () => void
  onSaved: () => void
}

/**
 * Edita um usuário: lista de projetos vinculados, flag IsAdmin e DisplayName.
 * Promoção pra admin pede confirmação explícita pra evitar acidentes.
 * Rebaixar a si mesmo é bloqueado pra que admin não tranque o próprio acesso.
 */
export function UserMembershipsModal({ userId, onClose, onSaved }: Props) {
  const me = useMe()
  const [detail, setDetail] = useState<AdminUserDetail | null>(null)
  const [projects, setProjects] = useState<Project[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [selectedProjects, setSelectedProjects] = useState<Set<string>>(new Set())
  const [isAdmin, setIsAdmin] = useState(false)
  const [displayName, setDisplayName] = useState('')
  const [saving, setSaving] = useState(false)
  const [confirmingPromotion, setConfirmingPromotion] = useState(false)

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    setError(null)
    Promise.all([getAdminUser(userId), listProjects()])
      .then(([d, ps]) => {
        if (cancelled) return
        setDetail(d)
        setProjects(ps)
        setSelectedProjects(new Set(d.projectIds))
        setIsAdmin(d.isAdmin)
        setDisplayName(d.displayName)
      })
      .catch((err: unknown) => {
        if (cancelled) return
        setError(friendlyError(err, 'Não foi possível carregar o usuário.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [userId])

  const isSelf = !!(me && detail && me.userId === detail.id)
  const wouldDemoteSelf = isSelf && detail?.isAdmin === true && !isAdmin

  const dirty = useMemo(() => {
    if (!detail) return false
    if (isAdmin !== detail.isAdmin) return true
    if (displayName.trim() !== detail.displayName.trim()) return true
    if (selectedProjects.size !== detail.projectIds.length) return true
    for (const id of detail.projectIds) {
      if (!selectedProjects.has(id)) return true
    }
    return false
  }, [detail, isAdmin, displayName, selectedProjects])

  const toggleProject = (projectId: string) => {
    setSelectedProjects((current) => {
      const next = new Set(current)
      if (next.has(projectId)) next.delete(projectId)
      else next.add(projectId)
      return next
    })
  }

  const handleSave = async () => {
    if (!detail) return
    const promotingToAdmin = isAdmin && !detail.isAdmin
    if (promotingToAdmin && !confirmingPromotion) {
      setConfirmingPromotion(true)
      return
    }
    setSaving(true)
    setError(null)
    try {
      const projectsChanged =
        selectedProjects.size !== detail.projectIds.length ||
        detail.projectIds.some((id) => !selectedProjects.has(id))
      if (projectsChanged) {
        await setUserProjects(detail.id, Array.from(selectedProjects))
      }
      const patch: { isAdmin?: boolean; displayName?: string } = {}
      if (isAdmin !== detail.isAdmin) patch.isAdmin = isAdmin
      if (displayName.trim() && displayName.trim() !== detail.displayName.trim()) {
        patch.displayName = displayName.trim()
      }
      if (Object.keys(patch).length > 0) {
        await patchAdminUser(detail.id, patch)
      }
      onSaved()
    } catch (err: unknown) {
      setError(friendlyError(err, 'Não foi possível salvar as alterações.'))
    } finally {
      setSaving(false)
      setConfirmingPromotion(false)
    }
  }

  const projectsExceptDefault = projects.filter((p) => p.id !== 'default')

  return (
    <Modal
      open
      onClose={onClose}
      size="lg"
      title={detail ? `Editar ${detail.displayName}` : 'Editar usuário'}
      description={detail ? `Identificador: ${detail.externalUserId}` : undefined}
      footer={
        <div className="flex items-center justify-between">
          <span className="text-[11px] text-fg-dim">
            {wouldDemoteSelf
              ? 'Você não pode remover seu próprio acesso de administrador.'
              : confirmingPromotion
                ? 'Confirme a promoção para administrador clicando em Salvar novamente.'
                : ''}
          </span>
          <div className="flex gap-2">
            <Button variant="ghost" onClick={onClose}>
              Cancelar
            </Button>
            <Button
              onClick={handleSave}
              disabled={!dirty || saving || loading || wouldDemoteSelf}
              loading={saving}
            >
              {confirmingPromotion ? 'Confirmar promoção' : 'Salvar'}
            </Button>
          </div>
        </div>
      }
    >
      {loading ? (
        <div className="flex justify-center py-10">
          <Spinner />
        </div>
      ) : !detail ? (
        <ErrorMessage message={error ?? 'Usuário não encontrado.'} />
      ) : (
        <div className="space-y-5">
          {error && <ErrorMessage message={error} />}

          <Input
            label="Nome humano"
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            hint="Visível em telas administrativas; default é o próprio identificador."
          />

          <div className="rounded-lg border border-border bg-bg-soft p-3">
            <label className="flex items-start gap-3">
              <input
                type="checkbox"
                checked={isAdmin}
                disabled={wouldDemoteSelf}
                onChange={(e) => {
                  setIsAdmin(e.target.checked)
                  if (!e.target.checked) setConfirmingPromotion(false)
                }}
                className="mt-0.5 h-4 w-4 rounded border-border accent-accent"
              />
              <div>
                <div className="text-sm font-medium text-fg">Administrador</div>
                <p className="text-[11px] text-fg-muted">
                  Administradores enxergam todos os projetos e podem editar
                  vínculos de outros usuários. Promover requer confirmação.
                </p>
              </div>
            </label>
          </div>

          <div>
            <div className="mb-2 flex items-center justify-between">
              <span className="text-xs font-medium text-fg-muted">Projetos vinculados</span>
              <Badge tone="neutral">{selectedProjects.size}</Badge>
            </div>
            {isAdmin ? (
              <div className="rounded-lg border border-dashed border-border px-3 py-4 text-center text-xs text-fg-muted">
                Administradores enxergam todos os projetos do tenant — o vínculo
                explícito é dispensado.
              </div>
            ) : projectsExceptDefault.length === 0 ? (
              <div className="rounded-lg border border-dashed border-border px-3 py-4 text-center text-xs text-fg-muted">
                Nenhum projeto criado neste tenant ainda.
              </div>
            ) : (
              <ul className="divide-y divide-border rounded-lg border border-border">
                {projectsExceptDefault.map((p) => {
                  const selected = selectedProjects.has(p.id)
                  return (
                    <li key={p.id}>
                      <button
                        type="button"
                        onClick={() => toggleProject(p.id)}
                        className={cn(
                          'flex w-full items-center justify-between gap-3 px-3 py-2 text-left transition hover:bg-surface-hover',
                        )}
                      >
                        <div className="min-w-0">
                          <div className="truncate text-sm text-fg">{p.name}</div>
                          <div className="truncate font-mono text-[11px] text-fg-dim">{p.id}</div>
                        </div>
                        <span
                          className={cn(
                            'flex h-5 w-5 shrink-0 items-center justify-center rounded border',
                            selected
                              ? 'border-accent bg-accent text-bg'
                              : 'border-border bg-surface',
                          )}
                        >
                          {selected && <CheckIcon className="h-3 w-3" />}
                        </span>
                      </button>
                    </li>
                  )
                })}
              </ul>
            )}
          </div>
        </div>
      )}
    </Modal>
  )
}
