import { useEffect, useState } from 'react'
import { Button } from '../../../shared/ui/Button'
import { Modal } from '../../../shared/ui/Modal'
import { Textarea } from '../../../shared/ui/Textarea'
import { ApiError } from '../../../api/client'
import { usePublishAgentVersion } from '../../../api/agents'
import { toast } from '../../../stores/toast'

interface PublishVersionModalProps {
  agentId: string
  agentName?: string
  open: boolean
  onClose: () => void
}

/**
 * Modal pra publicar nova AgentVersion declarando intent (breaking ou patch).
 * BreakingChange=true exige ChangeReason não-vazio (validado client + server).
 */
export function PublishVersionModal({ agentId, agentName, open, onClose }: PublishVersionModalProps) {
  const publishMutation = usePublishAgentVersion()
  const [breakingChange, setBreakingChange] = useState(false)
  const [changeReason, setChangeReason] = useState('')
  const [touched, setTouched] = useState(false)

  // Reset state ao abrir/fechar.
  useEffect(() => {
    if (!open) {
      setBreakingChange(false)
      setChangeReason('')
      setTouched(false)
    }
  }, [open])

  const reasonRequired = breakingChange
  const reasonInvalid = reasonRequired && changeReason.trim().length === 0
  const submitDisabled = publishMutation.isPending || reasonInvalid

  const handleSubmit = () => {
    setTouched(true)
    if (reasonInvalid) return
    publishMutation.mutate(
      {
        id: agentId,
        body: {
          breakingChange,
          changeReason: changeReason.trim() || undefined,
        },
      },
      {
        onSuccess: (version) => {
          toast.success(`Version ${version.agentVersionId.slice(0, 8)}… publicada (rev ${version.revision}).`)
          onClose()
        },
        onError: (err) => {
          const msg = err instanceof ApiError ? err.message : 'Erro ao publicar version.'
          toast.error(msg)
        },
      },
    )
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={`Publicar nova versão${agentName ? ` — ${agentName}` : ''}`}
      size="md"
    >
      <div className="flex flex-col gap-4">
        <p className="text-sm text-text-secondary">
          Snapshot atual da definição vai ser persistido como nova
          <code className="mx-1 px-1 py-0.5 bg-bg-tertiary rounded text-xs">AgentVersion</code>.
          Idempotente por content hash — re-publish sem mudança retorna a version existing.
        </p>

        <label className="flex items-start gap-3 cursor-pointer p-3 rounded-md border border-border-primary hover:bg-bg-tertiary/50 transition-colors">
          <input
            type="checkbox"
            checked={breakingChange}
            onChange={(e) => setBreakingChange(e.target.checked)}
            className="mt-1 h-4 w-4 cursor-pointer accent-yellow-500"
          />
          <div className="flex-1">
            <div className="text-sm font-medium text-text-primary">
              Esta é uma breaking change?
            </div>
            <p className="text-xs text-text-muted mt-1 leading-relaxed">
              Workflows pinados em ancestor desta version <strong>não receberão</strong> esta
              atualização automaticamente — caller fica preso ao snapshot anterior até revisão
              manual. Use quando a mudança quebra contrato (schema do output, breaking de tools,
              prompt incompatível). Patches (não-breaking) propagam livremente.
            </p>
          </div>
        </label>

        <div>
          <label className="block text-sm font-medium text-text-primary mb-1">
            Motivo da mudança {reasonRequired && <span className="text-yellow-500">*</span>}
          </label>
          <Textarea
            value={changeReason}
            onChange={(e) => setChangeReason(e.target.value)}
            onBlur={() => setTouched(true)}
            placeholder={
              breakingChange
                ? 'Ex: schema do output mudou de {answer} pra {answer, citations[]}'
                : 'Opcional — ex: typo no prompt'
            }
            rows={3}
            className={touched && reasonInvalid ? 'border-yellow-500' : ''}
          />
          {touched && reasonInvalid && (
            <p className="text-xs text-yellow-500 mt-1">
              ChangeReason é obrigatório quando breaking change — callers precisam de contexto
              pra decidir migrar.
            </p>
          )}
          {!reasonRequired && (
            <p className="text-xs text-text-dimmed mt-1">
              Patches sem reason são aceitos, mas reason ajuda histórico de versionamento.
            </p>
          )}
        </div>

        <div className="flex justify-end gap-2 pt-2 border-t border-border-primary">
          <Button variant="ghost" size="sm" onClick={onClose} disabled={publishMutation.isPending}>
            Cancelar
          </Button>
          <Button
            variant="primary"
            size="sm"
            onClick={handleSubmit}
            disabled={submitDisabled}
          >
            {publishMutation.isPending ? 'Publicando...' : 'Publicar'}
          </Button>
        </div>
      </div>
    </Modal>
  )
}
