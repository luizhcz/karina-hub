import { useState, useEffect, useCallback, useRef } from 'react'
import { api } from '../api'
import type { AgentDef, CreateAgentRequest, AgentToolDef, AgentStructuredOutput, AgentMiddlewareConfig, FunctionToolInfo } from '../types'

interface Props {
  onSaved: (agent: AgentDef) => void
  onCancel: () => void
  agent?: AgentDef
}

/* ════════════════════════════════════════════════════════════════════════════
   CreateAgentForm — Vercel-inspired progressive disclosure form
   Supports both create and edit modes via optional `agent` prop
   ════════════════════════════════════════════════════════════════════════════ */

export function CreateAgentForm({ onSaved, onCancel, agent: editAgent }: Props) {
  const isEdit = !!editAgent

  /* ── state ───────────────────────────────────────────────────────────────── */
  const [name, setName] = useState(editAgent?.name ?? '')
  const [id, setId] = useState(editAgent?.id ?? '')
  const [idLocked, setIdLocked] = useState(!isEdit)
  const [description, setDescription] = useState(editAgent?.description ?? '')
  const [deploymentName, setDeploymentName] = useState(editAgent?.model.deploymentName ?? '')
  const [temperature, setTemperature] = useState(editAgent?.model.temperature != null ? String(editAgent.model.temperature) : '')
  const [maxTokens, setMaxTokens] = useState(editAgent?.model.maxTokens != null ? String(editAgent.model.maxTokens) : '')
  const [providerType, setProviderType] = useState(editAgent?.provider?.type ?? 'AzureFoundry')
  const [clientType, setClientType] = useState(editAgent?.provider?.clientType ?? 'ChatCompletion')
  const [endpoint, setEndpoint] = useState(editAgent?.provider?.endpoint ?? '')
  const [instructions, setInstructions] = useState(editAgent?.instructions ?? '')
  const [tools, setTools] = useState<AgentToolDef[]>(editAgent?.tools ?? [])
  const [responseFormat, setResponseFormat] = useState(editAgent?.structuredOutput?.responseFormat ?? 'text')
  const [schemaName, setSchemaName] = useState(editAgent?.structuredOutput?.schemaName ?? '')
  const [schemaDescription, setSchemaDescription] = useState(editAgent?.structuredOutput?.schemaDescription ?? '')
  const [schemaJson, setSchemaJson] = useState(editAgent?.structuredOutput?.schema ? JSON.stringify(editAgent.structuredOutput.schema, null, 2) : '')
  const [middlewares, setMiddlewares] = useState<AgentMiddlewareConfig[]>(editAgent?.middlewares ?? [])
  const [metadata, setMetadata] = useState<{ key: string; value: string }[]>(
    editAgent?.metadata ? Object.entries(editAgent.metadata).map(([key, value]) => ({ key, value })) : []
  )

  // In edit mode, auto-expand sections that have data
  const [expanded, setExpanded] = useState<Set<string>>(() => {
    if (!editAgent) return new Set<string>()
    const sections = new Set<string>()
    if (editAgent.provider?.type || editAgent.provider?.clientType || editAgent.provider?.endpoint) sections.add('provider')
    if (editAgent.instructions) sections.add('instructions')
    if (editAgent.tools?.length) sections.add('tools')
    if (editAgent.structuredOutput) sections.add('structured')
    if (editAgent.middlewares?.length) sections.add('middlewares')
    if (editAgent.metadata && Object.keys(editAgent.metadata).length > 0) sections.add('metadata')
    return sections
  })
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [submitting, setSubmitting] = useState(false)
  const [apiError, setApiError] = useState('')
  const [dirty, setDirty] = useState(false)
  const [showDiscard, setShowDiscard] = useState(false)
  const discardTarget = useRef<(() => void) | null>(null)

  const [availableFunctions, setAvailableFunctions] = useState<FunctionToolInfo[]>([])

  useEffect(() => {
    api.getFunctions().then(r => setAvailableFunctions(r.functionTools))
  }, [])

  /* ── helpers ─────────────────────────────────────────────────────────────── */
  const slugify = (s: string) =>
    s.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '')

  const handleNameChange = (v: string) => {
    setName(v)
    setDirty(true)
    if (idLocked) setId(slugify(v))
  }

  const toggleSection = (key: string) =>
    setExpanded(prev => {
      const next = new Set(prev)
      next.has(key) ? next.delete(key) : next.add(key)
      return next
    })

  const guardDirty = (action: () => void) => {
    if (dirty) {
      discardTarget.current = action
      setShowDiscard(true)
    } else {
      action()
    }
  }

  /* ── tool helpers ────────────────────────────────────────────────────────── */
  const addTool = () => {
    setTools(prev => [...prev, { type: 'function' }])
    setDirty(true)
    if (!expanded.has('tools')) toggleSection('tools')
  }

  const updateTool = (i: number, patch: Partial<AgentToolDef>) => {
    setTools(prev => prev.map((t, idx) => (idx === i ? { ...t, ...patch } : t)))
    setDirty(true)
  }

  const removeTool = (i: number) => {
    setTools(prev => prev.filter((_, idx) => idx !== i))
    setDirty(true)
  }

  /* ── middleware helpers ──────────────────────────────────────────────────── */
  const addMiddleware = () => {
    setMiddlewares(prev => [...prev, { type: 'AccountGuard', enabled: true, settings: {} }])
    setDirty(true)
    if (!expanded.has('middlewares')) toggleSection('middlewares')
  }

  const updateMiddleware = (i: number, patch: Partial<AgentMiddlewareConfig>) => {
    setMiddlewares(prev => prev.map((m, idx) => (idx === i ? { ...m, ...patch } : m)))
    setDirty(true)
  }

  const removeMiddleware = (i: number) => {
    setMiddlewares(prev => prev.filter((_, idx) => idx !== i))
    setDirty(true)
  }

  /* ── metadata helpers ────────────────────────────────────────────────────── */
  const addMeta = () => {
    setMetadata(prev => [...prev, { key: '', value: '' }])
    setDirty(true)
    if (!expanded.has('metadata')) toggleSection('metadata')
  }

  const updateMeta = (i: number, field: 'key' | 'value', v: string) => {
    setMetadata(prev => prev.map((m, idx) => (idx === i ? { ...m, [field]: v } : m)))
    setDirty(true)
  }

  const removeMeta = (i: number) => {
    setMetadata(prev => prev.filter((_, idx) => idx !== i))
    setDirty(true)
  }

  /* ── validation ──────────────────────────────────────────────────────────── */
  const validate = (): boolean => {
    const e: Record<string, string> = {}
    if (!name.trim()) e.name = 'Name is required'
    if (!id.trim()) e.id = 'ID is required'
    else if (!/^[a-z0-9][a-z0-9-]*$/.test(id)) e.id = 'Only lowercase letters, numbers, and hyphens'
    if (!deploymentName.trim()) e.deploymentName = 'Deployment name is required'
    if (temperature && isNaN(Number(temperature))) e.temperature = 'Must be a number'
    if (maxTokens && isNaN(Number(maxTokens))) e.maxTokens = 'Must be a number'

    tools.forEach((t, i) => {
      if (t.type === 'function' && !t.name?.trim()) e[`tool.${i}.name`] = 'Function name is required'
      if (t.type === 'mcp') {
        if (!t.serverLabel?.trim()) e[`tool.${i}.serverLabel`] = 'Server label is required'
        if (!t.serverUrl?.trim()) e[`tool.${i}.serverUrl`] = 'Server URL is required'
        if (!t.allowedTools?.length) e[`tool.${i}.allowedTools`] = 'At least one tool is required'
      }
    })

    if (responseFormat === 'json_schema' && expanded.has('structured')) {
      if (!schemaJson.trim()) e.schema = 'Schema is required for json_schema format'
      else {
        try { JSON.parse(schemaJson) } catch { e.schema = 'Invalid JSON' }
      }
    }

    setErrors(e)
    return Object.keys(e).length === 0
  }

  /* ── submit ──────────────────────────────────────────────────────────────── */
  const handleSubmit = useCallback(async () => {
    if (!validate()) return
    setSubmitting(true)
    setApiError('')

    const body: CreateAgentRequest = {
      id: id.trim(),
      name: name.trim(),
      model: {
        deploymentName: deploymentName.trim(),
        ...(temperature ? { temperature: Number(temperature) } : {}),
        ...(maxTokens ? { maxTokens: Number(maxTokens) } : {}),
      },
    }

    if (description.trim()) body.description = description.trim()
    if (instructions.trim()) body.instructions = instructions.trim()

    if (providerType !== 'AzureFoundry' || clientType !== 'ChatCompletion' || endpoint.trim()) {
      body.provider = {
        ...(providerType !== 'AzureFoundry' ? { type: providerType } : {}),
        ...(clientType !== 'ChatCompletion' ? { clientType } : {}),
        ...(endpoint.trim() ? { endpoint: endpoint.trim() } : {}),
      }
    }

    if (tools.length > 0) body.tools = tools
    if (middlewares.length > 0) body.middlewares = middlewares
    if (expanded.has('structured') && responseFormat !== 'text') {
      const so: AgentStructuredOutput = { responseFormat }
      if (schemaName.trim()) so.schemaName = schemaName.trim()
      if (schemaDescription.trim()) so.schemaDescription = schemaDescription.trim()
      if (schemaJson.trim()) {
        try { so.schema = JSON.parse(schemaJson) } catch { /* validated earlier */ }
      }
      body.structuredOutput = so
    }

    const metaObj = metadata.reduce<Record<string, string>>((acc, m) => {
      if (m.key.trim()) acc[m.key.trim()] = m.value
      return acc
    }, {})
    if (Object.keys(metaObj).length > 0) body.metadata = metaObj

    try {
      const result = isEdit
        ? await api.updateAgent(id.trim(), body)
        : await api.createAgent(body)
      onSaved(result)
    } catch (err) {
      setApiError(err instanceof Error ? err.message : `Failed to ${isEdit ? 'update' : 'create'} agent`)
    } finally {
      setSubmitting(false)
    }
  }, [name, id, description, deploymentName, temperature, maxTokens, providerType, clientType, endpoint, instructions, tools, middlewares, responseFormat, schemaName, schemaDescription, schemaJson, metadata, expanded, onSaved, isEdit])

  /* ── render ──────────────────────────────────────────────────────────────── */
  return (
    <div className="max-w-[860px] mx-auto px-10 pt-10 pb-16">
      {/* Discard confirmation overlay */}
      {showDiscard && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60">
          <div className="bg-[#081529] border border-[#1A3357] rounded-lg p-6 max-w-sm w-full mx-4">
            <p className="text-[14px] text-[#DCE8F5] mb-1">Discard unsaved changes?</p>
            <p className="text-[12px] text-[#4A6B8A] mb-5">Your changes will be lost.</p>
            <div className="flex gap-2 justify-end">
              <button
                onClick={() => setShowDiscard(false)}
                className="px-3.5 py-[6px] rounded-md text-[12px] font-medium border border-[#1A3357] text-[#999] hover:border-[#254980] hover:text-[#B8CEE5] transition-colors"
              >
                Keep editing
              </button>
              <button
                onClick={() => { setShowDiscard(false); discardTarget.current?.() }}
                className="px-3.5 py-[6px] rounded-md text-[12px] font-medium bg-[#ff4444] text-white hover:bg-[#e03030] transition-colors"
              >
                Discard
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Header */}
      <header className="pb-8 mb-10 border-b border-[#0C1D38]">
        <div className="flex items-start justify-between gap-6">
          <div>
            <h1 className="text-[24px] font-semibold text-[#fafafa] tracking-[-0.025em]">{isEdit ? 'Edit Agent' : 'New Agent'}</h1>
            <p className="text-[13px] text-[#3E5F7D] mt-1">{isEdit ? 'Update the agent definition' : 'Configure and create a new agent definition'}</p>
          </div>
          <div className="flex gap-2 shrink-0 pt-1">
            <button
              onClick={() => guardDirty(onCancel)}
              className="px-3.5 py-[6px] rounded-md text-[12px] font-medium border border-[#1A3357] text-[#4A6B8A] hover:border-[#254980] hover:text-[#999] transition-colors"
            >
              Cancel
            </button>
            <button
              onClick={handleSubmit}
              disabled={submitting}
              className="px-4 py-[7px] rounded-md text-[12px] font-medium bg-white text-black hover:bg-[#e0e0e0] transition-colors disabled:opacity-50 flex items-center gap-2"
            >
              {submitting && <div className="w-3 h-3 border-[1.5px] border-[#999] border-t-[#254980] rounded-full animate-spin" />}
              {isEdit ? 'Save' : 'Create'}
            </button>
          </div>
        </div>
      </header>

      {/* API error banner */}
      {apiError && (
        <div className="mb-8 bg-[#1a0808] border border-[#331111] text-[#ff4444] text-[13px] rounded-lg px-4 py-3 flex items-center justify-between">
          <span>{apiError}</span>
          <button onClick={() => setApiError('')} className="text-[#ff4444] hover:text-[#ff6666] ml-3 shrink-0">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M18 6L6 18M6 6l12 12" /></svg>
          </button>
        </div>
      )}

      {/* ── Basic Info (always visible) ─────────────────────────────────────── */}
      <Section title="Basic Info">
        <div className="bg-[#081529] border border-[#0C1D38] rounded-lg p-5 space-y-4">
          <Field label="Name" required error={errors.name}>
            <Input value={name} onChange={v => { handleNameChange(v); setErrors(e => ({ ...e, name: '' })) }} placeholder="My Agent" />
          </Field>
          <Field label="ID" required error={errors.id}>
            {isEdit ? (
              <Input value={id} onChange={() => {}} mono disabled />
            ) : (
              <div className="flex gap-2">
                <Input
                  value={id}
                  onChange={v => { setId(v); setDirty(true); setErrors(e => ({ ...e, id: '' })) }}
                  placeholder="my-agent"
                  mono
                  disabled={idLocked}
                  className="flex-1"
                />
                <button
                  onClick={() => setIdLocked(l => !l)}
                  className="px-2.5 rounded-md border border-[#1A3357] text-[#3E5F7D] hover:text-[#999] hover:border-[#254980] transition-colors shrink-0"
                  title={idLocked ? 'Unlock to edit manually' : 'Lock to auto-generate'}
                >
                  {idLocked ? (
                    <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><rect x="3" y="11" width="18" height="11" rx="2" /><path d="M7 11V7a5 5 0 0110 0v4" /></svg>
                  ) : (
                    <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><rect x="3" y="11" width="18" height="11" rx="2" /><path d="M7 11V7a5 5 0 019.9-1" /></svg>
                  )}
                </button>
              </div>
            )}
          </Field>
          <Field label="Description">
            <Input value={description} onChange={v => { setDescription(v); setDirty(true) }} placeholder="Optional description" />
          </Field>
        </div>
      </Section>

      {/* ── Model (always visible) ──────────────────────────────────────────── */}
      <Section title="Model">
        <div className="bg-[#081529] border border-[#0C1D38] rounded-lg p-5 space-y-4">
          <Field label="Deployment Name" required error={errors.deploymentName}>
            <Input value={deploymentName} onChange={v => { setDeploymentName(v); setDirty(true); setErrors(e => ({ ...e, deploymentName: '' })) }} placeholder="gpt-4o" mono />
          </Field>
          <div className="grid grid-cols-2 gap-4">
            <Field label="Temperature" error={errors.temperature}>
              <Input value={temperature} onChange={v => { setTemperature(v); setDirty(true) }} placeholder="0.7" type="number" />
            </Field>
            <Field label="Max Tokens" error={errors.maxTokens}>
              <Input value={maxTokens} onChange={v => { setMaxTokens(v); setDirty(true) }} placeholder="4096" type="number" />
            </Field>
          </div>
        </div>
      </Section>

      {/* ── Provider (collapsible) ──────────────────────────────────────────── */}
      <Collapsible title="Provider" open={expanded.has('provider')} onToggle={() => toggleSection('provider')}>
        <div className="bg-[#081529] border border-[#0C1D38] rounded-lg p-5 space-y-4">
          <Field label="Provider Type">
            <Select value={providerType} onChange={v => { setProviderType(v); setDirty(true) }} options={['AzureFoundry', 'AzureOpenAI', 'OpenAI']} />
          </Field>
          <Field label="Client Type">
            <Select value={clientType} onChange={v => { setClientType(v); setDirty(true) }} options={['ChatCompletion', 'Responses']} />
          </Field>
          <Field label="Endpoint">
            <Input value={endpoint} onChange={v => { setEndpoint(v); setDirty(true) }} placeholder="https://resource.openai.azure.com" mono />
          </Field>
        </div>
      </Collapsible>

      {/* ── Instructions (collapsible) ──────────────────────────────────────── */}
      <Collapsible title="Instructions" open={expanded.has('instructions')} onToggle={() => toggleSection('instructions')}>
        <div className="bg-[#081529] border border-[#0C1D38] rounded-lg overflow-hidden">
          <textarea
            value={instructions}
            onChange={e => { setInstructions(e.target.value); setDirty(true) }}
            placeholder="System instructions for the agent..."
            className="w-full bg-transparent text-[13px] text-[#B8CEE5] font-mono leading-[1.75] p-5 placeholder:text-[#444] focus:outline-none resize-y min-h-[120px]"
          />
        </div>
        {isEdit && (
          <p className="text-[11px] text-[#3E5F7D] mt-2">Saving with changed instructions will auto-create a new prompt version.</p>
        )}
      </Collapsible>

      {/* ── Tools (collapsible) ─────────────────────────────────────────────── */}
      <Collapsible
        title="Tools"
        count={tools.length}
        open={expanded.has('tools')}
        onToggle={() => toggleSection('tools')}
        action={<GhostAction label="Add Tool" onClick={addTool} />}
      >
        {tools.length === 0 ? (
          <div className="flex items-center justify-center py-10 border border-dashed border-[#0C1D38] rounded-lg">
            <button onClick={addTool} className="text-[12px] text-[#3E5F7D] hover:text-[#7596B8] transition-colors flex items-center gap-1.5">
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M12 5v14M5 12h14" /></svg>
              Add your first tool
            </button>
          </div>
        ) : (
          <div className="space-y-3">
            {tools.map((tool, i) => (
              <ToolCard key={i} tool={tool} index={i} errors={errors} onChange={updateTool} onRemove={removeTool} availableFunctions={availableFunctions} />
            ))}
          </div>
        )}
      </Collapsible>

      {/* ── Structured Output (collapsible) ─────────────────────────────────── */}
      <Collapsible title="Structured Output" open={expanded.has('structured')} onToggle={() => toggleSection('structured')}>
        <div className="bg-[#081529] border border-[#0C1D38] rounded-lg p-5 space-y-4">
          <Field label="Response Format">
            <Select value={responseFormat} onChange={v => { setResponseFormat(v); setDirty(true) }} options={['text', 'json', 'json_schema']} />
          </Field>
          {responseFormat === 'json_schema' && (
            <>
              <Field label="Schema Name">
                <Input value={schemaName} onChange={v => { setSchemaName(v); setDirty(true) }} placeholder="MySchema" />
              </Field>
              <Field label="Schema Description">
                <Input value={schemaDescription} onChange={v => { setSchemaDescription(v); setDirty(true) }} placeholder="Optional description" />
              </Field>
              <Field label="Schema (JSON)" required error={errors.schema}>
                <textarea
                  value={schemaJson}
                  onChange={e => { setSchemaJson(e.target.value); setDirty(true); setErrors(er => ({ ...er, schema: '' })) }}
                  placeholder='{ "type": "object", "properties": { ... } }'
                  className="w-full bg-[#04091A] border border-[#1A3357] rounded-md text-[12px] text-[#B8CEE5] font-mono leading-relaxed p-3 placeholder:text-[#444] focus:border-[#254980] focus:outline-none resize-y min-h-[100px] transition-colors"
                />
              </Field>
            </>
          )}
        </div>
      </Collapsible>

      {/* ── Middlewares (collapsible) ────────────────────────────────────────── */}
      <Collapsible
        title="Middlewares"
        count={middlewares.length}
        open={expanded.has('middlewares')}
        onToggle={() => toggleSection('middlewares')}
        action={<GhostAction label="Add" onClick={addMiddleware} />}
      >
        {middlewares.length === 0 ? (
          <div className="flex items-center justify-center py-10 border border-dashed border-[#0C1D38] rounded-lg">
            <button onClick={addMiddleware} className="text-[12px] text-[#3E5F7D] hover:text-[#7596B8] transition-colors flex items-center gap-1.5">
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M12 5v14M5 12h14" /></svg>
              Add middleware
            </button>
          </div>
        ) : (
          <div className="space-y-3">
            {middlewares.map((mw, i) => (
              <div key={i} className="bg-[#081529] border border-[#0C1D38] rounded-lg p-4">
                <div className="flex items-center gap-3 mb-3">
                  <span className="w-[6px] h-[6px] rounded-full shrink-0" style={{ background: mw.enabled ? '#50e3c2' : '#444' }} />
                  <Select
                    value={mw.type}
                    onChange={v => updateMiddleware(i, { type: v })}
                    options={['AccountGuard']}
                  />
                  <label className="flex items-center gap-2 cursor-pointer ml-auto mr-2">
                    <input
                      type="checkbox"
                      checked={mw.enabled}
                      onChange={e => updateMiddleware(i, { enabled: e.target.checked })}
                      className="accent-[#50e3c2]"
                    />
                    <span className="text-[11px] text-[#4A6B8A]">Enabled</span>
                  </label>
                  <button onClick={() => removeMiddleware(i)} className="p-1 text-[#444] hover:text-[#ff4444] transition-colors shrink-0">
                    <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M3 6h18M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6M8 6V4a2 2 0 012-2h4a2 2 0 012 2v2" /></svg>
                  </button>
                </div>

                {mw.type === 'AccountGuard' && (
                  <div className="space-y-2">
                    <p className="text-[11px] text-[#3E5F7D] leading-relaxed">
                      Protects the client account in agent output. If the LLM changes the account number, it reverts to the original from the input.
                    </p>
                    <Field label="Account Pattern (regex, optional)">
                      <Input
                        value={mw.settings?.accountPattern ?? ''}
                        onChange={v => updateMiddleware(i, { settings: { ...mw.settings, accountPattern: v } })}
                        placeholder="Default: digits after 'conta:' or 'account:'"
                        mono
                      />
                    </Field>
                  </div>
                )}
              </div>
            ))}
          </div>
        )}
      </Collapsible>

      {/* ── Metadata (collapsible) ──────────────────────────────────────────── */}
      <Collapsible
        title="Metadata"
        count={metadata.length}
        open={expanded.has('metadata')}
        onToggle={() => toggleSection('metadata')}
        action={<GhostAction label="Add" onClick={addMeta} />}
      >
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

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="mb-8">
      <h2 className="text-[12px] font-semibold text-[#7596B8] uppercase tracking-[0.06em] mb-4">{title}</h2>
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
        <svg
          width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="#666" strokeWidth="2"
          className={`transition-transform ${open ? 'rotate-90' : ''}`}
        >
          <path d="M9 18l6-6-6-6" />
        </svg>
        <h2 className="text-[12px] font-semibold text-[#7596B8] uppercase tracking-[0.06em] group-hover:text-[#aaa] transition-colors">{title}</h2>
        {count != null && count > 0 && <span className="text-[11px] text-[#444] tabular-nums">{count}</span>}
        {action && <span className="ml-auto">{action}</span>}
      </button>
      {open && children}
    </section>
  )
}

