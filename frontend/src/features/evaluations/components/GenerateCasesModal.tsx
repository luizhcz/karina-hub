import { useState } from 'react'
import { Modal } from '../../../shared/ui/Modal'
import { Button } from '../../../shared/ui/Button'
import { useAgents } from '../../../api/agents'
import {
  GeneratorFailureError,
  GeneratorTimeoutError,
  generateCases,
  type GeneratedCase,
} from '../../../api/testCaseGenerator'
import { toast } from '../../../stores/toast'

interface GenerateCasesModalProps {
  open: boolean
  onClose: () => void
  /** Chamado quando os cases são gerados; recebe lista pra adicionar/substituir. */
  onGenerated: (cases: GeneratedCase[], replace: boolean) => void
  /** Quando o editor já tem cases preenchidos, modal pergunta se substitui. */
  hasExistingCases: boolean
}

const COUNT_OPTIONS = [5, 10, 15] as const

export function GenerateCasesModal({
  open,
  onClose,
  onGenerated,
  hasExistingCases,
}: GenerateCasesModalProps) {
  const { data: agents } = useAgents()

  const [agentId, setAgentId] = useState<string>('')
  const [freeDescription, setFreeDescription] = useState('')
  const [count, setCount] = useState<number>(5)
  const [generating, setGenerating] = useState(false)
  const [errorMsg, setErrorMsg] = useState<string | null>(null)
  const [replaceMode, setReplaceMode] = useState(false)

  const selectedAgent = agents?.find((a) => a.id === agentId) ?? null

  const reset = () => {
    setAgentId('')
    setFreeDescription('')
    setCount(5)
    setReplaceMode(false)
    setErrorMsg(null)
  }

  const handleClose = () => {
    if (generating) return
    reset()
    onClose()
  }

  const handleGenerate = async () => {
    if (!selectedAgent && !freeDescription.trim()) {
      setErrorMsg('Escolha um agente da lista ou descreva o agente alvo.')
      return
    }
    setGenerating(true)
    setErrorMsg(null)
    try {
      const result = await generateCases({
        agentName: selectedAgent?.name ?? 'Agente alvo',
        description: selectedAgent?.description ?? freeDescription.trim() ?? undefined,
        instructions: selectedAgent?.instructions ?? undefined,
        tools: (selectedAgent?.tools ?? [])
          .filter((t) => !!t.name)
          .map((t) => ({ name: t.name as string, type: t.type })),
        targetCaseCount: count,
      })
      if (!result.testCases || result.testCases.length === 0) {
        setErrorMsg('Gerador não produziu nenhum case válido. Tente novamente.')
        return
      }
      onGenerated(result.testCases, replaceMode)
      toast.success(`${result.testCases.length} cases gerados — revise antes de salvar.`)
      reset()
      onClose()
    } catch (err) {
      if (err instanceof GeneratorTimeoutError || err instanceof GeneratorFailureError) {
        setErrorMsg(err.message)
      } else {
        setErrorMsg(err instanceof Error ? err.message : 'Erro ao gerar cases.')
      }
    } finally {
      setGenerating(false)
    }
  }

  return (
    <Modal open={open} onClose={handleClose} title="✨ Gerar cases com IA" size="lg">
      <div className="flex flex-col gap-4">
        <div className="rounded-md border border-blue-500/20 bg-blue-500/5 px-3 py-2 text-xs text-text-secondary">
          O conteúdo do agente é enviado a um modelo IA pra gerar cases. <strong>Não inclua dados de cliente reais</strong> nas instruções — use placeholders.
        </div>

        <div>
          <label className="block text-sm font-medium text-text-primary mb-1.5">
            Agente de referência (opcional)
          </label>
          <select
            value={agentId}
            onChange={(e) => setAgentId(e.target.value)}
            disabled={generating}
            className="w-full rounded-md border border-border-secondary bg-bg-secondary px-3 py-2 text-sm text-text-primary"
          >
            <option value="">— Sem agente; descrever manualmente —</option>
            {agents?.map((a) => (
              <option key={a.id} value={a.id}>
                {a.name} ({a.id})
              </option>
            ))}
          </select>
          {selectedAgent && (
            <p className="mt-1 text-[11px] text-text-muted">
              Tools disponíveis: {selectedAgent.tools && selectedAgent.tools.length > 0
                ? selectedAgent.tools.map((t) => t.name).filter(Boolean).join(', ')
                : 'nenhuma'}
            </p>
          )}
        </div>

        {!selectedAgent && (
          <div>
            <label className="block text-sm font-medium text-text-primary mb-1.5">
              Descrição do agente alvo
            </label>
            <textarea
              value={freeDescription}
              onChange={(e) => setFreeDescription(e.target.value)}
              disabled={generating}
              rows={3}
              placeholder="Ex.: Atendente virtual de RH que responde dúvidas sobre benefícios e folha. Não dá orientação jurídica nem médica."
              className="w-full rounded-md border border-border-secondary bg-bg-secondary px-3 py-2 text-sm text-text-primary"
            />
          </div>
        )}

        <div>
          <label className="block text-sm font-medium text-text-primary mb-1.5">
            Quantidade de cases
          </label>
          <div className="flex gap-1.5">
            {COUNT_OPTIONS.map((n) => (
              <button
                key={n}
                type="button"
                disabled={generating}
                onClick={() => setCount(n)}
                className={
                  'rounded-md border px-3 py-1.5 text-xs font-medium transition ' +
                  (count === n
                    ? 'border-blue-500 bg-blue-500/15 text-blue-300'
                    : 'border-border-secondary bg-bg-secondary text-text-secondary hover:text-text-primary')
                }
              >
                {n}
              </button>
            ))}
          </div>
          <p className="mt-1 text-[11px] text-text-muted">
            Cobertura padrão: 30% happy-path · 40% edge-case · 20% rejection · 10% tool-validation
          </p>
        </div>

        {hasExistingCases && (
          <label className="flex items-start gap-2 text-sm">
            <input
              type="checkbox"
              checked={replaceMode}
              onChange={(e) => setReplaceMode(e.target.checked)}
              disabled={generating}
              className="mt-0.5"
            />
            <span>
              <span className="font-medium text-text-primary">Substituir cases existentes</span>
              <span className="block text-xs text-text-muted mt-0.5">
                Sem marcar, os cases gerados são adicionados ao final da lista.
              </span>
            </span>
          </label>
        )}

        {errorMsg && (
          <div className="rounded-md border border-red-500/30 bg-red-500/10 px-3 py-2 text-sm text-red-400">
            {errorMsg}
          </div>
        )}

        <div className="flex justify-end gap-2 pt-2">
          <Button variant="secondary" onClick={handleClose} disabled={generating}>
            Cancelar
          </Button>
          <Button onClick={handleGenerate} loading={generating}>
            {generating ? 'Gerando…' : 'Gerar'}
          </Button>
        </div>
      </div>
    </Modal>
  )
}
