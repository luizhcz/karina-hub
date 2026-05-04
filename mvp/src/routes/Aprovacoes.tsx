import { useEffect, useMemo, useState } from 'react'
import {
  approveAgentDraft,
  getDraftApprovalHistory,
  listAgentApprovals,
  rejectAgentDraft,
  type AgentApprovalsStatusFilter,
  type DraftApprovalHistoryEntry,
} from '../api/agentApprovals'
import type { AgentDraft } from '../api/agentDrafts'
import { ApiError, friendlyError } from '../api/client'
import {
  AgentIcon,
  Badge,
  Button,
  Card,
  CardHeader,
  CheckIcon,
  ErrorMessage,
  Input,
  Modal,
  SearchIcon,
  Spinner,
  Textarea,
  cn,
} from '../ui'

type Tab = 'pending' | 'rejected'

const TAB_FILTER: Record<Tab, AgentApprovalsStatusFilter> = {
  pending: 'pending',
  rejected: 'rejected',
}

export function Aprovacoes() {
  const [activeTab, setActiveTab] = useState<Tab>('pending')
  const [drafts, setDrafts] = useState<AgentDraft[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')

  const [selected, setSelected] = useState<AgentDraft | null>(null)

  const reload = async (tab: Tab) => {
    setLoading(true)
    setError(null)
    try {
      const list = await listAgentApprovals(TAB_FILTER[tab])
      setDrafts(list)
    } catch (err) {
      if (err instanceof ApiError && err.status === 403) {
        setError(
          'Esta tela é restrita a administradores. Se você precisa revisar agentes, fale com o time de governança.',
        )
        setDrafts([])
      } else {
        setError(friendlyError(err, 'Não foi possível carregar a fila de aprovações.'))
      }
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void reload(activeTab)
  }, [activeTab])

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase()
    if (!q) return drafts
    return drafts.filter((d) => {
      const name = (d.name || d.payload?.name || '').toLowerCase()
      const desc = (d.payload?.description ?? '').toLowerCase()
      return name.includes(q) || desc.includes(q) || d.id.toLowerCase().includes(q)
    })
  }, [drafts, search])

  const handleResolved = async () => {
    setSelected(null)
    await reload(activeTab)
  }

  return (
    <div className="mx-auto max-w-6xl">
      <div className="mb-8">
        <h1 className="text-[28px] font-semibold tracking-tight">Aprovações</h1>
        <p className="mt-2 text-sm text-fg-muted">
          Revisão de rascunhos submetidos pelos times. Aprovar promove a uma versão publicada do agente; rejeitar
          devolve com feedback.
        </p>
      </div>

      <div className="mb-6 flex items-center gap-1 border-b border-border">
        <TabButton
          active={activeTab === 'pending'}
          label="Pendentes"
          onClick={() => setActiveTab('pending')}
        />
        <TabButton
          active={activeTab === 'rejected'}
          label="Rejeitados"
          onClick={() => setActiveTab('rejected')}
        />
      </div>

      <div className="mb-6 max-w-md">
        <Input
          placeholder="Buscar por nome, descrição ou id…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          leftAddon={<SearchIcon className="h-4 w-4" />}
        />
      </div>

      {loading ? (
        <Card className="flex items-center justify-center py-12">
          <Spinner className="h-6 w-6 text-fg-muted" />
        </Card>
      ) : error ? (
        <ErrorMessage message={error} />
      ) : filtered.length === 0 ? (
        <Card padded className="text-center">
          <p className="text-sm text-fg-muted">
            {drafts.length === 0
              ? activeTab === 'pending'
                ? 'Nenhum rascunho aguardando aprovação.'
                : 'Nenhum rascunho rejeitado.'
              : 'Nada bate com a busca. Tente ajustar o termo.'}
          </p>
        </Card>
      ) : (
        <div className="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3">
          {filtered.map((d) => (
            <DraftCard key={d.id} draft={d} onClick={() => setSelected(d)} />
          ))}
        </div>
      )}

      <ReviewModal draft={selected} onClose={() => setSelected(null)} onResolved={handleResolved} />
    </div>
  )
}

interface TabButtonProps {
  active: boolean
  label: string
  onClick: () => void
}

