import {
  useReactTable,
  getCoreRowModel,
  getSortedRowModel,
  getFilteredRowModel,
  getPaginationRowModel,
  flexRender,
  type ColumnDef,
  type SortingState,
} from '@tanstack/react-table'
import { useState } from 'react'
import { cn } from '../utils/cn'

interface DataTableProps<T> {
  data: T[]
  columns: ColumnDef<T, unknown>[]
  searchPlaceholder?: string
  pageSize?: number
  onRowClick?: (row: T) => void
  className?: string
}

export function DataTable<T>({ data, columns, searchPlaceholder = 'Buscar...', pageSize = 20, onRowClick, className }: DataTableProps<T>) {
  const [sorting, setSorting] = useState<SortingState>([])
  const [globalFilter, setGlobalFilter] = useState('')

  const table = useReactTable({
    data,
    columns,
    state: { sorting, globalFilter },
    onSortingChange: setSorting,
    onGlobalFilterChange: setGlobalFilter,
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
    getPaginationRowModel: getPaginationRowModel(),
    initialState: { pagination: { pageSize } },
  })

  return (
    <div className={cn('flex flex-col gap-3', className)}>
      <input
        value={globalFilter}
        onChange={(e) => setGlobalFilter(e.target.value)}
        placeholder={searchPlaceholder}
        className="bg-bg-tertiary border border-border-secondary rounded-lg px-3 py-2 text-sm text-text-primary placeholder:text-text-dimmed focus:outline-none focus:border-accent-blue max-w-xs"
      />

      <div className="overflow-auto rounded-xl border border-border-primary">
        <table className="w-full text-sm">
          <thead>
            {table.getHeaderGroups().map((hg) => (
              <tr key={hg.id} className="bg-bg-tertiary border-b border-border-primary">
                {hg.headers.map((header) => (
                  <th
                    key={header.id}
                    onClick={header.column.getToggleSortingHandler()}
                    className={cn(
                      'px-4 py-2.5 text-left text-xs font-semibold text-text-muted uppercase tracking-wider',
                      header.column.getCanSort() && 'cursor-pointer select-none hover:text-text-secondary'
                    )}
                  >
                    <div className="flex items-center gap-1">
                      {flexRender(header.column.columnDef.header, header.getContext())}
                      {{ asc: ' ↑', desc: ' ↓' }[header.column.getIsSorted() as string] ?? ''}
                    </div>
                  </th>
                ))}
              </tr>
            ))}
          </thead>
          <tbody>
            {table.getRowModel().rows.map((row) => (
              <tr
                key={row.id}
                onClick={() => onRowClick?.(row.original)}
                className={cn(
                  'border-b border-border-primary/50 hover:bg-bg-tertiary/50 transition-colors',
                  onRowClick && 'cursor-pointer'
                )}
              >
                {row.getVisibleCells().map((cell) => (
                  <td key={cell.id} className="px-4 py-2.5 text-text-secondary">
                    {flexRender(cell.column.columnDef.cell, cell.getContext())}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {table.getPageCount() > 1 && (
        <div className="flex items-center justify-between text-xs text-text-muted">
          <span>
            {table.getFilteredRowModel().rows.length} resultado(s) — Página {table.getState().pagination.pageIndex + 1} de {table.getPageCount()}
          </span>
          <div className="flex gap-1">
            <button
              onClick={() => table.previousPage()}
              disabled={!table.getCanPreviousPage()}
              className="px-2.5 py-1 bg-bg-tertiary border border-border-secondary rounded-md disabled:opacity-40 hover:bg-border-primary"
            >
              Anterior
            </button>
            <button
              onClick={() => table.nextPage()}
              disabled={!table.getCanNextPage()}
              className="px-2.5 py-1 bg-bg-tertiary border border-border-secondary rounded-md disabled:opacity-40 hover:bg-border-primary"
            >
              Próxima
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
