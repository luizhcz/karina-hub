import { useState, useEffect } from 'react'
import { Card } from '../../shared/ui/Card'
import { Tabs } from '../../shared/ui/Tabs'
import { Badge } from '../../shared/ui/Badge'
import { Button } from '../../shared/ui/Button'
import { Input } from '../../shared/ui/Input'

// ── Local config store ────────────────────────────────────────────────────────

interface LocalConfig {
  key: string
  value: string
  description: string
  updatedAt: string
}

const DEFAULT_CONFIGS: LocalConfig[] = [
  { key: 'MAX_CONCURRENT_EXECUTIONS', value: '10', description: 'Máximo de execuções simultâneas', updatedAt: new Date().toISOString() },
  { key: 'DEFAULT_TIMEOUT_MS', value: '30000', description: 'Timeout padrão de execução em ms', updatedAt: new Date().toISOString() },
  { key: 'RETRY_MAX_ATTEMPTS', value: '3', description: 'Número máximo de retentativas', updatedAt: new Date().toISOString() },
  { key: 'TOKEN_BUDGET_DEFAULT', value: '4096', description: 'Budget padrão de tokens por execução', updatedAt: new Date().toISOString() },
  { key: 'AUDIT_RETENTION_DAYS', value: '90', description: 'Dias para reter logs de auditoria', updatedAt: new Date().toISOString() },
]

// ── Circuit Breakers & Queue data types ──────────────────────────────────────

interface CircuitBreakerStatus {
  name: string
  state: 'Closed' | 'Open' | 'HalfOpen'
  failureCount: number
  lastFailureAt?: string
}

interface QueueStatus {
  queueName: string
  depth: number
  processingCount: number
  errorCount: number
}

const MOCK_CIRCUIT_BREAKERS: CircuitBreakerStatus[] = [
  { name: 'OpenAI Provider', state: 'Closed', failureCount: 0 },
  { name: 'Azure Foundry', state: 'Closed', failureCount: 1, lastFailureAt: new Date(Date.now() - 3600000).toISOString() },
  { name: 'External Tool API', state: 'HalfOpen', failureCount: 3, lastFailureAt: new Date(Date.now() - 600000).toISOString() },
]

const MOCK_QUEUES: QueueStatus[] = [
  { queueName: 'execution-queue', depth: 5, processingCount: 2, errorCount: 0 },
  { queueName: 'hitl-queue', depth: 1, processingCount: 0, errorCount: 0 },
  { queueName: 'notification-queue', depth: 12, processingCount: 3, errorCount: 1 },
]

const TAB_ITEMS = [
  { key: 'settings', label: 'Settings' },
  { key: 'circuit-breakers', label: 'Circuit Breakers' },
  { key: 'queue-health', label: 'Queue Health' },
]

// ── Settings Tab ─────────────────────────────────────────────────────────────

function SettingsTab() {
  const [configs, setConfigs] = useState<LocalConfig[]>(DEFAULT_CONFIGS)
  const [editValues, setEditValues] = useState<Record<string, string>>(() =>
    Object.fromEntries(DEFAULT_CONFIGS.map((c) => [c.key, c.value]))
  )
  const [savedKeys, setSavedKeys] = useState<Set<string>>(new Set())

  const handleSave = (key: string) => {
    setConfigs((prev) =>
      prev.map((c) =>
        c.key === key
          ? { ...c, value: editValues[key] ?? c.value, updatedAt: new Date().toISOString() }
          : c
      )
    )
    setSavedKeys((prev) => new Set([...prev, key]))
    setTimeout(() => {
      setSavedKeys((prev) => {
        const s = new Set(prev)
        s.delete(key)
        return s
      })
    }, 2000)
  }

  return (
    <div className="flex flex-col gap-2 pt-4">
      {configs.map((cfg) => (
        <div
          key={cfg.key}
          className="flex items-center gap-4 p-4 bg-bg-tertiary border border-border-primary rounded-xl"
        >
          <div className="flex-1 min-w-0">
            <p className="text-sm font-mono font-medium text-text-primary">{cfg.key}</p>
            <p className="text-xs text-text-muted mt-0.5">{cfg.description}</p>
            <p className="text-xs text-text-dimmed mt-1">
              Atualizado: {new Date(cfg.updatedAt).toLocaleString('pt-BR')}
            </p>
          </div>
          <div className="flex items-center gap-2">
            <Input
              value={editValues[cfg.key] ?? cfg.value}
              onChange={(e) => setEditValues((prev) => ({ ...prev, [cfg.key]: e.target.value }))}
              className="w-48"
            />
            <Button
              size="sm"
              variant={savedKeys.has(cfg.key) ? 'secondary' : 'primary'}
              onClick={() => handleSave(cfg.key)}
            >
              {savedKeys.has(cfg.key) ? 'Salvo!' : 'Salvar'}
            </Button>
          </div>
        </div>
      ))}
    </div>
  )
}

