import { Input } from '../ui/Input'

interface CronEditorProps {
  value: string
  onChange: (value: string) => void
}

const presets = [
  { label: 'A cada minuto', cron: '* * * * *' },
  { label: 'A cada 5 min', cron: '*/5 * * * *' },
  { label: 'A cada hora', cron: '0 * * * *' },
  { label: 'Diário 9h', cron: '0 9 * * *' },
  { label: 'Seg-Sex 9h', cron: '0 9 * * 1-5' },
]

export function CronEditor({ value, onChange }: CronEditorProps) {
  return (
    <div className="flex flex-col gap-3">
      <Input
        label="Cron Expression"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder="* * * * *"
        className="font-mono"
      />
      <div className="flex flex-wrap gap-1.5">
        {presets.map((p) => (
          <button
            key={p.cron}
            type="button"
            onClick={() => onChange(p.cron)}
            className="px-2 py-1 text-[10px] bg-bg-tertiary border border-border-secondary rounded-md text-text-muted hover:text-text-secondary hover:border-border-accent transition-colors"
          >
            {p.label}
          </button>
        ))}
      </div>
      <div className="text-xs text-text-dimmed font-mono">
        formato: minuto hora dia mês dia-semana
      </div>
    </div>
  )
}
