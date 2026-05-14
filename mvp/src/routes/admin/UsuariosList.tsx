import { useEffect, useState } from 'react'
import {
  listAdminUsers,
  type AdminUserSummary,
} from '../../api/admin/users'
import { friendlyError } from '../../api/client'
import { useIsAdmin } from '../../stores/me'
import { UserMembershipsModal } from '../../components/admin/UserMembershipsModal'
import {
  Badge,
  Button,
  Card,
  EmptyState,
  ErrorMessage,
  Input,
  SearchIcon,
  Spinner,
  cn,
} from '../../ui'

const PAGE_SIZE = 25

function formatDate(iso: string): string {
  try {
    return new Date(iso).toLocaleString('pt-BR', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    })
  } catch {
    return iso
  }
}

export function UsuariosList() {
  const isAdmin = useIsAdmin()
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [items, setItems] = useState<AdminUserSummary[]>([])
  const [total, setTotal] = useState(0)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [selectedUserId, setSelectedUserId] = useState<string | null>(null)
  const [reloadToken, setReloadToken] = useState(0)

  useEffect(() => {
    if (isAdmin !== true) return
    let cancelled = false
    setLoading(true)
    setError(null)
    listAdminUsers({ search: search.trim() || undefined, page, pageSize: PAGE_SIZE })
      .then((res) => {
        if (cancelled) return
        setItems(res.items)
        setTotal(res.total)
      })
      .catch((err: unknown) => {
        if (cancelled) return
        setError(friendlyError(err, 'Não foi possível carregar a lista de usuários.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [isAdmin, search, page, reloadToken])

  if (isAdmin === null) return null
  if (isAdmin === false) {
    return (
      <Card>
        <EmptyState
          title="Acesso restrito"
          description="Esta área é exclusiva para administradores."
        />
      </Card>
    )
  }

  const lastPage = Math.max(1, Math.ceil(total / PAGE_SIZE))

  return (
    <div className="space-y-5">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">Usuários</h1>
          <p className="mt-1 text-sm text-fg-muted">
            Gerencie quem acessa a plataforma e quais projetos cada pessoa enxerga.
          </p>
        </div>
      </div>

      <Card>
        <div className="mb-4 flex items-center gap-3">
          <Input
            placeholder="Buscar por nome ou identificador"
            value={search}
            onChange={(e) => {
              setPage(1)
              setSearch(e.target.value)
            }}
            leftAddon={<SearchIcon className="h-4 w-4" />}
            className="max-w-sm"
          />
        </div>

        {error && <ErrorMessage message={error} className="mb-3" />}

        {loading ? (
          <div className="flex justify-center py-10">
            <Spinner className="h-6 w-6 text-fg-muted" />
          </div>
        ) : items.length === 0 ? (
          <EmptyState
            title="Nenhum usuário encontrado"
            description="Pessoas aparecem aqui automaticamente após o primeiro acesso à plataforma."
          />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-border text-left text-[11px] uppercase tracking-wider text-fg-dim">
                  <th className="py-2 pr-3 font-medium">Nome</th>
                  <th className="py-2 pr-3 font-medium">Identificador</th>
                  <th className="py-2 pr-3 font-medium">Tipo</th>
                  <th className="py-2 pr-3 font-medium">Perfil</th>
                  <th className="py-2 pr-3 font-medium text-right">Projetos</th>
                  <th className="py-2 pr-3 font-medium">Último acesso</th>
                </tr>
              </thead>
              <tbody>
                {items.map((u) => (
                  <tr
                    key={u.id}
                    onClick={() => setSelectedUserId(u.id)}
                    className={cn(
                      'cursor-pointer border-b border-border/40 transition hover:bg-surface-hover',
                    )}
                  >
                    <td className="py-2 pr-3 text-fg">{u.displayName}</td>
                    <td className="py-2 pr-3 font-mono text-[12px] text-fg-muted">
                      {u.externalUserId}
                    </td>
                    <td className="py-2 pr-3 text-fg-muted">{u.userType}</td>
                    <td className="py-2 pr-3">
                      {u.isAdmin ? (
                        <Badge tone="accent">Admin</Badge>
                      ) : (
                        <Badge tone="neutral">Usuário</Badge>
                      )}
                    </td>
                    <td className="py-2 pr-3 text-right font-mono text-fg">{u.projectCount}</td>
                    <td className="py-2 pr-3 text-fg-muted">{formatDate(u.lastSeenAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {total > PAGE_SIZE && (
          <div className="mt-4 flex items-center justify-between text-xs text-fg-muted">
            <span>
              Página {page} de {lastPage} · {total} usuários no total
            </span>
            <div className="flex gap-2">
              <Button
                variant="ghost"
                disabled={page <= 1}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
              >
                Anterior
              </Button>
              <Button
                variant="ghost"
                disabled={page >= lastPage}
                onClick={() => setPage((p) => Math.min(lastPage, p + 1))}
              >
                Próxima
              </Button>
            </div>
          </div>
        )}
      </Card>

      {selectedUserId && (
        <UserMembershipsModal
          userId={selectedUserId}
          onClose={() => setSelectedUserId(null)}
          onSaved={() => {
            setSelectedUserId(null)
            setReloadToken((t) => t + 1)
          }}
        />
      )}
    </div>
  )
}
