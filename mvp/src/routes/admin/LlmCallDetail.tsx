import { useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router'
import { useIsAdmin } from '../../stores/me'
import { friendlyError } from '../../api/client'
import {
  exportLlmCallCurl,
  getLlmCall,
  type LlmCallDetail,
  type PromptSection,
} from '../../api/admin/llmCalls'
import {
  Badge,
  Button,
  Card,
  ErrorMessage,
  Spinner,
  cn,
} from '../../ui'

type Tab = 'messages' | 'chatOptions' | 'response'

// Mapa de cor por origem da seção. Mantém legenda na UI consistente entre turns.
const SOURCE_COLORS: Record<string, { bg: string; label: string }> = {
  'agent.instructions':                { bg: 'bg-accent/15',  label: 'Instructions' },
  'agent.routerIntents':               { bg: 'bg-rose/15',    label: 'Router Intents' },
  'agent.workerScope':                 { bg: 'bg-amber/15',   label: 'Worker Scope' },
  'agent.operationalMemoryInstructions': { bg: 'bg-sky/15',  label: 'Memory Instructions' },
  'persona.system':                    { bg: 'bg-violet/15',  label: 'Persona' },
  'persona.userReinforcement':         { bg: 'bg-violet/15',  label: 'Persona reinforcement' },
  'operationalMemory.preamble':        { bg: 'bg-sky/15',     label: 'Memory preamble' },
  'operationalMemory.state':           { bg: 'bg-sky/25',     label: 'Memory state' },
  'history.user':                      { bg: 'bg-fg-dim/10',  label: 'History user' },
  'history.assistant':                 { bg: 'bg-fg-dim/15',  label: 'History assistant' },
  'input.user':                        { bg: 'bg-emerald/15', label: 'Input atual' },
  'context.metadata':                  { bg: 'bg-bg-soft',    label: 'Context metadata' },
}

function sourceMeta(source: string) {
  return SOURCE_COLORS[source] ?? { bg: 'bg-bg-soft', label: source }
}

function roleBadgeTone(role: string): 'neutral' | 'accent' | 'success' | 'warning' {
  if (role === 'system') return 'neutral'
  if (role === 'user') return 'accent'
  if (role === 'assistant') return 'success'
  return 'warning'
}

export function LlmCallDetailPage() {
  const isAdmin = useIsAdmin()
  const { id } = useParams<{ id: string }>()
  const [detail, setDetail] = useState<LlmCallDetail | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [tab, setTab] = useState<Tab>('messages')
  const [exporting, setExporting] = useState(false)
  const [curlPreview, setCurlPreview] = useState<string | null>(null)

  useEffect(() => {
    if (isAdmin === false || !id) return
    setLoading(true)
    getLlmCall(Number(id))
      .then(setDetail)
      .catch((err) => setError(friendlyError(err, 'Falha ao carregar detalhes.')))
      .finally(() => setLoading(false))
  }, [id, isAdmin])

  const handleCopyCurl = async () => {
    if (!id) return
    setExporting(true)
    try {
      const { command } = await exportLlmCallCurl(Number(id))
      setCurlPreview(command)
      if (typeof navigator !== 'undefined' && navigator.clipboard) {
        await navigator.clipboard.writeText(command)
      }
    } catch (err) {
      setError(friendlyError(err, 'Falha ao gerar comando cURL.'))
    } finally {
      setExporting(false)
    }
  }

  if (isAdmin === false) {
    return (
      <Card padded className="mx-auto max-w-3xl text-center">
        <p className="text-sm text-fg-muted">Acesso restrito a administradores.</p>
      </Card>
    )
  }

  if (loading) {
    return (
      <Card className="mx-auto flex max-w-5xl items-center justify-center py-12">
        <Spinner className="h-6 w-6 text-fg-muted" />
      </Card>
    )
  }

  if (error) return <ErrorMessage message={error} />
  if (!detail) return <p>Não encontrado.</p>

  return (
    <div className="mx-auto max-w-6xl space-y-4">
      <Link to="/admin/llm-calls" className="text-xs text-accent hover:underline">
        ← voltar à lista
      </Link>

      <Card padded className="space-y-3">
        <div className="flex items-start justify-between gap-4">
          <div>
            <h1 className="text-2xl font-semibold">{detail.agentId}</h1>
            <p className="mt-1 text-xs text-fg-muted">
              <span className="font-mono">{detail.providerResolved}/{detail.model}</span> ·{' '}
              {new Date(detail.createdAt).toLocaleString('pt-BR')}
            </p>
          </div>
          <div className="flex gap-2">
            <Button variant="secondary" size="sm" onClick={handleCopyCurl} disabled={exporting}>
              {exporting ? <Spinner className="h-4 w-4" /> : 'Copiar cURL'}
            </Button>
          </div>
        </div>

        <div className="grid grid-cols-2 gap-3 text-xs sm:grid-cols-4">
          <div>
            <p className="text-fg-dim">Intent</p>
            <p className="font-mono">{detail.intent ?? '—'}</p>
          </div>
          <div>
            <p className="text-fg-dim">Status</p>
            <Badge tone={detail.status === 'Completed' ? 'success' : 'danger'}>{detail.status}</Badge>
          </div>
          <div>
            <p className="text-fg-dim">Tokens</p>
            <p className="font-mono">
              {detail.inputTokens} in · {detail.outputTokens} out
              {detail.cachedTokens > 0 && <> · {detail.cachedTokens} cached</>}
            </p>
          </div>
          <div>
            <p className="text-fg-dim">Duração</p>
            <p className="font-mono">{Math.round(detail.durationMs)} ms</p>
          </div>
          <div>
            <p className="text-fg-dim">ExecutionId</p>
            <p className="truncate font-mono" title={detail.executionId ?? ''}>
              {detail.executionId ?? '—'}
            </p>
          </div>
          <div>
            <p className="text-fg-dim">TurnId</p>
            <p className="truncate font-mono">{detail.turnId}</p>
          </div>
          <div>
            <p className="text-fg-dim">Tamanho</p>
            <p className="font-mono">
              {Math.round(detail.requestSizeBytes / 1024)}KB req · {Math.round(detail.responseSizeBytes / 1024)}KB resp
              {detail.truncated && <Badge tone="warning" className="ml-1">truncated</Badge>}
            </p>
          </div>
          <div>
            <p className="text-fg-dim">Project</p>
            <p className="font-mono">{detail.projectId ?? '—'}</p>
          </div>
        </div>
      </Card>

      {curlPreview && (
        <Card padded className="space-y-2">
          <p className="text-xs text-fg-muted">
            cURL copiado pra clipboard. <Badge tone="warning">Grava em admin_audit_log</Badge>
          </p>
          <pre className="max-h-32 overflow-auto rounded-md border border-border bg-bg-soft px-3 py-2 text-[11px] font-mono">
            {curlPreview}
          </pre>
        </Card>
      )}

      <Card>
        <div className="flex border-b border-border">
          {(['messages', 'chatOptions', 'response'] as Tab[]).map((t) => (
            <button
              key={t}
              type="button"
              onClick={() => setTab(t)}
              className={cn(
                'px-4 py-2.5 text-sm font-medium transition focus-visible:outline-none',
                tab === t
                  ? 'border-b-2 border-accent text-accent'
                  : 'text-fg-muted hover:text-fg',
              )}
            >
              {t === 'messages' ? 'Messages' : t === 'chatOptions' ? 'ChatOptions' : 'Response'}
            </button>
          ))}
        </div>

        <div className="p-4">
          {tab === 'messages' && <MessagesTab detail={detail} />}
          {tab === 'chatOptions' && <ChatOptionsTab data={detail.chatOptions} />}
          {tab === 'response' && <ResponseTab data={detail.response} />}
        </div>
      </Card>
    </div>
  )
}

function MessagesTab({ detail }: { detail: LlmCallDetail }) {
  const messages = useMemo(() => {
    if (!Array.isArray(detail.request)) return []
    return detail.request as Array<{ role: string; content: string; type?: string }>
  }, [detail.request])

  const composition = detail.composition ?? []
  const messageSections = composition.filter((s) => s.messageIndex !== null)
  const charSections = composition.filter((s) => s.messageIndex === null && s.charOffset !== null)

  const uniqueSources = Array.from(new Set(composition.map((s) => s.source)))

  return (
    <div className="space-y-3">
      {composition.length > 0 && (
        <div className="rounded-md border border-border bg-bg-soft p-3">
          <p className="mb-2 text-xs font-semibold text-fg-muted">Legenda de provenance</p>
          <div className="flex flex-wrap gap-2 text-[10px]">
            {uniqueSources.map((source) => {
              const meta = sourceMeta(source)
              return (
                <span
                  key={source}
                  className={cn('rounded px-2 py-0.5 font-mono', meta.bg)}
                  title={source}
                >
                  {meta.label}
                </span>
              )
            })}
          </div>
        </div>
      )}

      {messages.map((msg, idx) => {
        const tag = messageSections.find((s) => s.messageIndex === idx)
        const isSystem = msg.role === 'system'
        // Char-range sections atualmente só apareceriam no primeiro system message
        // (skeleton + persona + routerIntents etc.). Outros system messages
        // (mapper metadata, memory) vêm via per-message tracking.
        const inlineSections = isSystem && idx === 0 ? charSections : []
        return (
          <div key={idx} className="rounded-md border border-border">
            <div className="flex items-center justify-between border-b border-border px-3 py-1.5">
              <div className="flex items-center gap-2">
                <Badge tone={roleBadgeTone(msg.role)}>{msg.role}</Badge>
                <span className="font-mono text-[10px] text-fg-dim">msg[{idx}]</span>
                {tag && (
                  <span className={cn('rounded px-2 py-0.5 text-[10px] font-mono', sourceMeta(tag.source).bg)}>
                    {sourceMeta(tag.source).label}
                    {tag.note && <span className="ml-1 text-fg-dim">· {tag.note}</span>}
                  </span>
                )}
              </div>
              <span className="font-mono text-[10px] text-fg-dim">{msg.content?.length ?? 0} chars</span>
            </div>
            <div className="px-3 py-2 text-xs">
              {inlineSections.length > 0 ? (
                <SystemMessageWithHighlights content={msg.content} sections={inlineSections} />
              ) : (
                <pre className="whitespace-pre-wrap font-mono text-[11px] leading-relaxed">
                  {msg.content}
                </pre>
              )}
            </div>
          </div>
        )
      })}
    </div>
  )
}

function SystemMessageWithHighlights({
  content,
  sections,
}: {
  content: string
  sections: PromptSection[]
}) {
  // Constrói intervalos não sobrepostos a partir das sections.
  const sortedSections = [...sections].sort((a, b) => (a.charOffset ?? 0) - (b.charOffset ?? 0))
  const fragments: { text: string; source: string | null }[] = []
  let cursor = 0
  for (const s of sortedSections) {
    const start = s.charOffset ?? 0
    const end = start + (s.charLength ?? 0)
    if (start > cursor) fragments.push({ text: content.substring(cursor, start), source: null })
    fragments.push({ text: content.substring(start, end), source: s.source })
    cursor = Math.max(cursor, end)
  }
  if (cursor < content.length) fragments.push({ text: content.substring(cursor), source: null })

  return (
    <pre className="whitespace-pre-wrap font-mono text-[11px] leading-relaxed">
      {fragments.map((f, i) =>
        f.source ? (
          <span
            key={i}
            className={cn('rounded px-0.5', sourceMeta(f.source).bg)}
            title={`${sourceMeta(f.source).label} (${f.source})`}
          >
            {f.text}
          </span>
        ) : (
          <span key={i}>{f.text}</span>
        ),
      )}
    </pre>
  )
}

function ChatOptionsTab({ data }: { data: unknown }) {
  if (!data || typeof data !== 'object') {
    return <p className="text-xs text-fg-muted">ChatOptions não capturadas neste turno.</p>
  }
  return (
    <pre className="max-h-[60vh] overflow-auto whitespace-pre-wrap rounded-md border border-border bg-bg-soft px-3 py-2 font-mono text-[11px]">
      {JSON.stringify(data, null, 2)}
    </pre>
  )
}

function ResponseTab({ data }: { data: unknown }) {
  return (
    <pre className="max-h-[60vh] overflow-auto whitespace-pre-wrap rounded-md border border-border bg-bg-soft px-3 py-2 font-mono text-[11px]">
      {JSON.stringify(data, null, 2)}
    </pre>
  )
}
