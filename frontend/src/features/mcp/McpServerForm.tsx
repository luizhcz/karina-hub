import { useState } from 'react'
import type { CreateMcpServerRequest, McpServer } from '../../api/mcpServers'
import { Card } from '../../shared/ui/Card'
import { Button } from '../../shared/ui/Button'
import { Input } from '../../shared/ui/Input'

interface Props {
  initial?: McpServer
  onSubmit: (payload: CreateMcpServerRequest) => void
  loading?: boolean
  submitLabel?: string
  /** Em edit mode o Id não pode mudar (é chave primária). */
  idLocked?: boolean
}

interface FormState {
  id: string
  name: string
  description: string
  serverLabel: string
  serverUrl: string
  allowedToolsText: string   // newline-separated pra UX simples
  headersText: string         // JSON string pra KV map
  requireApproval: 'never' | 'always'
}

function toFormState(initial?: McpServer): FormState {
  return {
    id: initial?.id ?? '',
    name: initial?.name ?? '',
    description: initial?.description ?? '',
    serverLabel: initial?.serverLabel ?? '',
    serverUrl: initial?.serverUrl ?? '',
    allowedToolsText: (initial?.allowedTools ?? []).join('\n'),
    headersText: JSON.stringify(initial?.headers ?? {}, null, 2),
    requireApproval: initial?.requireApproval ?? 'never',
  }
}

export function McpServerForm({ initial, onSubmit, loading, submitLabel = 'Salvar', idLocked }: Props) {
  const [form, setForm] = useState<FormState>(toFormState(initial))
  const [error, setError] = useState<string | null>(null)

  const update = <K extends keyof FormState>(key: K, value: FormState[K]) =>
    setForm((prev) => ({ ...prev, [key]: value }))

  const handleSubmit = () => {
    setError(null)

    if (!form.id.trim()) { setError('Id é obrigatório.'); return }
    if (!form.name.trim()) { setError('Nome é obrigatório.'); return }
    if (!form.serverLabel.trim()) { setError('ServerLabel é obrigatório.'); return }
    if (!form.serverUrl.trim()) { setError('ServerUrl é obrigatório.'); return }
    try {
      const u = new URL(form.serverUrl.trim())
      if (u.protocol !== 'http:' && u.protocol !== 'https:') throw new Error()
    } catch {
      setError('ServerUrl deve ser URL absoluta http/https.')
      return
    }

    const allowedTools = form.allowedToolsText
      .split('\n')
      .map((s) => s.trim())
      .filter(Boolean)
    if (allowedTools.length === 0) { setError('AllowedTools precisa ao menos um item (uma tool por linha).'); return }

    let headers: Record<string, string>
    try {
      headers = form.headersText.trim() === '' ? {} : JSON.parse(form.headersText)
      if (typeof headers !== 'object' || Array.isArray(headers) || headers === null) throw new Error()
      for (const v of Object.values(headers)) if (typeof v !== 'string') throw new Error()
    } catch {
      setError('Headers precisa ser JSON objeto { "key": "value" }.')
      return
    }

    onSubmit({
      id: form.id.trim(),
      name: form.name.trim(),
      description: form.description.trim() || undefined,
      serverLabel: form.serverLabel.trim(),
      serverUrl: form.serverUrl.trim(),
      allowedTools,
      headers,
      requireApproval: form.requireApproval,
    })
  }

  return (
    <div className="flex flex-col gap-4">
      <Card title="Identificação">
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          <Input
            label="Id*"
            placeholder="ex: mcp-filesystem"
            value={form.id}
            onChange={(e) => update('id', e.target.value)}
            disabled={idLocked}
          />
          <Input
            label="Nome*"
            placeholder="ex: Filesystem MCP"
            value={form.name}
            onChange={(e) => update('name', e.target.value)}
          />
        </div>
        <div className="mt-4">
          <Input
            label="Descrição"
            placeholder="Opcional — descreva o propósito deste MCP server"
            value={form.description}
            onChange={(e) => update('description', e.target.value)}
          />
        </div>
      </Card>

      <Card title="Conexão">
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          <Input
            label="ServerLabel*"
            placeholder="ex: filesystem"
            value={form.serverLabel}
            onChange={(e) => update('serverLabel', e.target.value)}
          />
          <Input
            label="ServerUrl* (http/https)"
            placeholder="https://meu-mcp.example.com"
            value={form.serverUrl}
            onChange={(e) => update('serverUrl', e.target.value)}
          />
        </div>
      </Card>

      <Card title="AllowedTools">
        <p className="text-xs text-text-muted mb-2">
          Uma tool por linha — só os nomes listados aqui ficam disponíveis para os agentes.
        </p>
        <textarea
          value={form.allowedToolsText}
          onChange={(e) => update('allowedToolsText', e.target.value)}
          placeholder="read_file&#10;list_dir&#10;write_file"
          rows={6}
          className="w-full bg-bg-tertiary border border-border-primary rounded-lg px-3 py-2 text-sm text-text-primary font-mono resize-y focus:outline-none focus:border-accent-blue"
        />
      </Card>

      <Card title="Headers (JSON)">
        <p className="text-xs text-text-muted mb-2">
          Headers HTTP passados ao MCP server. Ex: <code className="text-xs">{`{ "Authorization": "Bearer …" }`}</code>. Deixe <code className="text-xs">{`{}`}</code> se não precisa de auth.
        </p>
        <textarea
          value={form.headersText}
          onChange={(e) => update('headersText', e.target.value)}
          placeholder='{}'
          rows={5}
          className="w-full bg-bg-tertiary border border-border-primary rounded-lg px-3 py-2 text-sm text-text-primary font-mono resize-y focus:outline-none focus:border-accent-blue"
        />
      </Card>

      <Card title="Política de Aprovação">
        <div className="flex flex-col gap-1">
          <label className="text-xs text-text-muted">RequireApproval</label>
          <select
            value={form.requireApproval}
            onChange={(e) => update('requireApproval', e.target.value as 'never' | 'always')}
            className="bg-bg-tertiary border border-border-primary rounded-md px-2.5 py-1.5 text-sm text-text-primary focus:outline-none focus:border-accent-blue"
          >
            <option value="never">never — sem HITL</option>
            <option value="always">always — cada invocação requer aprovação humana</option>
          </select>
        </div>
      </Card>

      {error && (
        <div className="px-4 py-3 rounded-lg text-sm bg-red-500/10 border border-red-500/30 text-red-400">
          {error}
        </div>
      )}

      <div className="flex justify-end gap-2">
        <Button variant="primary" onClick={handleSubmit} loading={loading}>
          {submitLabel}
        </Button>
      </div>
    </div>
  )
}
