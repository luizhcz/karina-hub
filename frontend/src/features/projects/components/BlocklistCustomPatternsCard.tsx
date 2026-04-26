import { useState } from 'react'
import { Card } from '../../../shared/ui/Card'
import { Button } from '../../../shared/ui/Button'
import { Input } from '../../../shared/ui/Input'
import { Select } from '../../../shared/ui/Select'
import { Badge } from '../../../shared/ui/Badge'
import { EmptyState } from '../../../shared/ui/EmptyState'
import type {
  BlocklistAction,
  BlocklistCustomPattern,
  BlocklistPatternType,
  BlocklistSettings,
} from '../../../api/blocklist'

interface Props {
  settings: BlocklistSettings
  onChange: (next: BlocklistSettings) => void
  disabled?: boolean
}

const TYPE_OPTIONS = [
  { value: 'Literal', label: 'Literal (texto exato)' },
  { value: 'Regex', label: 'Regex' },
]

const ACTION_OPTIONS = [
  { value: 'Block', label: 'Bloquear' },
  { value: 'Redact', label: 'Redactar' },
  { value: 'Warn', label: 'Apenas avisar' },
]

export function BlocklistCustomPatternsCard({ settings, onChange, disabled }: Props) {
  const [draft, setDraft] = useState<BlocklistCustomPattern>(emptyDraft())

  const customs = settings.customPatterns ?? []

  const addPattern = () => {
    if (!draft.id.trim() || !draft.pattern.trim()) return
    if (customs.some((p) => p.id === draft.id)) return
    onChange({ ...settings, customPatterns: [...customs, draft] })
    setDraft(emptyDraft())
  }

  const removePattern = (id: string) => {
    onChange({ ...settings, customPatterns: customs.filter((p) => p.id !== id) })
  }

  return (
    <Card title="Patterns Customizados do Projeto">
      {customs.length === 0 ? (
        <EmptyState
          title="Sem patterns customizados"
          description="Adicione termos específicos do seu domínio (ex: codenames, IDs internos)."
        />
      ) : (
        <div className="flex flex-col gap-2 mb-4">
          {customs.map((p) => (
            <div
              key={p.id}
              className="flex items-center justify-between gap-3 p-3 bg-bg-secondary rounded-md"
            >
              <div className="flex items-center gap-2 flex-1 min-w-0">
                <Badge variant="purple">{p.type}</Badge>
                <Badge variant={p.action === 'Block' ? 'red' : 'yellow'}>{p.action}</Badge>
                <span className="text-sm font-medium text-text-primary truncate">{p.id}</span>
                <code className="text-xs text-text-muted font-mono truncate">{p.pattern}</code>
              </div>
              <Button
                variant="danger"
                size="sm"
                disabled={disabled}
                onClick={() => removePattern(p.id)}
              >
                Remover
              </Button>
            </div>
          ))}
        </div>
      )}

      <div className="border-t border-border-secondary pt-4">
        <div className="text-sm font-medium text-text-primary mb-3">Adicionar novo</div>
        <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
          <Input
            label="ID *"
            value={draft.id}
            onChange={(e) => setDraft({ ...draft, id: e.target.value })}
            placeholder="ex: codename_alpha"
            disabled={disabled}
          />
          <Input
            label="Padrão *"
            value={draft.pattern}
            onChange={(e) => setDraft({ ...draft, pattern: e.target.value })}
            placeholder="ex: Projeto Atlas"
            disabled={disabled}
          />
          <Select
            label="Tipo"
            options={TYPE_OPTIONS}
            value={draft.type}
            onChange={(e) => setDraft({ ...draft, type: e.target.value as BlocklistPatternType })}
            disabled={disabled}
          />
          <Select
            label="Ação"
            options={ACTION_OPTIONS}
            value={draft.action}
            onChange={(e) => setDraft({ ...draft, action: e.target.value as BlocklistAction })}
            disabled={disabled}
          />
        </div>
        <div className="flex items-center gap-4 mt-3">
          <label className="flex items-center gap-2 text-xs text-text-secondary cursor-pointer">
            <input
              type="checkbox"
              className="h-3.5 w-3.5 rounded accent-accent-blue"
              checked={draft.wholeWord}
              disabled={disabled}
              onChange={(e) => setDraft({ ...draft, wholeWord: e.target.checked })}
            />
            Whole-word (boundary \b\b)
          </label>
          <label className="flex items-center gap-2 text-xs text-text-secondary cursor-pointer">
            <input
              type="checkbox"
              className="h-3.5 w-3.5 rounded accent-accent-blue"
              checked={draft.caseSensitive}
              disabled={disabled}
              onChange={(e) => setDraft({ ...draft, caseSensitive: e.target.checked })}
            />
            Case-sensitive
          </label>
          <Button
            onClick={addPattern}
            disabled={disabled || !draft.id.trim() || !draft.pattern.trim()}
            size="sm"
          >
            Adicionar
          </Button>
        </div>
      </div>
    </Card>
  )
}

function emptyDraft(): BlocklistCustomPattern {
  return {
    id: '',
    type: 'Literal',
    pattern: '',
    action: 'Block',
    wholeWord: true,
    caseSensitive: false,
  }
}
