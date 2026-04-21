import { useState, useEffect, useMemo } from 'react'
import { api, tokenApi } from '../../api'
import type { AgentDef, LlmTokenUsage } from '../../types'

function fmtTime(iso: string) {
  const d = new Date(iso)
  return `${d.getDate().toString().padStart(2, '0')}/${(d.getMonth() + 1).toString().padStart(2, '0')} ${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}`
}

type Annotation = '👍' | '👎'

export function LlmOutputsExplorer() {
  const [agents, setAgents] = useState<AgentDef[]>([])
  const [selectedAgent, setSelectedAgent] = useState('')
  const [history, setHistory] = useState<LlmTokenUsage[]>([])
  const [loading, setLoading] = useState(false)
  const [versionFilter, setVersionFilter] = useState('')
  const [expandedId, setExpandedId] = useState<number | null>(null)
  const [annotations, setAnnotations] = useState<Record<number, Annotation>>({})

  useEffect(() => {
    api.getAgents().then(setAgents)
  }, [])

  useEffect(() => {
    if (!selectedAgent) { setHistory([]); return }
    setLoading(true)
    tokenApi.getAgentHistory(selectedAgent, 200)
      .then(setHistory)
      .finally(() => setLoading(false))
  }, [selectedAgent])

  const versions = useMemo(() => {
    const set = new Set<string>()
    for (const h of history) {
      if (h.promptVersionId) set.add(h.promptVersionId)
    }
    return Array.from(set)
  }, [history])

  const filtered = useMemo(() => {
    if (!versionFilter) return history
    return history.filter(h => h.promptVersionId === versionFilter)
  }, [history, versionFilter])

  const annotate = (id: number, val: Annotation) => {
    setAnnotations(prev => ({ ...prev, [id]: prev[id] === val ? undefined as unknown as Annotation : val }))
  }

  const exportJSONL = () => {
    const lines = filtered.map(h => JSON.stringify({
      id: h.id,
      agentId: h.agentId,
      modelId: h.modelId,
      promptVersionId: h.promptVersionId,
      inputTokens: h.inputTokens,
      outputTokens: h.outputTokens,
      durationMs: h.durationMs,
      outputContent: h.outputContent,
      annotation: annotations[h.id] ?? null,
      createdAt: h.createdAt,
    })).join('\n')
    const blob = new Blob([lines], { type: 'application/jsonl;charset=utf-8;' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `llm-outputs-${selectedAgent}-${new Date().toISOString().slice(0, 10)}.jsonl`
    document.body.appendChild(a)
    a.click()
    document.body.removeChild(a)
    URL.revokeObjectURL(url)
  }

  return (
    <div className="p-4 space-y-4">
      {/* Filters */}
      <div className="flex items-center gap-3 flex-wrap">
        <div className="flex-1 min-w-48">
          <div className="text-[10px] text-[#4A6B8A] uppercase tracking-wider mb-1">Agente</div>
          <select
            value={selectedAgent}
            onChange={e => { setSelectedAgent(e.target.value); setVersionFilter('') }}
            className="w-full bg-[#04091A] border border-[#1A3357] rounded-md px-3 py-[7px] text-[13px] text-[#DCE8F5] focus:border-[#254980] focus:outline-none transition-colors appearance-none"
          >
            <option value="">Selecione um agente…</option>
            {agents.map(a => (
              <option key={a.id} value={a.id}>{a.name || a.id}</option>
            ))}
          </select>
        </div>

        {versions.length > 0 && (
          <div className="flex-1 min-w-48">
            <div className="text-[10px] text-[#4A6B8A] uppercase tracking-wider mb-1">Prompt Version</div>
            <select
              value={versionFilter}
              onChange={e => setVersionFilter(e.target.value)}
              className="w-full bg-[#04091A] border border-[#1A3357] rounded-md px-3 py-[7px] text-[13px] text-[#DCE8F5] focus:border-[#254980] focus:outline-none transition-colors appearance-none"
            >
              <option value="">Todas as versões</option>
              {versions.map(v => (
                <option key={v} value={v}>{v}</option>
              ))}
            </select>
          </div>
        )}

        <div className="flex items-end gap-2">
          {filtered.length > 0 && (
            <button
              onClick={exportJSONL}
              className="px-3.5 py-[7px] rounded-md text-[12px] font-medium border border-[#1A3357] text-[#4A6B8A] hover:border-[#254980] hover:text-[#B8CEE5] transition-colors mt-5"
            >
              ↓ Export JSONL
            </button>
          )}
        </div>
      </div>

      {/* Stats bar */}
      {filtered.length > 0 && (
        <div className="flex items-center gap-4 text-[11px] text-[#4A6B8A]">
          <span>{filtered.length} chamadas</span>
          <span>{filtered.filter(h => annotations[h.id] === '👍').length} 👍</span>
          <span>{filtered.filter(h => annotations[h.id] === '👎').length} 👎</span>
          <span className="ml-auto">
            {Math.round(filtered.reduce((s, h) => s + h.totalTokens, 0) / Math.max(filtered.length, 1)).toLocaleString()} avg tokens
          </span>
        </div>
      )}

      {/* Content */}
      {!selectedAgent ? (
        <div className="text-center text-[#3E5F7D] text-sm py-12">Selecione um agente para explorar os outputs</div>
      ) : loading ? (
        <div className="flex items-center justify-center py-16">
          <div className="w-4 h-4 border-[1.5px] border-[#254980] border-t-[#7596B8] rounded-full animate-spin" />
        </div>
      ) : filtered.length === 0 ? (
        <div className="text-center text-[#3E5F7D] text-sm py-12">Nenhum registro encontrado</div>
      ) : (
        <div className="bg-[#04091A] border border-[#0C1D38] rounded-xl overflow-hidden">
          {/* Header */}
          <div className="grid grid-cols-12 px-4 py-2 border-b border-[#0C1D38]">
            <span className="text-[10px] text-[#4A6B8A] uppercase tracking-wider col-span-2">Horário</span>
            <span className="text-[10px] text-[#4A6B8A] uppercase tracking-wider col-span-2">Modelo</span>
            <span className="text-[10px] text-[#4A6B8A] uppercase tracking-wider col-span-2">Version</span>
            <span className="text-[10px] text-[#4A6B8A] uppercase tracking-wider text-right col-span-1">In</span>
            <span className="text-[10px] text-[#4A6B8A] uppercase tracking-wider text-right col-span-1">Out</span>
            <span className="text-[10px] text-[#4A6B8A] uppercase tracking-wider text-right col-span-1">ms</span>
            <span className="text-[10px] text-[#4A6B8A] uppercase tracking-wider col-span-2">Preview</span>
            <span className="text-[10px] text-[#4A6B8A] uppercase tracking-wider text-center col-span-1">Nota</span>
          </div>

          {filtered.map(h => {
            const isExpanded = expandedId === h.id
            const ann = annotations[h.id]
            const preview = h.outputContent
              ? h.outputContent.replace(/\n/g, ' ').slice(0, 60) + (h.outputContent.length > 60 ? '…' : '')
              : '—'

            return (
              <div key={h.id} className="border-b border-[#0C1D38]/50 last:border-b-0">
                <div
                  className="grid grid-cols-12 px-4 py-2.5 hover:bg-[#081529] transition-colors cursor-pointer"
                  onClick={() => setExpandedId(isExpanded ? null : h.id)}
                >
                  <span className="text-[11px] text-[#7596B8] font-mono col-span-2">{fmtTime(h.createdAt)}</span>
                  <span className="text-[11px] text-[#B8CEE5] font-mono col-span-2 truncate pr-1">{h.modelId.split('/').pop()}</span>
                  <span className="col-span-2 pr-1">
                    {h.promptVersionId ? (
                      <span className="px-1 py-0.5 rounded text-[9px] bg-violet-500/10 text-violet-400 border border-violet-500/20 font-mono truncate block max-w-full">
                        {h.promptVersionId}
                      </span>
                    ) : (
                      <span className="text-[11px] text-[#3E5F7D]">—</span>
                    )}
                  </span>
                  <span className="text-[11px] text-[#B8CEE5] font-mono text-right col-span-1">{h.inputTokens.toLocaleString()}</span>
                  <span className="text-[11px] text-[#B8CEE5] font-mono text-right col-span-1">{h.outputTokens.toLocaleString()}</span>
                  <span className="text-[11px] text-[#7596B8] font-mono text-right col-span-1">{Math.round(h.durationMs)}</span>
                  <span className="text-[11px] text-[#4A6B8A] truncate col-span-2 pr-1">{preview}</span>
                  <div className="col-span-1 flex items-center justify-center gap-1" onClick={e => e.stopPropagation()}>
                    <button
                      onClick={() => annotate(h.id, '👍')}
                      className={`text-sm transition-opacity ${ann === '👍' ? 'opacity-100' : 'opacity-30 hover:opacity-70'}`}
                      title="Boa resposta"
                    >👍</button>
                    <button
                      onClick={() => annotate(h.id, '👎')}
                      className={`text-sm transition-opacity ${ann === '👎' ? 'opacity-100' : 'opacity-30 hover:opacity-70'}`}
                      title="Resposta ruim"
                    >👎</button>
                  </div>
                </div>

                {isExpanded && h.outputContent && (
                  <div className="px-4 pb-3 bg-[#04091A]">
                    <div className="text-[10px] text-[#4A6B8A] uppercase tracking-wider mb-1">Output completo</div>
                    <pre className="text-[12px] text-[#B8CEE5] font-mono leading-relaxed whitespace-pre-wrap bg-[#081529] border border-[#0C1D38] rounded-lg p-3 max-h-60 overflow-y-auto">
                      {h.outputContent}
                    </pre>
                  </div>
                )}
                {isExpanded && !h.outputContent && (
                  <div className="px-4 pb-3 text-[11px] text-[#3E5F7D] italic">Output não capturado para este registro</div>
                )}
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}
