import { useState, useMemo } from 'react'

export interface Column<T> {
  key: string
  label: string
  render: (row: T) => React.ReactNode
  sortValue?: (row: T) => number | string
  align?: 'left' | 'right' | 'center'
}

interface Props<T> {
  columns: Column<T>[]
  data: T[]
  keyFn: (row: T) => string
  emptyMessage?: string
}

export function DataTable<T>({ columns, data, keyFn, emptyMessage = 'Sem dados' }: Props<T>) {
  const [sortCol, setSortCol] = useState<string | null>(null)
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('desc')

  const sorted = useMemo(() => {
    if (!sortCol) return data
    const col = columns.find(c => c.key === sortCol)
    if (!col?.sortValue) return data
    const fn = col.sortValue
    return [...data].sort((a, b) => {
      const va = fn(a)
      const vb = fn(b)
      const cmp = typeof va === 'number' && typeof vb === 'number'
        ? va - vb
        : String(va).localeCompare(String(vb))
      return sortDir === 'asc' ? cmp : -cmp
    })
  }, [data, sortCol, sortDir, columns])

  const handleSort = (key: string) => {
    if (sortCol === key) {
      setSortDir(d => d === 'asc' ? 'desc' : 'asc')
    } else {
      setSortCol(key)
      setSortDir('desc')
    }
  }

  if (data.length === 0) {
    return <div className="text-center text-[#3E5F7D] text-sm py-8">{emptyMessage}</div>
  }

  const ALIGN = { left: 'text-left', right: 'text-right', center: 'text-center' }

  return (
    <div className="rounded-lg border border-[#1A3357] overflow-hidden">
      <table className="w-full text-xs">
        <thead>
          <tr className="bg-[#081529] text-[#4A6B8A] uppercase text-[10px] tracking-wider">
            {columns.map(col => (
              <th
                key={col.key}
                className={`${ALIGN[col.align ?? 'left']} px-3 py-2 font-medium ${col.sortValue ? 'cursor-pointer hover:text-[#B8CEE5] select-none' : ''}`}
                onClick={() => col.sortValue && handleSort(col.key)}
              >
                {col.label}
                {sortCol === col.key && (
                  <span className="ml-1 text-[#0057E0]">{sortDir === 'asc' ? '\u25B2' : '\u25BC'}</span>
                )}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-[#0C1D38]">
          {sorted.map(row => (
            <tr key={keyFn(row)} className="hover:bg-[#081529]/50 transition-colors">
              {columns.map(col => (
                <td key={col.key} className={`${ALIGN[col.align ?? 'left']} px-3 py-2`}>
                  {col.render(row)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
