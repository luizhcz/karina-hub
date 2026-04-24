import { useState } from 'react'
import { cn } from '../../../shared/utils/cn'
import type { FunctionToolInfo, CodeExecutorInfo } from '../../../api/tools'


interface ToolGroup {
  key: string
  label: string
  items: ToolItem[]
}

interface ToolItem {
  name: string
  description?: string
  meta?: string
}

interface ToolPickerProps {
  functionTools: FunctionToolInfo[]
  codeExecutors: CodeExecutorInfo[]
  selected: string[]
  onChange: (selected: string[]) => void
}


export function ToolPicker({ functionTools, codeExecutors, selected, onChange }: ToolPickerProps) {
  const [search, setSearch] = useState('')
  const [collapsed, setCollapsed] = useState<Record<string, boolean>>({})

  const groups: ToolGroup[] = [
    {
      key: 'function',
      label: 'Function Tools',
      items: functionTools.map((f) => ({
        name: f.name,
        description: f.description,
      })),
    },
    {
      key: 'executor',
      label: 'Code Executors',
      items: codeExecutors.map((c) => ({
        name: c.name,
        meta: c.inputType && c.outputType ? `${c.inputType} → ${c.outputType}` : undefined,
      })),
    },
  ].filter((g) => g.items.length > 0)

  const query = search.toLowerCase().trim()

  const filteredGroups = groups
    .map((g) => ({
      ...g,
      items: query
        ? g.items.filter(
            (item) =>
              item.name.toLowerCase().includes(query) ||
              item.description?.toLowerCase().includes(query)
          )
        : g.items,
    }))
    .filter((g) => g.items.length > 0)

  const totalCount = groups.reduce((sum, g) => sum + g.items.length, 0)
  const selectedCount = selected.length

  const toggle = (name: string) => {
    onChange(
      selected.includes(name)
        ? selected.filter((t) => t !== name)
        : [...selected, name]
    )
  }

  const selectGroup = (group: ToolGroup) => {
    const names = group.items.map((i) => i.name)
    const allSelected = names.every((n) => selected.includes(n))
    if (allSelected) {
      onChange(selected.filter((s) => !names.includes(s)))
    } else {
      const merged = new Set([...selected, ...names])
      onChange([...merged])
    }
  }

  const toggleCollapse = (key: string) => {
    setCollapsed((prev) => ({ ...prev, [key]: !prev[key] }))
  }

  const groupSelectedCount = (group: ToolGroup) =>
    group.items.filter((i) => selected.includes(i.name)).length

  if (totalCount === 0) {
    return <p className="text-sm text-text-dimmed">Nenhuma tool disponivel.</p>
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center gap-3">
        <div className="relative flex-1">
          <svg
            className="absolute left-2.5 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-text-dimmed pointer-events-none"
            fill="none"
            viewBox="0 0 24 24"
            stroke="currentColor"
            strokeWidth={2}
          >
            <path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-4.35-4.35M11 19a8 8 0 100-16 8 8 0 000 16z" />
          </svg>
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Buscar tools..."
            className="w-full bg-bg-tertiary border border-border-secondary rounded-lg pl-8 pr-3 py-1.5 text-sm text-text-primary placeholder:text-text-dimmed focus:outline-none focus:border-accent-blue transition-colors"
          />
        </div>
        <span className="text-xs text-text-dimmed whitespace-nowrap">
          {selectedCount}/{totalCount} selecionadas
        </span>
      </div>

      <div className="flex flex-col gap-2 max-h-80 overflow-y-auto pr-1">
        {filteredGroups.map((group) => {
          const isCollapsed = collapsed[group.key] ?? false
          const selCount = groupSelectedCount(group)
          const allSelected = group.items.every((i) => selected.includes(i.name))

          return (
            <div key={group.key} className="border border-border-secondary rounded-lg overflow-hidden">
              <button
                type="button"
                onClick={() => toggleCollapse(group.key)}
                className="flex items-center justify-between w-full px-3 py-2 bg-bg-tertiary hover:bg-bg-tertiary/80 transition-colors text-left"
              >
                <div className="flex items-center gap-2">
                  <svg
                    className={cn(
                      'w-3 h-3 text-text-dimmed transition-transform',
                      !isCollapsed && 'rotate-90'
                    )}
                    fill="currentColor"
                    viewBox="0 0 20 20"
                  >
                    <path
                      fillRule="evenodd"
                      d="M7.21 14.77a.75.75 0 01.02-1.06L11.168 10 7.23 6.29a.75.75 0 111.04-1.08l4.5 4.25a.75.75 0 010 1.08l-4.5 4.25a.75.75 0 01-1.06-.02z"
                      clipRule="evenodd"
                    />
                  </svg>
                  <span className="text-xs font-semibold text-text-secondary">{group.label}</span>
                  {selCount > 0 && (
                    <span className="inline-flex items-center px-1.5 py-0.5 text-[10px] font-medium rounded-full bg-accent-blue/15 text-blue-400 border border-blue-500/30">
                      {selCount}
                    </span>
                  )}
                </div>
                <button
                  type="button"
                  onClick={(e) => {
                    e.stopPropagation()
                    selectGroup(group)
                  }}
                  className="text-[10px] text-text-dimmed hover:text-accent-blue transition-colors"
                >
                  {allSelected ? 'Limpar' : 'Selecionar tudo'}
                </button>
              </button>

              {!isCollapsed && (
                <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-px bg-border-secondary/30">
                  {group.items.map((item) => {
                    const isSelected = selected.includes(item.name)
                    return (
                      <label
                        key={item.name}
                        className={cn(
                          'flex items-start gap-2.5 px-3 py-2 cursor-pointer transition-colors bg-bg-secondary',
                          isSelected
                            ? 'bg-accent-blue/5'
                            : 'hover:bg-bg-tertiary/50'
                        )}
                      >
                        <input
                          type="checkbox"
                          checked={isSelected}
                          onChange={() => toggle(item.name)}
                          className="accent-accent-blue mt-0.5 shrink-0"
                        />
                        <div className="flex flex-col min-w-0">
                          <span
                            className={cn(
                              'text-sm truncate',
                              isSelected ? 'text-text-primary font-medium' : 'text-text-secondary'
                            )}
                          >
                            {item.name}
                          </span>
                          {item.description && (
                            <span className="text-[11px] text-text-dimmed truncate">
                              {item.description}
                            </span>
                          )}
                          {item.meta && (
                            <span className="text-[11px] text-text-dimmed font-mono truncate">
                              {item.meta}
                            </span>
                          )}
                        </div>
                      </label>
                    )
                  })}
                </div>
              )}
            </div>
          )
        })}

        {filteredGroups.length === 0 && query && (
          <p className="text-sm text-text-dimmed py-4 text-center">
            Nenhuma tool encontrada para "{search}"
          </p>
        )}
      </div>
    </div>
  )
}
