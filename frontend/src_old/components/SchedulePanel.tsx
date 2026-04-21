import { useState, useEffect, useCallback } from 'react'
import { api } from '../api'
import type { WorkflowDef, WorkflowTrigger } from '../types'

type Selection =
  | { kind: 'view'; item: WorkflowDef }
  | { kind: 'create' }
  | { kind: 'edit'; item: WorkflowDef }

/* ════════════════════════════════════════════════════════════════════════════
   SchedulePanel — manage workflow triggers (enable / disable / create)
   ════════════════════════════════════════════════════════════════════════════ */

export function SchedulePanel() {
  const [workflows, setWorkflows] = useState<WorkflowDef[]>([])
  const [selected, setSelected] = useState<Selection | null>(null)
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [toggling, setToggling] = useState(false)

  const refresh = useCallback(async () => {
    const wfs = await api.getWorkflows()
    setWorkflows(wfs)
    setSelected(prev => {
      if (!prev || prev.kind === 'create') return prev
      const updated = wfs.find(w => w.id === prev.item.id)
      return updated ? { ...prev, item: updated } : prev
    })
  }, [])

  useEffect(() => {
    api.getWorkflows()
      .then(wfs => {
        setWorkflows(wfs)
        const withTrigger = wfs.filter(w => w.trigger && w.trigger.type !== 'OnDemand')
        if (withTrigger.length > 0) setSelected({ kind: 'view', item: withTrigger[0] })
        else if (wfs.length > 0) setSelected({ kind: 'view', item: wfs[0] })
      })
      .finally(() => setLoading(false))
  }, [])

  const handleToggle = useCallback(async (wf: WorkflowDef, enabled: boolean) => {
    setToggling(true)
    try {
      await api.toggleWorkflowTrigger(wf, enabled)
      await refresh()
    } finally {
      setToggling(false)
    }
  }, [refresh])

  const q = search.toLowerCase()
  const filtered = q
    ? workflows.filter(w => w.name.toLowerCase().includes(q) || w.id.toLowerCase().includes(q))
    : workflows

  const withTrigger = filtered.filter(w => w.trigger && w.trigger.type !== 'OnDemand')
  const onDemand = filtered.filter(w => !w.trigger || w.trigger.type === 'OnDemand')

  return (
    <div className="flex flex-1 overflow-hidden" style={{ minHeight: 0 }}>
      {/* ── Sidebar ──────────────────────────────────────────── */}
      <nav
        className="flex flex-col overflow-hidden bg-[#04091A] border-r border-[#0C1D38]"
        style={{ width: 280, minWidth: 280, maxWidth: 280 }}
      >
        <div className="p-3 border-b border-[#0C1D38]">
          <div className="relative">
            <svg className="absolute left-2.5 top-1/2 -translate-y-1/2 text-[#444]" width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <circle cx="11" cy="11" r="7" /><path d="M21 21l-4.35-4.35" />
            </svg>
            <input
              value={search}
              onChange={e => setSearch(e.target.value)}
              placeholder="Search workflows..."
              className="w-full bg-[#081529] border border-[#1A3357] rounded-md pl-8 pr-3 py-[7px] text-[13px] text-[#DCE8F5] placeholder:text-[#3E5F7D] focus:border-[#254980] focus:outline-none transition-colors"
            />
          </div>
        </div>

        <div className="flex-1 overflow-y-auto" style={{ scrollbarWidth: 'none' }}>
          {loading ? (
            <div className="flex items-center justify-center h-24">
              <div className="w-4 h-4 border-[1.5px] border-[#254980] border-t-[#7596B8] rounded-full animate-spin" />
            </div>
          ) : (
            <>
              <NavGroup label="Scheduled / Event" count={withTrigger.length}>
                {withTrigger.map(wf => (
                  <NavItem
                    key={wf.id}
                    active={selected?.kind !== 'create' && selected?.item.id === wf.id}
                    onClick={() => setSelected({ kind: 'view', item: wf })}
                    label={wf.name}
                    meta={triggerMeta(wf)}
                    dot={triggerDot(wf)}
                  />
                ))}
              </NavGroup>

              <NavGroup label="On Demand" count={onDemand.length}>
                {onDemand.map(wf => (
                  <NavItem
                    key={wf.id}
                    active={selected?.kind !== 'create' && selected?.item.id === wf.id}
                    onClick={() => setSelected({ kind: 'view', item: wf })}
                    label={wf.name}
                    meta="OnDemand"
                    dot="#444"
                  />
                ))}
              </NavGroup>
            </>
          )}
        </div>

        <div className="p-3 border-t border-[#0C1D38]">
          <GhostBtn label="New Schedule" onClick={() => setSelected({ kind: 'create' })} />
        </div>
      </nav>

      {/* ── Detail ──────────────────────────────────────────── */}
      <main className="flex-1 min-w-0 overflow-y-auto bg-[#04091A]" style={{ scrollbarWidth: 'none' }}>
        {!selected ? (
          <div className="flex flex-col items-center justify-center h-full gap-3">
            <p className="text-[13px] text-[#4A6B8A]">Select a workflow</p>
          </div>
        ) : selected.kind === 'create' ? (
          <CreateScheduleForm
            workflows={workflows}
            onSaved={async (updated) => { await refresh(); setSelected({ kind: 'view', item: updated }) }}
            onCancel={() => {
              const first = workflows.find(w => w.trigger && w.trigger.type !== 'OnDemand') ?? workflows[0]
              setSelected(first ? { kind: 'view', item: first } : null)
            }}
          />
        ) : selected.kind === 'edit' ? (
          <EditScheduleForm
            wf={selected.item}
            onSaved={async (updated) => { await refresh(); setSelected({ kind: 'view', item: updated }) }}
            onCancel={() => setSelected({ kind: 'view', item: selected.item })}
          />
        ) : (
          <TriggerDetail
            wf={selected.item}
            toggling={toggling}
            onToggle={handleToggle}
            onEdit={() => setSelected({ kind: 'edit', item: selected.item })}
          />
        )}
      </main>
    </div>
  )
}

