interface Props {
  label: string
  value: number
  color: 'blue' | 'amber' | 'emerald' | 'red'
  pulse?: boolean
}

const DOT: Record<Props['color'], string> = {
  blue: 'bg-blue-400', amber: 'bg-amber-400', emerald: 'bg-emerald-400', red: 'bg-red-400',
}
const TEXT: Record<Props['color'], string> = {
  blue: 'text-blue-400', amber: 'text-amber-400', emerald: 'text-emerald-400', red: 'text-red-400',
}

export function StatusPill({ label, value, color, pulse }: Props) {
  return (
    <div className="text-center">
      <div className="flex items-center justify-center gap-1 mb-0.5">
        <span className={`w-1.5 h-1.5 rounded-full ${DOT[color]} ${pulse ? 'animate-pulse' : ''}`} />
        <span className="text-[10px] text-[#4A6B8A] uppercase tracking-wider">{label}</span>
      </div>
      <span className={`text-sm font-mono font-bold ${TEXT[color]}`}>{value}</span>
    </div>
  )
}
