import { useEffect, useState } from 'react'
import { useIsAdmin } from '../../stores/me'
import { friendlyError } from '../../api/client'
import {
  getCaptureConfig,
  updateCaptureConfig,
  type LlmCaptureConfig,
} from '../../api/admin/llmCalls'
import {
  Badge,
  Button,
  Card,
  ErrorMessage,
  Input,
  Select,
  Spinner,
} from '../../ui'

const DURATION_OPTIONS = [
  { value: '1', label: '1 hora' },
  { value: '4', label: '4 horas' },
  { value: '8', label: '8 horas' },
  { value: '24', label: '24 horas' },
  { value: '0', label: 'Sem auto-off (até desligar manualmente)' },
]

function formatRemaining(seconds: number | null | undefined): string {
  if (seconds == null || seconds <= 0) return '—'
  const h = Math.floor(seconds / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  return h > 0 ? `${h}h${m.toString().padStart(2, '0')}` : `${m}min`
}

function parseList(raw: string): string[] | undefined {
  const items = raw
    .split(/[,\n]/)
    .map((s) => s.trim())
    .filter((s) => s.length > 0)
  return items.length > 0 ? items : undefined
}

export function LlmCaptureControl() {
  const isAdmin = useIsAdmin()
  const [config, setConfig] = useState<LlmCaptureConfig | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const [duration, setDuration] = useState('1')
  const [projectIdsText, setProjectIdsText] = useState('')
  const [agentIdsText, setAgentIdsText] = useState('')
  const [workflowIdsText, setWorkflowIdsText] = useState('')

  const reload = async () => {
    setLoading(true)
    setError(null)
    try {
      const cfg = await getCaptureConfig()
      setConfig(cfg)
      setProjectIdsText((cfg.projectIds ?? []).join(', '))
      setAgentIdsText((cfg.agentIds ?? []).join(', '))
      setWorkflowIdsText((cfg.workflowIds ?? []).join(', '))
    } catch (err) {
      setError(friendlyError(err, 'Não foi possível carregar o estado da captura.'))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    if (isAdmin !== false) void reload()
  }, [isAdmin])

  const handleEnable = async () => {
    setSaving(true)
    setError(null)
    try {
      const hours = Number(duration)
      const body = {
        enabled: true,
        projectIds: parseList(projectIdsText),
        agentIds: parseList(agentIdsText),
        workflowIds: parseList(workflowIdsText),
        durationHours: hours > 0 ? hours : undefined,
      }
      await updateCaptureConfig(body)
      await reload()
    } catch (err) {
      setError(friendlyError(err, 'Falha ao ligar a captura.'))
    } finally {
      setSaving(false)
    }
  }

  const handleDisable = async () => {
    setSaving(true)
    setError(null)
    try {
      await updateCaptureConfig({ enabled: false })
      await reload()
    } catch (err) {
      setError(friendlyError(err, 'Falha ao desligar a captura.'))
    } finally {
      setSaving(false)
    }
  }

  if (isAdmin === false) {
    return (
      <Card padded className="mx-auto max-w-3xl text-center">
        <p className="text-sm text-fg-muted">Acesso restrito a administradores.</p>
      </Card>
    )
  }

  return (
    <div className="mx-auto max-w-3xl">
      <div className="mb-8">
        <h1 className="text-[28px] font-semibold tracking-tight">Captura de Prompts LLM</h1>
        <p className="mt-2 text-sm text-fg-muted">
          Quando ligada, todo turno LLM dos agentes selecionados é gravado em{' '}
          <code className="rounded bg-bg-soft px-1 py-0.5 text-[12px]">aihub.llm_invocation_log</code>{' '}
          com request + response + provenance por origem. Captura é{' '}
          <strong>opt-in</strong> e tem TTL automático — desligue assim que terminar o
          diagnóstico. Conteúdo capturado pode conter PII do usuário.
        </p>
      </div>

      {loading && (
        <Card className="flex items-center justify-center py-12">
          <Spinner className="h-6 w-6 text-fg-muted" />
        </Card>
      )}

      {error && <ErrorMessage message={error} />}

      {config && (
        <div className="space-y-6">
          <Card padded className="space-y-4">
            <div className="flex items-center justify-between gap-4">
              <div>
                <h2 className="text-lg font-semibold">Estado atual</h2>
                <p className="mt-1 text-xs text-fg-muted">
                  Atualizado em {new Date(config.updatedAt).toLocaleString('pt-BR')}
                </p>
              </div>
              {config.enabled ? (
                <Badge tone="warning">
                  <span className="mr-2 inline-block h-2 w-2 animate-pulse rounded-full bg-warning" />
                  CAPTURANDO
                </Badge>
              ) : (
                <Badge tone="neutral">Desligada</Badge>
              )}
            </div>

            {config.enabled && (
              <div className="grid grid-cols-2 gap-3 rounded-md border border-warning/30 bg-warning/5 px-4 py-3 text-xs">
                <div>
                  <p className="font-semibold text-fg-muted">Expira em</p>
                  <p className="mt-0.5 font-mono">
                    {formatRemaining(config.secondsRemaining)}
                    {config.expiresAt && (
                      <span className="ml-2 text-fg-dim">
                        ({new Date(config.expiresAt).toLocaleString('pt-BR')})
                      </span>
                    )}
                  </p>
                </div>
                <div>
                  <p className="font-semibold text-fg-muted">Ligada por</p>
                  <p className="mt-0.5 font-mono">{config.enabledBy ?? '—'}</p>
                </div>
                <div className="col-span-2">
                  <p className="font-semibold text-fg-muted">Escopo</p>
                  <p className="mt-0.5">
                    {(config.projectIds?.length ?? 0) + (config.agentIds?.length ?? 0) + (config.workflowIds?.length ?? 0) === 0
                      ? 'Todos os projetos / agents / workflows'
                      : [
                          config.projectIds?.length ? `${config.projectIds.length} projeto(s)` : null,
                          config.agentIds?.length ? `${config.agentIds.length} agent(s)` : null,
                          config.workflowIds?.length ? `${config.workflowIds.length} workflow(s)` : null,
                        ]
                          .filter(Boolean)
                          .join(' · ')}
                  </p>
                </div>
              </div>
            )}
          </Card>

          {!config.enabled && (
            <Card padded className="space-y-4">
              <h2 className="text-lg font-semibold">Ligar captura</h2>
              <p className="text-xs text-fg-muted">
                Deixe os filtros vazios para capturar tudo. Para escopo específico, separe IDs
                por vírgula ou quebra de linha.
              </p>

              <Select
                label="Duração"
                value={duration}
                onChange={(e) => setDuration(e.target.value)}
                options={DURATION_OPTIONS}
              />

              <Input
                label="Projetos (opcional)"
                placeholder="default, projeto-x"
                value={projectIdsText}
                onChange={(e) => setProjectIdsText(e.target.value)}
              />
              <Input
                label="Agentes (opcional)"
                placeholder="router-atendimento-cliente"
                value={agentIdsText}
                onChange={(e) => setAgentIdsText(e.target.value)}
              />
              <Input
                label="Workflows (opcional)"
                placeholder="atendimento-cliente"
                value={workflowIdsText}
                onChange={(e) => setWorkflowIdsText(e.target.value)}
              />

              <div className="flex justify-end gap-2 pt-2">
                <Button onClick={handleEnable} disabled={saving}>
                  {saving ? <Spinner className="h-4 w-4" /> : 'Ligar captura'}
                </Button>
              </div>
            </Card>
          )}

          {config.enabled && (
            <Card padded className="flex items-center justify-between">
              <p className="text-sm text-fg-muted">
                Desligue assim que terminar de debugar. Próximo turno após desligar não grava nada.
              </p>
              <Button variant="secondary" onClick={handleDisable} disabled={saving}>
                {saving ? <Spinner className="h-4 w-4" /> : 'Desligar captura'}
              </Button>
            </Card>
          )}
        </div>
      )}
    </div>
  )
}
