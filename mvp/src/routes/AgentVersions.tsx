import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { getAgent, type Agent } from '../api/agents'
import { listAgentVersions, type AgentVersion } from '../api/agentVersions'
import { friendlyError } from '../api/client'
import {
  ArrowLeftIcon,
  Badge,
  Button,
  Card,
  CardHeader,
  ErrorMessage,
  Spinner,
  cn,
} from '../ui'

const MAX_SELECTED = 2

export function AgentVersions() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()

  const [agent, setAgent] = useState<Agent | null>(null)
  const [versions, setVersions] = useState<AgentVersion[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [selected, setSelected] = useState<string[]>([])

  useEffect(() => {
    if (!id) return
    let cancelled = false
    setLoading(true)
    setError(null)
    Promise.all([getAgent(id), listAgentVersions(id)])
      .then(([a, vs]) => {
        if (cancelled) return
        setAgent(a)
        setVersions(vs)
        // Pré-seleciona as duas mais recentes pra comparar de cara.
        if (vs.length >= 2) setSelected([vs[0].agentVersionId, vs[1].agentVersionId])
        else if (vs.length === 1) setSelected([vs[0].agentVersionId])
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(friendlyError(err, 'Não foi possível carregar as versões deste agente.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [id])

  const toggleSelect = (versionId: string) => {
    setSelected((prev) => {
      if (prev.includes(versionId)) return prev.filter((v) => v !== versionId)
      if (prev.length >= MAX_SELECTED) return [prev[1], versionId]
      return [...prev, versionId]
    })
  }

  const compareTarget = useMemo(() => {
    if (selected.length !== 2) return null
    const a = versions.find((v) => v.agentVersionId === selected[0])
    const b = versions.find((v) => v.agentVersionId === selected[1])
    if (!a || !b) return null
    // Ordena por revision asc — esquerda = mais antiga, direita = mais nova.
    return a.revision < b.revision ? { left: a, right: b } : { left: b, right: a }
  }, [selected, versions])

  if (loading) {
    return (
      <Card className="mx-auto max-w-5xl flex items-center justify-center py-12">
        <Spinner className="h-6 w-6 text-fg-muted" />
      </Card>
    )
  }
  if (error) return <ErrorMessage message={error} className="mx-auto max-w-5xl" />

  return (
    <div className="mx-auto max-w-5xl space-y-6">
      <div className="flex items-center gap-3">
        <Button variant="ghost" size="sm" onClick={() => navigate('/agentes?tab=published')} leftIcon={<ArrowLeftIcon className="h-4 w-4" />}>
          Voltar
        </Button>
        <div className="min-w-0 flex-1">
          <h1 className="truncate text-2xl font-semibold tracking-tight">Versões — {agent?.name ?? id}</h1>
          <p className="mt-1 text-xs text-fg-muted">
            Selecione até duas versões para comparar campo a campo.
          </p>
        </div>
      </div>

      {versions.length === 0 ? (
        <Card padded className="text-center">
          <p className="text-sm text-fg-muted">
            Nenhuma versão registrada ainda. Toda aprovação de rascunho gera uma nova versão.
          </p>
        </Card>
      ) : (
        <div className="space-y-2">
          {versions.map((v) => (
            <VersionRow
              key={v.agentVersionId}
              version={v}
              selected={selected.includes(v.agentVersionId)}
              order={selected.indexOf(v.agentVersionId)}
              onToggle={() => toggleSelect(v.agentVersionId)}
            />
          ))}
        </div>
      )}

      {compareTarget && <ComparePanel left={compareTarget.left} right={compareTarget.right} />}
    </div>
  )
}

interface VersionRowProps {
  version: AgentVersion
  selected: boolean
  order: number
  onToggle: () => void
}

function VersionRow({ version, selected, order, onToggle }: VersionRowProps) {
  return (
    <button
      type="button"
      onClick={onToggle}
      className={cn(
        'group flex w-full items-center gap-4 rounded-lg border bg-surface px-4 py-3 text-left transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/40',
        selected ? 'border-accent ring-2 ring-accent/30' : 'border-border hover:border-accent/50',
      )}
    >
      <div
        className={cn(
          'flex h-9 w-9 shrink-0 items-center justify-center rounded-full text-xs font-semibold',
          selected ? 'bg-accent text-accent-contrast' : 'bg-bg-soft text-fg-muted',
        )}
      >
        {selected ? (order === 0 ? 'A' : 'B') : `r${version.revision}`}
      </div>
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <span className="font-mono text-sm font-semibold text-fg">Revisão {version.revision}</span>
          {version.breakingChange ? (
            <Badge tone="warning">Breaking</Badge>
          ) : (
            <Badge tone="success">Patch</Badge>
          )}
          <span className="font-mono text-[10px] uppercase tracking-wider text-fg-dim">
            {version.contentHash.slice(0, 12)}
          </span>
        </div>
        <p className="mt-0.5 text-xs text-fg-muted">
          por <span className="font-mono">{version.createdBy ?? 'system'}</span> · {formatAbsolute(version.createdAt)}
        </p>
        {version.changeReason && (
          <p className="mt-1 line-clamp-2 text-xs text-fg">{version.changeReason}</p>
        )}
      </div>
    </button>
  )
}

interface ComparePanelProps {
  left: AgentVersion
  right: AgentVersion
}

interface DiffEntry {
  label: string
  left: string
  right: string
  multiline?: boolean
}

function ComparePanel({ left, right }: ComparePanelProps) {
  const diffs = useMemo(() => buildDiff(left, right), [left, right])
  const same = diffs.length === 0

  return (
    <Card className="space-y-4">
      <CardHeader
        title={`Comparando r${left.revision} → r${right.revision}`}
        description={
          same
            ? 'Snapshots idênticos no nível de campo.'
            : `${diffs.length} campo${diffs.length === 1 ? '' : 's'} com diferença.`
        }
      />
      {same ? (
        <p className="text-sm text-fg-muted">
          As duas revisões têm o mesmo conteúdo lógico (mesmo ContentHash do ponto de vista dos
          campos versionados).
        </p>
      ) : (
        <div className="space-y-4">
          {diffs.map((d) => (
            <DiffField key={d.label} entry={d} />
          ))}
        </div>
      )}
    </Card>
  )
}

function DiffField({ entry }: { entry: DiffEntry }) {
  return (
    <div className="rounded-lg border border-border">
      <div className="border-b border-border bg-bg-soft px-3 py-1.5 text-[11px] font-semibold uppercase tracking-wider text-fg-muted">
        {entry.label}
      </div>
      <div className="grid grid-cols-1 gap-px bg-border md:grid-cols-2">
        <DiffSide value={entry.left} tone="left" multiline={entry.multiline} />
        <DiffSide value={entry.right} tone="right" multiline={entry.multiline} />
      </div>
    </div>
  )
}

function DiffSide({
  value,
  tone,
  multiline,
}: {
  value: string
  tone: 'left' | 'right'
  multiline?: boolean
}) {
  const empty = value.length === 0
  const bg = tone === 'left' ? 'bg-danger/5' : 'bg-success/5'
  return (
    <div className={cn('px-3 py-2 text-xs', bg)}>
      {empty ? (
        <span className="italic text-fg-dim">∅</span>
      ) : multiline ? (
        <pre className="whitespace-pre-wrap break-words font-mono text-[11px] leading-relaxed text-fg">
          {value}
        </pre>
      ) : (
        <span className="font-mono text-fg">{value}</span>
      )}
    </div>
  )
}

function buildDiff(a: AgentVersion, b: AgentVersion): DiffEntry[] {
  const out: DiffEntry[] = []

  push(out, 'Prompt (instructions)', a.promptContent ?? '', b.promptContent ?? '', true)
  push(out, 'Description', a.description ?? '', b.description ?? '', true)
  push(out, 'Model · deployment', a.model?.deploymentName ?? '', b.model?.deploymentName ?? '')
  push(out, 'Model · temperature', stringify(a.model?.temperature), stringify(b.model?.temperature))
  push(out, 'Model · maxTokens', stringify(a.model?.maxTokens), stringify(b.model?.maxTokens))
  push(out, 'Provider · type', a.provider?.type ?? '', b.provider?.type ?? '')
  push(out, 'Provider · clientType', a.provider?.clientType ?? '', b.provider?.clientType ?? '')
  push(out, 'Provider · endpoint', a.provider?.endpoint ?? '', b.provider?.endpoint ?? '')
  push(out, 'Tools', toolsLabel(a.tools), toolsLabel(b.tools), true)
  push(
    out,
    'Output schema',
    a.outputSchema?.schemaJson ?? a.outputSchema?.schemaName ?? '',
    b.outputSchema?.schemaJson ?? b.outputSchema?.schemaName ?? '',
    true,
  )
  push(out, 'Breaking change', String(a.breakingChange), String(b.breakingChange))
  push(out, 'Change reason', a.changeReason ?? '', b.changeReason ?? '', true)

  return out
}

function push(out: DiffEntry[], label: string, left: string, right: string, multiline = false) {
  if (left === right) return
  out.push({ label, left, right, multiline })
}

function stringify(n: number | null | undefined): string {
  if (n === null || n === undefined) return ''
  return String(n)
}

function toolsLabel(tools: AgentVersion['tools']): string {
  if (!tools || tools.length === 0) return ''
  return tools.map((t) => `${t.type}${t.name ? `:${t.name}` : ''}`).join('\n')
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
