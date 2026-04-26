import { useState } from 'react'
import { Card } from '../../../shared/ui/Card'
import { Badge } from '../../../shared/ui/Badge'
import { Select } from '../../../shared/ui/Select'
import type {
  BlocklistAction,
  BlocklistCatalogResponse,
  BlocklistGroupOverride,
  BlocklistSettings,
} from '../../../api/blocklist'

interface Props {
  catalog: BlocklistCatalogResponse | undefined
  settings: BlocklistSettings
  onChange: (next: BlocklistSettings) => void
  disabled?: boolean
}

const ACTION_OPTIONS = [
  { value: '', label: 'Padrão do catálogo' },
  { value: 'Block', label: 'Bloquear' },
  { value: 'Redact', label: 'Redactar' },
  { value: 'Warn', label: 'Apenas avisar' },
]

export function BlocklistGroupsCard({ catalog, settings, onChange, disabled }: Props) {
  const [expanded, setExpanded] = useState<Record<string, boolean>>({})

  const groups = settings.groups ?? {}

  const updateGroup = (groupId: string, patch: Partial<BlocklistGroupOverride>) => {
    const current = groups[groupId] ?? { enabled: true }
    const nextGroups = { ...groups, [groupId]: { ...current, ...patch } }
    onChange({ ...settings, groups: nextGroups })
  }

  const togglePatternDisabled = (groupId: string, patternId: string) => {
    const current = groups[groupId] ?? { enabled: true }
    const set = new Set(current.disabledPatterns ?? [])
    if (set.has(patternId)) set.delete(patternId)
    else set.add(patternId)
    updateGroup(groupId, { disabledPatterns: [...set] })
  }

  if (!catalog) {
    return (
      <Card title="Grupos do Catálogo">
        <p className="text-sm text-text-muted">Carregando catálogo curado…</p>
      </Card>
    )
  }

  return (
    <Card title={`Grupos do Catálogo (v${catalog.version})`}>
      <div className="flex flex-col gap-4">
        {catalog.groups.map((group) => {
          const override = groups[group.id]
          const groupEnabled = override?.enabled ?? true
          const isExpanded = expanded[group.id] ?? false
          const groupPatterns = catalog.patterns.filter((p) => p.groupId === group.id)
          const disabledPatternsCount = override?.disabledPatterns?.length ?? 0

          return (
            <div key={group.id} className="border border-border-secondary rounded-lg p-4">
              <div className="flex items-start justify-between gap-3">
                <label className={`flex items-start gap-3 flex-1 ${disabled ? 'opacity-50' : 'cursor-pointer'}`}>
                  <input
                    type="checkbox"
                    className="mt-1 h-4 w-4 rounded accent-accent-blue"
                    checked={groupEnabled}
                    disabled={disabled}
                    onChange={(e) => updateGroup(group.id, { enabled: e.target.checked })}
                  />
                  <div className="flex-1">
                    <div className="flex items-center gap-2">
                      <span className="text-sm font-semibold text-text-primary">{group.name}</span>
                      <Badge variant="gray">{group.id}</Badge>
                      <Badge variant="blue">{groupPatterns.length} patterns</Badge>
                      {disabledPatternsCount > 0 && (
                        <Badge variant="yellow">{disabledPatternsCount} desligado(s)</Badge>
                      )}
                    </div>
                    {group.description && (
                      <div className="text-xs text-text-muted mt-1">{group.description}</div>
                    )}
                  </div>
                </label>

                <div className="w-48">
                  <Select
                    options={ACTION_OPTIONS}
                    value={override?.actionOverride ?? ''}
                    disabled={disabled || !groupEnabled}
                    onChange={(e) =>
                      updateGroup(group.id, {
                        actionOverride: (e.target.value || null) as BlocklistAction | null,
                      })
                    }
                  />
                </div>
              </div>

              {groupEnabled && (
                <button
                  type="button"
                  className="mt-3 text-xs text-accent-blue hover:underline"
                  onClick={() => setExpanded({ ...expanded, [group.id]: !isExpanded })}
                  disabled={disabled}
                >
                  {isExpanded ? '▼ Ocultar patterns' : '▶ Ver patterns individuais'}
                </button>
              )}

              {isExpanded && groupEnabled && (
                <div className="mt-3 pl-7 flex flex-col gap-1.5">
                  {groupPatterns.map((p) => {
                    const isDisabled = override?.disabledPatterns?.includes(p.id) ?? false
                    return (
                      <label key={p.id} className="flex items-center gap-2 text-xs cursor-pointer">
                        <input
                          type="checkbox"
                          className="h-3.5 w-3.5 rounded accent-accent-blue"
                          checked={!isDisabled}
                          disabled={disabled}
                          onChange={() => togglePatternDisabled(group.id, p.id)}
                        />
                        <span className={`font-mono ${isDisabled ? 'line-through text-text-muted' : 'text-text-secondary'}`}>
                          {p.id}
                        </span>
                        <Badge variant="gray">{p.type}</Badge>
                        {p.validator !== 'None' && <Badge variant="purple">{p.validator}</Badge>}
                        <Badge variant={p.defaultAction === 'Block' ? 'red' : 'yellow'}>{p.defaultAction}</Badge>
                      </label>
                    )
                  })}
                </div>
              )}
            </div>
          )
        })}
      </div>
    </Card>
  )
}
