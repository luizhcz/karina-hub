import { useState } from 'react'
import { Button } from '../../../shared/ui/Button'
import type { LocalMsg } from '../types'

export function ApprovalBubble({
  item,
  onResolve,
}: {
  item: Extract<LocalMsg, { kind: 'approval' }>
  onResolve: (toolCallId: string, response: string) => void
}) {
  const [inputValue, setInputValue] = useState('')

  if (item.resolved) {
    const label =
      item.resolved === 'approved' ? '✓ Operação aprovada'
      : item.resolved === 'rejected' ? '✗ Operação rejeitada'
      : `✓ ${item.resolved}`
    return (
      <div className="flex justify-start">
        <div className="max-w-[70%] px-4 py-2.5 rounded-2xl rounded-bl-sm text-sm bg-bg-tertiary border border-border-primary text-text-muted italic">
          {label}
          {item.createdAt && (
            <p className="text-[10px] mt-1 text-text-dimmed not-italic">
              {new Date(item.createdAt).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}
            </p>
          )}
        </div>
      </div>
    )
  }

  const isInput = item.interactionType === 'Input'

  const title = isInput ? 'Resposta necessária' : 'Aprovação necessária'

  if (isInput) {
    return (
      <div className="flex justify-start">
        <div className="max-w-[80%] px-4 py-3 rounded-2xl rounded-bl-sm text-sm bg-accent-blue/10 border border-accent-blue/40">
          <p className="font-semibold text-text-primary mb-1">{title}</p>
          <p className="text-xs text-text-secondary mb-3 leading-relaxed">{item.question}</p>
          <textarea
            className="w-full px-3 py-2 rounded-lg border border-border-primary bg-bg-primary text-text-primary text-sm resize-none focus:outline-none focus:ring-1 focus:ring-accent-blue"
            rows={3}
            placeholder="Digite sua resposta..."
            value={inputValue}
            onChange={(e) => setInputValue(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && !e.shiftKey && inputValue.trim()) {
                e.preventDefault()
                onResolve(item.toolCallId, inputValue.trim())
              }
            }}
          />
          <div className="flex justify-end mt-2">
            <Button
              variant="primary"
              size="sm"
              disabled={!inputValue.trim()}
              onClick={() => onResolve(item.toolCallId, inputValue.trim())}
            >
              Enviar
            </Button>
          </div>
        </div>
      </div>
    )
  }

  const buttons = item.options
    ? item.options.map((opt) => ({ label: opt, value: opt, variant: 'secondary' as const }))
    : [
        { label: 'Aprovar', value: 'approved', variant: 'primary' as const },
        { label: 'Rejeitar', value: 'rejected', variant: 'danger' as const },
      ]

  return (
    <div className="flex justify-start">
      <div className="max-w-[80%] px-4 py-3 rounded-2xl rounded-bl-sm text-sm bg-accent-blue/10 border border-accent-blue/40">
        <p className="font-semibold text-text-primary mb-1">{title}</p>
        <p className="text-xs text-text-secondary mb-3 leading-relaxed">{item.question}</p>
        <div className="flex flex-wrap gap-2">
          {buttons.map((btn) => (
            <Button
              key={btn.value}
              variant={btn.variant}
              size="sm"
              onClick={() => onResolve(item.toolCallId, btn.value)}
            >
              {btn.label}
            </Button>
          ))}
        </div>
      </div>
    </div>
  )
}
