import { Card } from '../../../shared/ui/Card'
import { Input } from '../../../shared/ui/Input'
import type { BlocklistSettings } from '../../../api/blocklist'

interface Props {
  settings: BlocklistSettings
  onChange: (next: BlocklistSettings) => void
  disabled?: boolean
}

export function BlocklistStatusCard({ settings, onChange, disabled }: Props) {
  const update = (patch: Partial<BlocklistSettings>) => onChange({ ...settings, ...patch })

  return (
    <Card title="Status">
      <div className="flex flex-col gap-4">
        <ToggleRow
          label="Habilitado"
          description="Quando desligado, nenhum scan acontece nesse projeto."
          checked={settings.enabled}
          onChange={(v) => update({ enabled: v })}
          disabled={disabled}
        />
        <ToggleRow
          label="Scan de input (mensagens do usuário)"
          description="Bloqueia conteúdo proibido antes de chamar o LLM (token zero em violação)."
          checked={settings.scanInput}
          onChange={(v) => update({ scanInput: v })}
          disabled={disabled || !settings.enabled}
        />
        <ToggleRow
          label="Scan de output (resposta do agente)"
          description="Bloqueia/redacta conteúdo proibido antes de entregar ao cliente."
          checked={settings.scanOutput}
          onChange={(v) => update({ scanOutput: v })}
          disabled={disabled || !settings.enabled}
        />
        <ToggleRow
          label="Audit log de violações"
          description="Grava cada bloqueio em admin_audit_log com hash + contexto ofuscado."
          checked={settings.auditBlocks}
          onChange={(v) => update({ auditBlocks: v })}
          disabled={disabled || !settings.enabled}
        />
        <Input
          label="Texto de substituição (modo redact)"
          value={settings.replacement}
          onChange={(e) => update({ replacement: e.target.value })}
          placeholder="[REDACTED]"
          disabled={disabled || !settings.enabled}
        />
      </div>
    </Card>
  )
}

function ToggleRow({
  label,
  description,
  checked,
  onChange,
  disabled,
}: {
  label: string
  description: string
  checked: boolean
  onChange: (v: boolean) => void
  disabled?: boolean
}) {
  return (
    <label className={`flex items-start gap-3 ${disabled ? 'opacity-50' : 'cursor-pointer'}`}>
      <input
        type="checkbox"
        className="mt-1 h-4 w-4 rounded accent-accent-blue"
        checked={checked}
        disabled={disabled}
        onChange={(e) => onChange(e.target.checked)}
      />
      <div className="flex-1">
        <div className="text-sm font-medium text-text-primary">{label}</div>
        <div className="text-xs text-text-muted mt-0.5">{description}</div>
      </div>
    </label>
  )
}
