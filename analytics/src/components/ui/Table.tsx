/**
 * Table<T> — tabela tipada genérica com sort, sticky header, loading e empty.
 *
 * Reusabilidade: 100% genérica sobre T. Caller declara colunas como
 * `Column<T>[]`: `key` é identifier estável (sort dedupe), `header` é texto,
 * `cell` é renderizador, `sortBy` opcional retorna o valor comparável.
 * Sem sortBy a coluna é não-sortable.
 *
 * NÃO faz:
 *   - Paginação (V1 dos analytics tem top N curto; quando precisar, externa).
 *   - Seleção de linha (responsabilidade do caller via rowProps).
 *   - Filtros de coluna (overkill pra V1).
 *
 * Exemplo: <Table columns={agentColumns} rows={agents} keyOf={a => a.agentId} />
 *          Reusado por: Top agents, Cost por workflow, Cost por agente.
 */
import { useMemo, useState } from 'react'
import { cn } from './cn'
import { Skeleton } from './Skeleton'
import { EmptyState } from './EmptyState'

export interface Column<T> {
  /** Identifier estável (também é o React key). */
  key: string
  header: React.ReactNode
  /** Renderiza a célula. Recebe a row inteira pra evitar lookup em cada chamada. */
  cell: (row: T) => React.ReactNode
  /**
   * Função que extrai valor comparável pra sort. Quando ausente, a coluna não
   * tem ícone de sort. Strings comparadas via localeCompare, números via -.
   */
  sortBy?: (row: T) => number | string | null | undefined
  /** Alinhamento da célula. Default 'left'. */
  align?: 'left' | 'right' | 'center'
  /** Largura CSS opcional (ex.: '120px', '20%'). */
  width?: string
  /** Classes extras na td (truncate, monospace, etc.). */
  cellClassName?: string
}

interface TableProps<T> {
  columns: ReadonlyArray<Column<T>>
  rows: ReadonlyArray<T> | null
  /** Loading state — quando true ignora rows e exibe esqueleto. */
  loading?: boolean
  /** Empty state quando rows está definido e vazio. */
  empty?: { title: string; description?: string }
  /** Extrator estável de chave pra reconciliação React. */
  keyOf: (row: T, index: number) => string | number
  /** Skeletons exibidos enquanto loading. Default 5. */
  loadingRows?: number
  /**
   * Handler de clique na linha. Quando definido, a row vira interativa
   * (cursor-pointer + role=button) — usado pra abrir drawer de detalhe.
   */
  onRowClick?: (row: T) => void
  className?: string
}

type SortState = { key: string; direction: 'asc' | 'desc' } | null

function alignClass(align: 'left' | 'right' | 'center' | undefined): string {
  if (align === 'right') return 'text-right'
  if (align === 'center') return 'text-center'
  return 'text-left'
}

export function Table<T>({
  columns,
  rows,
  loading = false,
  empty,
  keyOf,
  loadingRows = 5,
  onRowClick,
  className,
}: TableProps<T>): React.ReactElement {
  const [sort, setSort] = useState<SortState>(null)

  const sortedRows = useMemo<ReadonlyArray<T> | null>(() => {
    if (!rows || !sort) return rows
    const col = columns.find((c) => c.key === sort.key)
    if (!col?.sortBy) return rows
    const sortBy = col.sortBy
    const direction = sort.direction === 'asc' ? 1 : -1
    return [...rows].sort((a, b) => {
      const va = sortBy(a)
      const vb = sortBy(b)
      // Null/undefined sempre vão pro fim, independente de direction. Isso
      // evita confusão "ordenei por custo, agente sem custo apareceu primeiro".
      if (va == null && vb == null) return 0
      if (va == null) return 1
      if (vb == null) return -1
      if (typeof va === 'number' && typeof vb === 'number') return (va - vb) * direction
      return String(va).localeCompare(String(vb), 'pt-BR') * direction
    })
  }, [rows, sort, columns])

  function toggleSort(col: Column<T>) {
    if (!col.sortBy) return
    setSort((prev) => {
      if (!prev || prev.key !== col.key) return { key: col.key, direction: 'desc' }
      if (prev.direction === 'desc') return { key: col.key, direction: 'asc' }
      return null
    })
  }

  return (
    <div className={cn('overflow-hidden rounded-xl border border-border', className)}>
      <div className="max-h-[28rem] overflow-auto">
        <table className="w-full border-collapse text-sm">
          <thead className="sticky top-0 z-10 bg-bg-soft">
            <tr>
              {columns.map((col) => {
                const sortable = !!col.sortBy
                const active = sort?.key === col.key
                return (
                  <th
                    key={col.key}
                    scope="col"
                    style={col.width ? { width: col.width } : undefined}
                    className={cn(
                      'border-b border-border px-3 py-2 text-xs font-semibold uppercase tracking-wide text-fg-muted',
                      alignClass(col.align),
                      sortable && 'cursor-pointer select-none hover:text-fg',
                    )}
                    onClick={sortable ? () => toggleSort(col) : undefined}
                    aria-sort={
                      active
                        ? sort?.direction === 'asc'
                          ? 'ascending'
                          : 'descending'
                        : undefined
                    }
                  >
                    <span className="inline-flex items-center gap-1">
                      {col.header}
                      {sortable && (
                        <span className={cn('text-[10px]', active ? 'text-fg' : 'text-fg-dim')}>
                          {active ? (sort?.direction === 'asc' ? '▲' : '▼') : '↕'}
                        </span>
                      )}
                    </span>
                  </th>
                )
              })}
            </tr>
          </thead>
          <tbody>
            {loading
              ? Array.from({ length: loadingRows }).map((_, i) => (
                  <tr key={`sk-${i}`} className="border-b border-border last:border-b-0">
                    {columns.map((col) => (
                      <td
                        key={col.key}
                        className={cn('px-3 py-3', alignClass(col.align))}
                      >
                        <Skeleton className="h-4 w-3/4" />
                      </td>
                    ))}
                  </tr>
                ))
              : sortedRows && sortedRows.length > 0
              ? sortedRows.map((row, idx) => (
                  <tr
                    key={keyOf(row, idx)}
                    onClick={onRowClick ? () => onRowClick(row) : undefined}
                    onKeyDown={
                      onRowClick
                        ? (e) => {
                            if (e.key === 'Enter' || e.key === ' ') {
                              e.preventDefault()
                              onRowClick(row)
                            }
                          }
                        : undefined
                    }
                    role={onRowClick ? 'button' : undefined}
                    tabIndex={onRowClick ? 0 : undefined}
                    className={cn(
                      'border-b border-border transition-colors last:border-b-0 hover:bg-surface-hover',
                      onRowClick && 'cursor-pointer focus:outline-none focus:bg-surface-hover',
                    )}
                  >
                    {columns.map((col) => (
                      <td
                        key={col.key}
                        className={cn(
                          'px-3 py-3 text-fg',
                          alignClass(col.align),
                          col.cellClassName,
                        )}
                      >
                        {col.cell(row)}
                      </td>
                    ))}
                  </tr>
                ))
              : null}
          </tbody>
        </table>
        {!loading && sortedRows && sortedRows.length === 0 && empty && (
          <EmptyState title={empty.title} description={empty.description} />
        )}
      </div>
    </div>
  )
}
