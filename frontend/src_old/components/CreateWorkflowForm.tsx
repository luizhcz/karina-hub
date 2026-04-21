import { useState, useEffect, useCallback, useRef } from 'react'
import { api } from '../api'
import type { AgentDef, WorkflowDef, CreateWorkflowRequest, WorkflowAgentRef, WorkflowEdge, WorkflowExecutorStep, WorkflowTrigger, WorkflowConfiguration, CodeExecutorInfo } from '../types'

interface Props {
  onSaved: (wf: WorkflowDef) => void
  onCancel: () => void
  workflow?: WorkflowDef
  agents: AgentDef[]
}

const ORCHESTRATION_MODES = ['Sequential', 'Concurrent', 'Handoff', 'GroupChat', 'Graph']
const EDGE_TYPES = ['Direct', 'Conditional', 'Switch', 'FanOut', 'FanIn'] as const
const TRIGGER_TYPES = ['OnDemand', 'Scheduled', 'EventDriven'] as const

export function CreateWorkflowForm({ onSaved, onCancel, workflow: editWf, agents }: Props) {
  const isEdit = !!editWf

  /* ── state ───────────────────────────────────────────────────────────────── */
  const [name, setName] = useState(editWf?.name ?? '')
  const [id, setId] = useState(editWf?.id ?? '')
  const [idLocked, setIdLocked] = useState(!isEdit)
  const [description, setDescription] = useState(editWf?.description ?? '')
  const [version, setVersion] = useState(editWf?.version ?? '1.0.0')
  const [orchestrationMode, setOrchestrationMode] = useState(editWf?.orchestrationMode ?? 'Handoff')

  const [wfAgents, setWfAgents] = useState<WorkflowAgentRef[]>(editWf?.agents ?? [])
  const [executors, setExecutors] = useState<WorkflowExecutorStep[]>(editWf?.executors ?? [])
  const [edges, setEdges] = useState<WorkflowEdge[]>(editWf?.edges ?? [])

  const [triggerType, setTriggerType] = useState<string>(editWf?.trigger?.type ?? 'OnDemand')
  const [cronExpression, setCronExpression] = useState(editWf?.trigger?.cronExpression ?? '')
  const [eventTopic, setEventTopic] = useState(editWf?.trigger?.eventTopic ?? '')

  const [inputMode, setInputMode] = useState(editWf?.configuration?.inputMode ?? 'Standalone')
  const [timeoutSeconds, setTimeoutSeconds] = useState(String(editWf?.configuration?.timeoutSeconds ?? 300))
  const [maxAgentInvocations, setMaxAgentInvocations] = useState(String(editWf?.configuration?.maxAgentInvocations ?? 10))
  const [maxHistoryMessages, setMaxHistoryMessages] = useState(String(editWf?.configuration?.maxHistoryMessages ?? 20))
  const [enableHitl, setEnableHitl] = useState(editWf?.configuration?.enableHumanInTheLoop ?? false)
  const [maxRounds, setMaxRounds] = useState(editWf?.configuration?.maxRounds != null ? String(editWf.configuration.maxRounds) : '')
  const [exposeAsAgent, setExposeAsAgent] = useState(editWf?.configuration?.exposeAsAgent ?? false)
  const [exposedAgentDesc, setExposedAgentDesc] = useState(editWf?.configuration?.exposedAgentDescription ?? '')

  const [metadata, setMetadata] = useState<{ key: string; value: string }[]>(
    editWf?.metadata ? Object.entries(editWf.metadata).map(([key, value]) => ({ key, value })) : []
  )

  const [expanded, setExpanded] = useState<Set<string>>(() => {
    if (!editWf) return new Set<string>()
    const s = new Set<string>()
    if (editWf.executors?.length) s.add('executors')
    if (editWf.edges?.length) s.add('edges')
    if (editWf.trigger && editWf.trigger.type !== 'OnDemand') s.add('trigger')
    if (editWf.metadata && Object.keys(editWf.metadata).length > 0) s.add('metadata')
    s.add('configuration')
    return s
  })

  const [errors, setErrors] = useState<Record<string, string>>({})
  const [submitting, setSubmitting] = useState(false)
  const [apiError, setApiError] = useState('')
  const [dirty, setDirty] = useState(false)
  const [showDiscard, setShowDiscard] = useState(false)
  const discardTarget = useRef<(() => void) | null>(null)

  const [codeExecutors, setCodeExecutors] = useState<CodeExecutorInfo[]>([])

  useEffect(() => {
    api.getFunctions().then(r => setCodeExecutors(r.codeExecutors))
  }, [])

  /* ── helpers ─────────────────────────────────────────────────────────────── */
  const slugify = (s: string) =>
    s.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '')

  const handleNameChange = (v: string) => {
    setName(v); setDirty(true)
    if (idLocked) setId(slugify(v))
  }

  const toggleSection = (key: string) =>
    setExpanded(prev => { const n = new Set(prev); n.has(key) ? n.delete(key) : n.add(key); return n })

  const guardDirty = (action: () => void) => {
    if (dirty) { discardTarget.current = action; setShowDiscard(true) } else action()
  }

  const isGraph = orchestrationMode === 'Graph'
  const isGroupChat = orchestrationMode === 'GroupChat'
  const isHandoff = orchestrationMode === 'Handoff'
  const isChat = inputMode === 'Chat'

  /* ── agent helpers ──────────────────────────────────────────────────────── */
  const addAgent = () => { setWfAgents(prev => [...prev, { agentId: '' }]); setDirty(true) }
  const updateAgent = (i: number, patch: Partial<WorkflowAgentRef>) => {
    setWfAgents(prev => prev.map((a, idx) => idx === i ? { ...a, ...patch } : a)); setDirty(true)
  }
  const removeAgent = (i: number) => { setWfAgents(prev => prev.filter((_, idx) => idx !== i)); setDirty(true) }

  /* ── executor helpers ───────────────────────────────────────────────────── */
  const addExecutor = () => {
    setExecutors(prev => [...prev, { id: '', functionName: '' }]); setDirty(true)
    if (!expanded.has('executors')) toggleSection('executors')
  }
  const updateExecutor = (i: number, patch: Partial<WorkflowExecutorStep>) => {
    setExecutors(prev => prev.map((e, idx) => idx === i ? { ...e, ...patch } : e)); setDirty(true)
  }
  const removeExecutor = (i: number) => { setExecutors(prev => prev.filter((_, idx) => idx !== i)); setDirty(true) }

  /* ── edge helpers ───────────────────────────────────────────────────────── */
  const addEdge = () => {
    setEdges(prev => [...prev, { edgeType: 'Direct' as const }]); setDirty(true)
    if (!expanded.has('edges')) toggleSection('edges')
  }
  const updateEdge = (i: number, patch: Partial<WorkflowEdge>) => {
    setEdges(prev => prev.map((e, idx) => idx === i ? { ...e, ...patch } : e)); setDirty(true)
  }
  const removeEdge = (i: number) => { setEdges(prev => prev.filter((_, idx) => idx !== i)); setDirty(true) }

  /* ── metadata helpers ───────────────────────────────────────────────────── */
  const addMeta = () => {
    setMetadata(prev => [...prev, { key: '', value: '' }]); setDirty(true)
    if (!expanded.has('metadata')) toggleSection('metadata')
  }
  const updateMeta = (i: number, field: 'key' | 'value', v: string) => {
    setMetadata(prev => prev.map((m, idx) => idx === i ? { ...m, [field]: v } : m)); setDirty(true)
  }
  const removeMeta = (i: number) => { setMetadata(prev => prev.filter((_, idx) => idx !== i)); setDirty(true) }

  /* ── validation ─────────────────────────────────────────────────────────── */
  const validate = (): boolean => {
    const e: Record<string, string> = {}
    if (!name.trim()) e.name = 'Name is required'
    if (!id.trim()) e.id = 'ID is required'
    else if (!/^[a-z0-9][a-z0-9-]*$/.test(id)) e.id = 'Only lowercase letters, numbers, and hyphens'
    if (wfAgents.length === 0) e.agents = 'At least one agent is required'
    wfAgents.forEach((a, i) => { if (!a.agentId) e[`agent.${i}`] = 'Select an agent' })
    if (isGraph && edges.length === 0) e.edges = 'Graph mode requires at least one edge'
    executors.forEach((ex, i) => {
      if (!ex.id.trim()) e[`exec.${i}.id`] = 'ID is required'
      if (!ex.functionName.trim()) e[`exec.${i}.fn`] = 'Function is required'
    })
    setErrors(e)
    return Object.keys(e).length === 0
  }

  /* ── submit ─────────────────────────────────────────────────────────────── */
  const handleSubmit = useCallback(async () => {
    if (!validate()) return
    setSubmitting(true); setApiError('')

    const config: WorkflowConfiguration = {
      inputMode,
      timeoutSeconds: Number(timeoutSeconds) || 300,
      maxAgentInvocations: Number(maxAgentInvocations) || 10,
      enableHumanInTheLoop: enableHitl,
    }
    if (isChat) config.maxHistoryMessages = Number(maxHistoryMessages) || 20
    if (isGroupChat && maxRounds) config.maxRounds = Number(maxRounds)
    if (exposeAsAgent) { config.exposeAsAgent = true; if (exposedAgentDesc.trim()) config.exposedAgentDescription = exposedAgentDesc.trim() }

    const trigger: WorkflowTrigger | undefined = triggerType !== 'OnDemand'
      ? { type: triggerType as WorkflowTrigger['type'], ...(cronExpression ? { cronExpression } : {}), ...(eventTopic ? { eventTopic } : {}) }
      : undefined

    const metaObj = metadata.reduce<Record<string, string>>((acc, m) => { if (m.key.trim()) acc[m.key.trim()] = m.value; return acc }, {})

    const body: CreateWorkflowRequest = {
      id: id.trim(),
      name: name.trim(),
      orchestrationMode,
      agents: wfAgents.filter(a => a.agentId),
      ...(description.trim() ? { description: description.trim() } : {}),
      ...(version.trim() !== '1.0.0' ? { version: version.trim() } : {}),
      ...(executors.length > 0 ? { executors } : {}),
      ...(edges.length > 0 ? { edges } : {}),
      ...(trigger ? { trigger } : {}),
      configuration: config,
      ...(Object.keys(metaObj).length > 0 ? { metadata: metaObj } : {}),
    }

    try {
      const result = isEdit ? await api.updateWorkflow(id.trim(), body) : await api.createWorkflow(body)
      onSaved(result)
    } catch (err) {
      setApiError(err instanceof Error ? err.message : `Failed to ${isEdit ? 'update' : 'create'} workflow`)
    } finally { setSubmitting(false) }
  }, [name, id, description, version, orchestrationMode, wfAgents, executors, edges, triggerType, cronExpression, eventTopic, inputMode, timeoutSeconds, maxAgentInvocations, maxHistoryMessages, enableHitl, maxRounds, exposeAsAgent, exposedAgentDesc, metadata, onSaved, isEdit])

  /* ── node IDs for edge dropdowns ────────────────────────────────────────── */
  const allNodeIds = [
    ...wfAgents.filter(a => a.agentId).map(a => a.agentId),
    ...executors.filter(e => e.id).map(e => e.id),
  ]

  /* ── render ─────────────────────────────────────────────────────────────── */
  return (
    <div className="max-w-[860px] mx-auto px-10 pt-10 pb-16">
      {showDiscard && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60">
          <div className="bg-[#081529] border border-[#1A3357] rounded-lg p-6 max-w-sm w-full mx-4">
            <p className="text-[14px] text-[#DCE8F5] mb-1">Discard unsaved changes?</p>
            <p className="text-[12px] text-[#4A6B8A] mb-5">Your changes will be lost.</p>
            <div className="flex gap-2 justify-end">
              <button onClick={() => setShowDiscard(false)} className="px-3.5 py-[6px] rounded-md text-[12px] font-medium border border-[#1A3357] text-[#999] hover:border-[#254980] hover:text-[#B8CEE5] transition-colors">Keep editing</button>
              <button onClick={() => { setShowDiscard(false); discardTarget.current?.() }} className="px-3.5 py-[6px] rounded-md text-[12px] font-medium bg-[#ff4444] text-white hover:bg-[#e03030] transition-colors">Discard</button>
            </div>
          </div>
        </div>
      )}

      <header className="pb-8 mb-10 border-b border-[#0C1D38]">
        <div className="flex items-start justify-between gap-6">
          <div>
            <h1 className="text-[24px] font-semibold text-[#fafafa] tracking-[-0.025em]">{isEdit ? 'Edit Workflow' : 'New Workflow'}</h1>
            <p className="text-[13px] text-[#3E5F7D] mt-1">{isEdit ? 'Update the workflow definition' : 'Configure and create a new workflow definition'}</p>
          </div>
          <div className="flex gap-2 shrink-0 pt-1">
            <button onClick={() => guardDirty(onCancel)} className="px-3.5 py-[6px] rounded-md text-[12px] font-medium border border-[#1A3357] text-[#4A6B8A] hover:border-[#254980] hover:text-[#999] transition-colors">Cancel</button>
            <button onClick={handleSubmit} disabled={submitting} className="px-4 py-[7px] rounded-md text-[12px] font-medium bg-white text-black hover:bg-[#e0e0e0] transition-colors disabled:opacity-50 flex items-center gap-2">
              {submitting && <div className="w-3 h-3 border-[1.5px] border-[#999] border-t-[#254980] rounded-full animate-spin" />}
              {isEdit ? 'Save' : 'Create'}
            </button>
          </div>
        </div>
      </header>

      {apiError && (
        <div className="mb-8 bg-[#1a0808] border border-[#331111] text-[#ff4444] text-[13px] rounded-lg px-4 py-3 flex items-center justify-between">
          <span>{apiError}</span>
          <button onClick={() => setApiError('')} className="text-[#ff4444] hover:text-[#ff6666] ml-3 shrink-0">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M18 6L6 18M6 6l12 12" /></svg>
          </button>
        </div>
      )}

      {/* ── Basic Info ─────────────────────────────────────────────────────── */}
      <Section title="Basic Info">
        <div className="bg-[#081529] border border-[#0C1D38] rounded-lg p-5 space-y-4">
          <Field label="Name" required error={errors.name}>
            <Input value={name} onChange={v => { handleNameChange(v); setErrors(e => ({ ...e, name: '' })) }} placeholder="My Workflow" />
          </Field>
          <Field label="ID" required error={errors.id}>
            {isEdit ? (
              <Input value={id} onChange={() => {}} mono disabled />
            ) : (
              <div className="flex gap-2">
                <Input value={id} onChange={v => { setId(v); setDirty(true); setErrors(e => ({ ...e, id: '' })) }} placeholder="my-workflow" mono disabled={idLocked} className="flex-1" />
                <button onClick={() => setIdLocked(l => !l)} className="px-2.5 rounded-md border border-[#1A3357] text-[#3E5F7D] hover:text-[#999] hover:border-[#254980] transition-colors shrink-0" title={idLocked ? 'Unlock to edit manually' : 'Lock to auto-generate'}>
                  {idLocked ? (
                    <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><rect x="3" y="11" width="18" height="11" rx="2" /><path d="M7 11V7a5 5 0 0110 0v4" /></svg>
                  ) : (
                    <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><rect x="3" y="11" width="18" height="11" rx="2" /><path d="M7 11V7a5 5 0 019.9-1" /></svg>
                  )}
                </button>
              </div>
            )}
          </Field>
          <div className="grid grid-cols-2 gap-4">
            <Field label="Description">
              <Input value={description} onChange={v => { setDescription(v); setDirty(true) }} placeholder="Optional description" />
            </Field>
            <Field label="Version">
              <Input value={version} onChange={v => { setVersion(v); setDirty(true) }} placeholder="1.0.0" mono />
            </Field>
          </div>
        </div>
      </Section>

      {/* ── Orchestration ──────────────────────────────────────────────────── */}
      <Section title="Orchestration">
        <div className="bg-[#081529] border border-[#0C1D38] rounded-lg p-5">
          <Field label="Mode" required>
            <Select value={orchestrationMode} onChange={v => { setOrchestrationMode(v); setDirty(true) }} options={ORCHESTRATION_MODES} />
          </Field>
        </div>
      </Section>

      {/* ── Agents ─────────────────────────────────────────────────────────── */}
      <Section title="Agents" count={wfAgents.length} error={errors.agents} action={<GhostAction label="Add Agent" onClick={addAgent} />}>
        {wfAgents.length === 0 ? (
          <div className="flex items-center justify-center py-10 border border-dashed border-[#0C1D38] rounded-lg">
            <button onClick={addAgent} className="text-[12px] text-[#3E5F7D] hover:text-[#7596B8] transition-colors flex items-center gap-1.5">
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M12 5v14M5 12h14" /></svg>
              Add your first agent
            </button>
          </div>
        ) : (
          <div className="space-y-3">
            {wfAgents.map((ag, i) => (
              <div key={i} className="bg-[#081529] border border-[#0C1D38] rounded-lg p-4">
                <div className="flex items-center gap-3">
                  <span className="w-[6px] h-[6px] rounded-full bg-[#3291ff] shrink-0" />
                  <div className="flex-1 space-y-3">
                    <Field label="Agent" required error={errors[`agent.${i}`]}>
                      <select
                        value={ag.agentId}
                        onChange={e => updateAgent(i, { agentId: e.target.value })}
                        className="w-full bg-[#04091A] border border-[#1A3357] rounded-md px-3 py-[7px] text-[13px] text-[#DCE8F5] font-mono focus:border-[#254980] focus:outline-none transition-colors appearance-none cursor-pointer"
                        style={{ backgroundImage: `url("data:image/svg+xml,%3Csvg width='12' height='12' viewBox='0 0 24 24' fill='none' stroke='%23666' stroke-width='2' xmlns='http://www.w3.org/2000/svg'%3E%3Cpath d='M6 9l6 6 6-6'/%3E%3C/svg%3E")`, backgroundRepeat: 'no-repeat', backgroundPosition: 'right 10px center' }}
                      >
                        <option value="">Select an agent...</option>
                        {agents.map(a => <option key={a.id} value={a.id}>{a.name} ({a.id})</option>)}
                      </select>
                    </Field>
                    {isGroupChat && (
                      <Field label="Role">
                        <Select value={ag.role ?? 'participant'} onChange={v => updateAgent(i, { role: v })} options={['manager', 'participant']} />
                      </Field>
                    )}
                    {isHandoff && (
                      <Field label="Handoff Condition">
                        <Input value={ag.handoffCondition ?? ''} onChange={v => updateAgent(i, { handoffCondition: v || undefined })} placeholder="Keyword that triggers handoff to this agent" />
                      </Field>
                    )}
                  </div>
                  <button onClick={() => removeAgent(i)} className="p-1 text-[#444] hover:text-[#ff4444] transition-colors shrink-0 self-start mt-6">
                    <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M3 6h18M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6M8 6V4a2 2 0 012-2h4a2 2 0 012 2v2" /></svg>
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </Section>

      {/* ── Executors (Graph only) ─────────────────────────────────────────── */}
      {isGraph && (
        <Collapsible title="Executors" count={executors.length} open={expanded.has('executors')} onToggle={() => toggleSection('executors')} action={<GhostAction label="Add" onClick={addExecutor} />}>
          {executors.length === 0 ? (
            <div className="flex items-center justify-center py-10 border border-dashed border-[#0C1D38] rounded-lg">
              <button onClick={addExecutor} className="text-[12px] text-[#3E5F7D] hover:text-[#7596B8] transition-colors flex items-center gap-1.5">
                <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M12 5v14M5 12h14" /></svg>
                Add executor step
              </button>
            </div>
          ) : (
            <div className="space-y-3">
              {executors.map((ex, i) => (
                <div key={i} className="bg-[#081529] border border-[#0C1D38] rounded-lg p-4 space-y-3">
                  <div className="flex items-center gap-3">
                    <span className="w-[6px] h-[6px] rounded-full bg-[#50e3c2] shrink-0" />
                    <div className="flex-1 grid grid-cols-2 gap-3">
                      <Field label="Step ID" required error={errors[`exec.${i}.id`]}>
                        <Input value={ex.id} onChange={v => updateExecutor(i, { id: v })} placeholder="step-1" mono />
                      </Field>
                      <Field label="Function" required error={errors[`exec.${i}.fn`]}>
                        {codeExecutors.length > 0 ? (
                          <select value={ex.functionName} onChange={e => updateExecutor(i, { functionName: e.target.value })} className="w-full bg-[#04091A] border border-[#1A3357] rounded-md px-3 py-[7px] text-[13px] text-[#DCE8F5] font-mono focus:border-[#254980] focus:outline-none transition-colors appearance-none cursor-pointer" style={{ backgroundImage: `url("data:image/svg+xml,%3Csvg width='12' height='12' viewBox='0 0 24 24' fill='none' stroke='%23666' stroke-width='2' xmlns='http://www.w3.org/2000/svg'%3E%3Cpath d='M6 9l6 6 6-6'/%3E%3C/svg%3E")`, backgroundRepeat: 'no-repeat', backgroundPosition: 'right 10px center' }}>
                            <option value="">Select...</option>
                            {codeExecutors.map(ce => <option key={ce.name} value={ce.name}>{ce.name}</option>)}
                          </select>
                        ) : (
                          <Input value={ex.functionName} onChange={v => updateExecutor(i, { functionName: v })} placeholder="fetch_ativos" mono />
                        )}
                      </Field>
                    </div>
                    <button onClick={() => removeExecutor(i)} className="p-1 text-[#444] hover:text-[#ff4444] transition-colors shrink-0 self-start mt-6">
                      <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M3 6h18M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6M8 6V4a2 2 0 012-2h4a2 2 0 012 2v2" /></svg>
                    </button>
                  </div>
                  <Field label="Description">
                    <Input value={ex.description ?? ''} onChange={v => updateExecutor(i, { description: v || undefined })} placeholder="Optional step description" />
                  </Field>
                </div>
              ))}
            </div>
          )}
        </Collapsible>
      )}

      {/* ── Edges (Graph only) ─────────────────────────────────────────────── */}
      {isGraph && (
        <Collapsible title="Edges" count={edges.length} open={expanded.has('edges')} onToggle={() => toggleSection('edges')} action={<GhostAction label="Add" onClick={addEdge} />}>
          {errors.edges && <p className="text-[11px] text-[#ff4444] mb-2">{errors.edges}</p>}
          {edges.length === 0 ? (
            <div className="flex items-center justify-center py-10 border border-dashed border-[#0C1D38] rounded-lg">
              <button onClick={addEdge} className="text-[12px] text-[#3E5F7D] hover:text-[#7596B8] transition-colors flex items-center gap-1.5">
                <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M12 5v14M5 12h14" /></svg>
                Add edge
              </button>
            </div>
          ) : (
            <div className="space-y-3">
              {edges.map((edge, i) => (
                <div key={i} className="bg-[#081529] border border-[#0C1D38] rounded-lg p-4 space-y-3">
                  <div className="flex items-center gap-3">
                    <span className="w-[6px] h-[6px] rounded-full bg-[#0057E0] shrink-0" />
                    <div className="flex-1 grid grid-cols-3 gap-3">
                      <Field label="From">
                        <NodeSelect value={edge.from ?? ''} onChange={v => updateEdge(i, { from: v || undefined })} nodeIds={allNodeIds} />
                      </Field>
                      <Field label="To">
                        <NodeSelect value={edge.to ?? ''} onChange={v => updateEdge(i, { to: v || undefined })} nodeIds={allNodeIds} />
                      </Field>
                      <Field label="Type">
                        <Select value={edge.edgeType} onChange={v => updateEdge(i, { edgeType: v as WorkflowEdge['edgeType'] })} options={[...EDGE_TYPES]} />
                      </Field>
                    </div>
                    <button onClick={() => removeEdge(i)} className="p-1 text-[#444] hover:text-[#ff4444] transition-colors shrink-0 self-start mt-6">
                      <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M3 6h18M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6M8 6V4a2 2 0 012-2h4a2 2 0 012 2v2" /></svg>
                    </button>
                  </div>
                  {edge.edgeType === 'Conditional' && (
                    <Field label="Condition"><Input value={edge.condition ?? ''} onChange={v => updateEdge(i, { condition: v || undefined })} placeholder="Substring match on output" /></Field>
                  )}
                  {(edge.edgeType === 'FanOut' || edge.edgeType === 'Switch') && (
                    <Field label="Targets"><Input value={edge.targets?.join(', ') ?? ''} onChange={v => updateEdge(i, { targets: v.split(',').map(s => s.trim()).filter(Boolean) })} placeholder="node1, node2" mono /></Field>
                  )}
                  {edge.edgeType === 'FanIn' && (
                    <Field label="Sources"><Input value={(edge as { sources?: string[] }).sources?.join(', ') ?? ''} onChange={v => updateEdge(i, { ...edge, sources: v.split(',').map(s => s.trim()).filter(Boolean) } as WorkflowEdge)} placeholder="node1, node2" mono /></Field>
                  )}
                </div>
              ))}
            </div>
          )}
        </Collapsible>
      )}

      {/* ── Configuration ──────────────────────────────────────────────────── */}
      <Collapsible title="Configuration" open={expanded.has('configuration')} onToggle={() => toggleSection('configuration')}>
        <div className="bg-[#081529] border border-[#0C1D38] rounded-lg p-5 space-y-4">
          <div className="grid grid-cols-2 gap-4">
            <Field label="Input Mode">
              <Select value={inputMode} onChange={v => { setInputMode(v); setDirty(true) }} options={['Standalone', 'Chat']} />
            </Field>
            <Field label="Timeout (seconds)">
              <Input value={timeoutSeconds} onChange={v => { setTimeoutSeconds(v); setDirty(true) }} placeholder="300" type="number" />
            </Field>
          </div>
          <div className="grid grid-cols-2 gap-4">
            <Field label="Max Agent Invocations">
              <Input value={maxAgentInvocations} onChange={v => { setMaxAgentInvocations(v); setDirty(true) }} placeholder="10" type="number" />
            </Field>
            {isChat && (
              <Field label="Max History Messages">
                <Input value={maxHistoryMessages} onChange={v => { setMaxHistoryMessages(v); setDirty(true) }} placeholder="20" type="number" />
              </Field>
            )}
          </div>
          {isGroupChat && (
            <Field label="Max Rounds">
              <Input value={maxRounds} onChange={v => { setMaxRounds(v); setDirty(true) }} placeholder="e.g. 5" type="number" />
            </Field>
          )}
          <label className="flex items-center gap-2 cursor-pointer">
            <input type="checkbox" checked={enableHitl} onChange={e => { setEnableHitl(e.target.checked); setDirty(true) }} className="accent-[#3291ff]" />
            <span className="text-[12px] text-[#7596B8]">Enable Human-in-the-Loop</span>
          </label>
          <label className="flex items-center gap-2 cursor-pointer">
            <input type="checkbox" checked={exposeAsAgent} onChange={e => { setExposeAsAgent(e.target.checked); setDirty(true) }} className="accent-[#3291ff]" />
            <span className="text-[12px] text-[#7596B8]">Expose as Agent</span>
          </label>
          {exposeAsAgent && (
            <Field label="Exposed Agent Description">
              <Input value={exposedAgentDesc} onChange={v => { setExposedAgentDesc(v); setDirty(true) }} placeholder="Description when exposed as agent" />
            </Field>
          )}
        </div>
      </Collapsible>

      {/* ── Trigger ────────────────────────────────────────────────────────── */}
      <Collapsible title="Trigger" open={expanded.has('trigger')} onToggle={() => toggleSection('trigger')}>
        <div className="bg-[#081529] border border-[#0C1D38] rounded-lg p-5 space-y-4">
          <Field label="Type">
            <Select value={triggerType} onChange={v => { setTriggerType(v); setDirty(true) }} options={[...TRIGGER_TYPES]} />
          </Field>
          {triggerType === 'Scheduled' && (
            <Field label="Cron Expression">
              <Input value={cronExpression} onChange={v => { setCronExpression(v); setDirty(true) }} placeholder="0 */6 * * *" mono />
            </Field>
          )}
          {triggerType === 'EventDriven' && (
            <Field label="Event Topic">
              <Input value={eventTopic} onChange={v => { setEventTopic(v); setDirty(true) }} placeholder="orders.created" mono />
            </Field>
          )}
        </div>
      </Collapsible>

      {/* ── Metadata ───────────────────────────────────────────────────────── */}
      <Collapsible title="Metadata" count={metadata.length} open={expanded.has('metadata')} onToggle={() => toggleSection('metadata')} action={<GhostAction label="Add" onClick={addMeta} />}>
        {metadata.length === 0 ? (
          <div className="flex items-center justify-center py-10 border border-dashed border-[#0C1D38] rounded-lg">
            <button onClick={addMeta} className="text-[12px] text-[#3E5F7D] hover:text-[#7596B8] transition-colors flex items-center gap-1.5">
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M12 5v14M5 12h14" /></svg>
              Add metadata entry
            </button>
          </div>
        ) : (
          <div className="space-y-2">
            {metadata.map((m, i) => (
              <div key={i} className="flex gap-2 items-center">
                <Input value={m.key} onChange={v => updateMeta(i, 'key', v)} placeholder="key" mono className="flex-1" />
                <Input value={m.value} onChange={v => updateMeta(i, 'value', v)} placeholder="value" className="flex-1" />
                <button onClick={() => removeMeta(i)} className="p-1.5 text-[#444] hover:text-[#ff4444] transition-colors shrink-0">
                  <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M3 6h18M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6M8 6V4a2 2 0 012-2h4a2 2 0 012 2v2" /></svg>
                </button>
              </div>
            ))}
          </div>
        )}
      </Collapsible>
    </div>
  )
}

/* ── Form primitives ───────────────────────────────────────────────────────── */

function Section({ title, count, error, action, children }: { title: string; count?: number; error?: string; action?: React.ReactNode; children: React.ReactNode }) {
  return (
    <section className="mb-8">
      <div className="flex items-center gap-2 mb-4">
        <h2 className="text-[12px] font-semibold text-[#7596B8] uppercase tracking-[0.06em]">{title}</h2>
        {count != null && count > 0 && <span className="text-[11px] text-[#444] tabular-nums">{count}</span>}
        {action && <span className="ml-auto">{action}</span>}
      </div>
      {error && <p className="text-[11px] text-[#ff4444] mb-2">{error}</p>}
      {children}
    </section>
  )
}

function Collapsible({ title, count, open, onToggle, children, action }: {
  title: string; count?: number; open: boolean; onToggle: () => void; children: React.ReactNode; action?: React.ReactNode
}) {
  return (
    <section className="mb-8">
      <button onClick={onToggle} className="flex items-center gap-2 mb-4 group w-full text-left">
        <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="#666" strokeWidth="2" className={`transition-transform ${open ? 'rotate-90' : ''}`}><path d="M9 18l6-6-6-6" /></svg>
        <h2 className="text-[12px] font-semibold text-[#7596B8] uppercase tracking-[0.06em] group-hover:text-[#aaa] transition-colors">{title}</h2>
        {count != null && count > 0 && <span className="text-[11px] text-[#444] tabular-nums">{count}</span>}
        {action && <span className="ml-auto">{action}</span>}
      </button>
      {open && children}
    </section>
  )
}

function Field({ label, required, error, children }: { label: string; required?: boolean; error?: string; children: React.ReactNode }) {
  return (
    <div>
      <label className="block text-[12px] text-[#4A6B8A] mb-1.5">{label}{required && <span className="text-[#ff4444] ml-0.5">*</span>}</label>
      {children}
      {error && <p className="text-[11px] text-[#ff4444] mt-1">{error}</p>}
    </div>
  )
}

function Input({ value, onChange, placeholder, mono, disabled, type, className }: {
  value: string; onChange: (v: string) => void; placeholder?: string; mono?: boolean; disabled?: boolean; type?: string; className?: string
}) {
  return (
    <input
      value={value} onChange={e => onChange(e.target.value)} placeholder={placeholder} disabled={disabled} type={type}
      className={`w-full bg-[#04091A] border border-[#1A3357] rounded-md px-3 py-[7px] text-[13px] text-[#DCE8F5] placeholder:text-[#444] focus:border-[#254980] focus:outline-none transition-colors disabled:opacity-50 disabled:cursor-not-allowed ${mono ? 'font-mono' : ''} ${className ?? ''}`}
    />
  )
}

function Select({ value, onChange, options }: { value: string; onChange: (v: string) => void; options: string[] }) {
  return (
    <select
      value={value} onChange={e => onChange(e.target.value)}
      className="w-full bg-[#04091A] border border-[#1A3357] rounded-md px-3 py-[7px] text-[13px] text-[#DCE8F5] focus:border-[#254980] focus:outline-none transition-colors appearance-none cursor-pointer"
      style={{ backgroundImage: `url("data:image/svg+xml,%3Csvg width='12' height='12' viewBox='0 0 24 24' fill='none' stroke='%23666' stroke-width='2' xmlns='http://www.w3.org/2000/svg'%3E%3Cpath d='M6 9l6 6 6-6'/%3E%3C/svg%3E")`, backgroundRepeat: 'no-repeat', backgroundPosition: 'right 10px center' }}
    >
      {options.map(o => <option key={o} value={o}>{o}</option>)}
    </select>
  )
}

function NodeSelect({ value, onChange, nodeIds }: { value: string; onChange: (v: string) => void; nodeIds: string[] }) {
  return (
    <select
      value={value} onChange={e => onChange(e.target.value)}
      className="w-full bg-[#04091A] border border-[#1A3357] rounded-md px-3 py-[7px] text-[13px] text-[#DCE8F5] font-mono focus:border-[#254980] focus:outline-none transition-colors appearance-none cursor-pointer"
      style={{ backgroundImage: `url("data:image/svg+xml,%3Csvg width='12' height='12' viewBox='0 0 24 24' fill='none' stroke='%23666' stroke-width='2' xmlns='http://www.w3.org/2000/svg'%3E%3Cpath d='M6 9l6 6 6-6'/%3E%3C/svg%3E")`, backgroundRepeat: 'no-repeat', backgroundPosition: 'right 10px center' }}
    >
      <option value="">Select node...</option>
      {nodeIds.map(n => <option key={n} value={n}>{n}</option>)}
    </select>
  )
}

function GhostAction({ label, onClick }: { label: string; onClick: (e: React.MouseEvent) => void }) {
  return (
    <button onClick={e => { e.stopPropagation(); onClick(e) }} className="text-[11px] text-[#3E5F7D] hover:text-[#999] transition-colors flex items-center gap-1">
      <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5"><path d="M12 5v14M5 12h14" /></svg>
      {label}
    </button>
  )
}
