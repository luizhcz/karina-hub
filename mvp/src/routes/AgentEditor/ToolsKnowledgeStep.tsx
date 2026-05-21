import { Badge, Card, CardHeader } from '../../ui'
import type { GenericTool } from '../../api/genericTools'
import { CatalogPicker } from './CatalogPicker'
import { toggleId } from './formCodec'
import type { FormState } from './types'

interface ToolsKnowledgeStepProps {
  form: FormState
  setForm: (mutator: (prev: FormState) => FormState) => void
  tools: GenericTool[]
  toolsLoading: boolean
  toolsError: string | null
  readonly: boolean
}

export function ToolsKnowledgeStep({
  form,
  setForm,
  tools,
  toolsLoading,
  toolsError,
  readonly,
}: ToolsKnowledgeStepProps) {
  const setToolIds = (next: string[]) => setForm((prev) => ({ ...prev, toolIds: next }))

  return (
    <div className="space-y-5">
      <Card className="space-y-3">
        <CardHeader
          title="Ferramentas"
          description="Endpoints HTTP que o agente pode chamar durante a conversa."
          actions={
            form.toolIds.length > 0 ? (
              <Badge tone="accent">{form.toolIds.length} selecionada(s)</Badge>
            ) : undefined
          }
        />
        <CatalogPicker
          loading={toolsLoading}
          error={toolsError}
          items={tools.map((t) => ({
            id: t.id,
            primary: t.name || t.id,
            secondary: `${t.httpMethod} ${t.urlTemplate}`,
            badge: t.httpMethod,
          }))}
          selectedIds={form.toolIds}
          onToggle={(id) => setToolIds(toggleId(form.toolIds, id))}
          emptyHint="Nenhuma ferramenta cadastrada neste projeto. Crie em /ferramentas para liberar para o agente."
          disabled={readonly}
        />
      </Card>
    </div>
  )
}
