interface Props {
  label: string
  value: string
  color: 'violet' | 'blue' | 'emerald' | 'amber' | 'red' | 'slate'
  subtitle?: string
}

const COLORS: Record<Props['color'], string> = {
  violet:  'bg-[#0057E0]/10 text-[#4D8EF5] border-[#0057E0]/20',
  blue:    'bg-blue-500/10 text-blue-400 border-blue-500/20',
  emerald: 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20',
  amber:   'bg-amber-500/10 text-amber-400 border-amber-500/20',
  red:     'bg-red-500/10 text-red-400 border-red-500/20',
  slate:   'bg-[#081529] text-[#7596B8] border-[#254980]/30',
}

export function MetricCard({ label, value, color, subtitle }: Props) {
  return (
    <div className={`rounded-lg border ${COLORS[color]} px-3 py-2.5 overflow-hidden`}>
      <div className="text-[10px] text-[#4A6B8A] uppercase tracking-wider font-medium mb-0.5 truncate">{label}</div>
      <span className="text-lg font-semibold font-mono truncate block">{value}</span>
      {subtitle && <div className="text-[10px] text-[#4A6B8A] mt-0.5 truncate">{subtitle}</div>}
    </div>
  )
}
