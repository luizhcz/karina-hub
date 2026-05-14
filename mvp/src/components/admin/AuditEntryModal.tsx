import type { AdminAuditEntry } from '../../api/admin/auditLog'
import { Badge, Modal, cn } from '../../ui'

interface Props {
  entry: AdminAuditEntry
  onClose: () => void
}

function formatDate(iso: string): string {
  try {
    return new Date(iso).toLocaleString('pt-BR', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    })
  } catch {
    return iso
  }
}

function prettyJson(value: unknown | null): string {
  if (value === null || value === undefined) return ''
  try {
    return JSON.stringify(value, null, 2)
  } catch {
    return String(value)
  }
}

export function AuditEntryModal({ entry, onClose }: Props) {
  const before = prettyJson(entry.payloadBefore)
  const after = prettyJson(entry.payloadAfter)
  const hasBefore = before.length > 0
  const hasAfter = after.length > 0

  return (
    <Modal
      open
      onClose={onClose}
      size="xl"
      title={
        <span className="font-mono text-[13px]">{entry.action}</span>
      }
      description={`${entry.resourceType} · ${entry.resourceId}`}
    >
      <div className="space-y-5">
        <Section title="Identificação">
          <Pair label="Quando" value={formatDate(entry.timestamp)} />
          <Pair label="Ator" value={entry.actorUserId} mono />
          {entry.actorUserType && <Pair label="Tipo do ator" value={entry.actorUserType} />}
          <Pair label="Projeto" value={entry.projectId ?? '—'} />
          <Pair label="Tenant" value={entry.tenantId ?? '—'} />
          <Pair label="ID do evento" value={String(entry.id)} mono />
        </Section>

        <div
          className={cn(
            'grid grid-cols-1 gap-4',
            hasBefore && hasAfter ? 'md:grid-cols-2' : 'md:grid-cols-1',
          )}
        >
          {hasBefore && (
            <PayloadCard
              title="Antes"
              tone="neutral"
              content={before}
            />
          )}
          {hasAfter && (
            <PayloadCard
              title="Depois"
              tone="accent"
              content={after}
            />
          )}
          {!hasBefore && !hasAfter && (
            <div className="rounded-lg border border-dashed border-border p-6 text-center text-xs text-fg-muted">
              Esta operação não persistiu payload — só metadados.
            </div>
          )}
        </div>
      </div>
    </Modal>
  )
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div>
      <div className="mb-2 text-[10px] font-medium uppercase tracking-widest text-fg-dim">
        {title}
      </div>
      <div className="grid grid-cols-2 gap-x-6 gap-y-2 rounded-lg border border-border bg-bg-soft p-3">
        {children}
      </div>
    </div>
  )
}

function Pair({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div>
      <div className="text-[10px] uppercase tracking-wider text-fg-dim">{label}</div>
      <div className={cn('text-sm text-fg', mono && 'font-mono text-[12px]')}>{value}</div>
    </div>
  )
}

function PayloadCard({
  title,
  tone,
  content,
}: {
  title: string
  tone: 'neutral' | 'accent'
  content: string
}) {
  return (
    <div className="overflow-hidden rounded-lg border border-border">
      <div className="flex items-center justify-between border-b border-border bg-bg-soft px-3 py-2">
        <span className="text-[11px] font-medium uppercase tracking-widest text-fg-muted">
          {title}
        </span>
        <Badge tone={tone}>{title === 'Antes' ? 'before' : 'after'}</Badge>
      </div>
      <pre className="max-h-[420px] overflow-auto bg-surface p-3 font-mono text-[11px] leading-relaxed text-fg">
        {content}
      </pre>
    </div>
  )
}