function Field({ label, required, error, children }: {
  label: string; required?: boolean; error?: string; children: React.ReactNode
}) {
  return (
    <div>
      <label className="block text-[12px] text-[#4A6B8A] mb-1.5">
        {label}{required && <span className="text-[#ff4444] ml-0.5">*</span>}
      </label>
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
      value={value}
      onChange={e => onChange(e.target.value)}
      placeholder={placeholder}
      disabled={disabled}
      type={type}
      className={`w-full bg-[#04091A] border border-[#1A3357] rounded-md px-3 py-[7px] text-[13px] text-[#DCE8F5] placeholder:text-[#444] focus:border-[#254980] focus:outline-none transition-colors disabled:opacity-50 disabled:cursor-not-allowed ${mono ? 'font-mono' : ''} ${className ?? ''}`}
    />
  )
}

function Select({ value, onChange, options }: {
  value: string; onChange: (v: string) => void; options: string[]
}) {
  return (
    <select
      value={value}
      onChange={e => onChange(e.target.value)}
      className="w-full bg-[#04091A] border border-[#1A3357] rounded-md px-3 py-[7px] text-[13px] text-[#DCE8F5] focus:border-[#254980] focus:outline-none transition-colors appearance-none cursor-pointer"
      style={{ backgroundImage: `url("data:image/svg+xml,%3Csvg width='12' height='12' viewBox='0 0 24 24' fill='none' stroke='%23666' stroke-width='2' xmlns='http://www.w3.org/2000/svg'%3E%3Cpath d='M6 9l6 6 6-6'/%3E%3C/svg%3E")`, backgroundRepeat: 'no-repeat', backgroundPosition: 'right 10px center' }}
    >
      {options.map(o => <option key={o} value={o}>{o}</option>)}
    </select>
  )
}

