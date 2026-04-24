import { useState } from 'react'
import {
  usePersona,
  useInvalidatePersona,
  type UserType,
  type ClientPersona,
  type AdminPersona,
} from '../../api/personas'
import { Card } from '../../shared/ui/Card'
import { Input } from '../../shared/ui/Input'
import { Select } from '../../shared/ui/Select'
import { Button } from '../../shared/ui/Button'
import { Badge } from '../../shared/ui/Badge'
import { EmptyState } from '../../shared/ui/EmptyState'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
import { JsonViewer } from '../../shared/data/JsonViewer'

const USER_TYPE_OPTIONS: { value: UserType; label: string }[] = [
  { value: 'cliente', label: 'Cliente' },
  { value: 'admin', label: 'Admin' },
]

export function PersonasAdminPage() {
  // Inputs controlled. Consulta só dispara no clique do botão — não bombardear
  // a API externa a cada keystroke.
  const [userIdDraft, setUserIdDraft] = useState('')
  const [userTypeDraft, setUserTypeDraft] = useState<UserType>('cliente')

  // Valores "aplicados" que disparam a query.
  const [applied, setApplied] = useState<{ userId: string; userType: UserType } | null>(null)

  const query = usePersona(applied?.userId ?? '', applied?.userType ?? 'cliente', applied !== null)
  const invalidate = useInvalidatePersona()

  const [invalidateOpen, setInvalidateOpen] = useState(false)

  const handleConsult = () => {
    const id = userIdDraft.trim()
    if (!id) return
    setApplied({ userId: id, userType: userTypeDraft })
  }

  const handleInvalidateConfirm = async () => {
    if (!applied) return
    try {
      await invalidate.mutateAsync(applied)
      await query.refetch()
    } finally {
      setInvalidateOpen(false)
    }
  }

  const persona = query.data

  return (
    <div className="flex flex-col gap-6 p-6">
      <div>
        <h1 className="text-2xl font-bold text-text-primary">Personas — Debug & Cache</h1>
        <p className="text-sm text-text-muted mt-1">
          Resolve a persona (cliente ou admin) de um usuário passando pelo cache
          (L1 → Redis → API externa). Permite invalidação manual para LGPD/CRM.
        </p>
      </div>

      <Card title="Consultar persona">
        <div className="grid grid-cols-1 md:grid-cols-[1fr_200px_auto] gap-3 items-end">
          <Input
            label="User ID *"
            value={userIdDraft}
            onChange={(e) => setUserIdDraft(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') handleConsult()
            }}
            placeholder="011982329"
          />
          <Select
            label="Tipo de usuário"
            value={userTypeDraft}
            onChange={(e) => setUserTypeDraft(e.target.value as UserType)}
            options={USER_TYPE_OPTIONS}
          />
          <Button
            onClick={handleConsult}
            loading={query.isFetching && query.isLoading}
            disabled={!userIdDraft.trim()}
          >
            Consultar
          </Button>
        </div>
        <p className="text-xs text-text-muted mt-3">
          Cliente vai no endpoint <code className="font-mono">/personas/clientes/{'{userId}'}</code>,
          Admin vai no <code className="font-mono">/personas/admins/{'{userId}'}</code>.
        </p>
      </Card>

      {applied === null && (
        <EmptyState
          title="Nenhuma persona consultada"
          description="Informe o UserId e clique em Consultar."
        />
      )}

      {applied !== null && query.isError && (
        <ErrorCard
          message={`Falha ao consultar persona: ${(query.error as Error)?.message ?? 'erro desconhecido'}`}
          onRetry={() => query.refetch()}
        />
      )}

      {persona && (
        <Card title="Persona resolvida">
          {persona.userType === 'cliente' ? (
            <ClientPersonaView persona={persona} />
          ) : (
            <AdminPersonaView persona={persona} />
          )}

          <details className="mb-4">
            <summary className="cursor-pointer text-xs text-text-muted hover:text-text-primary">
              Ver JSON bruto
            </summary>
            <div className="mt-2">
              <JsonViewer data={persona as unknown as Record<string, unknown>} collapsed={false} />
            </div>
          </details>

          <div className="flex justify-end gap-3 pt-4 border-t border-border-primary">
            <Button
              variant="secondary"
              onClick={() => query.refetch()}
              loading={query.isFetching}
            >
              Recarregar
            </Button>
            <Button variant="danger" onClick={() => setInvalidateOpen(true)}>
              Invalidar cache
            </Button>
          </div>
        </Card>
      )}

      <ConfirmDialog
        open={invalidateOpen}
        onClose={() => setInvalidateOpen(false)}
        title="Invalidar cache da persona?"
        message={
          applied
            ? `Remove a entrada L1 (in-memory) e L2 (Redis) para ${applied.userType}:${applied.userId}. Próxima consulta bate direto na API externa.`
            : ''
        }
        confirmLabel="Invalidar"
        variant="danger"
        loading={invalidate.isPending}
        onConfirm={handleInvalidateConfirm}
      />
    </div>
  )
}


