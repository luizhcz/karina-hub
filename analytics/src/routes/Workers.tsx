// Tela "Workers" — registry dos IHostedServices (background services).
// Read-only: o backend só expõe lista. Útil pra ops/SRE confirmar quais
// rotinas estão registradas + qual o intervalo. Global do tenant (não tem
// project-scoping nessa rota — services rodam por instalação).

import { useCallback } from 'react'
import { Card, CardHeader, ErrorState, Table, type Column } from '../components/ui'
import { useApi } from '../hooks/useApi'
import {
  listBackgroundServices,
  type BackgroundServiceItem,
} from '../api/backgroundServices'
import { formatInterval, formatInt } from '../utils/format'

export function Workers() {
  const state = useApi(
    useCallback((signal) => listBackgroundServices({ signal }), []),
    [],
  )

  return (
    <div className="flex flex-col gap-6">
      <Card padded={false}>
        <div className="p-5">
          <CardHeader
            title="Workers em execução"
            description={
              state.data
                ? `${formatInt(state.data.total)} IHostedService${state.data.total === 1 ? '' : 's'} registrados`
                : 'IHostedServices registrados pelo composition root'
            }
          />
        </div>
        {state.error ? (
          <div className="px-5 pb-5">
            <ErrorState error={state.error} onRetry={state.refetch} />
          </div>
        ) : (
          <Table
            columns={workerColumns}
            rows={state.data?.items ?? null}
            loading={state.loading}
            keyOf={(row) => row.name}
            empty={{ title: 'Nenhum worker registrado' }}
          />
        )}
      </Card>
    </div>
  )
}

const workerColumns: ReadonlyArray<Column<BackgroundServiceItem>> = [
  {
    key: 'name',
    header: 'Nome',
    cell: (r) => <span className="font-mono text-xs text-fg">{r.name}</span>,
    sortBy: (r) => r.name,
  },
  {
    key: 'lifecycle',
    header: 'Lifecycle',
    cell: (r) => (
      <span
        className={
          r.lifecycle === 'OneTime'
            ? 'text-xs text-fg-muted'
            : 'text-xs text-fg'
        }
      >
        {r.lifecycle === 'Continuous'
          ? 'Contínuo'
          : r.lifecycle === 'OneTime'
            ? 'One-time'
            : r.lifecycle}
      </span>
    ),
    sortBy: (r) => r.lifecycle,
  },
  {
    key: 'interval',
    header: 'Intervalo',
    align: 'right',
    cell: (r) =>
      r.intervalSeconds == null ? (
        <span className="text-xs text-fg-dim">event-driven</span>
      ) : (
        formatInterval(r.intervalSeconds)
      ),
    sortBy: (r) => r.intervalSeconds ?? Number.MAX_SAFE_INTEGER,
  },
  {
    key: 'description',
    header: 'Descrição',
    cell: (r) => <span className="text-xs text-fg-muted">{r.description}</span>,
    sortBy: (r) => r.description,
  },
  {
    key: 'type',
    header: 'Tipo',
    cell: (r) => <span className="font-mono text-[11px] text-fg-dim">{r.typeName}</span>,
    sortBy: (r) => r.typeName,
  },
]
