import { useState } from 'react'
import { Button } from '../../../shared/ui/Button'
import { Modal } from '../../../shared/ui/Modal'

const MIN_REASON = 10

interface UpdateAgentReasonModalProps {
  open: boolean
  loading: boolean
  onClose: () => void
  onConfirm: (changeReason: string, breakingChange: boolean) => void
}

/**
 * Modal de confirmação obrigatório no PUT /api/agents/{id} — backend exige
 * <c>changeReason</c> (min 10 chars). Toda atualização vira AdminOverride no
 * audit. Captura também <c>breakingChange</c> opcional (default false).
 */
export function UpdateAgentReasonModal({ open, loading, onClose, onConfirm }: UpdateAgentReasonModalProps) {
  const [reason, setReason] = useState('')
  const [breaking, setBreaking] = useState(false)

  const handleClose = () => {
    setReason('')
    setBreaking(false)
    onClose()
  }

  const handleConfirm = () => {
    const trimmed = reason.trim()
    if (trimmed.length < MIN_REASON) return
    onConfirm(trimmed, breaking)
  }

  return (
    <Modal open={open} onClose={handleClose} title="Confirmar atualização" size="md">
      <div className="flex flex-col gap-4">
        <p className="text-sm text-text-muted">
          Toda atualização é registrada na trilha de governança como AdminOverride.
          Descreva a motivação da mudança ({MIN_REASON}+ caracteres).
        </p>

        <div>
          <label className="block text-sm font-medium text-text-primary mb-1.5">
            Justificativa <span className="text-danger">*</span>
          </label>
          <textarea
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            rows={4}
            autoFocus
            placeholder="Ex.: ajuste no prompt para incluir disclaimer regulatório CVM."
            className="w-full rounded-md border border-border-primary bg-bg-primary px-3 py-2 text-sm text-text-primary focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/30"
          />
          <p className="mt-1 text-xs text-text-muted">
            {reason.trim().length}/{MIN_REASON} caracteres mínimos
          </p>
        </div>

        <label className="flex items-start gap-2 text-sm">
          <input
            type="checkbox"
            checked={breaking}
            onChange={(e) => setBreaking(e.target.checked)}
            className="mt-0.5"
          />
          <span>
            <span className="font-medium text-text-primary">Mudança breaking</span>
            <span className="block text-xs text-text-muted mt-0.5">
              Workflows pinados em versões anteriores não recebem patch automático.
              Marque quando alterar comportamento crítico (proibições, schema de output, tools removidas).
            </span>
          </span>
        </label>

        <div className="flex justify-end gap-2 pt-2">
          <Button variant="ghost" onClick={handleClose} disabled={loading}>
            Cancelar
          </Button>
          <Button
            onClick={handleConfirm}
            loading={loading}
            disabled={reason.trim().length < MIN_REASON}
          >
            Salvar
          </Button>
        </div>
      </div>
    </Modal>
  )
}