function ClientPersonaView({ persona }: { persona: ClientPersona }) {
  return (
    <>
      <div className="flex items-center gap-2 mb-4 flex-wrap">
        <Badge variant="blue">Cliente</Badge>
        {persona.businessSegment && (
          <Badge variant="purple">Segmento: {persona.businessSegment}</Badge>
        )}
        {persona.suitabilityLevel && (
          <Badge variant="green">Suitability: {persona.suitabilityLevel}</Badge>
        )}
        {persona.country && <Badge variant="gray">{persona.country}</Badge>}
        {persona.isOffshore && <Badge variant="yellow">Offshore</Badge>}
      </div>
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        <FieldRow label="User ID" value={persona.userId} mono />
        <FieldRow label="Client Name" value={persona.clientName} />
        <FieldRow label="Suitability Level" value={persona.suitabilityLevel} />
        <FieldRow label="Business Segment" value={persona.businessSegment} />
        <FieldRow label="Country" value={persona.country} />
        <FieldRow label="Is Offshore" value={persona.isOffshore ? 'sim' : 'não'} />
        <div className="md:col-span-2">
          <FieldRow label="Suitability Description" value={persona.suitabilityDescription} />
        </div>
      </div>
    </>
  )
}

function AdminPersonaView({ persona }: { persona: AdminPersona }) {
  return (
    <>
      <div className="flex items-center gap-2 mb-4 flex-wrap">
        <Badge variant="purple">Admin</Badge>
        {persona.partnerType && <Badge variant="blue">{persona.partnerType}</Badge>}
        {persona.isInternal && <Badge variant="green">Interno</Badge>}
        {persona.isWm && <Badge variant="purple">WM</Badge>}
        {persona.isMaster && <Badge variant="yellow">Master</Badge>}
        {persona.isBroker && <Badge variant="gray">Broker</Badge>}
      </div>
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        <FieldRow label="User ID" value={persona.userId} mono />
        <FieldRow label="Username" value={persona.username} />
        <FieldRow label="Partner Type" value={persona.partnerType} />
        <FieldRow label="Segments" value={persona.segments.join(', ') || null} />
        <FieldRow label="Institutions" value={persona.institutions.join(', ') || null} />
        <FieldRow label="Is Internal" value={persona.isInternal ? 'sim' : 'não'} />
        <FieldRow label="Is WM" value={persona.isWm ? 'sim' : 'não'} />
        <FieldRow label="Is Master" value={persona.isMaster ? 'sim' : 'não'} />
        <FieldRow label="Is Broker" value={persona.isBroker ? 'sim' : 'não'} />
      </div>
    </>
  )
}

function FieldRow({ label, value, mono }: { label: string; value: string | null; mono?: boolean }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs font-medium text-text-muted">{label}</span>
      <span
        className={
          value
            ? mono
              ? 'text-sm font-mono text-text-primary'
              : 'text-sm text-text-primary'
            : 'text-sm text-text-dimmed italic'
        }
      >
        {value ?? '—'}
      </span>
    </div>
  )
}
