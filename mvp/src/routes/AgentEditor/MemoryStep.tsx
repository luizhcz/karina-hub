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
          title="Memória do agente"
          description="Quando ligada, o agente guarda um “bloco de anotações” entre as conversas — preferências do usuário, dados já confirmados, próximo passo. Em vez de reler todo o histórico a cada mensagem, ele atualiza esse bloco a cada turno."
        />
        <ToggleRow
          checked={form.memory.enabled}
          disabled={readonly}
          onChange={toggle}
          label="Ligar memória do agente"
          hint={
            form.memory.enabled
              ? 'O agente vai manter um resumo do que aprendeu sobre o usuário entre as mensagens. O usuário não vê esse resumo — ele fica nos bastidores.'
              : 'Sem memória, cada mensagem é tratada do zero — o agente não lembra o que aconteceu antes na conversa.'
          }
        />
      </Card>

      {form.memory.enabled && (
        <Card className="space-y-3">
          <CardHeader
            title="O que o agente vai lembrar"
            description="Defina os campos que o agente vai manter na memória — ex.: nome do usuário, preferências, etapa atual da tarefa. Cada campo vai virar uma anotação que o agente atualiza a cada mensagem."
          />
          <JsonSchemaBuilder
            value={form.memory.schema}
            onChange={(schema) => updateMemory((prev) => ({ ...prev, schema }))}
            emptyHint="Adicione os campos que o agente precisa lembrar entre as mensagens (preferências, fatos confirmados, próximo passo, etc.)."
          />
        </Card>
      )}
    </div>
  )
}
