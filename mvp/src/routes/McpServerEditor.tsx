import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import {
  createMcpServer,
  getMcpServer,
  updateMcpServer,
  type McpServer,
  type RequireApproval,
  type SaveMcpServerBody,
} from '../api/mcpServers'
import { friendlyError } from '../api/client'
import { KvTable, type KvRow } from '../components/PostmanEditor/KvTable'
import {
  ArrowLeftIcon,
  Button,
  Card,
  CardHeader,
  ErrorMessage,
  Input,
  Select,
  Spinner,
  StringListEditor,
  Textarea,
} from '../ui'

interface Props {
  mode: 'create' | 'edit'
}

interface FormState {
  name: string
  description: string
  serverLabel: string
  serverUrl: string
  allowedTools: string[]
  headerRows: KvRow<string>[]
  requireApproval: RequireApproval
}

function emptyForm(): FormState {
  return {
    name: '',
    description: '',
    serverLabel: '',
    serverUrl: '',
    allowedTools: [],
    headerRows: [],
    requireApproval: 'never',
  }
}

function shortId() {
  return Math.random().toString(36).slice(2, 10)
}

function fromServer(server: McpServer): FormState {
  return {
    name: server.name,
    description: server.description ?? '',
    serverLabel: server.serverLabel,
    serverUrl: server.serverUrl,
    allowedTools: [...server.allowedTools],
    headerRows: Object.entries(server.headers).map(([key, val]) => ({
      id: shortId(),
      key,
      val,
    })),
    requireApproval: server.requireApproval,
  }
}

// Gera identificador único para novo MCP. Mesma estratégia de ToolEditor —
// crypto.randomUUID em browsers modernos com fallback Math.random.
function generateMcpId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }
  const segment = () => Math.random().toString(36).slice(2, 10)
  return `${segment()}-${segment()}-${segment()}-${segment()}`
}