function TabButton({ active, label, onClick }: TabButtonProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'relative flex items-center gap-2 px-4 py-2.5 text-sm font-medium transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/30',
        active ? 'text-fg' : 'text-fg-muted hover:text-fg',
      )}
    >
      {label}
      {active && <span className="absolute inset-x-0 bottom-0 h-0.5 bg-accent" aria-hidden="true" />}
    </button>
  )
}

interface DraftCardProps {
  draft: AgentDraft
  onClick: () => void
}

function DraftCard({ draft, onClick }: DraftCardProps) {
  const display = draft.name || draft.payload?.name || 'Rascunho sem nome'
  const description = draft.payload?.description ?? ''
  const submittedAt = draft.submittedAt ?? draft.updatedAt
  return (
    <Card
      interactive
      padded={false}
      role="button"
      tabIndex={0}
      onClick={onClick}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault()
          onClick()
        }
      }}
      className={cn(
        'group relative flex min-h-[180px] cursor-pointer flex-col gap-3 overflow-hidden p-5',
        'before:absolute before:inset-y-0 before:left-0 before:w-1',
        draft.status === 'Rejected' ? 'before:bg-warning' : 'before:bg-accent',
      )}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="flex min-w-0 items-center gap-3">
          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-accent-subtle text-accent">
            <AgentIcon className="h-5 w-5" />
          </div>
          <h3 className="min-w-0 truncate text-sm font-semibold text-fg">{display}</h3>
        </div>
        <Badge tone={draft.status === 'Rejected' ? 'warning' : 'accent'}>
          {draft.status === 'Rejected' ? 'Rejeitado' : 'Aguardando'}
        </Badge>
      </div>

      <p className="line-clamp-3 text-xs text-fg-muted">
        {description || <span className="italic text-fg-dim">sem descrição</span>}
      </p>

      <div className="mt-auto flex items-center justify-between text-[11px] text-fg-dim">
        <div className="flex items-center gap-2">
          {draft.isEditDraft && <Badge>edição</Badge>}
          <span>submetido {formatRelative(submittedAt)}</span>
        </div>
        <span className="opacity-0 transition group-hover:opacity-100">Revisar →</span>
      </div>
    </Card>
  )
}

interface ReviewModalProps {
  draft: AgentDraft | null
  onClose: () => void
  onResolved: () => void | Promise<void>
}

