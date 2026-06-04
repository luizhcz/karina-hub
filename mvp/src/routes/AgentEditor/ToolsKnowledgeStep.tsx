import { Badge, Card, CardHeader } from '../../ui'
import type { GenericTool } from '../../api/genericTools'
import type { FunctionToolInfo } from '../../api/functions'
import { CatalogPicker } from './CatalogPicker'
import { toggleId } from './formCodec'
import type { FormState } from './types'

interface ToolsKnowledgeStepProps {
  form: FormState
  setForm: (mutator: (prev: FormState) => FormState) => void
  tools: GenericTool[]
  toolsLoading: boolean
  toolsError: string | null
  functionTools: FunctionToolInfo[]
  functionToolsLoading: boolean
  functionToolsError: string | null
  readonly: boolean
}

export function ToolsKnowledgeStep({
  form,
  setForm,
  tools,
  toolsLoading,
  toolsError,
  functionTools,
  functionToolsLoading,
  functionToolsError,
  readonly,
}: ToolsKnowledgeStepProps) {
  const setToolIds = (next: string[]) => setForm((prev) => ({ ...prev, toolIds: next }))
  const setFunctionToolNames = (next: string[]) =>
    setForm((prev) => ({ ...prev, functionToolNames: next }))

  return (
    <div className="space-y-5">
      <Card className="space-y-3">
        <CardHeader
          title="Ferramentas integradas"
          description="Funções nativas do EfsAiHub. Disponíveis em todos os projetos, sem configuração extra."
          actions={
            form.functionToolNames.length > 0 ? (
              <Badge tone="accent">{form.functionToolNames.length} selecionada(s)</Badge>
            ) : undefined
          }
        />
        <CatalogPicker
          loading={functionToolsLoading}
          error={functionToolsError}
          items={functionTools.map((t) => ({
            id: t.name,
            primary: t.name,
            secondary: t.description ?? '',
          }))}
          selectedIds={form.functionToolNames}
          onToggle={(id) => setFunctionToolNames(toggleId(form.functionToolNames, id))}
          emptyHint="Nenhuma ferramenta integrada disponível."
          disabled={readonly}
        />
      </Card>

      <Card className="space-y-3">
        <CardHeader
          title="Conectores HTTP"
          description="Endpoints HTTP cadastrados pelo seu time. Use pra integrar APIs internas e externas."
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
          emptyHint="Nenhum conector HTTP cadastrado. Crie em /ferramentas para liberar para o agente."
          disabled={readonly}
        />
      </Card>
    </div>
  )
}