/* ════════════════════════════════════════════════════════════════════════════
   Cron Builder — visual schedule picker
   ════════════════════════════════════════════════════════════════════════════ */

type FreqType = 'minute' | 'hourly' | 'daily' | 'weekly' | 'monthly' | 'custom' | 'once'

interface CS {
  freq: FreqType
  hour: number    // 0-23
  minute: number  // 0-59
  days: number[]  // 0=Sun … 6=Sat  (weekly)
  dom: number     // 1-31           (monthly)
  custom: string
  onceDatetime: string  // ISO datetime-local value (once)
  disableAfterFire: boolean
}

const DOW_LABELS = ['S', 'M', 'T', 'W', 'T', 'F', 'S']
const DOW_FULL   = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']

const MONTH_ABBR = ['JAN','FEB','MAR','APR','MAY','JUN','JUL','AUG','SEP','OCT','NOV','DEC']

function buildCron(cs: CS): string {
  const m = cs.minute, h = cs.hour
  switch (cs.freq) {
    case 'minute':  return '* * * * *'
    case 'hourly':  return `${m} * * * *`
    case 'daily':   return `${m} ${h} * * *`
    case 'weekly': {
      const d = cs.days.length > 0 ? cs.days.join(',') : '1'
      return `${m} ${h} * * ${d}`
    }
    case 'monthly': return `${m} ${h} ${cs.dom} * *`
    case 'custom':  return cs.custom
    case 'once': {
      if (!cs.onceDatetime) return cs.custom || '* * * * *'
      const dt = new Date(cs.onceDatetime)
      if (isNaN(dt.getTime())) return cs.custom || '* * * * *'
      const mm = dt.getMinutes()
      const hh = dt.getHours()
      const dd = dt.getDate()
      const mon = MONTH_ABBR[dt.getMonth()]
      return `${mm} ${hh} ${dd} ${mon} *`
    }
  }
}

function parseCron(cron: string): CS {
  const base: CS = { freq: 'custom', hour: 8, minute: 0, days: [1, 2, 3, 4, 5], dom: 1, custom: cron, onceDatetime: '', disableAfterFire: false }
  const p = cron.trim().split(/\s+/)
  if (p.length !== 5) return base

  const [min, hr, dom, mon, dow] = p
  const toInt = (s: string) => { const n = parseInt(s); return isNaN(n) ? null : n }

  if (min === '*' && hr === '*' && dom === '*' && mon === '*' && dow === '*')
    return { ...base, freq: 'minute' }

  const m = toInt(min)
  if (m === null) return base

  if (hr === '*' && dom === '*' && mon === '*' && dow === '*')
    return { ...base, freq: 'hourly', minute: m }

  const h = toInt(hr)
  if (h === null) return base

  if (dom === '*' && mon === '*' && dow === '*')
    return { ...base, freq: 'daily', hour: h, minute: m }

  if (dom === '*' && mon === '*') {
    const dayNums = dow.split(',').flatMap(s => {
      if (s.includes('-')) {
        const [a, b] = s.split('-').map(Number)
        return Array.from({ length: b - a + 1 }, (_, i) => a + i)
      }
      const n = Number(s)
      return isNaN(n) ? [] : [n]
    }).filter(n => n >= 0 && n <= 6)
    return { ...base, freq: 'weekly', hour: h, minute: m, days: dayNums.length > 0 ? dayNums : [1, 2, 3, 4, 5] }
  }

  const d = toInt(dom)
  if (d !== null && mon === '*' && dow === '*')
    return { ...base, freq: 'monthly', hour: h, minute: m, dom: d }

  return base
}