function GhostAction({ label, onClick }: { label: string; onClick: (e: React.MouseEvent) => void }) {
  return (
    <button
      onClick={e => { e.stopPropagation(); onClick(e) }}
      className="text-[11px] text-[#3E5F7D] hover:text-[#999] transition-colors flex items-center gap-1"
    >
      <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5"><path d="M12 5v14M5 12h14" /></svg>
      {label}
    </button>
  )
}

/* ── Tool Card ─────────────────────────────────────────────────────────────── */

function ToolCard({ tool, index, errors, onChange, onRemove, availableFunctions }: {
  tool: AgentToolDef; index: number; errors: Record<string, string>
  onChange: (i: number, patch: Partial<AgentToolDef>) => void; onRemove: (i: number) => void
  availableFunctions: FunctionToolInfo[]
}) {
  const typeColor: Record<string, string> = {
    function: '#3291ff', mcp: '#7928ca', code_interpreter: '#50e3c2', file_search: '#0057E0', web_search: '#ff6b6b',
  }

  // Determine if current function name is a known registered function or custom
  const isKnownFunction = tool.name && availableFunctions.some(f => f.name === tool.name)
  const [isCustom, setIsCustom] = useState(!isKnownFunction && !!tool.name)
  const selectedFnInfo = availableFunctions.find(f => f.name === tool.name)

  return (
    <div className="bg-[#081529] border border-[#0C1D38] rounded-lg p-4">
      <div className="flex items-center gap-3 mb-3">
        <span className="w-[6px] h-[6px] rounded-full shrink-0" style={{ background: typeColor[tool.type] ?? '#4A6B8A' }} />
        <Select
          value={tool.type}
          onChange={v => { onChange(index, { type: v, name: undefined, serverLabel: undefined, serverUrl: undefined, allowedTools: undefined, requireApproval: undefined, connectionId: undefined }); setIsCustom(false) }}
          options={['function', 'mcp', 'code_interpreter', 'file_search', 'web_search']}
        />
        <button onClick={() => onRemove(index)} className="ml-auto p-1 text-[#444] hover:text-[#ff4444] transition-colors shrink-0">
          <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M3 6h18M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6M8 6V4a2 2 0 012-2h4a2 2 0 012 2v2" /></svg>
        </button>
      </div>

      {tool.type === 'function' && (
        <div className="space-y-3">
          <Field label="Function Name" required error={errors[`tool.${index}.name`]}>
            {availableFunctions.length > 0 && !isCustom ? (
              <select
                value={tool.name ?? ''}
                onChange={e => {
                  const val = e.target.value
                  if (val === '__custom__') {
                    setIsCustom(true)
                    onChange(index, { name: '' })
                  } else {
                    onChange(index, { name: val })
                  }
                }}
                className="w-full bg-[#04091A] border border-[#1A3357] rounded-md px-3 py-[7px] text-[13px] text-[#DCE8F5] font-mono focus:border-[#254980] focus:outline-none transition-colors appearance-none cursor-pointer"
                style={{ backgroundImage: `url("data:image/svg+xml,%3Csvg width='12' height='12' viewBox='0 0 24 24' fill='none' stroke='%23666' stroke-width='2' xmlns='http://www.w3.org/2000/svg'%3E%3Cpath d='M6 9l6 6 6-6'/%3E%3C/svg%3E")`, backgroundRepeat: 'no-repeat', backgroundPosition: 'right 10px center' }}
              >
                <option value="">Select a function...</option>
                {availableFunctions.map(f => (
                  <option key={f.name} value={f.name}>{f.name}</option>
                ))}
                <option disabled>──────────</option>
                <option value="__custom__">Custom...</option>
              </select>
            ) : (
              <div className="flex gap-2">
                <Input value={tool.name ?? ''} onChange={v => onChange(index, { name: v })} placeholder="custom_function_name" mono className="flex-1" />
                {availableFunctions.length > 0 && (
                  <button
                    onClick={() => { setIsCustom(false); onChange(index, { name: '' }) }}
                    className="px-2.5 rounded-md border border-[#1A3357] text-[#3E5F7D] hover:text-[#999] hover:border-[#254980] transition-colors shrink-0 text-[11px]"
                    title="Switch to dropdown"
                  >
                    List
                  </button>
                )}
              </div>
            )}
          </Field>
          {selectedFnInfo?.description && (
            <p className="text-[11px] text-[#3E5F7D] leading-relaxed -mt-1">{selectedFnInfo.description}</p>
          )}
          <label className="flex items-center gap-2 cursor-pointer">
            <input
              type="checkbox"
              checked={tool.requiresApproval ?? false}
              onChange={e => onChange(index, { requiresApproval: e.target.checked })}
              className="accent-[#3291ff]"
            />
            <span className="text-[12px] text-[#7596B8]">Requires approval</span>
          </label>
        </div>
      )}

      {tool.type === 'mcp' && (
        <div className="space-y-3">
          <Field label="Server Label" required error={errors[`tool.${index}.serverLabel`]}>
            <Input value={tool.serverLabel ?? ''} onChange={v => onChange(index, { serverLabel: v })} placeholder="my-mcp-server" />
          </Field>
          <Field label="Server URL" required error={errors[`tool.${index}.serverUrl`]}>
            <Input value={tool.serverUrl ?? ''} onChange={v => onChange(index, { serverUrl: v })} placeholder="https://mcp.example.com" mono />
          </Field>
          <Field label="Allowed Tools" required error={errors[`tool.${index}.allowedTools`]}>
            <Input
              value={tool.allowedTools?.join(', ') ?? ''}
              onChange={v => onChange(index, { allowedTools: v.split(',').map(s => s.trim()).filter(Boolean) })}
              placeholder="tool1, tool2, tool3"
              mono
            />
            <p className="text-[11px] text-[#444] mt-1">Comma-separated list of tool names</p>
          </Field>
          <Field label="Require Approval">
            <Select
              value={tool.requireApproval ?? 'never'}
              onChange={v => onChange(index, { requireApproval: v })}
              options={['never', 'always']}
            />
          </Field>
          <Field label="Headers">
            <div className="space-y-2">
              {Object.entries(tool.headers ?? {}).map(([hk, hv], hi) => (
                <div key={hi} className="flex gap-2 items-center">
                  <Input
                    value={hk}
                    onChange={v => {
                      const entries = Object.entries(tool.headers ?? {})
                      entries[hi] = [v, hv]
                      onChange(index, { headers: Object.fromEntries(entries) })
                    }}
                    placeholder="Header-Name"
                    mono
                    className="flex-1"
                  />
                  <Input
                    value={hv}
                    onChange={v => {
                      const h = { ...(tool.headers ?? {}) }
                      const key = Object.keys(h)[hi]
                      if (key) h[key] = v
                      onChange(index, { headers: h })
                    }}
                    placeholder="value"
                    className="flex-1"
                  />
                  <button
                    onClick={() => {
                      const entries = Object.entries(tool.headers ?? {}).filter((_, j) => j !== hi)
                      onChange(index, { headers: Object.fromEntries(entries) })
                    }}
                    className="p-1.5 text-[#444] hover:text-[#ff4444] transition-colors shrink-0"
                  >
                    <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M3 6h18M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6M8 6V4a2 2 0 012-2h4a2 2 0 012 2v2" /></svg>
                  </button>
                </div>
              ))}
              <button
                onClick={() => onChange(index, { headers: { ...(tool.headers ?? {}), '': '' } })}
                className="text-[11px] text-[#3E5F7D] hover:text-[#999] transition-colors flex items-center gap-1"
              >
                <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5"><path d="M12 5v14M5 12h14" /></svg>
                Add header
              </button>
            </div>
            <p className="text-[11px] text-[#444] mt-1">Custom headers for MCP server authentication (API keys, etc.)</p>
          </Field>
        </div>
      )}

      {(tool.type === 'code_interpreter' || tool.type === 'file_search') && (
        <p className="text-[12px] text-[#3E5F7D] italic">No additional configuration needed</p>
      )}

      {tool.type === 'web_search' && (
        <div className="space-y-3">
          <p className="text-[11px] text-[#3E5F7D] leading-relaxed">
            Native web search powered by the provider. Azure Foundry uses Bing Grounding (requires a connection ID). OpenAI/AzureOpenAI use the built-in HostedWebSearchTool.
          </p>
          <Field label="Connection ID" error={errors[`tool.${index}.connectionId`]}>
            <Input
              value={tool.connectionId ?? ''}
              onChange={v => onChange(index, { connectionId: v })}
              placeholder="Bing Grounding connection ID (required for Azure Foundry)"
              mono
            />
            <p className="text-[11px] text-[#444] mt-1">Only required for Azure Foundry provider. Leave empty for OpenAI/AzureOpenAI.</p>
          </Field>
        </div>
      )}
    </div>
  )
}