// ── Circuit Breakers Tab ──────────────────────────────────────────────────────

function CircuitBreakersTab() {
  const [tick, setTick] = useState(0)

  useEffect(() => {
    const id = setInterval(() => setTick((t) => t + 1), 30000)
    return () => clearInterval(id)
  }, [])

  const stateVariant = (state: CircuitBreakerStatus['state']): 'green' | 'red' | 'yellow' => {
    if (state === 'Closed') return 'green'
    if (state === 'Open') return 'red'
    return 'yellow'
  }

  return (
    <div className="flex flex-col gap-4 pt-4">
      <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
        {MOCK_CIRCUIT_BREAKERS.map((cb) => (
          <Card key={cb.name} title={cb.name}>
            <div className="flex flex-col gap-3">
              <div className="flex items-center justify-between">
                <span className="text-xs text-text-muted">Estado</span>
                <Badge variant={stateVariant(cb.state)}>{cb.state}</Badge>
              </div>
              <div className="flex items-center justify-between">
                <span className="text-xs text-text-muted">Falhas</span>
                <span className={`text-sm font-bold ${cb.failureCount > 0 ? 'text-red-400' : 'text-emerald-400'}`}>
                  {cb.failureCount}
                </span>
              </div>
              {cb.lastFailureAt && (
                <div className="flex items-center justify-between">
                  <span className="text-xs text-text-muted">Última falha</span>
                  <span className="text-xs text-text-secondary">
                    {new Date(cb.lastFailureAt).toLocaleString('pt-BR')}
                  </span>
                </div>
              )}
            </div>
          </Card>
        ))}
      </div>
      <p className="text-xs text-text-dimmed text-right">Auto-refresh a cada 30s · tick #{tick}</p>
    </div>
  )
}

// ── Queue Health Tab ──────────────────────────────────────────────────────────

function QueueHealthTab() {
  const [tick, setTick] = useState(0)

  useEffect(() => {
    const id = setInterval(() => setTick((t) => t + 1), 30000)
    return () => clearInterval(id)
  }, [])

  return (
    <div className="flex flex-col gap-4 pt-4">
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        {MOCK_QUEUES.map((q) => (
          <Card key={q.queueName} title={q.queueName}>
            <div className="grid grid-cols-3 gap-3 mt-2">
              <div className="text-center">
                <p className="text-2xl font-bold text-text-primary">{q.depth}</p>
                <p className="text-xs text-text-muted mt-1">Profundidade</p>
              </div>
              <div className="text-center">
                <p className="text-2xl font-bold text-blue-400">{q.processingCount}</p>
                <p className="text-xs text-text-muted mt-1">Processando</p>
              </div>
              <div className="text-center">
                <p className={`text-2xl font-bold ${q.errorCount > 0 ? 'text-red-400' : 'text-text-primary'}`}>
                  {q.errorCount}
                </p>
                <p className="text-xs text-text-muted mt-1">Erros</p>
              </div>
            </div>
          </Card>
        ))}
      </div>
      <p className="text-xs text-text-dimmed text-right">Auto-refresh a cada 30s · tick #{tick}</p>
    </div>
  )
}

// ── Main ConfigPage ───────────────────────────────────────────────────────────

export function ConfigPage() {
  const [activeTab, setActiveTab] = useState('settings')

  return (
    <div className="flex flex-col gap-6 p-6">
      <div>
        <h1 className="text-2xl font-bold text-text-primary">Configurações do Sistema</h1>
        <p className="text-sm text-text-muted mt-1">Settings, circuit breakers e saúde das filas</p>
      </div>

      <Tabs items={TAB_ITEMS} active={activeTab} onChange={setActiveTab} />

      {activeTab === 'settings' && <SettingsTab />}
      {activeTab === 'circuit-breakers' && <CircuitBreakersTab />}
      {activeTab === 'queue-health' && <QueueHealthTab />}
    </div>
  )
}