function describeSchedule(cs: CS): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  const time = `${pad(cs.hour)}:${pad(cs.minute)}`
  switch (cs.freq) {
    case 'minute':  return 'Every minute'
    case 'hourly':  return `Every hour at :${pad(cs.minute)}`
    case 'daily':   return `Every day at ${time}`
    case 'weekly': {
      const names = cs.days.map(d => DOW_FULL[d]).join(', ')
      return `Every ${names} at ${time}`
    }
    case 'monthly': return `Day ${cs.dom} of every month at ${time}`
    case 'custom':  return 'Custom expression'
    case 'once':    return cs.onceDatetime ? `Once at ${new Date(cs.onceDatetime).toLocaleString('pt-BR')}` : 'Once (pick date/time)'
  }
}

function CronBuilder({ value, onChange, onDisableAfterFireChange }: { value: string; onChange: (v: string) => void; onDisableAfterFireChange?: (v: boolean) => void }) {
  const [cs, setCs] = useState<CS>(() => parseCron(value))

  const update = (patch: Partial<CS>) => {
    setCs(prev => {
      const next = { ...prev, ...patch }
      onChange(buildCron(next))
      if (patch.disableAfterFire !== undefined) {
        onDisableAfterFireChange?.(patch.disableAfterFire)
      }
      return next
    })
  }

  const toggleDay = (d: number) => {
    const days = cs.days.includes(d) ? cs.days.filter(x => x !== d) : [...cs.days, d].sort()
    update({ days })
  }

  const FREQS: { id: FreqType; label: string }[] = [
    { id: 'minute',  label: 'Every min' },
    { id: 'hourly',  label: 'Hourly' },
    { id: 'daily',   label: 'Daily' },
    { id: 'weekly',  label: 'Weekly' },
    { id: 'monthly', label: 'Monthly' },
    { id: 'once',    label: 'Once' },
    { id: 'custom',  label: 'Custom' },
  ]

  const showTime   = cs.freq !== 'minute' && cs.freq !== 'custom' && cs.freq !== 'once'
  const showDow    = cs.freq === 'weekly'
  const showDom    = cs.freq === 'monthly'

  return (
    <div className="space-y-5">
      {/* Frequency pills */}
      <div>
        <label className="text-[11px] font-medium text-[#4A6B8A] uppercase tracking-[0.05em] block mb-2">Frequency</label>
        <div className="flex gap-1.5 flex-wrap">
          {FREQS.map(f => (
            <button
              key={f.id}
              onClick={() => {
                const next = { ...cs, freq: f.id }
                if (f.id === 'custom') next.custom = buildCron(cs)
                setCs(next)
                onChange(buildCron(next))
              }}
              className={`px-3 py-[5px] rounded-md text-[12px] font-medium border transition-colors ${
                cs.freq === f.id
                  ? 'border-[#0057E0] bg-[#0057E00d] text-[#0057E0]'
                  : 'border-[#1A3357] text-[#4A6B8A] hover:border-[#254980] hover:text-[#999]'
              }`}
            >
              {f.label}
            </button>
          ))}
        </div>
      </div>

      {/* Time picker */}
      {showTime && (
        <div>
          <label className="text-[11px] font-medium text-[#4A6B8A] uppercase tracking-[0.05em] block mb-2">Time (UTC)</label>
          <div className="flex items-center gap-3 bg-[#081529] border border-[#0C1D38] rounded-lg px-5 py-4 w-fit">
            {/* Hour */}
            <div className="flex flex-col items-center gap-1">
              <button
                onClick={() => update({ hour: (cs.hour + 1) % 24 })}
                className="w-7 h-6 flex items-center justify-center rounded hover:bg-[#0C1D38] text-[#3E5F7D] hover:text-[#999] transition-colors"
              >
                <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5"><path d="M18 15l-6-6-6 6"/></svg>
              </button>
              <div className="w-12 text-center text-[26px] font-mono font-semibold text-[#DCE8F5] leading-none">
                {String(cs.hour).padStart(2, '0')}
              </div>
              <button
                onClick={() => update({ hour: (cs.hour + 23) % 24 })}
                className="w-7 h-6 flex items-center justify-center rounded hover:bg-[#0C1D38] text-[#3E5F7D] hover:text-[#999] transition-colors"
              >
                <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5"><path d="M6 9l6 6 6-6"/></svg>
              </button>
              <span className="text-[10px] text-[#444] uppercase tracking-wide mt-0.5">hour</span>
            </div>

            <span className="text-[28px] font-mono text-[#444] pb-5">:</span>

            {/* Minute */}
            <div className="flex flex-col items-center gap-1">
              <button
                onClick={() => update({ minute: (cs.minute + 5) % 60 })}
                className="w-7 h-6 flex items-center justify-center rounded hover:bg-[#0C1D38] text-[#3E5F7D] hover:text-[#999] transition-colors"
              >
                <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5"><path d="M18 15l-6-6-6 6"/></svg>
              </button>
              <div className="w-12 text-center text-[26px] font-mono font-semibold text-[#DCE8F5] leading-none">
                {String(cs.minute).padStart(2, '0')}
              </div>
              <button
                onClick={() => update({ minute: (cs.minute + 55) % 60 })}
                className="w-7 h-6 flex items-center justify-center rounded hover:bg-[#0C1D38] text-[#3E5F7D] hover:text-[#999] transition-colors"
              >
                <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5"><path d="M6 9l6 6 6-6"/></svg>
              </button>
              <span className="text-[10px] text-[#444] uppercase tracking-wide mt-0.5">min</span>
            </div>
          </div>
        </div>
      )}

      {/* Days of week (weekly) */}
      {showDow && (
        <div>
          <label className="text-[11px] font-medium text-[#4A6B8A] uppercase tracking-[0.05em] block mb-2">Days of Week</label>
          <div className="flex gap-1.5">
            {DOW_LABELS.map((lbl, i) => {
              const active = cs.days.includes(i)
              return (
                <button
                  key={i}
                  onClick={() => toggleDay(i)}
                  title={DOW_FULL[i]}
                  className={`w-9 h-9 rounded-lg text-[13px] font-semibold border transition-colors ${
                    active
                      ? 'border-[#0057E0] bg-[#0057E01a] text-[#0057E0]'
                      : 'border-[#1A3357] text-[#3E5F7D] hover:border-[#254980] hover:text-[#7596B8]'
                  }`}
                >
                  {lbl}
                </button>
              )
            })}
          </div>
          {cs.days.length === 0 && (
            <p className="text-[11px] text-[#ff6b6b] mt-1.5">Select at least one day.</p>
          )}
        </div>
      )}

      {/* Day of month (monthly) */}
      {showDom && (
        <div>
          <label className="text-[11px] font-medium text-[#4A6B8A] uppercase tracking-[0.05em] block mb-2">Day of Month</label>
          <div className="grid gap-1" style={{ gridTemplateColumns: 'repeat(7, 36px)' }}>
            {Array.from({ length: 31 }, (_, i) => i + 1).map(d => (
              <button
                key={d}
                onClick={() => update({ dom: d })}
                className={`h-9 rounded-lg text-[12px] font-medium border transition-colors ${
                  cs.dom === d
                    ? 'border-[#0057E0] bg-[#0057E01a] text-[#0057E0]'
                    : 'border-[#0C1D38] text-[#3E5F7D] hover:border-[#254980] hover:text-[#7596B8]'
                }`}
              >
                {d}
              </button>
            ))}
          </div>
        </div>
      )}

      {/* Custom input */}
      {cs.freq === 'custom' && (
        <div>
          <label className="text-[11px] font-medium text-[#4A6B8A] uppercase tracking-[0.05em] block mb-2">Cron Expression</label>
          <input
            value={cs.custom}
            onChange={e => {
              const custom = e.target.value
              setCs(prev => ({ ...prev, custom }))
              onChange(custom)
            }}
            placeholder="* * * * *"
            className="w-full bg-[#081529] border border-[#1A3357] rounded-md px-3 py-[8px] text-[13px] text-[#DCE8F5] font-mono placeholder:text-[#444] focus:border-[#444] focus:outline-none transition-colors"
          />
          <p className="text-[11px] text-[#3E5F7D] mt-1.5">Format: minute hour day-of-month month day-of-week</p>
        </div>
      )}

      {/* Once — datetime picker */}
      {cs.freq === 'once' && (
        <div className="space-y-3">
          <div>
            <label className="text-[11px] font-medium text-[#4A6B8A] uppercase tracking-[0.05em] block mb-2">Date &amp; Time</label>
            <input
              type="datetime-local"
              value={cs.onceDatetime}
              onChange={e => update({ onceDatetime: e.target.value })}
              className="bg-[#081529] border border-[#1A3357] rounded-md px-3 py-[8px] text-[13px] text-[#DCE8F5] focus:border-[#444] focus:outline-none transition-colors"
            />
          </div>
          <label className="flex items-center gap-2 cursor-pointer select-none">
            <input
              type="checkbox"
              checked={cs.disableAfterFire}
              onChange={e => update({ disableAfterFire: e.target.checked })}
              className="w-3.5 h-3.5 accent-[#0057E0]"
            />
            <span className="text-[12px] text-[#7596B8]">Desabilitar após execução</span>
          </label>
        </div>
      )}

      {/* Cron preview */}
      <div className="flex items-center gap-3 bg-[#081529] border border-[#0C1D38] rounded-lg px-4 py-3">
        <div className="flex-1 min-w-0">
          <div className="text-[11px] text-[#3E5F7D] uppercase tracking-wide mb-0.5">Schedule</div>
          <div className="text-[13px] text-[#DCE8F5]">{describeSchedule(cs)}</div>
        </div>
        <div className="shrink-0">
          <div className="text-[11px] text-[#3E5F7D] uppercase tracking-wide mb-0.5 text-right">Cron</div>
          <div className="text-[13px] font-mono text-[#0057E0]">{buildCron(cs)}</div>
        </div>
      </div>
    </div>
  )
}

