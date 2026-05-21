import { useEffect, useRef, useState } from 'react'
import { Button, Modal, Textarea } from '../../ui'

interface Props {
  open: boolean
  /** Tipo do agente — usado no copy explicativo. */
  agentType: 'Router'
  onConfirm: (reason: string) => void
  onClose: () => void
  /** Estado externo de "salvando" — desabilita botão e mostra spinner. */
  submitting?: boolean
}

const MIN_LENGTH = 10
const MAX_LENGTH = 500

/**
 * Modal obrigatório de motivo da mudança no submit de Router. O motivo entra
 * no histórico do agente em <c>agent_approval_history</c> e dá contexto ao
 * revisor humano da fila de aprovações — por isso é exigido ≥10 chars.
 */
export function ChangeReasonModal({ open, agentType, onConfirm, onClose, submitting }: Props) {
  const [reason, setReason] = useState('')
  const textareaRef = useRef<HTMLTextAreaElement>(null)
  const trimmedLength = reason.trim().length
  const tooShort = trimmedLength > 0 && trimmedLength < MIN_LENGTH
  const canConfirm = trimmedLength >= MIN_LENGTH && trimmedLength <= MAX_LENGTH && !submitting

  // Reset value e foca textarea cada vez que reabre. Garante UX consistente
  // se o user fecha e abre de novo após edição.
  useEffect(() => {
    if (!open) return
    setReason('')
    const t = setTimeout(() => textareaRef.current?.focus(), 50)
    return () => clearTimeout(t)
  }, [open])

  const handleConfirm = () => {
    if (!canConfirm) return
    onConfirm(reason.trim())
  }

  return (
    <Modal
      open={open}
      onClose={submitting ? () => {} : onClose}
      size="md"
      title={`Enviar ${agentType} para aprovação`}
      description={`Descreva o que mudou neste ${agentType}. O motivo fica registrado no histórico e ajuda o time de governança a revisar a fila.`}
      footer={
        <div className="flex justify-end gap-2">
          <Button variant="ghost" onClick={onClose} disabled={submitting}>
            Cancelar
          </Button>
          <Button onClick={handleConfirm} disabled={!canConfirm} loading={submitting}>
            Enviar para aprovação
          </Button>
        </div>
      }
    >
      <div className="space-y-2">
        <Textarea
          ref={textareaRef}
          label="Motivo da mudança"
          placeholder="Ex.: troquei a descrição pra refletir o nome novo da squad e adicionei intent de cancelamento."
          value={reason}
          onChange={(e) => setReason(e.target.value.slice(0, MAX_LENGTH))}
          autoGrow
          maxAutoGrowHeight={180}
          error={tooShort ? `Mínimo ${MIN_LENGTH} caracteres.` : undefined}
          aria-required="true"
        />
        <div className="flex justify-end text-[11px] text-fg-dim">
          {trimmedLength}/{MAX_LENGTH}
        </div>
      </div>
    </Modal>
  )
}
