import { Badge, Card, CardHeader, cn } from '../../ui'
import type { GenericTool } from '../../api/genericTools'
import type { McpServer } from '../../api/mcpServers'
import { CatalogPicker } from './CatalogPicker'
import { toggleId } from './formCodec'
import type { FormState } from './types'

interface ToolsKnowledgeStepProps {
  form: FormState
  setForm: (mutator: (prev: FormState) => FormState) => void
  tools: GenericTool[]
  toolsLoading: boolean
  toolsError: string | null
  mcps: McpServer[]
  mcpsLoading: boolean
  mcpsError: string | null
  readonly: boolean
}

export function ToolsKnowledgeStep({
  form,
  setForm,
  tools,
  toolsLoading,
  toolsError,
  mcps,
  mcpsLoading,
  mcpsError,
  readonly,
}: ToolsKnowledgeStepProps) {
  const setToolIds = (next: string[]) => setForm((prev) => ({ ...prev, toolIds: next }))
  const setMcpIds = (next: string[]) => setForm((prev) => ({ ...prev, mcpIds: next }))

  return (
    <div className="space-y-5">
      <Card className="space-y-3">
        <CardHeader
          title="Conhecimento"
          description="Bases de conhecimento que o agente pode consultar (RAG). Em desenvolvimento."
          actions={<Badge tone="warning">Em breve</Badge>}
        />
        <label
          className={cn(
            'flex items-center gap-3 rounded-lg border border-dashed border-border bg-bg-soft px-4 py-3',
            'cursor-not-allowed opacity-60',
          )}
        >
          <input
            type="checkbox"
            className="h-4 w-4 accent-accent"
            checked={false}
            disabled
            readOnly
          />
          <div className="min-w-0 flex-1">
            <p className="text-sm font-medium text-fg">Habilitar conhecimento</p>
            <p className="text-xs text-fg-muted">
              Em breve você poderá conectar bases de conhecimento ao agente.
            </p>
          </div>
        </label>
      </Card>

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
            secondary: t.description || `${t.httpMethod} ${t.urlTemplate}`,
            badge: t.httpMethod,
          }))}
          selectedIds={form.toolIds}
          onToggle={(id) => setToolIds(toggleId(form.toolIds, id))}
          emptyHint="Nenhuma ferramenta cadastrada neste projeto. Crie em /ferramentas para liberar para o agente."
          disabled={readonly}
        />
      </Card>

      <Card className="space-y-3">
        <CardHeader
          title="MCPs"
          description="Servidores MCP que o agente pode invocar. URL, label e tools permitidas são resolvidos em runtime."
          actions={
            form.mcpIds.length > 0 ? (
              <Badge tone="accent">{form.mcpIds.length} selecionado(s)</Badge>
            ) : undefined
          }
        />
        <CatalogPicker
          loading={mcpsLoading}
          error={mcpsError}
          items={mcps.map((m) => ({
            id: m.id,
            primary: m.name || m.id,
            secondary: m.description || m.serverUrl,
            badge: m.serverLabel,
          }))}
          selectedIds={form.mcpIds}
          onToggle={(id) => setMcpIds(toggleId(form.mcpIds, id))}
          emptyHint="Nenhum MCP cadastrado neste projeto. Cadastre em /mcps para liberar para o agente."
          disabled={readonly}
        />
      </Card>
    </div>
  )
}
