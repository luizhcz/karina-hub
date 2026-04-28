import { useMemo } from 'react'
import { useEvaluatorCatalog } from '../../../api/evaluations'
import type { CatalogEntry, EvaluatorBinding } from '../../../api/evaluations'
import { Badge } from '../../../shared/ui/Badge'

interface Props {
  selected: EvaluatorBinding[]
  onChange: (next: EvaluatorBinding[]) => void
}

// Agrupa catalog por dimensão (Relevance, Coherence, Safety...) com sub-label
// de fonte (Local/Meai/Foundry) — evita selecionar 2 evaluators que medem a
// mesma dimensão sem perceber.
export function EvaluatorPicker({ selected, onChange }: Props) {
  const { data: catalog, isLoading, error } = useEvaluatorCatalog()

  const grouped = useMemo(() => {
    if (!catalog) return new Map<string, CatalogEntry[]>()
    const map = new Map<string, CatalogEntry[]>()
    for (const entry of catalog) {
      const list = map.get(entry.dimension) ?? []
      list.push(entry)
      map.set(entry.dimension, list)
    }
    return map
  }, [catalog])

  const isSelected = (entry: CatalogEntry) =>
    selected.some((b) => b.kind === entry.kind && b.name === entry.name)

  const dimensionsCovered = useMemo(() => {
    if (!catalog) return new Set<string>()
    return new Set(
      selected
        .map((b) => catalog.find((c) => c.kind === b.kind && c.name === b.name)?.dimension)
        .filter((d): d is string => !!d),
    )
  }, [catalog, selected])

  if (isLoading) return <div className="text-sm text-text-muted">Carregando catalog…</div>
  if (error) return <div className="text-sm text-red-400">Erro ao carregar catalog.</div>
  if (!catalog) return null

  const toggle = (entry: CatalogEntry) => {
    const exists = isSelected(entry)
    if (exists) {
      onChange(selected.filter((b) => !(b.kind === entry.kind && b.name === entry.name)))
    } else {
      const nextIndex = selected.filter((b) => b.kind === entry.kind && b.name === entry.name).length
      const params = entry.paramsExampleJson ? JSON.parse(entry.paramsExampleJson) : undefined
      onChange([
        ...selected,
        {
          kind: entry.kind,
          name: entry.name,
          params,
          enabled: true,
          weight: 1.0,
          bindingIndex: nextIndex,
        },
      ])
    }
  }

  return (
    <div className="space-y-4">
      {Array.from(grouped.entries()).map(([dimension, entries]) => {
        const dimSelectedCount = entries.filter(isSelected).length
        const overlapWarning = dimSelectedCount > 1

        return (
          <div key={dimension}>
            <div className="flex items-center gap-2 mb-2">
              <h4 className="text-sm font-semibold text-text-primary">{dimension}</h4>
              {dimensionsCovered.has(dimension) && (
                <Badge variant="green">{dimSelectedCount} selecionado{dimSelectedCount > 1 ? 's' : ''}</Badge>
              )}
              {overlapWarning && (
                <Badge variant="yellow">⚠ múltiplos evaluators na mesma dimensão</Badge>
              )}
            </div>
            <div className="grid grid-cols-1 md:grid-cols-2 gap-2">
              {entries.map((entry) => {
                const sel = isSelected(entry)
                return (
                  <button
                    key={`${entry.kind}.${entry.name}`}
                    type="button"
                    onClick={() => toggle(entry)}
                    className={
                      'text-left rounded-lg border px-3 py-2 transition-colors ' +
                      (sel
                        ? 'border-blue-500/50 bg-blue-500/10'
                        : 'border-border-secondary bg-bg-secondary hover:border-border-primary')
                    }
                  >
                    <div className="flex items-center gap-2 mb-1">
                      <Badge variant={entry.kind === 'Local' ? 'gray' : entry.kind === 'Foundry' ? 'purple' : 'blue'}>
                        {entry.kind}
                      </Badge>
                      <span className="text-sm font-mono text-text-primary">{entry.name}</span>
                    </div>
                    <div className="text-xs text-text-muted leading-relaxed">{entry.description}</div>
                  </button>
                )
              })}
            </div>
          </div>
        )
      })}
    </div>
  )
}