export function McpServerEditor({ mode }: Props) {
  const navigate = useNavigate()
  const { id } = useParams<{ id?: string }>()
  const [form, setForm] = useState<FormState>(emptyForm)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [loading, setLoading] = useState(mode === 'edit')

  useEffect(() => {
    if (mode !== 'edit' || !id) return
    let cancelled = false
    setLoading(true)
    getMcpServer(id)
      .then((server) => {
        if (cancelled) return
        setForm(fromServer(server))
      })
      .catch((err: unknown) => {
        if (cancelled) return
        setLoadError(friendlyError(err, 'Não foi possível carregar o MCP.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [mode, id])

  const set = <K extends keyof FormState>(key: K, value: FormState[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }))
  }

  const buildBody = (): SaveMcpServerBody | null => {
    const name = form.name.trim()
    const serverLabel = form.serverLabel.trim()
    const serverUrl = form.serverUrl.trim()
    if (!name) {
      setError('Informe um nome para o MCP.')
      return null
    }
    if (!serverLabel) {
      setError('Informe o ServerLabel (ex.: filesystem, github).')
      return null
    }
    if (!serverUrl) {
      setError('Informe a URL do servidor MCP.')
      return null
    }
    if (!/^https?:\/\//i.test(serverUrl)) {
      setError('URL precisa começar com http:// ou https://.')
      return null
    }
    if (form.allowedTools.length === 0) {
      setError('Adicione ao menos uma tool permitida.')
      return null
    }

    const headers: Record<string, string> = {}
    for (const r of form.headerRows) {
      const key = r.key.trim()
      if (!key) continue
      headers[key] = r.val
    }

    // Em modo edit usa o id da rota; em create gera novo GUID — PMs nunca
    // veem nem escolhem o id (igual a ferramentas).
    const persistedId = mode === 'edit' && id ? id : generateMcpId()

    return {
      id: persistedId,
      name,
      description: form.description.trim() || null,
      serverLabel,
      serverUrl,
      allowedTools: form.allowedTools,
      headers,
      requireApproval: form.requireApproval,
    }
  }

  const onSave = async () => {
    setError(null)
    const body = buildBody()
    if (!body) return
    setSubmitting(true)
    try {
      if (mode === 'edit' && id) {
        await updateMcpServer(id, body)
      } else {
        await createMcpServer(body)
      }
      navigate('/mcps')
    } catch (err) {
      setError(friendlyError(err, 'Não foi possível salvar.'))
    } finally {
      setSubmitting(false)
    }
  }

  if (loading) {
    return (
      <Card className="mx-auto max-w-3xl flex items-center justify-center py-12">
        <Spinner className="h-6 w-6 text-fg-muted" />
      </Card>
    )
  }
  if (loadError) {
    return <ErrorMessage message={loadError} className="mx-auto max-w-3xl" />
  }

  return (
    <div className="mx-auto max-w-3xl">
      <div className="mb-6">
        <Button
          variant="ghost"
          size="sm"
          leftIcon={<ArrowLeftIcon className="h-4 w-4" />}
          onClick={() => navigate('/mcps')}
          className="-ml-2 mb-1"
        >
          Voltar
        </Button>
        <h1 className="text-2xl font-semibold tracking-tight">
          {mode === 'edit' ? 'Editar MCP' : 'Novo MCP'}
        </h1>
        <p className="mt-1 text-sm text-fg-muted">
          Os agentes referenciam o MCP pelo identificador interno e o runtime resolve URL, label
          e tools permitidas a cada execução.
        </p>
      </div>

      <Card className="mb-5 space-y-4">
        <CardHeader title="Identificação" description="Visível pra você e pros agentes deste projeto." />
        <Input
          label="Nome"
          value={form.name}
          onChange={(e) => set('name', e.target.value)}
          placeholder="Ex.: Filesystem Local"
          autoFocus
        />
        <Textarea
          label="Descrição"
          value={form.description}
          onChange={(e) => set('description', e.target.value)}
          placeholder="Quando o agente deve usar este MCP?"
        />
      </Card>

      <Card className="mb-5 space-y-4">
        <CardHeader
          title="Conexão"
          description="Endereço público do servidor MCP. ServerLabel é o que o provider LLM enxerga."
        />
        <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
          <Input
            label="ServerLabel"
            value={form.serverLabel}
            onChange={(e) => set('serverLabel', e.target.value)}
            placeholder="ex.: filesystem"
            monospace
            hint="curto, sem espaços"
          />
          <div className="md:col-span-2">
            <Input
              label="ServerUrl"
              value={form.serverUrl}
              onChange={(e) => set('serverUrl', e.target.value)}
              placeholder="https://mcp.example.com/sse"
              monospace
            />
          </div>
        </div>
      </Card>

      <Card className="mb-5 space-y-3">
        <CardHeader
          title="Tools permitidas"
          description="Whitelist de ferramentas do MCP que os agentes podem invocar. Pelo menos uma."
        />
        <StringListEditor
          values={form.allowedTools}
          onChange={(next) => set('allowedTools', next)}
          itemPlaceholder="ex.: read_file"
          emptyHint="Nenhuma tool liberada ainda."
          monospace
        />
      </Card>

      <Card className="mb-5 space-y-3">
        <CardHeader
          title="Headers"
          description="Enviados em toda chamada ao MCP. Coloque tokens de autenticação aqui (ex.: Authorization)."
        />
        <KvTable<string>
          rows={form.headerRows}
          onChange={(rows) => set('headerRows', rows)}
          keyPlaceholder="header"
          valLabel="valor"
          buildEmpty={() => ''}
          renderVal={(val, onChange) => (
            <Input
              value={val}
              onChange={(e) => onChange(e.target.value)}
              placeholder="ex.: Bearer ${TOKEN}"
            />
          )}
        />
      </Card>

      <Card className="mb-5 space-y-3">
        <CardHeader
          title="Aprovação humana"
          description="Quando exigido, o agente pausa e espera HITL antes de invocar tools deste MCP."
        />
        <Select
          className="max-w-xs"
          value={form.requireApproval}
          onChange={(e) => set('requireApproval', e.target.value as RequireApproval)}
          options={[
            { value: 'never', label: 'Nunca pedir aprovação' },
            { value: 'always', label: 'Sempre pedir aprovação' },
          ]}
        />
      </Card>

      {error && <ErrorMessage message={error} className="mb-4" />}

      <div className="flex items-center justify-end gap-2">
        <Button variant="ghost" onClick={() => navigate('/mcps')}>
          Cancelar
        </Button>
        <Button onClick={onSave} loading={submitting}>
          {mode === 'edit' ? 'Salvar alterações' : 'Cadastrar MCP'}
        </Button>
      </div>
    </div>
  )
}
