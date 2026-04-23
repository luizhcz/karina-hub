import { useState } from 'react'
import { usePersona, useInvalidatePersona, type UserType } from '../../api/personas'
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
  { value: 'assessor', label: 'Assessor' },
]

export function PersonasAdminPage() {
  // Inputs (controlled). Consulta só dispara no clique do botão —
  // evitamos bombardear a API externa a cada keystroke.
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
      // força refetch imediato com dado fresco da API externa
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
          Resolve a persona de um usuário (passa pelo cache L1 → Redis → API externa) e permite
          invalidação manual do cache para LGPD ou sincronização pós-mudança no CRM.
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
          A chamada passa pelo cache normal — primeira consulta de um userId novo bate na API
          externa, subsequentes retornam do Redis em ~1ms.
        </p>
      </Card>

      {/* Estado inicial */}
      {applied === null && (
        <EmptyState
          title="Nenhuma persona consultada"
          description="Informe o UserId acima e clique em Consultar para ver a persona resolvida."
        />
      )}

      {/* Erro */}
      {applied !== null && query.isError && (
        <ErrorCard
          message={`Falha ao consultar persona: ${(query.error as Error)?.message ?? 'erro desconhecido'}`}
          onRetry={() => query.refetch()}
        />
      )}

      {/* Resultado */}
      {persona && (
        <Card title="Persona resolvida">
          <div className="flex items-center gap-2 mb-4 flex-wrap">
            <Badge variant={persona.segment ? 'blue' : 'gray'}>
              {persona.segment ? `Segment: ${persona.segment}` : 'Sem segment'}
            </Badge>
            <Badge variant={persona.riskProfile ? 'green' : 'gray'}>
              {persona.riskProfile ? `Risk: ${persona.riskProfile}` : 'Sem risk'}
            </Badge>
            {persona.displayName ? (
              <Badge variant="purple">{persona.displayName}</Badge>
            ) : (
              <Badge variant="yellow">Anonymous</Badge>
            )}
            {persona.advisorId && (
              <Badge variant="gray">Advisor: {persona.advisorId}</Badge>
            )}
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mb-4">
            <FieldRow label="User ID" value={persona.userId} mono />
            <FieldRow label="User Type" value={persona.userType} />
            <FieldRow label="Display Name" value={persona.displayName} />
            <FieldRow label="Segment" value={persona.segment} />
            <FieldRow label="Risk Profile" value={persona.riskProfile} />
            <FieldRow label="Advisor ID" value={persona.advisorId} />
          </div>

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
            ? `Remove a entrada L1 (in-memory) e L2 (Redis) para ${applied.userType}:${applied.userId}. A próxima consulta bate direto na API externa.`
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

// Row simples label/valor — evita repetir markup em 6 linhas similares.
// Value null vira "—" cinza (null object pattern no display, consistente com Anonymous).
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
