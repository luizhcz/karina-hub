import type { ColumnDef } from '@tanstack/react-table'
import { DataTable } from '../../shared/data/DataTable'
import { Card } from '../../shared/ui/Card'
import { Badge } from '../../shared/ui/Badge'
import { Button } from '../../shared/ui/Button'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { EmptyState } from '../../shared/ui/EmptyState'
import { useBackgroundServices } from '../../api/backgroundServices'
import type { BackgroundServiceInfo } from '../../api/backgroundServices'

// OneTime roda uma vez no startup (ex: DatabaseBootstrap); Continuous é long-running
// (timer, listener LISTEN/NOTIFY ou channel cleanup). Cor segue a paleta do Badge.
const LIFECYCLE_COLOR: Record<string, 'green' | 'blue' | 'gray'> = {
  OneTime: 'blue',
  Continuous: 'green',
}

function formatInterval(seconds?: number): string {
  if (seconds == null) return '—'
  if (seconds < 60) return `${seconds}s`
  if (seconds < 3600) return `${Math.round(seconds / 60)}min`
  if (seconds < 86400) return `${Math.round(seconds / 3600)}h`
  return `${Math.round(seconds / 86400)}d`
}

export function BackgroundJobsPage() {
  const { data, isLoading, error, refetch } = useBackgroundServices()

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar background services" onRetry={refetch} />

  const items = data?.items ?? []

  const columns: ColumnDef<BackgroundServiceInfo, unknown>[] = [
    {
      accessorKey: 'name',
      header: 'Nome',
      cell: ({ getValue }) => (
        <span className="font-semibold text-text-primary">{String(getValue())}</span>
      ),
    },
    {
      accessorKey: 'description',
      header: 'Descrição',
      cell: ({ getValue }) => (
        <span className="text-sm text-text-secondary">{(getValue() as string) ?? '—'}</span>
      ),
    },
    {
      accessorKey: 'lifecycle',
      header: 'Ciclo',
      cell: ({ getValue }) => {
        const v = String(getValue())
        return <Badge variant={LIFECYCLE_COLOR[v] ?? 'gray'}>{v}</Badge>
      },
    },
    {
      accessorKey: 'intervalSeconds',
      header: 'Intervalo',
      cell: ({ getValue }) => (
        <span className="text-xs text-text-muted font-mono">
          {formatInterval(getValue() as number | undefined)}
        </span>
      ),
    },
    {
      accessorKey: 'typeName',
      header: 'Classe',
      cell: ({ getValue }) => (
        <span className="font-mono text-xs text-text-dimmed">{String(getValue())}</span>
      ),
    },
  ]

  return (
    <div className="flex flex-col gap-6 p-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Background Services</h1>
          <p className="text-sm text-text-muted mt-1">
            IHostedService registrados no <code className="text-xs bg-bg-tertiary px-1 py-0.5 rounded">BackgroundServiceRegistry</code>.
            Rodam automaticamente no processo backend — leitura apenas, não são disparáveis pela UI.
          </p>
        </div>
        <Button variant="secondary" size="sm" onClick={() => refetch()}>Atualizar</Button>
      </div>

      <Card padding={false}>
        {items.length === 0 ? (
          <EmptyState
            title="Nenhum serviço registrado"
            description="O BackgroundServiceRegistry está vazio. Verifique AddBackgroundServiceRegistry() no startup."
          />
        ) : (
          <DataTable
            data={items}
            columns={columns}
            searchPlaceholder="Buscar serviço..."
          />
        )}
      </Card>

      <Card title="Referência">
        <ul className="text-xs text-text-muted space-y-2 leading-relaxed">
          <li>
            <strong className="text-text-secondary">OneTime</strong>: executa uma vez no startup
            (ex: <code>DatabaseBootstrap</code> limpa execuções órfãs após restart).
          </li>
          <li>
            <strong className="text-text-secondary">Continuous</strong>: long-running — timer periódico
            (ex: <code>AuditRetention</code> a cada 24h) ou listener reativo
            (ex: <code>CrossNodeCoordinator</code> via LISTEN/NOTIFY).
          </li>
          <li>
            Observabilidade: métricas de execução (contadores, duração) são emitidas via OpenTelemetry —
            dashboards de tracing/metrics dão a visão de saúde em tempo real.
          </li>
        </ul>
      </Card>
    </div>
  )
}
