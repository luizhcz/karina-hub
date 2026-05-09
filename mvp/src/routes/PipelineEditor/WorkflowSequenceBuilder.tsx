import { useState } from 'react'
import type { WorkflowAgentReference } from '../../api/workflows'
import {
  AgentIcon,
  Button,
  PlusIcon,
  cn,
} from '../../ui'
import { AgentPickerModal, type PickedAgent } from './AgentPickerModal'

export interface PipelineStep extends WorkflowAgentReference {
  /** Cache do nome do agente para render — não viaja pro backend. */
  name?: string
  /** Cache da descrição/resumo. */
  description?: string | null
}

interface WorkflowSequenceBuilderProps {
  steps: PipelineStep[]
  onChange: (next: PipelineStep[]) => void
  readOnly?: boolean
}

/**
 * Lista vertical numerada de agentes que compõem a sequência. Reorder via
 * setas ↑/↓ (sem drag-drop pra simplicidade e acessibilidade). Setas verticais
 * entre cards reforçam visualmente que a saída de um vira entrada do próximo.
 */
export function WorkflowSequenceBuilder({ steps, onChange, readOnly }: WorkflowSequenceBuilderProps) {
  const [pickerOpen, setPickerOpen] = useState(false)

  const excludeIds = new Set(steps.map((s) => s.agentId))

  const moveStep = (index: number, delta: number) => {
    const target = index + delta
    if (target < 0 || target >= steps.length) return
    const next = [...steps]
    const [item] = next.splice(index, 1)
    next.splice(target, 0, item)
    onChange(next)
  }

  const removeStep = (index: number) => {
    const next = steps.filter((_, i) => i !== index)
    onChange(next)
  }

  const addAgent = ({ agent, agentVersionId }: PickedAgent) => {
    setPickerOpen(false)
    onChange([
      ...steps,
      {
        agentId: agent.id,
        agentVersionId,
        role: null,
        name: agent.name,
        description: agent.description,
      },
    ])
  }

  return (
    <div className="space-y-3">
      {steps.length === 0 ? (
        <div className="rounded-xl border border-dashed border-border bg-bg-soft/50 px-5 py-8 text-center">
          <p className="text-sm text-fg-muted">Nenhum agente adicionado ainda.</p>
          <p className="mt-1 text-xs text-fg-dim">
            Clique em <strong>Adicionar agente</strong> para começar.
          </p>
        </div>
      ) : (
        <ol className="space-y-0">
          {steps.map((step, index) => (
            <li key={`${step.agentId}-${index}`}>
              <StepCard
                step={step}
                index={index}
                isFirst={index === 0}
                isLast={index === steps.length - 1}
                readOnly={readOnly}
                onMoveUp={() => moveStep(index, -1)}
                onMoveDown={() => moveStep(index, +1)}
                onRemove={() => removeStep(index)}
              />
              {index < steps.length - 1 && <Connector />}
            </li>
          ))}
        </ol>
      )}

      {!readOnly && (
        <div className="pt-2">
          <Button
            variant="secondary"
            size="sm"
            leftIcon={<PlusIcon className="h-4 w-4" />}
            onClick={() => setPickerOpen(true)}
          >
            Adicionar agente
          </Button>
        </div>
      )}

      <AgentPickerModal
        open={pickerOpen}
        onClose={() => setPickerOpen(false)}
        excludeIds={excludeIds}
        onPick={addAgent}
      />
    </div>
  )
}

interface StepCardProps {
  step: PipelineStep
  index: number
  isFirst: boolean
  isLast: boolean
  readOnly?: boolean
  onMoveUp: () => void
  onMoveDown: () => void
  onRemove: () => void
}

function StepCard({ step, index, isFirst, isLast, readOnly, onMoveUp, onMoveDown, onRemove }: StepCardProps) {
  return (
    <div className="flex items-start gap-3 rounded-xl border border-border bg-surface p-4 transition hover:border-accent/40">
      <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-accent text-sm font-semibold text-accent-contrast">
        {index + 1}
      </span>
      <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-accent-subtle text-accent">
        <AgentIcon className="h-4 w-4" />
      </div>
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-baseline gap-2">
          <span className="text-sm font-semibold text-fg">{step.name ?? step.agentId}</span>
          {step.role && (
            <span className="rounded-full bg-bg-soft px-2 py-0.5 text-[10px] font-medium uppercase tracking-wider text-fg-muted">
              {step.role}
            </span>
          )}
        </div>
        <p className="mt-0.5 truncate font-mono text-[10px] uppercase tracking-wider text-fg-dim">
          {step.agentId}
        </p>
        {step.description && (
          <p className="mt-1 line-clamp-1 text-xs text-fg-muted">{step.description}</p>
        )}
      </div>
      {!readOnly && (
        <div className="flex shrink-0 items-center gap-1">
          <IconButton title="Mover para cima" onClick={onMoveUp} disabled={isFirst}>
            ↑
          </IconButton>
          <IconButton title="Mover para baixo" onClick={onMoveDown} disabled={isLast}>
            ↓
          </IconButton>
          <IconButton title="Remover" onClick={onRemove} variant="danger">
            ×
          </IconButton>
        </div>
      )}
    </div>
  )
}

function Connector() {
  return (
    <div
      className="flex justify-center py-1"
      aria-hidden="true"
    >
      <div className="flex flex-col items-center gap-0">
        <span className="h-3 w-px bg-border" />
        <span className="text-[10px] text-fg-dim">▼</span>
        <span className="h-3 w-px bg-border" />
      </div>
    </div>
  )
}

interface IconButtonProps {
  onClick: () => void
  disabled?: boolean
  title: string
  children: React.ReactNode
  variant?: 'default' | 'danger'
}

function IconButton({ onClick, disabled, title, children, variant = 'default' }: IconButtonProps) {
  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      disabled={disabled}
      className={cn(
        'flex h-7 w-7 items-center justify-center rounded-md border border-border bg-surface text-sm transition',
        'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/30',
        disabled && 'cursor-not-allowed opacity-40',
        !disabled && variant === 'default' && 'text-fg-muted hover:border-accent/60 hover:text-fg',
        !disabled && variant === 'danger' && 'text-fg-muted hover:border-danger/60 hover:text-danger',
      )}
    >
      {children}
    </button>
  )
}