function ReviewModal({ draft, onClose, onResolved }: ReviewModalProps) {
  const [history, setHistory] = useState<DraftApprovalHistoryEntry[]>([])
  const [historyLoading, setHistoryLoading] = useState(false)

  const [approving, setApproving] = useState(false)
  const [changeReason, setChangeReason] = useState('')
  const [approveError, setApproveError] = useState<string | null>(null)

  const [rejectMode, setRejectMode] = useState(false)
  const [rejecting, setRejecting] = useState(false)
  const [feedback, setFeedback] = useState('')
  const [rejectError, setRejectError] = useState<string | null>(null)

  useEffect(() => {
    if (!draft) return
    setApproveError(null)
    setRejectError(null)
    setRejectMode(false)
    setChangeReason('')
    setFeedback('')

    let cancelled = false
    setHistoryLoading(true)
    getDraftApprovalHistory(draft.id)
      .then((entries) => {
        if (!cancelled) setHistory(entries)
      })
      .catch(() => {
        if (!cancelled) setHistory([])
      })
      .finally(() => {
        if (!cancelled) setHistoryLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [draft])

  if (!draft) return null

  const busy = approving || rejecting

  const onApprove = async () => {
    setApproving(true)
    setApproveError(null)
    try {
      await approveAgentDraft(draft.id, { changeReason: changeReason.trim() || null })
      await onResolved()
    } catch (err) {
      setApproveError(friendlyError(err, 'Não foi possível aprovar este rascunho.'))
    } finally {
      setApproving(false)
    }
  }

  const onReject = async () => {
    if (feedback.trim().length < 10) {
      setRejectError('O feedback é obrigatório e precisa ter pelo menos 10 caracteres.')
      return
    }
    setRejecting(true)
    setRejectError(null)
    try {
      await rejectAgentDraft(draft.id, { feedback: feedback.trim() })
      await onResolved()
    } catch (err) {
      setRejectError(friendlyError(err, 'Não foi possível rejeitar este rascunho.'))
    } finally {
      setRejecting(false)
    }
  }

  const isPending = draft.status === 'PendingApproval'
  const isRejected = draft.status === 'Rejected'

  return (
    <Modal
      open
      onClose={busy ? () => undefined : onClose}
      size="lg"
      title={draft.name || draft.payload?.name || draft.id}
      description={
        draft.isEditDraft
          ? `Edição do agente ${draft.baseAgentId} (rev. ${draft.baseRevision ?? '?'})`
          : 'Novo agente'
      }
    >
      <div className="space-y-5">
        <div className="flex flex-wrap items-center gap-2">
          <Badge tone={isRejected ? 'warning' : 'accent'}>
            {isRejected ? 'Rejeitado' : 'Aguardando aprovação'}
          </Badge>
          {draft.isEditDraft && <Badge>edição</Badge>}
          {draft.createdBy && (
            <span className="text-[11px] text-fg-dim">
              autor: <span className="font-mono text-fg">{draft.createdBy}</span>
            </span>
          )}
          {draft.submittedAt && (
            <span className="text-[11px] text-fg-dim">
              submetido em {formatAbsolute(draft.submittedAt)}
            </span>
          )}
        </div>

        <PayloadPreview draft={draft} />

        {isRejected && draft.rejectionFeedback && (
          <Card padded className="border-warning/40 bg-warning/5">
            <CardHeader title="Feedback de rejeição anterior" />
            <p className="mt-2 whitespace-pre-wrap text-xs text-fg">{draft.rejectionFeedback}</p>
          </Card>
        )}

        <Card padded className="space-y-2">
          <CardHeader title="Histórico do rascunho" />
          {historyLoading ? (
            <div className="flex items-center justify-center py-4">
              <Spinner className="h-5 w-5 text-fg-muted" />
            </div>
          ) : history.length === 0 ? (
            <p className="text-xs text-fg-muted">Sem eventos registrados.</p>
          ) : (
            <ol className="relative space-y-2 border-l border-border pl-4">
              {history.map((e) => (
                <li key={e.id} className="text-xs">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="font-semibold text-fg">{e.action}</span>
                    {e.tier && <Badge tone={e.tier === 'Cosmetic' ? 'success' : 'accent'}>{e.tier}</Badge>}
                    <span className="text-fg-dim">{formatAbsolute(e.occurredAt)}</span>
                  </div>
                  <p className="mt-0.5 text-fg-muted">
                    por <span className="font-mono text-fg">{e.actorUserId}</span>
                  </p>
                  {e.feedback && (
                    <p className="mt-1 whitespace-pre-wrap rounded-md border border-border bg-bg-soft px-2 py-1 text-fg">
                      {e.feedback}
                    </p>
                  )}
                </li>
              ))}
            </ol>
          )}
        </Card>

        {isPending && !rejectMode && (
          <div className="space-y-2">
            <label className="block text-xs font-medium text-fg-muted">Motivo da aprovação (opcional)</label>
            <Textarea
              value={changeReason}
              onChange={(e) => setChangeReason(e.target.value)}
              placeholder="Ex.: validado com a área de risco, sem impacto comportamental…"
              rows={2}
              disabled={busy}
            />
            <p className="text-[11px] text-fg-dim">
              Fica registrado no histórico junto com o seu nome de admin.
            </p>
          </div>
        )}

        {isPending && rejectMode && (
          <div className="space-y-2">
            <label className="block text-xs font-medium text-fg-muted">
              Feedback obrigatório (mínimo 10 caracteres)
            </label>
            <Textarea
              value={feedback}
              onChange={(e) => setFeedback(e.target.value)}
              placeholder="Explique o que precisa ser ajustado pra que o time autor possa corrigir…"
              rows={3}
              disabled={busy}
            />
            <div className="flex items-center justify-between text-[11px] text-fg-dim">
              <span>{feedback.trim().length}/10 caracteres mínimos</span>
            </div>
          </div>
        )}

        {approveError && <ErrorMessage message={approveError} />}
        {rejectError && <ErrorMessage message={rejectError} />}

        {isPending && (
          <div className="flex flex-wrap items-center justify-end gap-2">
            {!rejectMode ? (
              <>
                <Button variant="ghost" onClick={onClose} disabled={busy}>
                  Fechar
                </Button>
                <Button variant="danger" onClick={() => setRejectMode(true)} disabled={busy}>
                  Rejeitar
                </Button>
                <Button onClick={onApprove} loading={approving} leftIcon={<CheckIcon className="h-4 w-4" />}>
                  Aprovar
                </Button>
              </>
            ) : (
              <>
                <Button variant="ghost" onClick={() => setRejectMode(false)} disabled={busy}>
                  Voltar
                </Button>
                <Button variant="danger" onClick={onReject} loading={rejecting}>
                  Confirmar rejeição
                </Button>
              </>
            )}
          </div>
        )}

        {isRejected && (
          <div className="flex justify-end">
            <Button variant="ghost" onClick={onClose}>
              Fechar
            </Button>
          </div>
        )}
      </div>
    </Modal>
  )
}

function PayloadPreview({ draft }: { draft: AgentDraft }) {
  const p = draft.payload || {}
  const model = p.model as { deploymentName?: string; predefinedModelId?: string | null } | undefined
  const tools = (p.tools as Array<{ type: string; name?: string | null; genericToolId?: string | null; mcpServerId?: string | null }>) || []
  const visibility = (p as { visibility?: string }).visibility ?? '—'
  const enabled = (p as { enabled?: boolean }).enabled
  const instructions = p.instructions ?? ''

  return (
    <Card padded className="space-y-3">
      <CardHeader title="Conteúdo do rascunho" description="Snapshot do payload enviado pelo time autor." />
      <dl className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <Field label="Nome" value={(p.name as string) || draft.name || '—'} />
        <Field label="Visibilidade" value={visibility} />
        <Field
          label="Modelo"
          value={model?.predefinedModelId || model?.deploymentName || <span className="italic text-fg-dim">não definido</span>}
        />
        <Field label="Habilitado ao publicar" value={enabled === false ? 'não' : 'sim'} />
        <Field label="Ferramentas" value={tools.length === 0 ? <span className="italic text-fg-dim">nenhuma</span> : `${tools.length} ferramenta${tools.length === 1 ? '' : 's'}`} />
        <Field
          label="Descrição"
          value={(p.description as string) || <span className="italic text-fg-dim">sem descrição</span>}
        />
      </dl>

      {tools.length > 0 && (
        <div>
          <p className="text-[11px] uppercase tracking-wider text-fg-dim">Tools referenciadas</p>
          <ul className="mt-1 space-y-1">
            {tools.map((t, i) => (
              <li key={i} className="font-mono text-[11px] text-fg-muted">
                <span className="font-semibold text-fg">{t.type}</span>
                {t.genericToolId ? ` · ${t.genericToolId}` : t.mcpServerId ? ` · ${t.mcpServerId}` : t.name ? ` · ${t.name}` : ''}
              </li>
            ))}
          </ul>
        </div>
      )}

      <div>
        <p className="text-[11px] uppercase tracking-wider text-fg-dim">Instruções</p>
        <pre className="mt-1 max-h-64 overflow-y-auto whitespace-pre-wrap rounded-md border border-border bg-bg-soft px-3 py-2 font-mono text-[11px] leading-relaxed text-fg">
          {instructions || <span className="italic text-fg-dim">sem instruções</span>}
        </pre>
      </div>
    </Card>
  )
}

function Field({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div>
      <dt className="text-[11px] uppercase tracking-wider text-fg-dim">{label}</dt>
      <dd className="mt-0.5 text-sm text-fg">{value}</dd>
    </div>
  )
}

function formatAbsolute(iso: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return iso
  return date.toLocaleString('pt-BR', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function formatRelative(iso: string | null | undefined): string {
  if (!iso) return ''
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return ''
  const diffMs = Date.now() - date.getTime()
  const minutes = Math.round(diffMs / 60_000)
  if (minutes < 1) return 'agora'
  if (minutes < 60) return `há ${minutes} min`
  const hours = Math.round(minutes / 60)
  if (hours < 24) return `há ${hours} h`
  const days = Math.round(hours / 24)
  if (days < 7) return `há ${days} d`
  return date.toLocaleDateString('pt-BR')
}
