import { useState } from 'react'
import type { MiddlewareTypeInfo } from '../../../api/tools'

// ── Types ──────────────────────────────────────────────────────────────────

export interface MiddlewareEntry {
  type: string
  enabled: boolean
  settings: Record<string, string>
}

interface MiddlewarePickerProps {
  availableTypes: MiddlewareTypeInfo[]
  value: MiddlewareEntry[]
  onChange: (next: MiddlewareEntry[]) => void
}

// ── Component ──────────────────────────────────────────────────────────────

const PHASE_BADGE: Record<string, { label: string; className: string }> = {
  Pre: { label: 'Pre-LLM', className: 'bg-yellow-500/15 text-yellow-400 border-yellow-500/30' },
  Post: { label: 'Post-LLM', className: 'bg-blue-500/15 text-blue-400 border-blue-500/30' },
  Both: { label: 'Pre + Post', className: 'bg-purple-500/15 text-purple-400 border-purple-500/30' },
}

export function MiddlewarePicker({
  availableTypes,
  value,
  onChange,
}: MiddlewarePickerProps) {
  const [search, setSearch] = useState('')

  const filtered = availableTypes.filter((m) =>
    m.name.toLowerCase().includes(search.toLowerCase()),
  )

  const entryMap = new Map(value.map((e) => [e.type, e]))

  function toggle(type: string) {
    const existing = entryMap.get(type)
    if (existing) {
      onChange(value.filter((e) => e.type !== type))
    } else {
      const mw = availableTypes.find((m) => m.name === type)
      const defaults: Record<string, string> = {}
      mw?.settings?.forEach((s) => {
        defaults[s.key] = s.defaultValue
      })
      onChange([...value, { type, enabled: true, settings: defaults }])
    }
  }

  function updateSetting(type: string, key: string, val: string) {
    onChange(
      value.map((e) =>
        e.type === type ? { ...e, settings: { ...e.settings, [key]: val } } : e,
      ),
    )
  }

  return (
    <div className="flex flex-col gap-3">
      {availableTypes.length > 4 && (
        <input
          type="text"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Buscar middleware..."
          className="bg-bg-tertiary border border-border-secondary rounded-md px-3 py-1.5 text-sm text-text-primary placeholder:text-text-dimmed focus:outline-none focus:border-accent-blue"
        />
      )}

      <div className="flex flex-col gap-3 max-h-[420px] overflow-y-auto pr-1">
      {filtered.length === 0 && (
        <p className="text-sm text-text-dimmed">Nenhum middleware disponivel.</p>
      )}

      {filtered.map((mw) => {
        const type = mw.name
        const label = mw.label || mw.name
        const description = mw.description || 'Middleware registrado no sistema.'
        const settings = mw.settings ?? []
        const entry = entryMap.get(type)
        const isActive = !!entry
        const badge = mw.phase ? PHASE_BADGE[mw.phase] : undefined

        return (
          <div
            key={type}
            className={`rounded-lg border transition-colors ${
              isActive
                ? 'border-accent-blue bg-accent-blue/5'
                : 'border-border-secondary bg-bg-tertiary'
            }`}
          >
            {/* Header toggle */}
            <button
              type="button"
              onClick={() => toggle(type)}
              className="w-full flex items-center gap-3 px-4 py-3 text-left"
            >
              <div
                className={`w-4 h-4 rounded border flex items-center justify-center flex-shrink-0 transition-colors ${
                  isActive
                    ? 'bg-accent-blue border-accent-blue'
                    : 'border-border-secondary'
                }`}
              >
                {isActive && (
                  <svg
                    className="w-3 h-3 text-white"
                    fill="none"
                    viewBox="0 0 24 24"
                    stroke="currentColor"
                    strokeWidth={3}
                  >
                    <path
                      strokeLinecap="round"
                      strokeLinejoin="round"
                      d="M5 13l4 4L19 7"
                    />
                  </svg>
                )}
              </div>
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2">
                  <span className="text-sm font-medium text-text-primary">
                    {label}
                  </span>
                  {badge && (
                    <span className={`text-[10px] font-semibold px-1.5 py-0.5 rounded border ${badge.className}`}>
                      {badge.label}
                    </span>
                  )}
                </div>
                <p className="text-xs text-text-dimmed mt-0.5 leading-relaxed">
                  {description}
                </p>
              </div>
            </button>

            {/* Settings (only when active and has settings from API) */}
            {isActive && settings.length > 0 && (
              <div className="px-4 pb-3 pt-0 border-t border-border-secondary/50">
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 pt-3">
                  {settings.map((setting) => (
                    <div key={setting.key} className="flex flex-col gap-1">
                      <label className="text-xs font-medium text-text-muted">
                        {setting.label}
                      </label>
                      {setting.type === 'select' && setting.options ? (
                        <select
                          value={entry.settings[setting.key] ?? setting.defaultValue}
                          onChange={(e) =>
                            updateSetting(type, setting.key, e.target.value)
                          }
                          className="bg-bg-secondary border border-border-secondary rounded-md px-2.5 py-1.5 text-sm text-text-primary focus:outline-none focus:border-accent-blue"
                        >
                          {setting.options.map((opt) => (
                            <option key={opt.value} value={opt.value}>
                              {opt.label}
                            </option>
                          ))}
                        </select>
                      ) : (
                        <input
                          type="text"
                          value={entry.settings[setting.key] ?? ''}
                          onChange={(e) =>
                            updateSetting(type, setting.key, e.target.value)
                          }
                          className="bg-bg-secondary border border-border-secondary rounded-md px-2.5 py-1.5 text-sm text-text-primary focus:outline-none focus:border-accent-blue"
                        />
                      )}
                    </div>
                  ))}
                </div>
              </div>
            )}
          </div>
        )
      })}
      </div>

      {value.length > 0 && (
        <p className="text-xs text-text-dimmed">
          {value.length} middleware{value.length !== 1 ? 's' : ''} ativo
          {value.length !== 1 ? 's' : ''}
        </p>
      )}
    </div>
  )
}