/* ── Create Schedule Form ─────────────────────────────────────────────────── */

function CreateScheduleForm({
  workflows,
  onSaved,
  onCancel,
}: {
  workflows: WorkflowDef[]
  onSaved: (wf: WorkflowDef) => void
  onCancel: () => void
}) {
  const [workflowId, setWorkflowId] = useState(workflows[0]?.id ?? '')
  const [type, setType] = useState<'Scheduled' | 'EventDriven'>('Scheduled')
  const [cron, setCron] = useState('0 8 * * 1-5')
  const [topic, setTopic] = useState('')
  const [enabled, setEnabled] = useState(true)
  const [disableAfterFire, setDisableAfterFire] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handleSave = async () => {
    const wf = workflows.find(w => w.id === workflowId)
    if (!wf) return
    if (type === 'Scheduled' && !cron.trim()) { setError('Cron expression is required.'); return }
    if (type === 'EventDriven' && !topic.trim()) { setError('Event topic is required.'); return }
    setError(null)
    setSaving(true)
    try {
      const trigger: WorkflowTrigger = type === 'Scheduled'
        ? { type: 'Scheduled', cronExpression: cron.trim(), enabled, disableAfterFire }
        : { type: 'EventDriven', eventTopic: topic.trim(), enabled, disableAfterFire }
      const updated = await api.setWorkflowTrigger(wf, trigger)
      onSaved(updated)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to save.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="max-w-[640px] mx-auto px-10 pt-10 pb-16">
      <header className="pb-8 mb-10 border-b border-[#0C1D38]">
        <h1 className="text-[22px] font-semibold text-[#fafafa] tracking-[-0.025em]">New Schedule</h1>
        <p className="text-[13px] text-[#3E5F7D] mt-2">Attach a trigger to an existing workflow.</p>
      </header>

      <div className="space-y-8">
        {/* Workflow */}
        <Field label="Workflow">
          <select
            value={workflowId}
            onChange={e => setWorkflowId(e.target.value)}
            className="w-full bg-[#081529] border border-[#1A3357] rounded-md px-3 py-[8px] text-[13px] text-[#DCE8F5] focus:border-[#444] focus:outline-none transition-colors"
          >
            {workflows.map(w => (
              <option key={w.id} value={w.id}>{w.name} ({w.id})</option>
            ))}
          </select>
        </Field>

        {/* Trigger type */}
        <Field label="Trigger Type">
          <div className="flex gap-2">
            {(['Scheduled', 'EventDriven'] as const).map(t => (
              <button
                key={t}
                onClick={() => setType(t)}
                className={`px-4 py-[7px] rounded-md text-[13px] font-medium border transition-colors ${
                  type === t
                    ? 'border-[#3E5F7D] bg-[#0C1D38] text-[#DCE8F5]'
                    : 'border-[#1A3357] text-[#4A6B8A] hover:border-[#254980] hover:text-[#999]'
                }`}
              >
                {t}
              </button>
            ))}
          </div>
        </Field>

        {/* Cron builder / Event topic */}
        {type === 'Scheduled' ? (
          <div>
            <label className="text-[11px] font-medium text-[#4A6B8A] uppercase tracking-[0.05em] block mb-3">Schedule</label>
            <CronBuilder value={cron} onChange={setCron} onDisableAfterFireChange={setDisableAfterFire} />
          </div>
        ) : (
          <Field label="Event Topic">
            <input
              value={topic}
              onChange={e => setTopic(e.target.value)}
              placeholder="e.g. market.open"
              className="w-full bg-[#081529] border border-[#1A3357] rounded-md px-3 py-[8px] text-[13px] text-[#DCE8F5] font-mono placeholder:text-[#444] focus:border-[#444] focus:outline-none transition-colors"
            />
          </Field>
        )}

        {/* Enabled toggle */}
        <Field label="Start Enabled">
          <ToggleSwitch checked={enabled} onChange={setEnabled} />
        </Field>

        {error && <p className="text-[12px] text-[#ff6b6b]">{error}</p>}

        <div className="flex gap-2 pt-2">
          <button
            onClick={handleSave}
            disabled={saving}
            className="px-5 py-[8px] rounded-md text-[13px] font-medium bg-[#DCE8F5] text-[#04091A] hover:bg-white disabled:opacity-50 transition-colors flex items-center gap-2"
          >
            {saving && <div className="w-3 h-3 border-[1.5px] border-black/30 border-t-black rounded-full animate-spin" />}
            Save Schedule
          </button>
          <button
            onClick={onCancel}
            disabled={saving}
            className="px-5 py-[8px] rounded-md text-[13px] font-medium border border-[#1A3357] text-[#7596B8] hover:border-[#254980] hover:text-[#B8CEE5] disabled:opacity-50 transition-colors"
          >
            Cancel
          </button>
        </div>
      </div>
    </div>
  )
}

/* ── Edit Schedule Form ───────────────────────────────────────────────────── */

function EditScheduleForm({
  wf,
  onSaved,
  onCancel,
}: {
  wf: WorkflowDef
  onSaved: (updated: WorkflowDef) => void
  onCancel: () => void
}) {
  const existingTrigger = wf.trigger
  const [type, setType] = useState<'Scheduled' | 'EventDriven'>(
    existingTrigger?.type === 'EventDriven' ? 'EventDriven' : 'Scheduled'
  )
  const [cron, setCron] = useState(existingTrigger?.cronExpression ?? '0 8 * * 1-5')
  const [topic, setTopic] = useState(existingTrigger?.eventTopic ?? '')
  const [enabled, setEnabled] = useState(existingTrigger?.enabled !== false)
  const [disableAfterFire, setDisableAfterFire] = useState(existingTrigger?.disableAfterFire ?? false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handleSave = async () => {
    if (type === 'Scheduled' && !cron.trim()) { setError('Cron expression is required.'); return }
    if (type === 'EventDriven' && !topic.trim()) { setError('Event topic is required.'); return }
    setError(null)
    setSaving(true)
    try {
      const trigger: WorkflowTrigger = type === 'Scheduled'
        ? { type: 'Scheduled', cronExpression: cron.trim(), enabled, disableAfterFire }
        : { type: 'EventDriven', eventTopic: topic.trim(), enabled, disableAfterFire }
      const updated = await api.setWorkflowTrigger(wf, trigger)
      onSaved(updated)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to save.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="max-w-[640px] mx-auto px-10 pt-10 pb-16">
      <header className="pb-8 mb-10 border-b border-[#0C1D38]">
        <h1 className="text-[22px] font-semibold text-[#fafafa] tracking-[-0.025em]">Edit Schedule</h1>
        <p className="text-[12px] text-[#3E5F7D] font-mono mt-1">{wf.id}</p>
      </header>

      <div className="space-y-8">
        <Field label="Trigger Type">
          <div className="flex gap-2">
            {(['Scheduled', 'EventDriven'] as const).map(t => (
              <button
                key={t}
                onClick={() => setType(t)}
                className={`px-4 py-[7px] rounded-md text-[13px] font-medium border transition-colors ${
                  type === t
                    ? 'border-[#3E5F7D] bg-[#0C1D38] text-[#DCE8F5]'
                    : 'border-[#1A3357] text-[#4A6B8A] hover:border-[#254980] hover:text-[#999]'
                }`}
              >
                {t}
              </button>
            ))}
          </div>
        </Field>

        {type === 'Scheduled' ? (
          <div>
            <label className="text-[11px] font-medium text-[#4A6B8A] uppercase tracking-[0.05em] block mb-3">Schedule</label>
            <CronBuilder value={cron} onChange={setCron} onDisableAfterFireChange={setDisableAfterFire} />
          </div>
        ) : (
          <Field label="Event Topic">
            <input
              value={topic}
              onChange={e => setTopic(e.target.value)}
              placeholder="e.g. market.open"
              className="w-full bg-[#081529] border border-[#1A3357] rounded-md px-3 py-[8px] text-[13px] text-[#DCE8F5] font-mono placeholder:text-[#444] focus:border-[#444] focus:outline-none transition-colors"
            />
          </Field>
        )}

        <Field label="Enabled">
          <ToggleSwitch checked={enabled} onChange={setEnabled} />
        </Field>

        {error && <p className="text-[12px] text-[#ff6b6b]">{error}</p>}

        <div className="flex gap-2 pt-2">
          <button
            onClick={handleSave}
            disabled={saving}
            className="px-5 py-[8px] rounded-md text-[13px] font-medium bg-[#DCE8F5] text-[#04091A] hover:bg-white disabled:opacity-50 transition-colors flex items-center gap-2"
          >
            {saving && <div className="w-3 h-3 border-[1.5px] border-black/30 border-t-black rounded-full animate-spin" />}
            Save
          </button>
          <button
            onClick={onCancel}
            disabled={saving}
            className="px-5 py-[8px] rounded-md text-[13px] font-medium border border-[#1A3357] text-[#7596B8] hover:border-[#254980] hover:text-[#B8CEE5] disabled:opacity-50 transition-colors"
          >
            Cancel
          </button>
        </div>
      </div>
    </div>
  )
}

/* ── Trigger Detail ──────────────────────────────────────────────────────── */

function getNextCronRun(_cronExpr: string): string {
  return '—'
}

function TriggerDetail({ wf, toggling, onToggle, onEdit }: {
  wf: WorkflowDef
  toggling: boolean
  onToggle: (wf: WorkflowDef, enabled: boolean) => void
  onEdit: () => void
}) {
  const [runNowSuccess, setRunNowSuccess] = useState(false)
  const [runNowRunning, setRunNowRunning] = useState(false)

  const handleRunNow = async () => {
    setRunNowRunning(true)
    try {
      await api.triggerWorkflow(wf.id, '')
      setRunNowSuccess(true)
      setTimeout(() => setRunNowSuccess(false), 2000)
    } finally {
      setRunNowRunning(false)
    }
  }

  const trigger = wf.trigger
  const isEnabled = trigger?.enabled !== false
  const hasTrigger = !!trigger && trigger.type !== 'OnDemand'

  // Human-readable cron description in the detail view
  const cronDescription = trigger?.cronExpression
    ? describeSchedule(parseCron(trigger.cronExpression))
    : null

  return (
    <div className="max-w-[860px] mx-auto px-10 pt-10 pb-16">
      <header className="pb-8 mb-10 border-b border-[#0C1D38]">
        <div className="flex items-start justify-between gap-6">
          <div className="min-w-0">
            <div className="flex items-center gap-3 flex-wrap">
              <h1 className="text-[24px] font-semibold text-[#fafafa] tracking-[-0.025em]">{wf.name}</h1>
              <TriggerPill type={trigger?.type ?? 'OnDemand'} />
              {hasTrigger && <StatusPill enabled={isEnabled} />}
            </div>
            <div className="mt-2.5">
              <span className="text-[12px] text-[#3E5F7D] font-mono">{wf.id}</span>
            </div>
            {wf.description && (
              <p className="text-[14px] text-[#777] mt-4 leading-[1.6] max-w-2xl">{wf.description}</p>
            )}
          </div>

          <div className="flex gap-2 shrink-0 pt-1">
            <ActionBtn label="Edit" onClick={onEdit} />
            <button
              onClick={handleRunNow}
              disabled={runNowRunning}
              className="bg-[#0C1D38] hover:bg-[#142540] text-[#DCE8F5] border border-[#254980] rounded-md px-3 py-1.5 text-xs font-medium disabled:opacity-50 transition-colors"
            >
              {runNowSuccess ? 'Triggered!' : 'Run Now'}
            </button>
            {hasTrigger && (
              <button
                onClick={() => onToggle(wf, !isEnabled)}
                disabled={toggling}
                className={`flex items-center gap-2 px-4 py-[7px] rounded-md text-[12px] font-medium border transition-colors disabled:opacity-50 ${
                  isEnabled
                    ? 'border-[#ff444433] bg-[#ff44440d] text-[#ff6b6b] hover:bg-[#ff444420]'
                    : 'border-[#50e3c233] bg-[#50e3c20d] text-[#50e3c2] hover:bg-[#50e3c220]'
                }`}
              >
                {toggling && <div className="w-3 h-3 border-[1.5px] border-current/30 border-t-current rounded-full animate-spin" />}
                {isEnabled ? 'Disable' : 'Enable'}
              </button>
            )}
          </div>
        </div>
      </header>

      <SectionBlock title="Trigger Configuration">
        {!hasTrigger ? (
          <div className="bg-[#081529] border border-[#0C1D38] rounded-lg px-5 py-4">
            <p className="text-[13px] text-[#3E5F7D]">
              No scheduled or event trigger configured. Click <span className="text-[#7596B8]">Edit</span> to add one.
            </p>
          </div>
        ) : (
          <div className="bg-[#081529] border border-[#0C1D38] rounded-lg px-5">
            <Kv label="Type" value={trigger!.type} />
            <Kv label="Status" value={isEnabled ? 'Enabled' : 'Disabled'} highlight={isEnabled ? '#50e3c2' : '#ff6b6b'} />
            {cronDescription && <Kv label="Schedule" value={cronDescription} />}
            {trigger!.cronExpression && <Kv label="Cron Expression" value={trigger!.cronExpression} mono />}
            {trigger!.eventTopic && <Kv label="Event Topic" value={trigger!.eventTopic} mono />}
            <Kv label="Last Fired" value={trigger!.lastFiredAt ? new Date(trigger!.lastFiredAt).toLocaleString('pt-BR') : 'Nunca'} />
            {trigger!.cronExpression && <Kv label="Next Run" value={getNextCronRun(trigger!.cronExpression)} />}
          </div>
        )}
      </SectionBlock>

      <SectionBlock title="Workflow">
        <div className="bg-[#081529] border border-[#0C1D38] rounded-lg px-5">
          <Kv label="Orchestration Mode" value={wf.orchestrationMode} />
          <Kv label="Agents" value={String(wf.agents?.length ?? 0)} />
          {wf.configuration?.timeoutSeconds != null && <Kv label="Timeout" value={`${wf.configuration.timeoutSeconds}s`} />}
          {wf.createdAt && <Kv label="Created" value={fmtDate(wf.createdAt)} />}
          {wf.updatedAt && <Kv label="Updated" value={fmtDate(wf.updatedAt)} />}
        </div>
      </SectionBlock>
    </div>
  )
}

/* ── Nav primitives ──────────────────────────────────────────────────────── */

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

function GhostBtn({ label, onClick }: { label: string; onClick?: () => void }) {
  return (
    <button
      onClick={onClick}
      className="w-full flex items-center justify-center gap-1.5 px-3 py-[6px] rounded-md border border-dashed border-[#254980] text-[12px] text-[#7596B8] hover:border-[#3E5F7D] hover:text-[#B8CEE5] transition-colors"
    >
      <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5"><path d="M12 5v14M5 12h14" /></svg>
      {label}
    </button>
  )
}

function ActionBtn({ label, onClick }: { label: string; onClick?: () => void }) {
  return (
    <button onClick={onClick} className="px-3.5 py-[6px] rounded-md text-[12px] font-medium border border-[#1A3357] text-[#999] hover:border-[#254980] hover:text-[#B8CEE5] transition-colors">
      {label}
    </button>
  )
}

/* ── Shared primitives ──────────────────────────────────────────────────── */

function SectionBlock({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="mb-10">
      <h2 className="text-[12px] font-medium text-[#4A6B8A] uppercase tracking-[0.05em] mb-3">{title}</h2>
      {children}
    </section>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="space-y-1.5">
      <label className="text-[12px] font-medium text-[#999] block">{label}</label>
      {children}
    </div>
  )
}

function ToggleSwitch({ checked, onChange }: { checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <button
      role="switch"
      aria-checked={checked}
      onClick={() => onChange(!checked)}
      className={`relative inline-flex w-9 h-5 rounded-full transition-colors ${checked ? 'bg-[#50e3c2]' : 'bg-[#254980]'}`}
    >
      <span className={`absolute top-0.5 left-0.5 w-4 h-4 rounded-full bg-white shadow-sm transition-transform ${checked ? 'translate-x-4' : ''}`} />
    </button>
  )
}

function Kv({ label, value, mono, highlight }: { label: string; value: string; mono?: boolean; highlight?: string }) {
  return (
    <div className="py-3 flex items-baseline justify-between gap-4 border-b border-[#0C1D38] last:border-0">
      <span className="text-[12px] text-[#4A6B8A] shrink-0">{label}</span>
      <span className={`text-[13px] text-right truncate ${mono ? 'font-mono' : ''}`} style={{ color: highlight ?? '#DCE8F5' }}>
        {value}
      </span>
    </div>
  )
}

function TriggerPill({ type }: { type: string }) {
  const color = type === 'Scheduled' ? '#0057E0' : type === 'EventDriven' ? '#3291ff' : '#4A6B8A'
  return (
    <span className="inline-flex items-center px-[7px] py-[2px] rounded-full text-[11px] font-medium border" style={{ color, borderColor: color + '33', background: color + '0d' }}>
      {type}
    </span>
  )
}

function StatusPill({ enabled }: { enabled: boolean }) {
  const color = enabled ? '#50e3c2' : '#ff6b6b'
  return (
    <span className="inline-flex items-center gap-1.5 px-[7px] py-[2px] rounded-full text-[11px] font-medium border" style={{ color, borderColor: color + '33', background: color + '0d' }}>
      <span className="w-[5px] h-[5px] rounded-full" style={{ background: color }} />
      {enabled ? 'Active' : 'Disabled'}
    </span>
  )
}

/* ── Helpers ──────────────────────────────────────────────────────────────── */

function triggerMeta(wf: WorkflowDef): string {
  const t = wf.trigger
  if (!t) return 'OnDemand'
  if (t.type === 'Scheduled' && t.cronExpression) return describeSchedule(parseCron(t.cronExpression))
  if (t.type === 'EventDriven') return t.eventTopic ?? 'EventDriven'
  return t.type
}

function triggerDot(wf: WorkflowDef): string {
  const t = wf.trigger
  if (!t || t.type === 'OnDemand') return '#444'
  if (t.enabled === false) return '#ff6b6b'
  if (t.type === 'Scheduled') return '#0057E0'
  if (t.type === 'EventDriven') return '#3291ff'
  return '#50e3c2'
}

function fmtDate(iso: string): string {
  try { return new Date(iso).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' }) }
  catch { return iso }
}
