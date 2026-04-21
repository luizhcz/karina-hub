import { useState, useEffect, useCallback, useRef } from 'react'
import { api, promptApi, adminApi, interactionsApi, pricingApi, chatApi, sessionApi, tokenApi } from '../api'
import type { AgentDef, WorkflowDef, AgentPromptVersion, ConversationSession, HumanInteraction, ModelPricing, CreateModelPricingRequest, ChatMsg } from '../types'
import { CreateAgentForm } from './CreateAgentForm'
import { CreateWorkflowForm } from './CreateWorkflowForm'

type Selection =
  | { kind: 'workflow'; item: WorkflowDef }
  | { kind: 'agent'; item: AgentDef }
  | { kind: 'create-agent' }
  | { kind: 'edit-agent'; item: AgentDef }
  | { kind: 'create-workflow' }
  | { kind: 'edit-workflow'; item: WorkflowDef }
  | { kind: 'agent-prompts'; item: AgentDef }
  | { kind: 'agent-playground'; item: AgentDef }
  | { kind: 'conversations' }
  | { kind: 'hitl' }
  | { kind: 'pricing' }

/* ════════════════════════════════════════════════════════════════════════════
   AdminPanel — Vercel-inspired design
   ════════════════════════════════════════════════════════════════════════════ */

