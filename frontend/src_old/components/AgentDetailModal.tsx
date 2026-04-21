import { useEffect, useRef } from 'react'
import type { AgentDef } from '../types'

interface Props {
  agent: AgentDef | null
  loading: boolean
  onClose: () => void
}

export function AgentDetailModal({ agent, loading, onClose }: Props) {
  const overlayRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', handleKey)
    return () => window.removeEventListener('keydown', handleKey)
  }, [onClose])

  if (!agent && !loading) return null

  return (
    <div
      ref={overlayRef}
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm"
      onClick={e => { if (e.target === overlayRef.current) onClose() }}
    >
      <div className="bg-[#081529] border border-[#1A3357] rounded-xl shadow-2xl w-full max-w-lg max-h-[80vh] overflow-hidden flex flex-col animate-in">
        {loading ? (
          <div className="flex items-center justify-center py-20">
            <div className="w-6 h-6 border-2 border-[#0057E0] border-t-transparent rounded-full animate-spin" />
          </div>
        ) : agent ? (
          <>
            {/* Header */}
            <div className="px-5 py-4 border-b border-[#0C1D38] flex items-start justify-between gap-4 shrink-0">
              <div className="min-w-0">
                <div className="flex items-center gap-2 mb-1">
                  <span className="px-2 py-0.5 rounded bg-blue-900/60 text-blue-300 text-[10px] font-semibold uppercase tracking-wider">
                    Agent
                  </span>
                  <span className="text-[11px] text-[#4A6B8A] font-mono">{agent.id}</span>
                </div>
                <h2 className="text-lg font-semibold text-[#DCE8F5]">{agent.name}</h2>
                {agent.description && (
                  <p className="text-sm text-[#7596B8] mt-1 leading-relaxed">{agent.description}</p>
                )}
              </div>
              <button
                onClick={onClose}
                className="text-[#4A6B8A] hover:text-[#B8CEE5] transition-colors p-1 -mr-1 shrink-0"
              >
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                  <path d="M18 6L6 18M6 6l12 12" />
                </svg>
              </button>
            </div>

            {/* Body */}
            <div className="flex-1 overflow-y-auto px-5 py-4 space-y-5">
              {/* Model */}
              <Section title="Model">
                <div className="grid grid-cols-3 gap-3">
                  <InfoCard label="Deployment" value={agent.model.deploymentName} />
                  <InfoCard label="Temperature" value={agent.model.temperature?.toString() ?? '—'} />
                  <InfoCard label="Max Tokens" value={agent.model.maxTokens?.toLocaleString() ?? '—'} />
                </div>
              </Section>

              {/* Instructions */}
              {agent.instructions && (
                <Section title="Instructions">
                  <pre className="text-xs text-[#B8CEE5] font-mono bg-[#081529] rounded-lg p-3 whitespace-pre-wrap leading-relaxed max-h-48 overflow-y-auto">
                    {agent.instructions}
                  </pre>
                </Section>
              )}

              {/* Tools */}
              {agent.tools && agent.tools.length > 0 && (
                <Section title="Tools" count={agent.tools.length}>
                  <div className="space-y-2">
                    {agent.tools.map((tool, i) => (
                      <div key={i} className="bg-[#0C1D38] rounded-lg px-3 py-2.5 border border-[#1A3357]">
                        <div className="flex items-center gap-2 mb-1">
                          <ToolTypeBadge type={tool.type} />
                          {tool.name && <span className="text-sm text-[#DCE8F5] font-medium font-mono">{tool.name}</span>}
                          {tool.serverLabel && <span className="text-sm text-[#DCE8F5] font-medium">{tool.serverLabel}</span>}
                        </div>
                        {tool.serverUrl && (
                          <div className="text-[11px] text-[#4A6B8A] font-mono truncate">{tool.serverUrl}</div>
                        )}
                        {tool.allowedTools && tool.allowedTools.length > 0 && (
                          <div className="flex flex-wrap gap-1 mt-1.5">
                            {tool.allowedTools.map(t => (
                              <span key={t} className="text-[10px] bg-[#1A3357]/60text-[#B8CEE5] px-1.5 py-0.5 rounded font-mono">{t}</span>
                            ))}
                          </div>
                        )}
                        {tool.requiresApproval && (
                          <span className="text-[10px] text-amber-400 mt-1 block">Requires approval</span>
                        )}
                      </div>
                    ))}
                  </div>
                </Section>
              )}

              {/* Structured Output */}
              {agent.structuredOutput && agent.structuredOutput.responseFormat !== 'text' && (
                <Section title="Structured Output">
                  <div className="grid grid-cols-2 gap-3">
                    <InfoCard label="Format" value={agent.structuredOutput.responseFormat} />
                    {agent.structuredOutput.schemaName && (
                      <InfoCard label="Schema" value={agent.structuredOutput.schemaName} />
                    )}
                  </div>
                  {agent.structuredOutput.schemaDescription && (
                    <p className="text-xs text-[#7596B8] mt-2">{agent.structuredOutput.schemaDescription}</p>
                  )}
                </Section>
              )}

              {/* Metadata */}
              {agent.metadata && Object.keys(agent.metadata).length > 0 && (
                <Section title="Metadata">
                  <div className="grid grid-cols-2 gap-2">
                    {Object.entries(agent.metadata).map(([k, v]) => (
                      <InfoCard key={k} label={k} value={v} />
                    ))}
                  </div>
                </Section>
              )}

              {/* Timestamps */}
              {(agent.createdAt || agent.updatedAt) && (
                <div className="flex gap-4 pt-2 border-t border-[#0C1D38] text-[11px] text-[#4A6B8A]">
                  {agent.createdAt && <span>Created: {new Date(agent.createdAt).toLocaleString()}</span>}
                  {agent.updatedAt && <span>Updated: {new Date(agent.updatedAt).toLocaleString()}</span>}
                </div>
              )}
            </div>
          </>
        ) : null}
      </div>
    </div>
  )
}

function Section({ title, count, children }: { title: string; count?: number; children: React.ReactNode }) {
  return (
    <div>
      <div className="flex items-center gap-2 mb-2">
        <h3 className="text-[11px] uppercase tracking-wider text-[#4A6B8A] font-semibold">{title}</h3>
        {count !== undefined && (
          <span className="text-[10px] bg-[#0C1D38] text-[#7596B8] px-1.5 py-0.5 rounded-full">{count}</span>
        )}
      </div>
      {children}
    </div>
  )
}

function InfoCard({ label, value }: { label: string; value: string }) {
  return (
    <div className="bg-[#0C1D38] rounded-lg px-3 py-2 border border-[#1A3357]">
      <div className="text-[10px] text-[#4A6B8A] uppercase tracking-wider font-medium mb-0.5">{label}</div>
      <div className="text-sm text-[#DCE8F5] font-medium truncate" title={value}>{value}</div>
    </div>
  )
}

const TOOL_COLORS: Record<string, string> = {
  function:         'bg-[#0057E0]/20 text-[#7AACFF]',
  mcp:              'bg-cyan-900/60 text-cyan-300',
  code_interpreter: 'bg-amber-900/60 text-amber-300',
  file_search:      'bg-emerald-900/60 text-emerald-300',
}

function ToolTypeBadge({ type }: { type: string }) {
  return (
    <span className={`text-[10px] font-semibold uppercase tracking-wider px-1.5 py-0.5 rounded ${TOOL_COLORS[type] ?? 'bg-[#1A3357] text-[#B8CEE5]'}`}>
      {type}
    </span>
  )
}
