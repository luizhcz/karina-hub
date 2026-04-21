import type { TimeRange } from '../../../types'

interface Props {
  value: TimeRange
  onChange: (range: TimeRange) => void
}

const RANGES: TimeRange[] = ['1h', '24h', '7d', '30d']

export function TimeRangeSelector({ value, onChange }: Props) {
  return (
    <div className="flex gap-1">
      {RANGES.map(r => (
        <button
          key={r}
          onClick={() => onChange(r)}
          className={`px-2 py-0.5 rounded text-[11px] font-medium transition-colors ${
            value === r ? 'bg-[#DCE8F5] text-[#04091A]' : 'text-[#4A6B8A] hover:text-[#B8CEE5] hover:bg-[#0C1D38]'
          }`}
        >
          {r}
        </button>
      ))}
    </div>
  )
}

export function getFromDate(range: TimeRange): string {
  const now = new Date()
  switch (range) {
    case '1h':  return new Date(now.getTime() - 60 * 60 * 1000).toISOString()
    case '24h': return new Date(now.getTime() - 24 * 60 * 60 * 1000).toISOString()
    case '7d':  return new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000).toISOString()
    case '30d': return new Date(now.getTime() - 30 * 24 * 60 * 60 * 1000).toISOString()
  }
}