export function AdminPanel() {
  const [workflows, setWorkflows] = useState<WorkflowDef[]>([])
  const [agents, setAgents] = useState<AgentDef[]>([])
  const [selected, setSelected] = useState<Selection | null>(null)
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [deleteTarget, setDeleteTarget] = useState<AgentDef | null>(null)
  const [deleting, setDeleting] = useState(false)

  const refreshAgents = useCallback(() => {
    api.getAgents().then(setAgents)
  }, [])

  const refreshWorkflows = useCallback(() => {
    api.getWorkflows().then(setWorkflows)
  }, [])

  const handleDelete = useCallback(async (agent: AgentDef) => {
    setDeleting(true)
    try {
      await api.deleteAgent(agent.id)
      setDeleteTarget(null)
      refreshAgents()
      setSelected(prev => {
        if (prev && 'item' in prev && (prev as { item?: { id?: string } }).item?.id === agent.id) return null
        return prev
      })
    } catch {
      /* keep modal open on error */
    } finally {
      setDeleting(false)
    }
  }, [refreshAgents])

  useEffect(() => {
    Promise.all([api.getWorkflows(), api.getAgents()])
      .then(([wfs, ags]) => {
        setWorkflows(wfs)
        setAgents(ags)
        if (wfs.length > 0) setSelected({ kind: 'workflow', item: wfs[0] })
        else if (ags.length > 0) setSelected({ kind: 'agent', item: ags[0] })
      })
      .finally(() => setLoading(false))
  }, [])

  const q = search.toLowerCase()
  const fWf = q ? workflows.filter(w => w.name.toLowerCase().includes(q) || w.id.toLowerCase().includes(q)) : workflows
  const fAg = q ? agents.filter(a => a.name.toLowerCase().includes(q) || a.id.toLowerCase().includes(q)) : agents

  return (
    <div className="flex flex-1 overflow-hidden" style={{ minHeight: 0 }}>
      {/* ── Nav ──────────────────────────────────────────────── */}
      <nav
        className="flex flex-col overflow-hidden bg-[#04091A] border-r border-[#0C1D38]"
        style={{ width: 280, minWidth: 280, maxWidth: 280 }}
      >
        {/* Search */}
        <div className="p-3 border-b border-[#0C1D38]">
          <div className="relative">
            <svg className="absolute left-2.5 top-1/2 -translate-y-1/2 text-[#444]" width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><circle cx="11" cy="11" r="7" /><path d="M21 21l-4.35-4.35" /></svg>
            <input
              value={search}
              onChange={e => setSearch(e.target.value)}
              placeholder="Search..."
              className="w-full bg-[#081529] border border-[#1A3357] rounded-md pl-8 pr-3 py-[7px] text-[13px] text-[#DCE8F5] placeholder:text-[#3E5F7D] focus:border-[#254980] focus:outline-none transition-colors"
            />
          </div>
        </div>

        {/* List */}
        <div className="flex-1 overflow-y-auto" style={{ scrollbarWidth: 'none' }}>
          {loading ? (
            <div className="flex items-center justify-center h-24">
              <div className="w-4 h-4 border-[1.5px] border-[#254980] border-t-[#7596B8] rounded-full animate-spin" />
            </div>
          ) : (
            <>
              <NavGroup label="Workflows" count={fWf.length}>
                {fWf.map(wf => (
                  <NavItem
                    key={wf.id}
                    active={selected?.kind === 'workflow' && selected.item.id === wf.id}
                    onClick={() => setSelected({ kind: 'workflow', item: wf })}
                    label={wf.name}
                    meta={wf.orchestrationMode}
                    dot={wf.configuration?.inputMode === 'Chat' ? '#3291ff' : '#4A6B8A'}
                  />
                ))}
              </NavGroup>

              <NavGroup label="Agents" count={fAg.length}>
                {fAg.map(ag => (
                  <NavItem
                    key={ag.id}
                    active={selected?.kind === 'agent' && selected.item.id === ag.id}
                    onClick={() => setSelected({ kind: 'agent', item: ag })}
                    label={ag.name}
                    meta={ag.model.deploymentName}
                    dot={ag.provider?.type === 'OpenAI' ? '#50e3c2' : '#4A6B8A'}
                  />
                ))}
              </NavGroup>

              <NavGroup label="Conversations" count={1}>
                <NavFlatItem
                  active={selected?.kind === 'conversations'}
                  onClick={() => setSelected({ kind: 'conversations' })}
                  label="Conversations"
                  dot="#3291ff"
                />
              </NavGroup>

              <NavGroup label="Operations" count={2}>
                <NavFlatItem
                  active={selected?.kind === 'hitl'}
                  onClick={() => setSelected({ kind: 'hitl' })}
                  label="HITL Interactions"
                  dot="#f59e0b"
                />
                <NavFlatItem
                  active={selected?.kind === 'pricing'}
                  onClick={() => setSelected({ kind: 'pricing' })}
                  label="Model Pricing"
                  dot="#50e3c2"
                />
              </NavGroup>
            </>
          )}
        </div>

        {/* Bottom actions */}
        <div className="p-3 border-t border-[#0C1D38] space-y-1.5">
          <GhostBtn label="New Workflow" onClick={() => setSelected({ kind: 'create-workflow' })} />
          <GhostBtn label="New Agent" onClick={() => setSelected({ kind: 'create-agent' })} />
        </div>
      </nav>

      {/* ── Detail ──────────────────────────────────────────── */}
      <main className="flex-1 min-w-0 overflow-y-auto bg-[#04091A]" style={{ scrollbarWidth: 'none' }}>
        {!selected ? (
          <div className="flex flex-col items-center justify-center h-full gap-3">
            <p className="text-[13px] text-[#4A6B8A]">Select an item</p>
          </div>
        ) : selected.kind === 'create-agent' ? (
          <CreateAgentForm
            onSaved={(newAgent) => {
              refreshAgents()
              setSelected({ kind: 'agent', item: newAgent })
            }}
            onCancel={() => setSelected(agents.length > 0 ? { kind: 'agent', item: agents[0] } : null)}
          />
        ) : selected.kind === 'edit-agent' ? (
          <CreateAgentForm
            agent={selected.item}
            onSaved={(updated) => {
              refreshAgents()
              setSelected({ kind: 'agent', item: updated })
            }}
            onCancel={() => setSelected({ kind: 'agent', item: selected.item })}
          />
        ) : selected.kind === 'create-workflow' ? (
          <CreateWorkflowForm
            agents={agents}
            onSaved={(wf) => {
              refreshWorkflows()
              setSelected({ kind: 'workflow', item: wf })
            }}
            onCancel={() => setSelected(workflows.length > 0 ? { kind: 'workflow', item: workflows[0] } : null)}
          />
        ) : selected.kind === 'edit-workflow' ? (
          <CreateWorkflowForm
            workflow={selected.item}
            agents={agents}
            onSaved={(wf) => {
              refreshWorkflows()
              setSelected({ kind: 'workflow', item: wf })
            }}
            onCancel={() => setSelected({ kind: 'workflow', item: selected.item })}
          />
        ) : selected.kind === 'workflow' ? (
          <WorkflowDetail wf={selected.item} agents={agents} onEdit={() => setSelected({ kind: 'edit-workflow', item: selected.item })} />
        ) : selected.kind === 'agent-prompts' ? (
          <AgentPromptsView
            agent={selected.item}
            onBack={() => setSelected({ kind: 'agent', item: selected.item })}
          />
        ) : selected.kind === 'agent-playground' ? (
          <AgentPlaygroundView
            agent={selected.item}
            onBack={() => setSelected({ kind: 'agent', item: selected.item })}
          />
        ) : selected.kind === 'conversations' ? (
          <ConversationsAdmin />
        ) : selected.kind === 'hitl' ? (
          <HitlAdmin />
        ) : selected.kind === 'pricing' ? (
          <PricingAdmin />
        ) : (
          <AgentDetail
            agent={selected.item}
            onEdit={() => setSelected({ kind: 'edit-agent', item: selected.item })}
            onDelete={() => setDeleteTarget(selected.item)}
            onPrompts={() => setSelected({ kind: 'agent-prompts', item: selected.item })}
            onPlayground={() => setSelected({ kind: 'agent-playground', item: selected.item })}
          />
        )}
      </main>

      {/* Delete confirmation modal */}
      {deleteTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60">
          <div className="bg-[#081529] border border-[#1A3357] rounded-lg p-6 max-w-sm w-full mx-4">
            <p className="text-[14px] text-[#DCE8F5] mb-1">Delete agent?</p>
            <p className="text-[12px] text-[#4A6B8A] mb-1">
              This will permanently remove <span className="text-[#999] font-medium">{deleteTarget.name}</span>.
            </p>
            <p className="text-[11px] text-[#3E5F7D] font-mono mb-5">{deleteTarget.id}</p>
            <div className="flex gap-2 justify-end">
              <button
                onClick={() => setDeleteTarget(null)}
                disabled={deleting}
                className="px-3.5 py-[6px] rounded-md text-[12px] font-medium border border-[#1A3357] text-[#999] hover:border-[#254980] hover:text-[#B8CEE5] transition-colors disabled:opacity-50"
              >
                Cancel
              </button>
              <button
                onClick={() => handleDelete(deleteTarget)}
                disabled={deleting}
                className="px-3.5 py-[6px] rounded-md text-[12px] font-medium bg-[#ff4444] text-white hover:bg-[#e03030] transition-colors disabled:opacity-50 flex items-center gap-2"
              >
                {deleting && <div className="w-3 h-3 border-[1.5px] border-white/30 border-t-white rounded-full animate-spin" />}
                Delete
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

/* ── Conversations Admin ─────────────────────────────────────────────────────── */

function ConversationsAdmin() {
  const [items, setItems] = useState<ConversationSession[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [loading, setLoading] = useState(true)
  const [userIdFilter, setUserIdFilter] = useState('')
  const [workflowIdFilter, setWorkflowIdFilter] = useState('')
  const [deletingId, setDeletingId] = useState<string | null>(null)
  const [expandedId, setExpandedId] = useState<string | null>(null)
  const [messages, setMessages] = useState<ChatMsg[]>([])
  const [messagesLoading, setMessagesLoading] = useState(false)

  const pageSize = 50

  const load = useCallback(async (p: number, userId: string, workflowId: string) => {
    setLoading(true)
    try {
      const params: { pageSize: number; page: number; userId?: string; workflowId?: string } = { pageSize, page: p }
      if (userId.trim()) params.userId = userId.trim()
      if (workflowId.trim()) params.workflowId = workflowId.trim()
      const res = await adminApi.getConversations(params)
      setItems(res.items)
      setTotal(res.total)
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    load(page, userIdFilter, workflowIdFilter)
  }, [load, page, userIdFilter, workflowIdFilter])

  const handleDelete = async (id: string) => {
    setDeletingId(id)
    try {
      await chatApi.deleteConversation(id)
      if (expandedId === id) setExpandedId(null)
      load(page, userIdFilter, workflowIdFilter)
    } finally {
      setDeletingId(null)
    }
  }

  const handleRowClick = async (id: string) => {
    if (expandedId === id) {
      setExpandedId(null)
      return
    }
    setExpandedId(id)
    setMessagesLoading(true)
    try {
      const msgs = await chatApi.getMessages(id, 20, 0)
      setMessages(msgs)
    } finally {
      setMessagesLoading(false)
    }
  }

  const totalPages = Math.max(1, Math.ceil(total / pageSize))

  return (
    <div className="max-w-[1100px] mx-auto px-10 pt-10 pb-16">
      <header className="pb-8 mb-8 border-b border-[#0C1D38]">
        <h1 className="text-[24px] font-semibold text-[#fafafa] tracking-[-0.025em]">Conversations</h1>
        <p className="text-[13px] text-[#4A6B8A] mt-1">Browse and manage all conversation sessions.</p>
      </header>

      {/* Filters */}
      <div className="flex gap-3 mb-6 flex-wrap">
        <input
          value={userIdFilter}
          onChange={e => { setUserIdFilter(e.target.value); setPage(1) }}
          placeholder="Filter by User ID..."
          className="bg-[#081529] border border-[#1A3357] rounded-md px-3 py-[7px] text-[13px] text-[#DCE8F5] placeholder:text-[#3E5F7D] focus:border-[#254980] focus:outline-none w-[220px]"
        />
        <input
          value={workflowIdFilter}
          onChange={e => { setWorkflowIdFilter(e.target.value); setPage(1) }}
          placeholder="Filter by Workflow ID..."
          className="bg-[#081529] border border-[#1A3357] rounded-md px-3 py-[7px] text-[13px] text-[#DCE8F5] placeholder:text-[#3E5F7D] focus:border-[#254980] focus:outline-none w-[220px]"
        />
      </div>

      {loading ? (
        <div className="flex items-center justify-center py-24">
          <div className="w-5 h-5 border-[1.5px] border-[#254980] border-t-[#7596B8] rounded-full animate-spin" />
        </div>
      ) : (
        <>
          <div className="border border-[#0C1D38] rounded-lg overflow-hidden">
            {/* Table header */}
            <div className="grid grid-cols-[1fr_1fr_1fr_1fr_auto] gap-4 px-5 py-2.5 bg-[#081529] border-b border-[#0C1D38]">
              <span className="text-[10px] uppercase tracking-wider text-[#4A6B8A]">Date</span>
              <span className="text-[10px] uppercase tracking-wider text-[#4A6B8A]">User</span>
              <span className="text-[10px] uppercase tracking-wider text-[#4A6B8A]">Workflow</span>
              <span className="text-[10px] uppercase tracking-wider text-[#4A6B8A]">Last Message</span>
              <span className="text-[10px] uppercase tracking-wider text-[#4A6B8A]">Actions</span>
            </div>

            {items.length === 0 ? (
              <div className="flex items-center justify-center py-16">
                <span className="text-[12px] text-[#444]">No conversations found</span>
              </div>
            ) : (
              <div className="divide-y divide-[#0C1D38]">
                {items.map(item => (
                  <div key={item.conversationId}>
                    <div
                      className="grid grid-cols-[1fr_1fr_1fr_1fr_auto] gap-4 px-5 py-3.5 hover:bg-[#081529] transition-colors cursor-pointer items-center"
                      onClick={() => handleRowClick(item.conversationId)}
                    >
                      <span className="text-[12px] text-[#7596B8]">{fmtDate(item.createdAt)}</span>
                      <span className="text-[12px] text-[#DCE8F5] font-mono truncate">{item.userId ?? '—'}</span>
                      <span className="text-[12px] text-[#7596B8] font-mono truncate">{item.workflowId ?? '—'}</span>
                      <span className="text-[12px] text-[#4A6B8A]">{item.lastMessageAt ? fmtDate(item.lastMessageAt) : '—'}</span>
                      <button
                        onClick={e => { e.stopPropagation(); handleDelete(item.conversationId) }}
                        disabled={deletingId === item.conversationId}
                        className="flex items-center gap-1 px-2.5 py-1 rounded-md text-[11px] bg-red-500/10 hover:bg-red-500/20 text-red-400 border border-red-500/20 disabled:opacity-40 transition-colors"
                      >
                        {deletingId === item.conversationId && (
                          <div className="w-2.5 h-2.5 border-[1.5px] border-red-400/30 border-t-red-400 rounded-full animate-spin" />
                        )}
                        Delete
                      </button>
                    </div>

                    {/* Expanded messages drawer */}
                    {expandedId === item.conversationId && (
                      <div className="bg-[#081529] border-t border-[#0C1D38] px-5 py-4">
                        <div className="flex items-center justify-between mb-3">
                          <span className="text-[11px] font-medium text-[#7596B8] uppercase tracking-wider">Messages</span>
                          <span className="text-[10px] text-[#4A6B8A] font-mono">{item.conversationId}</span>
                        </div>
                        {messagesLoading ? (
                          <div className="flex items-center gap-2 py-4">
                            <div className="w-3.5 h-3.5 border-[1.5px] border-[#254980] border-t-[#7596B8] rounded-full animate-spin" />
                            <span className="text-[12px] text-[#3E5F7D]">Loading messages…</span>
                          </div>
                        ) : messages.length === 0 ? (
                          <span className="text-[12px] text-[#444]">No messages found.</span>
                        ) : (
                          <div className="space-y-2 max-h-[340px] overflow-y-auto" style={{ scrollbarWidth: 'none' }}>
                            {messages.map(msg => (
                              <div key={msg.messageId} className={`flex gap-3 ${msg.role === 'user' ? 'justify-end' : 'justify-start'}`}>
                                <div className={`max-w-[75%] rounded-lg px-3.5 py-2 ${
                                  msg.role === 'user'
                                    ? 'bg-[#0C1D38] text-[#DCE8F5]'
                                    : 'bg-[#04091A] border border-[#0C1D38] text-[#B8CEE5]'
                                }`}>
                                  <div className="flex items-center gap-2 mb-1">
                                    <span className="text-[10px] font-medium uppercase tracking-wider text-[#4A6B8A]">{msg.role}</span>
                                    <span className="text-[10px] text-[#3E5F7D]">{fmtDate(msg.createdAt)}</span>
                                  </div>
                                  <p className="text-[12px] leading-[1.6] whitespace-pre-wrap break-words">{msg.message}</p>
                                </div>
                              </div>
                            ))}
                          </div>
                        )}
                      </div>
                    )}
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* Pagination */}
          <div className="flex items-center justify-between mt-5">
            <span className="text-[12px] text-[#4A6B8A]">{total} total conversation{total !== 1 ? 's' : ''}</span>
            <div className="flex items-center gap-3">
              <button
                onClick={() => setPage(p => Math.max(1, p - 1))}
                disabled={page <= 1}
                className="px-3 py-1.5 rounded-md text-xs bg-[#0C1D38] hover:bg-[#142540] text-[#DCE8F5] disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                Prev
              </button>
              <span className="text-[12px] text-[#4A6B8A]">Page {page} of {totalPages}</span>
              <button
                onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                disabled={page >= totalPages}
                className="px-3 py-1.5 rounded-md text-xs bg-[#0C1D38] hover:bg-[#142540] text-[#DCE8F5] disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                Next
              </button>
            </div>
          </div>
        </>
      )}
    </div>
  )
}

/* ── HITL Admin ──────────────────────────────────────────────────────────────── */

function HitlAdmin() {
  const [tab, setTab] = useState<'pending' | 'all'>('pending')
  const [items, setItems] = useState<HumanInteraction[]>([])
  const [loading, setLoading] = useState(true)
  const [approveTarget, setApproveTarget] = useState<HumanInteraction | null>(null)
  const [approveResponse, setApproveResponse] = useState('')
  const [resolving, setResolving] = useState<string | null>(null)
  const [error, setError] = useState('')

  const load = useCallback(async (currentTab: 'pending' | 'all') => {
    setLoading(true)
    setError('')
    try {
      if (currentTab === 'pending') {
        setItems(await interactionsApi.getPending())
      } else {
        // For "All", get pending + fetch recently resolved via a known execution
        // Since there's no "getAll" endpoint, we fetch pending and show them for now
        // with a note that "All" shows all pending interactions
        setItems(await interactionsApi.getPending())
      }
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Failed to load interactions')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    load(tab)
  }, [load, tab])

  const handleReject = async (item: HumanInteraction) => {
    setResolving(item.interactionId)
    setError('')
    try {
      await interactionsApi.resolve(item.interactionId, '', false)
      load(tab)
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Failed to reject interaction')
    } finally {
      setResolving(null)
    }
  }

  const handleApprove = async () => {
    if (!approveTarget) return
    setResolving(approveTarget.interactionId)
    setError('')
    try {
      await interactionsApi.resolve(approveTarget.interactionId, approveResponse, true)
      setApproveTarget(null)
      setApproveResponse('')
      load(tab)
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Failed to approve interaction')
    } finally {
      setResolving(null)
    }
  }

  return (
    <div className="max-w-[1100px] mx-auto px-10 pt-10 pb-16">
      <header className="pb-8 mb-8 border-b border-[#0C1D38]">
        <div className="flex items-start justify-between">
          <div>
            <h1 className="text-[24px] font-semibold text-[#fafafa] tracking-[-0.025em]">HITL Interactions</h1>
            <p className="text-[13px] text-[#4A6B8A] mt-1">Human-in-the-loop interaction queue.</p>
          </div>
          <button
            onClick={() => load(tab)}
            className="flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs bg-[#0C1D38] hover:bg-[#142540] text-[#DCE8F5] transition-colors"
          >
            <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M1 4v6h6M23 20v-6h-6"/><path d="M20.49 9A9 9 0 005.64 5.64L1 10M23 14l-4.64 4.36A9 9 0 013.51 15"/></svg>
            Refresh
          </button>
        </div>
      </header>

      {/* Tab bar */}
      <div className="flex gap-1 mb-6 p-1 bg-[#081529] rounded-lg w-fit border border-[#0C1D38]">
        {(['pending', 'all'] as const).map(t => (
          <button
            key={t}
            onClick={() => { setTab(t); setItems([]) }}
            className={`px-4 py-1.5 rounded-md text-[12px] font-medium transition-colors capitalize ${
              tab === t ? 'bg-[#0C1D38] text-[#DCE8F5]' : 'text-[#4A6B8A] hover:text-[#7596B8]'
            }`}
          >
            {t === 'pending' ? 'Pending' : 'All'}
          </button>
        ))}
      </div>

      {/* Error banner */}
      {error && (
        <div className="mb-6 px-4 py-3 rounded-lg border border-[#441111] bg-[#110808] flex items-center justify-between">
          <span className="text-[12px] text-[#ff4444]">{error}</span>
          <button onClick={() => setError('')} className="text-[#ff4444] hover:text-[#ff6666] text-[11px]">Dismiss</button>
        </div>
      )}

      {loading ? (
        <div className="flex items-center justify-center py-24">
          <div className="w-5 h-5 border-[1.5px] border-[#254980] border-t-[#7596B8] rounded-full animate-spin" />
        </div>
      ) : (
        <div className="border border-[#0C1D38] rounded-lg overflow-hidden">
          {/* Table header */}
          <div className="grid grid-cols-[1fr_1fr_2fr_auto_auto_auto] gap-4 px-5 py-2.5 bg-[#081529] border-b border-[#0C1D38]">
            <span className="text-[10px] uppercase tracking-wider text-[#4A6B8A]">Execution ID</span>
            <span className="text-[10px] uppercase tracking-wider text-[#4A6B8A]">Workflow</span>
            <span className="text-[10px] uppercase tracking-wider text-[#4A6B8A]">Prompt</span>
            <span className="text-[10px] uppercase tracking-wider text-[#4A6B8A]">Status</span>
            <span className="text-[10px] uppercase tracking-wider text-[#4A6B8A]">Created</span>
            <span className="text-[10px] uppercase tracking-wider text-[#4A6B8A]">Actions</span>
          </div>

          {items.length === 0 ? (
            <div className="flex items-center justify-center py-16">
              <span className="text-[12px] text-[#444]">No interactions found</span>
            </div>
          ) : (
            <div className="divide-y divide-[#0C1D38]">
              {items.map(item => (
                <div key={item.interactionId} className="grid grid-cols-[1fr_1fr_2fr_auto_auto_auto] gap-4 px-5 py-3.5 hover:bg-[#081529] transition-colors items-center">
                  <span className="text-[11px] text-[#DCE8F5] font-mono truncate">{item.executionId}</span>
                  <span className="text-[11px] text-[#7596B8] font-mono truncate">{item.workflowId}</span>
                  <span className="text-[12px] text-[#B8CEE5] truncate" title={item.prompt}>
                    {item.prompt.length > 80 ? item.prompt.slice(0, 80) + '…' : item.prompt}
                  </span>
                  <span>
                    <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-[10px] font-medium border ${
                      item.status === 'Pending'
                        ? 'text-amber-400 border-amber-500/20 bg-amber-500/10'
                        : item.status === 'Resolved'
                        ? 'text-emerald-400 border-emerald-500/20 bg-emerald-500/10'
                        : 'text-red-400 border-red-500/20 bg-red-500/10'
                    }`}>
                      {item.status}
                    </span>
                  </span>
                  <span className="text-[11px] text-[#4A6B8A] whitespace-nowrap">{fmtDate(item.createdAt)}</span>
                  <div className="flex gap-1.5">
                    {item.status === 'Pending' && (
                      <>
                        <button
                          onClick={() => { setApproveTarget(item); setApproveResponse('') }}
                          disabled={resolving === item.interactionId}
                          className="flex items-center gap-1 px-2.5 py-1 rounded-md text-[11px] bg-emerald-500/10 hover:bg-emerald-500/20 text-emerald-400 border border-emerald-500/20 disabled:opacity-40 transition-colors"
                        >
                          Approve
                        </button>
                        <button
                          onClick={() => handleReject(item)}
                          disabled={resolving === item.interactionId}
                          className="flex items-center gap-1 px-2.5 py-1 rounded-md text-[11px] bg-red-500/10 hover:bg-red-500/20 text-red-400 border border-red-500/20 disabled:opacity-40 transition-colors"
                        >
                          {resolving === item.interactionId ? (
                            <div className="w-2.5 h-2.5 border-[1.5px] border-red-400/30 border-t-red-400 rounded-full animate-spin" />
                          ) : null}
                          Reject
                        </button>
                      </>
                    )}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* Approve modal */}
      {approveTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60">
          <div className="bg-[#081529] border border-[#1A3357] rounded-lg p-6 max-w-lg w-full mx-4">
            <p className="text-[14px] text-[#DCE8F5] mb-1 font-medium">Approve Interaction</p>
            <p className="text-[12px] text-[#4A6B8A] mb-4 font-mono truncate">{approveTarget.interactionId}</p>
            <div className="mb-4">
              <div className="bg-[#04091A] border border-[#0C1D38] rounded-md px-4 py-3 mb-4">
                <p className="text-[11px] text-[#4A6B8A] uppercase tracking-wider mb-1">Prompt</p>
                <p className="text-[13px] text-[#B8CEE5] leading-[1.6] whitespace-pre-wrap">{approveTarget.prompt}</p>
              </div>
              <label className="block text-[11px] text-[#4A6B8A] mb-1.5">Response</label>
              <textarea
                value={approveResponse}
                onChange={e => setApproveResponse(e.target.value)}
                rows={4}
                autoFocus
                className="w-full bg-[#04091A] border border-[#1A3357] rounded-md px-3 py-2.5 text-[13px] text-[#DCE8F5] placeholder:text-[#444] focus:border-[#254980] focus:outline-none resize-y"
                placeholder="Enter your response..."
              />
            </div>
            <div className="flex gap-2 justify-end">
              <button
                onClick={() => { setApproveTarget(null); setApproveResponse('') }}
                disabled={resolving === approveTarget.interactionId}
                className="px-3.5 py-[6px] rounded-md text-[12px] font-medium border border-[#1A3357] text-[#999] hover:border-[#254980] hover:text-[#B8CEE5] transition-colors disabled:opacity-50"
              >
                Cancel
              </button>
              <button
                onClick={handleApprove}
                disabled={resolving === approveTarget.interactionId || !approveResponse.trim()}
                className="px-3.5 py-[6px] rounded-md text-[12px] font-medium bg-emerald-500/20 hover:bg-emerald-500/30 text-emerald-400 border border-emerald-500/30 transition-colors disabled:opacity-40 disabled:cursor-not-allowed flex items-center gap-2"
              >
                {resolving === approveTarget.interactionId && (
                  <div className="w-3 h-3 border-[1.5px] border-emerald-400/30 border-t-emerald-400 rounded-full animate-spin" />
                )}
                Approve
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

/* ── Pricing Admin ───────────────────────────────────────────────────────────── */

function PricingAdmin() {
  const [items, setItems] = useState<ModelPricing[]>([])
  const [loading, setLoading] = useState(true)
  const [deletingId, setDeletingId] = useState<number | null>(null)
  const [showForm, setShowForm] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [form, setForm] = useState<CreateModelPricingRequest>({
    modelId: '',
    provider: '',
    pricePerInputToken: 0,
    pricePerOutputToken: 0,
    currency: 'USD',
    effectiveFrom: new Date().toISOString().slice(0, 10),
  })

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setItems(await pricingApi.getAll())
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { load() }, [load])

  const handleDelete = async (id: number) => {
    setDeletingId(id)
    setError('')
    try {
      await pricingApi.delete(id)
      load()
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Failed to delete pricing')
    } finally {
      setDeletingId(null)
    }
  }

  const handleSubmit = async () => {
    if (!form.modelId.trim() || !form.provider.trim() || !form.effectiveFrom) return
    setSaving(true)
    setError('')
    try {
      await pricingApi.create(form)
      setShowForm(false)
      setForm({
        modelId: '',
        provider: '',
        pricePerInputToken: 0,
        pricePerOutputToken: 0,
        currency: 'USD',
        effectiveFrom: new Date().toISOString().slice(0, 10),
      })
      load()
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Failed to create pricing')
    } finally {
      setSaving(false)
    }
  }

  const setField = <K extends keyof CreateModelPricingRequest>(key: K, val: CreateModelPricingRequest[K]) => {
    setForm(prev => ({ ...prev, [key]: val }))
  }

  return (
    <div className="max-w-[1100px] mx-auto px-10 pt-10 pb-16">
      <header className="pb-8 mb-8 border-b border-[#0C1D38]">
        <div className="flex items-start justify-between">
          <div>
            <h1 className="text-[24px] font-semibold text-[#fafafa] tracking-[-0.025em]">Model Pricing</h1>
            <p className="text-[13px] text-[#4A6B8A] mt-1">Manage pricing configuration per model.</p>
          </div>
          <button
            onClick={() => setShowForm(v => !v)}
            className="flex items-center gap-1.5 px-3 py-1.5 rounded-md text-xs bg-[#0C1D38] hover:bg-[#142540] text-[#DCE8F5] transition-colors"
          >
            <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5"><path d="M12 5v14M5 12h14" /></svg>
            Add Pricing
          </button>
        </div>
      </header>

      {/* Error banner */}
      {error && (
        <div className="mb-6 px-4 py-3 rounded-lg border border-[#441111] bg-[#110808] flex items-center justify-between">
          <span className="text-[12px] text-[#ff4444]">{error}</span>
          <button onClick={() => setError('')} className="text-[#ff4444] hover:text-[#ff6666] text-[11px]">Dismiss</button>
        </div>
      )}

      {/* Add form */}
      {showForm && (
        <div className="mb-8 border border-[#0C1D38] rounded-lg bg-[#081529] p-5">
          <h3 className="text-[13px] font-medium text-[#DCE8F5] mb-4">New Model Pricing</h3>
          <div className="grid grid-cols-2 gap-4 mb-4">
            <div>
              <label className="block text-[11px] text-[#4A6B8A] mb-1.5">Model ID</label>
              <input
                value={form.modelId}
                onChange={e => setField('modelId', e.target.value)}
                placeholder="gpt-4o"
                className="w-full bg-[#04091A] border border-[#1A3357] rounded-md px-3 py-[7px] text-[13px] text-[#DCE8F5] font-mono placeholder:text-[#444] focus:border-[#254980] focus:outline-none"
              />
            </div>
            <div>
              <label className="block text-[11px] text-[#4A6B8A] mb-1.5">Provider</label>
              <input
                value={form.provider}
                onChange={e => setField('provider', e.target.value)}
                placeholder="OpenAI"
                className="w-full bg-[#04091A] border border-[#1A3357] rounded-md px-3 py-[7px] text-[13px] text-[#DCE8F5] placeholder:text-[#444] focus:border-[#254980] focus:outline-none"
              />
            </div>
            <div>
              <label className="block text-[11px] text-[#4A6B8A] mb-1.5">Price Per Input Token</label>
              <input
                type="number"
                min={0}
                step="any"
                value={form.pricePerInputToken}
                onChange={e => setField('pricePerInputToken', parseFloat(e.target.value) || 0)}
                className="w-full bg-[#04091A] border border-[#1A3357] rounded-md px-3 py-[7px] text-[13px] text-[#DCE8F5] font-mono placeholder:text-[#444] focus:border-[#254980] focus:outline-none"
              />
            </div>
            <div>
              <label className="block text-[11px] text-[#4A6B8A] mb-1.5">Price Per Output Token</label>
              <input
                type="number"
                min={0}
                step="any"
                value={form.pricePerOutputToken}
                onChange={e => setField('pricePerOutputToken', parseFloat(e.target.value) || 0)}
                className="w-full bg-[#04091A] border border-[#1A3357] rounded-md px-3 py-[7px] text-[13px] text-[#DCE8F5] font-mono placeholder:text-[#444] focus:border-[#254980] focus:outline-none"
              />
            </div>
            <div>
              <label className="block text-[11px] text-[#4A6B8A] mb-1.5">Currency</label>
              <input
                value={form.currency ?? 'USD'}
                onChange={e => setField('currency', e.target.value)}
                placeholder="USD"
                className="w-full bg-[#04091A] border border-[#1A3357] rounded-md px-3 py-[7px] text-[13px] text-[#DCE8F5] font-mono placeholder:text-[#444] focus:border-[#254980] focus:outline-none"
              />
            </div>
            <div>
              <label className="block text-[11px] text-[#4A6B8A] mb-1.5">Effective From</label>
              <input
                type="date"
                value={form.effectiveFrom.slice(0, 10)}
                onChange={e => setField('effectiveFrom', e.target.value)}
                className="w-full bg-[#04091A] border border-[#1A3357] rounded-md px-3 py-[7px] text-[13px] text-[#DCE8F5] font-mono focus:border-[#254980] focus:outline-none"
              />
            </div>
          </div>
          <div className="flex gap-2 justify-end">
            <button
              onClick={() => setShowForm(false)}
              className="px-3.5 py-[6px] rounded-md text-[12px] font-medium border border-[#1A3357] text-[#999] hover:border-[#254980] hover:text-[#B8CEE5] transition-colors"
            >
              Cancel
            </button>
            <button
              onClick={handleSubmit}
              disabled={saving || !form.modelId.trim() || !form.provider.trim()}
              className="px-3.5 py-[6px] rounded-md text-[12px] font-medium bg-[#DCE8F5] text-[#04091A] hover:bg-white transition-colors disabled:opacity-40 disabled:cursor-not-allowed flex items-center gap-2"
            >
              {saving && <div className="w-3 h-3 border-[1.5px] border-[#04091A]/30 border-t-[#04091A] rounded-full animate-spin" />}
              Save Pricing
            </button>
          </div>
        </div>
      )}

      {loading ? (
        <div className="flex items-center justify-center py-24">
          <div className="w-5 h-5 border-[1.5px] border-[#254980] border-t-[#7596B8] rounded-full animate-spin" />
        </div>
      ) : (
        <div className="border border-[#0C1D38] rounded-lg overflow-hidden">
          {/* Table header */}
          <div className="grid grid-cols-[1fr_1fr_1fr_1fr_auto_1fr_1fr_auto] gap-3 px-5 py-2.5 bg-[#081529] border-b border-[#0C1D38]">
            <span className="text-[10px] uppercase tracking-wider text-[#4A6B8A]">Model ID</span>
            <span className="text-[10px] uppercase tracking-wider text-[#4A6B8A]">Provider</span>
            <span className="text-[10px] uppercase tracking-wider text-[#4A6B8A]">Input Price</span>
            <span className="text-[10px] uppercase tracking-wider text-[#4A6B8A]">Output Price</span>
            <span className="text-[10px] uppercase tracking-wider text-[#4A6B8A]">Currency</span>
            <span className="text-[10px] uppercase tracking-wider text-[#4A6B8A]">Effective From</span>
            <span className="text-[10px] uppercase tracking-wider text-[#4A6B8A]">Effective To</span>
            <span className="text-[10px] uppercase tracking-wider text-[#4A6B8A]">Actions</span>
          </div>

          {items.length === 0 ? (
            <div className="flex items-center justify-center py-16">
              <span className="text-[12px] text-[#444]">No pricing entries found</span>
            </div>
          ) : (
            <div className="divide-y divide-[#0C1D38]">
              {items.map(item => (
                <div key={item.id} className="grid grid-cols-[1fr_1fr_1fr_1fr_auto_1fr_1fr_auto] gap-3 px-5 py-3.5 hover:bg-[#081529] transition-colors items-center">
                  <span className="text-[12px] text-[#DCE8F5] font-mono truncate">{item.modelId}</span>
                  <span className="text-[12px] text-[#7596B8] truncate">{item.provider}</span>
                  <span className="text-[12px] text-[#B8CEE5] font-mono">{item.pricePerInputToken}</span>
                  <span className="text-[12px] text-[#B8CEE5] font-mono">{item.pricePerOutputToken}</span>
                  <span className="text-[12px] text-[#4A6B8A]">{item.currency}</span>
                  <span className="text-[11px] text-[#7596B8]">{item.effectiveFrom ? fmtDate(item.effectiveFrom) : '—'}</span>
                  <span className="text-[11px] text-[#4A6B8A]">{item.effectiveTo ? fmtDate(item.effectiveTo) : '—'}</span>
                  <button
                    onClick={() => handleDelete(item.id)}
                    disabled={deletingId === item.id}
                    className="flex items-center gap-1 px-2.5 py-1 rounded-md text-[11px] bg-red-500/10 hover:bg-red-500/20 text-red-400 border border-red-500/20 disabled:opacity-40 transition-colors"
                  >
                    {deletingId === item.id && (
                      <div className="w-2.5 h-2.5 border-[1.5px] border-red-400/30 border-t-red-400 rounded-full animate-spin" />
                    )}
                    Delete
                  </button>
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  )
}

/* ── Nav primitives ─────────────────────────────────────────────────────────── */

function NavGroup({ label, count, children }: { label: string; count: number; children: React.ReactNode }) {
  return (
    <div className="px-2 pt-4 pb-1">
      <div className="flex items-center justify-between px-2 mb-1">
        <span className="text-[11px] font-medium text-[#4A6B8A] uppercase tracking-[0.05em]">{label}</span>
        <span className="text-[10px] text-[#444] tabular-nums">{count}</span>
      </div>
      <div className="space-y-px">{children}</div>
    </div>
  )
}

function NavItem({ active, onClick, label, meta, dot }: {
  active: boolean; onClick: () => void; label: string; meta: string; dot: string
}) {
  return (
    <button
      onClick={onClick}
      className={`w-full text-left px-2.5 py-[7px] rounded-md flex items-center gap-2.5 transition-colors group ${
        active ? 'bg-[#0C1D38]' : 'hover:bg-[#081529]'
      }`}
    >
      <div className="w-[6px] h-[6px] rounded-full shrink-0" style={{ background: dot }} />
      <div className="min-w-0 flex-1">
        <div className={`text-[13px] truncate ${active ? 'text-[#DCE8F5] font-medium' : 'text-[#999] group-hover:text-[#B8CEE5]'}`}>{label}</div>
        <div className="text-[11px] text-[#444] truncate">{meta}</div>
      </div>
    </button>
  )
}

function NavFlatItem({ active, onClick, label, dot }: {
  active: boolean; onClick: () => void; label: string; dot: string
}) {
  return (
    <button
      onClick={onClick}
      className={`w-full text-left px-2.5 py-[7px] rounded-md flex items-center gap-2.5 transition-colors group ${
        active ? 'bg-[#0C1D38]' : 'hover:bg-[#081529]'
      }`}
    >
      <div className="w-[6px] h-[6px] rounded-full shrink-0" style={{ background: dot }} />
      <div className={`text-[13px] truncate ${active ? 'text-[#DCE8F5] font-medium' : 'text-[#999] group-hover:text-[#B8CEE5]'}`}>{label}</div>
    </button>
  )
}

function GhostBtn({ label, onClick }: { label: string; onClick?: () => void }) {
  const enabled = !!onClick
  return (
    <button
      disabled={!enabled}
      onClick={onClick}
      className={`w-full flex items-center justify-center gap-1.5 px-3 py-[6px] rounded-md border border-dashed text-[12px] transition-colors ${
        enabled
          ? 'border-[#254980] text-[#7596B8] hover:border-[#3E5F7D] hover:text-[#B8CEE5] cursor-pointer'
          : 'border-[#1A3357] text-[#444] cursor-not-allowed'
      }`}
    >
      <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5"><path d="M12 5v14M5 12h14" /></svg>
      {label}
    </button>
  )
}

/* ── Shared primitives ──────────────────────────────────────────────────────── */

function Copyable({ text }: { text: string }) {
  const [ok, setOk] = useState(false)
  const copy = useCallback(() => {
    navigator.clipboard.writeText(text).then(() => { setOk(true); setTimeout(() => setOk(false), 1400) })
  }, [text])
  return (
    <button onClick={copy} className="inline-flex items-center gap-1.5 text-[12px] text-[#3E5F7D] font-mono hover:text-[#999] transition-colors" title="Copy">
      <span className="truncate max-w-[280px]">{text}</span>
      {ok ? (
        <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="#50e3c2" strokeWidth="2.5"><path d="M20 6L9 17l-5-5" /></svg>
      ) : (
        <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><rect x="9" y="9" width="13" height="13" rx="2" /><path d="M5 15H4a2 2 0 01-2-2V4a2 2 0 012-2h9a2 2 0 012 2v1" /></svg>
      )}
    </button>
  )
}

function Pill({ children, color = '#4A6B8A' }: { children: React.ReactNode; color?: string }) {
  return (
    <span
      className="inline-flex items-center px-[7px] py-[2px] rounded-full text-[11px] font-medium border"
      style={{ color, borderColor: color + '33', background: color + '0d' }}
    >
      {children}
    </span>
  )
}

function Kv({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="py-3 flex items-baseline justify-between gap-4 border-b border-[#0C1D38] last:border-0">
      <span className="text-[12px] text-[#4A6B8A] shrink-0">{label}</span>
      <span className={`text-[13px] text-[#DCE8F5] text-right truncate ${mono ? 'font-mono' : ''}`}>{value}</span>
    </div>
  )
}

/* ── Workflow Detail ─────────────────────────────────────────────────────────── */

function WorkflowDetail({ wf, agents, onEdit }: { wf: WorkflowDef; agents: AgentDef[]; onEdit: () => void }) {
  const agentMap = new Map(agents.map(a => [a.id, a]))

  return (
    <div className="max-w-[860px] mx-auto px-10 pt-10 pb-16">
      {/* Header */}
      <header className="pb-8 mb-10 border-b border-[#0C1D38]">
        <div className="flex items-start justify-between gap-6">
          <div className="min-w-0">
            <div className="flex items-center gap-3 flex-wrap">
              <h1 className="text-[24px] font-semibold text-[#fafafa] tracking-[-0.025em]">{wf.name}</h1>
              <Pill color={wf.orchestrationMode === 'Handoff' ? '#0057E0' : '#50e3c2'}>{wf.orchestrationMode}</Pill>
              {wf.configuration?.inputMode && <Pill color={wf.configuration.inputMode === 'Chat' ? '#3291ff' : '#4A6B8A'}>{wf.configuration.inputMode}</Pill>}
            </div>
            <div className="mt-2.5"><Copyable text={wf.id} /></div>
            {wf.description && <p className="text-[14px] text-[#777] mt-4 leading-[1.6] max-w-2xl">{wf.description}</p>}
          </div>
          <div className="flex gap-2 shrink-0 pt-1">
            <ActionBtn label="Edit" onClick={onEdit} />
            <DisabledAction label="Delete" danger />
          </div>
        </div>
      </header>

      {/* Configuration */}
      <SectionBlock title="Configuration">
        <div className="bg-[#081529] border border-[#0C1D38] rounded-lg px-5">
          <Kv label="Orchestration Mode" value={wf.orchestrationMode} />
          {wf.configuration?.inputMode && <Kv label="Input Mode" value={wf.configuration.inputMode} />}
          {wf.configuration?.timeoutSeconds != null && <Kv label="Timeout" value={`${wf.configuration.timeoutSeconds}s`} />}
          {wf.configuration?.maxHistoryMessages != null && <Kv label="Max History Messages" value={String(wf.configuration.maxHistoryMessages)} />}
          {wf.configuration?.enableHumanInTheLoop != null && <Kv label="Human in the Loop" value={wf.configuration.enableHumanInTheLoop ? 'Enabled' : 'Disabled'} />}
          <Kv label="Created" value={wf.createdAt ? fmtDate(wf.createdAt) : '—'} />
        </div>
      </SectionBlock>

      {/* Agents */}
      <SectionBlock title="Agents" count={wf.agents?.length}>
        {wf.agents?.length ? (
          <div className="border border-[#0C1D38] rounded-lg overflow-hidden">
            {wf.agents.map((a, i) => {
              const def = agentMap.get(a.agentId)
              return (
                <div key={i} className={`flex items-center gap-4 px-5 py-3.5 ${i > 0 ? 'border-t border-[#0C1D38]' : ''} hover:bg-[#081529] transition-colors`}>
                  <div className="w-8 h-8 rounded-full bg-[#0C1D38] flex items-center justify-center text-[11px] font-bold text-[#4A6B8A] shrink-0 uppercase">
                    {(def?.name ?? a.agentId).charAt(0)}
                  </div>
                  <div className="min-w-0 flex-1">
                    <div className="text-[13px] text-[#DCE8F5] font-medium truncate">{def?.name ?? a.agentId}</div>
                    <div className="text-[11px] text-[#3E5F7D] font-mono truncate">{def?.model.deploymentName ?? a.agentId}</div>
                  </div>
                  {def?.provider?.type && <Pill color={def.provider.type === 'OpenAI' ? '#50e3c2' : '#3291ff'}>{def.provider.type}</Pill>}
                </div>
              )
            })}
          </div>
        ) : <EmptyBlock />}
      </SectionBlock>

      {/* Executors */}
      <SectionBlock title="Executors" count={wf.executors?.length}>
        {wf.executors?.length ? (
          <div className="border border-[#0C1D38] rounded-lg overflow-hidden">
            {wf.executors.map((ex, i) => (
              <div key={ex.id} className={`flex items-center gap-4 px-5 py-3.5 ${i > 0 ? 'border-t border-[#0C1D38]' : ''}`}>
                <div className="w-8 h-8 rounded-full bg-[#0C1D38] flex items-center justify-center shrink-0">
                  <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="#0057E0" strokeWidth="1.5"><path d="M13 2L3 14h9l-1 8 10-12h-9l1-8" /></svg>
                </div>
                <div className="min-w-0 flex-1">
                  <div className="text-[13px] text-[#DCE8F5] font-medium">{ex.id}</div>
                  <div className="text-[11px] text-[#3E5F7D] font-mono">{ex.functionName}</div>
                </div>
              </div>
            ))}
          </div>
        ) : <EmptyBlock />}
      </SectionBlock>

      {/* Edges */}
      <SectionBlock title="Edges" count={wf.edges?.length}>
        {wf.edges?.length ? (
          <div className="border border-[#0C1D38] rounded-lg overflow-hidden">
            {wf.edges.map((edge, i) => {
              const fromName = edge.from ? (agentMap.get(edge.from)?.name ?? edge.from) : 'START'
              const toName = edge.to ? (agentMap.get(edge.to)?.name ?? edge.to) : 'END'
              return (
                <div key={i} className={`flex items-center gap-3 px-5 py-3 ${i > 0 ? 'border-t border-[#0C1D38]' : ''}`}>
                  <span className={`text-[12px] font-mono ${edge.from ? 'text-[#DCE8F5]' : 'text-[#50e3c2]'}`}>{fromName}</span>
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#333" strokeWidth="1.5"><path d="M5 12h14M12 5l7 7-7 7" /></svg>
                  <span className={`text-[12px] font-mono ${edge.to ? 'text-[#DCE8F5]' : 'text-[#ff4444]'}`}>{toName}</span>
                  <span className="ml-auto"><Pill color={
                    edge.edgeType === 'Switch' ? '#0057E0' :
                    edge.edgeType === 'Conditional' ? '#3291ff' : '#444'
                  }>{edge.edgeType}</Pill></span>
                  {edge.cases && edge.cases.length > 0 && (
                    <div className="flex gap-1 ml-2">
                      {edge.cases.map((c, ci) => (
                        <span key={ci} className="text-[10px] text-[#3E5F7D] font-mono">
                          {c.isDefault ? 'default' : c.condition}→{c.targets.join(',')}
                        </span>
                      ))}
                    </div>
                  )}
                </div>
              )
            })}
          </div>
        ) : <EmptyBlock />}
      </SectionBlock>
    </div>
  )
}

/* ── Agent Detail ───────────────────────────────────────────────────────────── */

function AgentDetail({ agent, onEdit, onDelete, onPrompts, onPlayground }: { agent: AgentDef; onEdit: () => void; onDelete: () => void; onPrompts: () => void; onPlayground: () => void }) {
  return (
    <div className="max-w-[860px] mx-auto px-10 pt-10 pb-16">
      {/* Header */}
      <header className="pb-8 mb-10 border-b border-[#0C1D38]">
        <div className="flex items-start justify-between gap-6">
          <div className="min-w-0">
            <div className="flex items-center gap-3 flex-wrap">
              <h1 className="text-[24px] font-semibold text-[#fafafa] tracking-[-0.025em]">{agent.name}</h1>
              {agent.provider?.type && (
                <Pill color={agent.provider.type === 'OpenAI' ? '#50e3c2' : agent.provider.type === 'AzureFoundry' ? '#3291ff' : '#0057E0'}>
                  {agent.provider.type}
                </Pill>
              )}
            </div>
            <div className="mt-2.5"><Copyable text={agent.id} /></div>
            {agent.description && <p className="text-[14px] text-[#777] mt-4 leading-[1.6] max-w-2xl">{agent.description}</p>}
          </div>
          <div className="flex gap-2 shrink-0 pt-1">
            <ActionBtn label="Playground" onClick={onPlayground} />
            <ActionBtn label="Prompts" onClick={onPrompts} />
            <ActionBtn label="Edit" onClick={onEdit} />
            <ActionBtn label="Delete" danger onClick={onDelete} />
          </div>
        </div>
      </header>

      {/* Model & Provider */}
      <SectionBlock title="Model">
        <div className="bg-[#081529] border border-[#0C1D38] rounded-lg px-5">
          <Kv label="Deployment" value={agent.model.deploymentName} mono />
          {agent.model.temperature != null && <Kv label="Temperature" value={String(agent.model.temperature)} />}
          {agent.model.maxTokens != null && <Kv label="Max Tokens" value={agent.model.maxTokens.toLocaleString()} />}
          {agent.provider?.type && <Kv label="Provider" value={agent.provider.type} />}
          {agent.provider?.clientType && <Kv label="Client Type" value={agent.provider.clientType} />}
          {agent.provider?.endpoint && <Kv label="Endpoint" value={agent.provider.endpoint} mono />}
          <Kv label="Created" value={agent.createdAt ? fmtDate(agent.createdAt) : '—'} />
          <Kv label="Updated" value={agent.updatedAt ? fmtDate(agent.updatedAt) : '—'} />
        </div>
      </SectionBlock>

      {/* Instructions — preview with link to Prompts */}
      {agent.instructions && (
        <SectionBlock title="Instructions" action={
          <button onClick={onPrompts} className="text-[11px] text-[#3E5F7D] hover:text-[#999] transition-colors flex items-center gap-1">
            <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M9 18l6-6-6-6" /></svg>
            Manage Versions
          </button>
        }>
          <div className="bg-[#081529] border border-[#0C1D38] rounded-lg overflow-hidden cursor-pointer hover:border-[#254980] transition-colors" onClick={onPrompts}>
            <div className="px-5 py-4">
              <pre className="whitespace-pre-wrap text-[13px] text-[#B8CEE5] font-mono leading-[1.75] selection:bg-[#3291ff33] line-clamp-6">{agent.instructions}</pre>
            </div>
            {agent.instructions.length > 400 && (
              <div className="px-5 pb-3">
                <span className="text-[11px] text-[#3E5F7D]">Click to view all versions and full content</span>
              </div>
            )}
          </div>
        </SectionBlock>
      )}

      {/* Tools */}
      <SectionBlock title="Tools" count={agent.tools?.length}>
        {agent.tools?.length ? (
          <div className="border border-[#0C1D38] rounded-lg overflow-hidden">
            {agent.tools.map((tool, i) => (
              <div key={i} className={`px-5 py-3.5 ${i > 0 ? 'border-t border-[#0C1D38]' : ''} hover:bg-[#081529] transition-colors`}>
                <div className="flex items-center gap-3">
                  <span className="w-[6px] h-[6px] rounded-full shrink-0" style={{
                    background: tool.type === 'function' ? '#3291ff' : tool.type === 'mcp' ? '#7928ca' : tool.type === 'code_interpreter' ? '#50e3c2' : tool.type === 'web_search' ? '#ff6b6b' : '#0057E0'
                  }} />
                  <span className="text-[13px] text-[#DCE8F5] font-medium">{tool.name ?? tool.serverLabel ?? tool.type}</span>
                  <Pill color="#666">{tool.type}</Pill>
                  {tool.requiresApproval && <Pill color="#0057E0">approval</Pill>}
                </div>
                {tool.serverUrl && <div className="text-[11px] text-[#444] font-mono mt-1 ml-[18px] truncate">{tool.serverUrl}</div>}
                {tool.connectionId && <div className="text-[11px] text-[#444] font-mono mt-1 ml-[18px] truncate">Connection: {tool.connectionId}</div>}
                {tool.allowedTools && tool.allowedTools.length > 0 && (
                  <div className="flex flex-wrap gap-1.5 mt-2 ml-[18px]">
                    {tool.allowedTools.map((t, ti) => <span key={ti} className="text-[10px] text-[#3E5F7D] bg-[#0C1D38] px-2 py-0.5 rounded font-mono">{t}</span>)}
                  </div>
                )}
              </div>
            ))}
          </div>
        ) : <EmptyBlock />}
      </SectionBlock>

      {/* Structured Output */}
      {agent.structuredOutput && (
        <SectionBlock title="Structured Output">
          <div className="bg-[#081529] border border-[#0C1D38] rounded-lg px-5">
            <Kv label="Format" value={agent.structuredOutput.responseFormat} />
            {agent.structuredOutput.schemaName && <Kv label="Schema" value={agent.structuredOutput.schemaName} />}
          </div>
          {agent.structuredOutput.schemaDescription && (
            <p className="text-[13px] text-[#4A6B8A] mt-3">{agent.structuredOutput.schemaDescription}</p>
          )}
          {agent.structuredOutput.schema != null ? (
            <div className="bg-[#081529] border border-[#0C1D38] rounded-lg mt-3 p-5">
              <pre className="whitespace-pre-wrap text-[12px] text-[#7596B8] font-mono leading-relaxed">{JSON.stringify(agent.structuredOutput.schema, null, 2)}</pre>
            </div>
          ) : null}
        </SectionBlock>
      )}

      {/* Middlewares */}
      {agent.middlewares && agent.middlewares.length > 0 && (
        <SectionBlock title="Middlewares" count={agent.middlewares.length}>
          <div className="border border-[#0C1D38] rounded-lg overflow-hidden">
            {agent.middlewares.map((mw, i) => (
              <div key={i} className={`flex items-center gap-3 px-5 py-3.5 ${i > 0 ? 'border-t border-[#0C1D38]' : ''}`}>
                <span className="w-[6px] h-[6px] rounded-full shrink-0" style={{ background: mw.enabled ? '#50e3c2' : '#444' }} />
                <span className="text-[13px] text-[#DCE8F5] font-medium">{mw.type}</span>
                <Pill color={mw.enabled ? '#50e3c2' : '#4A6B8A'}>{mw.enabled ? 'active' : 'disabled'}</Pill>
                {mw.settings && Object.keys(mw.settings).length > 0 && (
                  <span className="text-[11px] text-[#444] font-mono ml-auto truncate max-w-[200px]">
                    {Object.entries(mw.settings).map(([k, v]) => `${k}=${v}`).join(', ')}
                  </span>
                )}
              </div>
            ))}
          </div>
        </SectionBlock>
      )}

      {/* Metadata */}
      {agent.metadata && Object.keys(agent.metadata).length > 0 && (
        <SectionBlock title="Metadata" count={Object.keys(agent.metadata).length}>
          <div className="bg-[#081529] border border-[#0C1D38] rounded-lg px-5">
            {Object.entries(agent.metadata).map(([k, v]) => (
              <Kv key={k} label={k} value={v} mono />
            ))}
          </div>
        </SectionBlock>
      )}
    </div>
  )
}

/* ── Prompt Viewer ──────────────────────────────────────────────────────────── */

function PromptViewer({ content }: { content: string }) {
  const lines = content.split('\n')
  const [copied, setCopied] = useState(false)
  const [search, setSearch] = useState('')
  const [showSearch, setShowSearch] = useState(false)

  const copy = () => {
    navigator.clipboard.writeText(content).then(() => { setCopied(true); setTimeout(() => setCopied(false), 1400) })
  }

  const searchLower = search.toLowerCase()
  const matchCount = search ? lines.filter(l => l.toLowerCase().includes(searchLower)).length : 0

  return (
    <div className="border border-[#0C1D38] rounded-lg overflow-hidden bg-[#0d0d0d]">
      {/* Toolbar */}
      <div className="flex items-center justify-between px-4 py-2 bg-[#081529] border-b border-[#0C1D38]">
        <div className="flex items-center gap-3">
          <span className="text-[11px] text-[#3E5F7D]">{lines.length} lines</span>
          <span className="text-[11px] text-[#254980]">·</span>
          <span className="text-[11px] text-[#3E5F7D]">{content.length.toLocaleString()} chars</span>
        </div>
        <div className="flex items-center gap-2">
          {showSearch && (
            <div className="flex items-center gap-2">
              <input
                value={search}
                onChange={e => setSearch(e.target.value)}
                placeholder="Search..."
                autoFocus
                className="bg-[#04091A] border border-[#1A3357] rounded px-2 py-[3px] text-[11px] text-[#DCE8F5] font-mono placeholder:text-[#444] focus:border-[#254980] focus:outline-none w-[140px]"
              />
              {search && <span className="text-[10px] text-[#3E5F7D]">{matchCount} match{matchCount !== 1 ? 'es' : ''}</span>}
            </div>
          )}
          <button
            onClick={() => { setShowSearch(!showSearch); if (showSearch) setSearch('') }}
            className="p-1 rounded hover:bg-[#0C1D38] transition-colors"
            title="Search"
          >
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke={showSearch ? '#999' : '#3E5F7D'} strokeWidth="2"><circle cx="11" cy="11" r="7" /><path d="M21 21l-4.35-4.35" /></svg>
          </button>
          <button onClick={copy} className="p-1 rounded hover:bg-[#0C1D38] transition-colors" title="Copy all">
            {copied
              ? <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="#50e3c2" strokeWidth="2.5"><path d="M20 6L9 17l-5-5" /></svg>
              : <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="#555" strokeWidth="2"><rect x="9" y="9" width="13" height="13" rx="2" /><path d="M5 15H4a2 2 0 01-2-2V4a2 2 0 012-2h9a2 2 0 012 2v1" /></svg>}
          </button>
        </div>
      </div>
      {/* Code area */}
      <div className="overflow-auto" style={{ maxHeight: '60vh' }}>
        <table className="w-full border-collapse">
          <tbody>
            {lines.map((line, i) => {
              const isMatch = search && line.toLowerCase().includes(searchLower)
              return (
                <tr key={i} className={isMatch ? 'bg-[#1a1a00]' : ''}>
                  <td className="select-none text-right pr-4 pl-4 py-0 text-[11px] text-[#254980] font-mono align-top" style={{ width: 1, whiteSpace: 'nowrap', lineHeight: '1.75' }}>
                    {i + 1}
                  </td>
                  <td className="pr-4 py-0">
                    <pre className="whitespace-pre-wrap text-[12px] text-[#B8CEE5] font-mono selection:bg-[#3291ff33]" style={{ lineHeight: '1.75' }}>
                      {search && isMatch ? highlightMatches(line, search) : (line || '\u00A0')}
                    </pre>
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
    </div>
  )
}

function highlightMatches(text: string, query: string): React.ReactNode {
  const parts: React.ReactNode[] = []
  const lower = text.toLowerCase()
  const qLower = query.toLowerCase()
  let last = 0
  let idx = lower.indexOf(qLower, last)
  while (idx !== -1) {
    if (idx > last) parts.push(text.slice(last, idx))
    parts.push(
      <span key={idx} className="bg-[#0057E055] text-[#0057E0] rounded-sm px-[1px]">
        {text.slice(idx, idx + query.length)}
      </span>
    )
    last = idx + query.length
    idx = lower.indexOf(qLower, last)
  }
  if (last < text.length) parts.push(text.slice(last))
  return <>{parts}</>
}

/* ── Agent Prompts View ─────────────────────────────────────────────────────── */

/* ── Agent Playground ────────────────────────────────────────────────────────── */

interface PlaygroundMsg { role: 'user' | 'assistant'; content: string }

function AgentPlaygroundView({ agent, onBack }: { agent: AgentDef; onBack: () => void }) {
  const [sessionId, setSessionId] = useState<string | null>(null)
  const [messages, setMessages] = useState<PlaygroundMsg[]>([])
  const [input, setInput] = useState('')
  const [streaming, setStreaming] = useState(false)
  const [streamText, setStreamText] = useState('')
  const [tokenInfo, setTokenInfo] = useState<{ promptVersionId?: string; inputTokens?: number; outputTokens?: number; durationMs?: number } | null>(null)
  const [error, setError] = useState('')
  const bottomRef = useRef<HTMLDivElement>(null)

  // Ensure session exists
  const ensureSession = async (): Promise<string> => {
    if (sessionId) return sessionId
    const s = await sessionApi.create(agent.id)
    setSessionId(s.sessionId)
    return s.sessionId
  }

  const clearSession = async () => {
    if (sessionId) {
      await sessionApi.delete(agent.id, sessionId).catch(() => {/* ignore */})
    }
    setSessionId(null)
    setMessages([])
    setStreamText('')
    setTokenInfo(null)
    setError('')
  }

  const sendMessage = async () => {
    if (!input.trim() || streaming) return
    const msg = input.trim()
    setInput('')
    setError('')
    setMessages(prev => [...prev, { role: 'user', content: msg }])
    setStreaming(true)
    setStreamText('')
    const startMs = Date.now()
    try {
      const sid = await ensureSession()
      const resp = await fetch(`/api/agents/${agent.id}/sessions/${sid}/stream`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ message: msg }),
      })
      if (!resp.ok) throw new Error(`HTTP ${resp.status}`)
      const reader = resp.body?.getReader()
      const decoder = new TextDecoder()
      let full = ''
      if (reader) {
        while (true) {
          const { done, value } = await reader.read()
          if (done) break
          const chunk = decoder.decode(value, { stream: true })
          for (const line of chunk.split('\n')) {
            if (line.startsWith('data: ')) {
              const tok = line.slice(6)
              if (tok === '[DONE]') break
              full += tok
              setStreamText(full)
            }
          }
        }
      }
      setMessages(prev => [...prev, { role: 'assistant', content: full }])
      setStreamText('')
      // Fetch token info from recent history
      tokenApi.getAgentHistory(agent.id, 1).then(history => {
        if (history.length > 0) {
          const latest = history[0]
          setTokenInfo({
            promptVersionId: latest.promptVersionId,
            inputTokens: latest.inputTokens,
            outputTokens: latest.outputTokens,
            durationMs: Date.now() - startMs,
          })
        }
      }).catch(() => {/* ignore */})
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Erro ao enviar mensagem')
      setMessages(prev => prev.filter(m => m.content !== msg || m.role !== 'user'))
    } finally {
      setStreaming(false)
    }
    setTimeout(() => bottomRef.current?.scrollIntoView({ behavior: 'smooth' }), 50)
  }

  return (
    <div className="max-w-[860px] mx-auto px-10 pt-10 pb-16 flex flex-col" style={{ height: 'calc(100vh - 60px)' }}>
      {/* Header */}
      <header className="pb-6 mb-6 border-b border-[#0C1D38] shrink-0">
        <button onClick={onBack} className="flex items-center gap-1.5 text-[12px] text-[#3E5F7D] hover:text-[#999] transition-colors mb-4">
          <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M15 18l-6-6 6-6" /></svg>
          Back to {agent.name}
        </button>
        <div className="flex items-center justify-between gap-4">
          <div>
            <h1 className="text-[24px] font-semibold text-[#fafafa] tracking-[-0.025em]">Playground</h1>
            <p className="text-[13px] text-[#4A6B8A] mt-1">Teste o agente <span className="text-[#7596B8] font-mono">{agent.name}</span> diretamente</p>
          </div>
          <button
            onClick={clearSession}
            className="px-3.5 py-[6px] rounded-md text-[12px] font-medium border border-[#1A3357] text-[#4A6B8A] hover:border-[#254980] hover:text-[#999] transition-colors"
          >
            Limpar
          </button>
        </div>
      </header>

      {/* Token info bar */}
      {tokenInfo && (
        <div className="flex items-center gap-4 px-3 py-2 rounded-lg bg-[#081529] border border-[#0C1D38] mb-4 shrink-0">
          {tokenInfo.promptVersionId && (
            <span className="px-1.5 py-0.5 rounded text-[10px] bg-violet-500/10 text-violet-400 border border-violet-500/20 font-mono">
              [{tokenInfo.promptVersionId}]
            </span>
          )}
          {tokenInfo.inputTokens != null && (
            <span className="text-[11px] text-[#4A6B8A]">
              <span className="text-[#7596B8]">{tokenInfo.inputTokens}</span> in · <span className="text-[#7596B8]">{tokenInfo.outputTokens}</span> out
            </span>
          )}
          {tokenInfo.durationMs != null && (
            <span className="text-[11px] text-[#3E5F7D] ml-auto">{(tokenInfo.durationMs / 1000).toFixed(1)}s</span>
          )}
        </div>
      )}

      {/* Messages */}
      <div className="flex-1 overflow-y-auto space-y-3 mb-4 min-h-0" style={{ scrollbarWidth: 'none' }}>
        {messages.length === 0 && !streaming && (
          <div className="flex items-center justify-center h-full text-center text-[#3E5F7D] text-sm">
            <div>
              <p className="font-medium text-[#4A6B8A] mb-1">Playground pronto</p>
              <p className="text-[12px]">Digite uma mensagem para testar o prompt do agente</p>
            </div>
          </div>
        )}
        {messages.map((m, i) => (
          <div key={i} className={`flex ${m.role === 'user' ? 'justify-end' : 'justify-start'}`}>
            <div className={`max-w-[80%] px-3.5 py-2.5 rounded-xl text-[13px] leading-[1.6] ${
              m.role === 'user'
                ? 'bg-white text-black rounded-tr-sm'
                : 'bg-[#081529] border border-[#0C1D38] text-[#B8CEE5] rounded-tl-sm'
            }`}>
              <pre className="whitespace-pre-wrap font-sans">{m.content}</pre>
            </div>
          </div>
        ))}
        {streaming && (
          <div className="flex justify-start">
            <div className="max-w-[80%] px-3.5 py-2.5 rounded-xl rounded-tl-sm bg-[#081529] border border-[#0C1D38] text-[13px] leading-[1.6] text-[#B8CEE5]">
              <pre className="whitespace-pre-wrap font-sans">{streamText || ' '}<span className="animate-pulse">▋</span></pre>
            </div>
          </div>
        )}
        {error && (
          <div className="px-3 py-2 rounded-lg bg-red-500/5 border border-red-500/10 text-[12px] text-red-400 font-mono">{error}</div>
        )}
        <div ref={bottomRef} />
      </div>

      {/* Input */}
      <div className="shrink-0 flex gap-2">
        <textarea
          value={input}
          onChange={e => setInput(e.target.value)}
          onKeyDown={e => {
            if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); sendMessage() }
          }}
          placeholder="Mensagem… (Enter para enviar, Shift+Enter para nova linha)"
          disabled={streaming}
          rows={2}
          className="flex-1 bg-[#04091A] border border-[#1A3357] rounded-md px-3 py-2.5 text-[13px] text-[#DCE8F5] placeholder:text-[#444] focus:border-[#254980] focus:outline-none transition-colors resize-none disabled:opacity-50"
        />
        <button
          onClick={sendMessage}
          disabled={!input.trim() || streaming}
          className="px-4 rounded-md text-[12px] font-medium bg-white text-black hover:bg-[#e0e0e0] transition-colors disabled:opacity-40 self-end py-[7px]"
        >
          {streaming ? (
            <div className="w-4 h-4 border-[1.5px] border-[#999] border-t-black rounded-full animate-spin" />
          ) : '↑'}
        </button>
      </div>
    </div>
  )
}

/* ── Prompt diff helper ───────────────────────────────────────────────────── */

function computeLineDiff(a: string, b: string): { type: 'same' | 'add' | 'remove'; text: string }[] {
  const la = a.split('\n'), lb = b.split('\n')
  const result: { type: 'same' | 'add' | 'remove'; text: string }[] = []
  const maxLen = Math.max(la.length, lb.length)
  for (let i = 0; i < maxLen; i++) {
    if (la[i] === lb[i]) {
      if (la[i] !== undefined) result.push({ type: 'same', text: la[i] })
    } else {
      if (la[i] !== undefined) result.push({ type: 'remove', text: la[i] })
      if (lb[i] !== undefined) result.push({ type: 'add', text: lb[i] })
    }
  }
  return result
}

/* ── Agent Prompts ────────────────────────────────────────────────────────────── */

function AgentPromptsView({ agent, onBack }: { agent: AgentDef; onBack: () => void }) {
  const [versions, setVersions] = useState<AgentPromptVersion[]>([])
  const [loading, setLoading] = useState(true)
  const [selectedVersion, setSelectedVersion] = useState<AgentPromptVersion | null>(null)
  const [showCreateForm, setShowCreateForm] = useState(false)
  const [newVersionId, setNewVersionId] = useState('')
  const [newVersionContent, setNewVersionContent] = useState('')
  const [creating, setCreating] = useState(false)
  const [activating, setActivating] = useState<string | null>(null)
  const [error, setError] = useState('')
  // Version stats (call count, avg tokens per version)
  const [versionStats, setVersionStats] = useState<Record<string, { calls: number; avgTokens: number; avgDurationMs: number }>>({})
  // Diff modal
  const [diffVersionId, setDiffVersionId] = useState('')
  const [showDiff, setShowDiff] = useState(false)

  const refreshVersions = useCallback(() => {
    promptApi.listVersions(agent.id).then(list => {
      const sorted = [...list].sort((a, b) => {
        if (a.isActive !== b.isActive) return a.isActive ? -1 : 1
        return b.versionId.localeCompare(a.versionId)
      })
      setVersions(sorted)
      setSelectedVersion(prev => {
        if (!prev) return null
        return sorted.find(v => v.versionId === prev.versionId) ?? null
      })
    })
  }, [agent.id])

  useEffect(() => {
    setLoading(true)
    setSelectedVersion(null)
    setShowCreateForm(false)
    setError('')
    promptApi.listVersions(agent.id).then(list => {
      const sorted = [...list].sort((a, b) => {
        if (a.isActive !== b.isActive) return a.isActive ? -1 : 1
        return b.versionId.localeCompare(a.versionId)
      })
      setVersions(sorted)
      setSelectedVersion(sorted.find(v => v.isActive) ?? sorted[0] ?? null)
    }).finally(() => setLoading(false))
  }, [agent.id])

  // Load version stats from token history
  useEffect(() => {
    tokenApi.getAgentHistory(agent.id, 200).then(history => {
      const map: Record<string, { calls: number; totalTokens: number; totalDurationMs: number }> = {}
      for (const h of history) {
        const v = h.promptVersionId ?? '__none__'
        if (!map[v]) map[v] = { calls: 0, totalTokens: 0, totalDurationMs: 0 }
        map[v].calls++
        map[v].totalTokens += h.totalTokens
        map[v].totalDurationMs += h.durationMs
      }
      const stats: Record<string, { calls: number; avgTokens: number; avgDurationMs: number }> = {}
      for (const [vid, s] of Object.entries(map)) {
        stats[vid] = { calls: s.calls, avgTokens: Math.round(s.totalTokens / s.calls), avgDurationMs: Math.round(s.totalDurationMs / s.calls) }
      }
      setVersionStats(stats)
    }).catch(() => {/* ignore */})
  }, [agent.id])

  const handleCreate = async () => {
    if (!newVersionId.trim() || !newVersionContent.trim()) return
    setCreating(true)
    setError('')
    try {
      await promptApi.saveVersion(agent.id, newVersionId.trim(), newVersionContent)
      setShowCreateForm(false)
      setNewVersionId('')
      setNewVersionContent('')
      refreshVersions()
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Failed to create version')
    } finally {
      setCreating(false)
    }
  }

  const handleActivate = async (versionId: string) => {
    setActivating(versionId)
    setError('')
    try {
      await promptApi.setMaster(agent.id, versionId)
      refreshVersions()
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Failed to activate version')
    } finally {
      setActivating(null)
    }
  }

  const handleDelete = async (versionId: string) => {
    setError('')
    try {
      await promptApi.deleteVersion(agent.id, versionId)
      if (selectedVersion?.versionId === versionId) setSelectedVersion(null)
      refreshVersions()
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Cannot delete active version')
    }
  }

  return (
    <div className="max-w-[860px] mx-auto px-10 pt-10 pb-16">
      {/* Header */}
      <header className="pb-8 mb-10 border-b border-[#0C1D38]">
        <button onClick={onBack} className="flex items-center gap-1.5 text-[12px] text-[#3E5F7D] hover:text-[#999] transition-colors mb-4">
          <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M15 18l-6-6 6-6" /></svg>
          Back to {agent.name}
        </button>
        <div className="flex items-center gap-3 flex-wrap">
          <h1 className="text-[24px] font-semibold text-[#fafafa] tracking-[-0.025em]">Prompt Versions</h1>
          <Pill>{agent.name}</Pill>
        </div>
        <div className="mt-2.5"><Copyable text={agent.id} /></div>
      </header>

      {/* Error banner */}
      {error && (
        <div className="mb-6 px-4 py-3 rounded-lg border border-[#441111] bg-[#110808] flex items-center justify-between">
          <span className="text-[12px] text-[#ff4444]">{error}</span>
          <button onClick={() => setError('')} className="text-[#ff4444] hover:text-[#ff6666] text-[11px]">Dismiss</button>
        </div>
      )}

      {/* Create form */}
      {showCreateForm && (
        <div className="mb-8 border border-[#0C1D38] rounded-lg bg-[#081529] p-5">
          <h3 className="text-[13px] font-medium text-[#DCE8F5] mb-4">New Prompt Version</h3>
          <div className="mb-3">
            <label className="block text-[11px] text-[#4A6B8A] mb-1.5">Version ID</label>
            <input
              value={newVersionId}
              onChange={e => setNewVersionId(e.target.value)}
              placeholder="v2.0"
              className="w-full bg-[#04091A] border border-[#1A3357] rounded-md px-3 py-[7px] text-[13px] text-[#DCE8F5] font-mono placeholder:text-[#444] focus:border-[#254980] focus:outline-none"
            />
          </div>
          <div className="mb-4">
            <div className="flex items-center justify-between mb-1.5">
              <label className="text-[11px] text-[#4A6B8A]">Content</label>
              {versions.some(v => v.isActive) && (
                <button
                  onClick={() => setNewVersionContent(versions.find(v => v.isActive)?.content ?? '')}
                  className="text-[11px] text-[#3E5F7D] hover:text-[#999] transition-colors"
                >
                  Copy from active
                </button>
              )}
            </div>
            <textarea
              value={newVersionContent}
              onChange={e => setNewVersionContent(e.target.value)}
              rows={10}
              className="w-full bg-[#04091A] border border-[#1A3357] rounded-md px-3 py-2.5 text-[13px] text-[#DCE8F5] font-mono placeholder:text-[#444] focus:border-[#254980] focus:outline-none resize-y leading-[1.75]"
              placeholder="Enter prompt content..."
            />
          </div>
          <div className="flex gap-2 justify-end">
            <button
              onClick={() => { setShowCreateForm(false); setNewVersionId(''); setNewVersionContent('') }}
              className="px-3.5 py-[6px] rounded-md text-[12px] font-medium border border-[#1A3357] text-[#999] hover:border-[#254980] hover:text-[#B8CEE5] transition-colors"
            >
              Cancel
            </button>
            <button
              onClick={handleCreate}
              disabled={creating || !newVersionId.trim() || !newVersionContent.trim()}
              className="px-3.5 py-[6px] rounded-md text-[12px] font-medium bg-[#DCE8F5] text-[#04091A] hover:bg-white transition-colors disabled:opacity-40 disabled:cursor-not-allowed flex items-center gap-2"
            >
              {creating && <div className="w-3 h-3 border-[1.5px] border-[#04091A]/30 border-t-[#04091A] rounded-full animate-spin" />}
              Save Version
            </button>
          </div>
        </div>
      )}

      {/* Version selector bar */}
      <div className="flex items-center gap-3 mb-6 flex-wrap">
        {loading ? (
          <div className="flex items-center gap-2">
            <div className="w-3.5 h-3.5 border-[1.5px] border-[#254980] border-t-[#7596B8] rounded-full animate-spin" />
            <span className="text-[12px] text-[#3E5F7D]">Loading versions...</span>
          </div>
        ) : (
          <>
            <div className="relative">
              <select
                value={selectedVersion?.versionId ?? ''}
                onChange={e => {
                  const v = versions.find(ver => ver.versionId === e.target.value)
                  setSelectedVersion(v ?? null)
                }}
                className="appearance-none bg-[#081529] border border-[#1A3357] rounded-md pl-3 pr-8 py-[7px] text-[13px] text-[#DCE8F5] font-mono focus:border-[#254980] focus:outline-none cursor-pointer hover:border-[#254980] transition-colors min-w-[200px]"
              >
                <option value="" disabled>Select version…</option>
                {versions.map(v => (
                  <option key={v.versionId} value={v.versionId}>
                    {v.versionId}{v.isActive ? ' ● active' : ''}
                  </option>
                ))}
              </select>
              <svg className="absolute right-2.5 top-1/2 -translate-y-1/2 pointer-events-none text-[#3E5F7D]" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M6 9l6 6 6-6" /></svg>
            </div>

            {selectedVersion && (
              <div className="flex items-center gap-2">
                {selectedVersion.isActive ? (
                  <Pill color="#50e3c2">active</Pill>
                ) : (
                  <>
                    <button
                      onClick={() => handleActivate(selectedVersion.versionId)}
                      disabled={activating === selectedVersion.versionId}
                      className="px-3 py-[5px] rounded-md text-[11px] font-medium border border-[#1A3357] text-[#999] hover:border-[#50e3c2] hover:text-[#50e3c2] transition-colors disabled:opacity-40 flex items-center gap-1.5"
                    >
                      {activating === selectedVersion.versionId
                        ? <div className="w-3 h-3 border-[1.5px] border-[#254980] border-t-[#50e3c2] rounded-full animate-spin" />
                        : <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5"><path d="M20 6L9 17l-5-5" /></svg>}
                      Set Active
                    </button>
                    <button
                      onClick={() => handleDelete(selectedVersion.versionId)}
                      className="px-3 py-[5px] rounded-md text-[11px] font-medium border border-[#441111] text-[#ff4444] hover:border-[#662222] hover:bg-[#1a0808] transition-colors"
                    >
                      Delete
                    </button>
                  </>
                )}
              </div>
            )}

            <div className="ml-auto flex items-center gap-2">
              <span className="text-[11px] text-[#444]">{versions.length} version{versions.length !== 1 ? 's' : ''}</span>
              {/* Compare: show only when a different version is available */}
              {selectedVersion && versions.length > 1 && (
                <>
                  <div className="relative">
                    <select
                      value={diffVersionId}
                      onChange={e => setDiffVersionId(e.target.value)}
                      className="appearance-none bg-[#081529] border border-[#1A3357] rounded-md pl-3 pr-7 py-[5px] text-[11px] text-[#7596B8] font-mono focus:border-[#254980] focus:outline-none cursor-pointer hover:border-[#254980] transition-colors"
                    >
                      <option value="">Comparar com…</option>
                      {versions.filter(v => v.versionId !== selectedVersion.versionId).map(v => (
                        <option key={v.versionId} value={v.versionId}>{v.versionId}</option>
                      ))}
                    </select>
                    <svg className="absolute right-2 top-1/2 -translate-y-1/2 pointer-events-none text-[#3E5F7D]" width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M6 9l6 6 6-6" /></svg>
                  </div>
                  {diffVersionId && (
                    <button
                      onClick={() => setShowDiff(true)}
                      className="px-3 py-[5px] rounded-md text-[11px] font-medium border border-[#0057E0]/40 text-[#4D8EF5] hover:border-[#0057E0] transition-colors"
                    >
                      Ver Diff
                    </button>
                  )}
                </>
              )}
              <button
                onClick={() => { setShowCreateForm(true); setNewVersionContent(versions.find(v => v.isActive)?.content ?? '') }}
                className="px-3 py-[5px] rounded-md text-[11px] font-medium border border-dashed border-[#254980] text-[#7596B8] hover:border-[#3E5F7D] hover:text-[#B8CEE5] transition-colors flex items-center gap-1.5"
              >
                <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5"><path d="M12 5v14M5 12h14" /></svg>
                New Version
              </button>
            </div>
          </>
        )}
      </div>

      {/* Version stats summary */}
      {!loading && Object.keys(versionStats).length > 0 && (
        <div className="mb-6 bg-[#081529] border border-[#0C1D38] rounded-lg p-4">
          <div className="text-[10px] text-[#4A6B8A] uppercase tracking-wider mb-3">Performance por Versão</div>
          <div className="space-y-2">
            {versions.map(v => {
              const stats = versionStats[v.versionId]
              if (!stats) return null
              return (
                <div key={v.versionId} className="flex items-center gap-3">
                  <span className={`text-[11px] font-mono w-40 shrink-0 ${v.isActive ? 'text-[#50e3c2]' : 'text-[#7596B8]'}`}>
                    {v.versionId}{v.isActive ? ' ●' : ''}
                  </span>
                  <span className="text-[11px] text-[#4A6B8A]">{stats.calls} chamadas</span>
                  <span className="text-[11px] text-[#4A6B8A]">· avg {stats.avgTokens.toLocaleString()} tokens</span>
                  <span className="text-[11px] text-[#4A6B8A]">· avg {(stats.avgDurationMs / 1000).toFixed(1)}s</span>
                </div>
              )
            })}
          </div>
        </div>
      )}

      {/* Full-width prompt viewer */}
      {selectedVersion ? (
        <PromptViewer content={selectedVersion.content} />
      ) : !loading && (
        <div className="flex items-center justify-center py-16 border border-dashed border-[#0C1D38] rounded-lg">
          <span className="text-[12px] text-[#444]">{versions.length === 0 ? 'No versions found' : 'Select a version to view its content'}</span>
        </div>
      )}

      {/* Diff modal */}
      {showDiff && selectedVersion && diffVersionId && (() => {
        const other = versions.find(v => v.versionId === diffVersionId)
        if (!other) return null
        const diff = computeLineDiff(selectedVersion.content, other.content)
        return (
          <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70" onClick={() => setShowDiff(false)}>
            <div
              className="bg-[#081529] border border-[#1A3357] rounded-lg w-full max-w-3xl mx-4 flex flex-col"
              style={{ maxHeight: '80vh' }}
              onClick={e => e.stopPropagation()}
            >
              <div className="flex items-center justify-between px-5 py-4 border-b border-[#0C1D38] shrink-0">
                <div className="text-[13px] font-medium text-[#DCE8F5]">
                  Diff: <span className="text-red-400 font-mono">{selectedVersion.versionId}</span>
                  {' → '}
                  <span className="text-emerald-400 font-mono">{diffVersionId}</span>
                </div>
                <button onClick={() => setShowDiff(false)} className="text-[#4A6B8A] hover:text-[#999] transition-colors">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M18 6L6 18M6 6l12 12" /></svg>
                </button>
              </div>
              <div className="overflow-y-auto p-4 font-mono text-[12px] space-y-px">
                {diff.map((line, i) => (
                  <div
                    key={i}
                    className={
                      line.type === 'same' ? 'text-[#4A6B8A]'
                      : line.type === 'add' ? 'text-emerald-400 bg-emerald-500/5 px-1 rounded'
                      : 'text-red-400 bg-red-500/5 px-1 rounded line-through'
                    }
                  >
                    {line.type === 'add' ? '+ ' : line.type === 'remove' ? '- ' : '  '}
                    {line.text || ' '}
                  </div>
                ))}
              </div>
            </div>
          </div>
        )
      })()}
    </div>
  )
}

function ActionBtn({ label, danger, onClick }: { label: string; danger?: boolean; onClick?: () => void }) {
  const enabled = !!onClick
  return (
    <button
      disabled={!enabled}
      onClick={onClick}
      className={`px-3.5 py-[6px] rounded-md text-[12px] font-medium transition-colors ${
        enabled ? 'cursor-pointer' : 'cursor-not-allowed opacity-40 transition-none'
      }`}
      style={{
        border: `1px solid ${danger ? (enabled ? '#441111' : '#331111') : '#1A3357'}`,
        color: danger ? '#ff4444' : '#4A6B8A',
        background: danger ? '#110808' : '#081529',
        ...(enabled && !danger ? { color: '#999' } : {}),
      }}
      onMouseEnter={e => {
        if (!enabled) return
        if (danger) {
          e.currentTarget.style.background = '#1a0808'
          e.currentTarget.style.borderColor = '#662222'
        } else {
          e.currentTarget.style.borderColor = '#254980'
          e.currentTarget.style.color = '#B8CEE5'
        }
      }}
      onMouseLeave={e => {
        if (!enabled) return
        if (danger) {
          e.currentTarget.style.background = '#110808'
          e.currentTarget.style.borderColor = '#441111'
        } else {
          e.currentTarget.style.borderColor = '#1A3357'
          e.currentTarget.style.color = '#999'
        }
      }}
    >
      {label}
    </button>
  )
}

function DisabledAction({ label, danger }: { label: string; danger?: boolean }) {
  return <ActionBtn label={label} danger={danger} />
}

/* ── Layout blocks ──────────────────────────────────────────────────────────── */

function SectionBlock({ title, count, children, action }: { title: string; count?: number; children: React.ReactNode; action?: React.ReactNode }) {
  return (
    <section className="mb-10">
      <div className="flex items-center gap-3 mb-4">
        <h2 className="text-[12px] font-semibold text-[#7596B8] uppercase tracking-[0.06em]">{title}</h2>
        {count != null && count > 0 && <span className="text-[11px] text-[#444] tabular-nums">{count}</span>}
        {action && <span className="ml-auto">{action}</span>}
      </div>
      {children}
    </section>
  )
}

function EmptyBlock() {
  return (
    <div className="flex items-center justify-center py-10 border border-dashed border-[#0C1D38] rounded-lg">
      <span className="text-[12px] text-[#444]">None configured</span>
    </div>
  )
}

/* ── Utils ──────────────────────────────────────────────────────────────────── */

function fmtDate(iso: string): string {
  return new Date(iso).toLocaleDateString('pt-BR', {
    day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit',
  })
}
