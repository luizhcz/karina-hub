interface Props {
  label: string
  values: number[]
  labels?: string[]
  color?: string
  height?: number
}

function fmtNum(n: number): string {
  if (n >= 1_000_000) return (n / 1_000_000).toFixed(1) + 'M'
  if (n >= 1_000) return (n / 1_000).toFixed(1) + 'K'
  return n.toLocaleString()
}

export function MiniBarChart({ label, values, labels, color = 'bg-[#0057E0]/60', height = 40 }: Props) {
  const max = Math.max(...values, 1)

  return (
    <div>
      <div className="flex items-center justify-between mb-1.5">
        <span className="text-[10px] text-[#4A6B8A] uppercase tracking-wider">{label}</span>
        <span className="text-[10px] text-[#3E5F7D] font-mono">max {fmtNum(max)}</span>
      </div>
      <div className="flex items-end gap-px" style={{ height }}>
        {values.map((v, i) => {
          const pct = max > 0 ? (v / max) * 100 : 0
          return (
            <div
              key={i}
              className="flex-1 min-w-0 group relative"
              title={labels?.[i] ? `${labels[i]}: ${fmtNum(v)}` : fmtNum(v)}
            >
              <div className="w-full bg-[#0C1D38] rounded-sm overflow-hidden" style={{ height }}>
                <div
                  className={`w-full ${color} rounded-sm transition-all duration-300`}
                  style={{
                    height: `${Math.max(pct, v > 0 ? 4 : 0)}%`,
                    marginTop: `${100 - Math.max(pct, v > 0 ? 4 : 0)}%`,
                  }}
                />
              </div>
            </div>
          )
        })}
      </div>
      {labels && labels.length > 0 && (
        <div className="flex justify-between mt-1">
          <span className="text-[9px] text-[#3E5F7D] font-mono">{labels[0]}</span>
          <span className="text-[9px] text-[#3E5F7D] font-mono">{labels[labels.length - 1]}</span>
        </div>
      )}
    </div>
  )
}
