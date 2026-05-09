import { Card, CardHeader, JsonSchemaBuilder } from '../../ui'
import { ToggleRow } from './ToggleRow'
import type { FormState, OperationalMemorySection } from './types'

interface MemoryStepProps {
  form: FormState
  setForm: (mutator: (prev: FormState) => FormState) => void
  readonly: boolean
}

export function MemoryStep({ form, setForm, readonly }: MemoryStepProps) {
  const updateMemory = (mutator: (prev: OperationalMemorySection) => OperationalMemorySection) =>
    setForm((prev) => ({ ...prev, memory: mutator(prev.memory) }))

  const toggle = (next: boolean) =>
    updateMemory((prev) => ({ ...prev, enabled: next }))

  return (
    <div className="space-y-5">
      <Card className="space-y-3">
        <CardHeader
          title="Memória operacional"
          description="Estado canônico que o agente atualiza a cada turno. Substitui ler todo o histórico — o middleware lê do banco e injeta no prompt antes da chamada, e persiste o que o LLM emite no campo `operationalMemory` ao final."
        />
        <ToggleRow
          checked={form.memory.enabled}
          disabled={readonly}
          onChange={toggle}
          label="Ativar memória operacional"
          hint={
            form.memory.enabled
              ? 'Quando ativa, o LLM passa a emitir um campo `operationalMemory` em todo turno (replace puro). O caller só recebe a resposta normal — o middleware faz strip antes de devolver.'
              : 'Sem memória, cada turno relê todo o histórico pra reconstruir estado.'
          }
        />
      </Card>

      {form.memory.enabled && (
        <>
          <Card className="space-y-3">
            <CardHeader
              title="Estrutura da memória"
              description="JSON Schema do payload que o agente vai manter. O schema é mergeado ao output estruturado do agente — não pode ter chave 'operationalMemory' colidindo no schema do output."
            />
            <JsonSchemaBuilder
              value={form.memory.schema}
              onChange={(schema) => updateMemory((prev) => ({ ...prev, schema }))}
              emptyHint="Adicione os campos canônicos do estado mental do agente (preferências, fatos confirmados, próximo passo, etc.)."
            />
          </Card>

          <div className="rounded-xl border border-warning/40 bg-warning/5 px-4 py-3 text-xs leading-relaxed text-warning">
            <strong>Streaming:</strong> quando memória está ativa, o middleware bufferiza
            todo o stream pra fazer o strip do campo no fim. O TTFT (tempo até o primeiro
            token visível ao caller) iguala o de uma chamada não-streaming.
          </div>
        </>
      )}
    </div>
  )
}

