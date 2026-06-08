// Tela "Workers" — observabilidade dos IHostedServices (background services).
// Combina o registry estático (descrição, categoria, lifecycle, intervalo
// esperado) com heartbeats em runtime (Started + RecordSuccess/RecordError
// per-pod) pra detectar serviços travados.
//
// Status por linha:
//   - Error:   últimas N atualizações foram exceptions (errorCount > 0 e
//              lastErrorAtUtc > lastSuccessAtUtc)
//   - Stale:   lifecycle=Continuous + Interval definido + lastTickAtUtc <
//              now - 2*interval (serviço deveria ter rodado e não rodou)
//   - Done:    lifecycle=OneTime e o heartbeat tem tick=1 (rodou no boot
//              e morreu, comportamento esperado)
//   - Idle:    heartbeat existe mas tickCount=0 (Started chamado mas
//              nenhuma iteração de trabalho — channel-driven sem demanda,
//              ou gateado por flag de config)
//   - Healthy: caso geral — recente o suficiente pra não ser stale
//   - Unknown: heartbeat=null (per-pod: o pod atual nunca executou; em
//              multi-instance, outro pod pode estar rodando)
//
// UX: lista agrupada por categoria, refresh manual via botão (não auto-poll
// pra evitar martelar o /admin endpoint).

import { useCallback, useState } from 'react'
import { Button, Card, CardHeader, ErrorState, cn } from '../components/ui'
import { useApi } from '../hooks/useApi'
import {
  listBackgroundServices,
  type BackgroundCategory,
  type BackgroundServiceItem,
} from '../api/backgroundServices'
import {
  formatInt,
  formatInterval,
  formatRelative,
  formatTimestampSecond,
  formatUptime,
} from '../utils/format'

type WorkerStatus = 'Healthy' | 'Stale' | 'Error' | 'Done' | 'Idle' | 'Unknown'

// Ordem fixa das categorias na tela — alinhada com o pipeline mental: o que
// roda primeiro, o que mantém o estado, o que recupera, o que limpa.
const CATEGORY_ORDER: BackgroundCategory[] = [
  'Bootstrap',
  'Persistence',
  'Dispatcher',
  'Evaluation',
  'Recovery',
  'Cleanup',
  'Messaging',
  'Guards',
]

const CATEGORY_LABELS: Record<BackgroundCategory, string> = {
  Bootstrap: 'Bootstrap',
  Persistence: 'Persistência',
  Dispatcher: 'Dispatchers',
  Evaluation: 'Avaliação',
  Recovery: 'Recovery',
  Cleanup: 'Cleanup / Retenção',
  Messaging: 'Mensageria',
  Guards: 'Guards',
}

const CATEGORY_DESCRIPTIONS: Record<BackgroundCategory, string> = {
  Bootstrap: 'Rodam uma vez no boot do processo e terminam.',
  Persistence: 'Drenam Channel bounded e persistem em batch (event-driven).',
  Dispatcher: 'Consomem filas (jobs standalone, webhooks pendentes).',
  Evaluation: 'Pickup e execução de evaluation runs.',
  Recovery: 'Recuperam estado pendurado após crash ou timeout.',
  Cleanup: 'TTLs, partições antigas, channels SSE órfãos.',
  Messaging: 'LISTEN/NOTIFY cross-pod.',
  Guards: 'Hot-reload de configs de runtime (blocklist, etc.).',
}

function computeStatus(item: BackgroundServiceItem, nowUtcMs: number): WorkerStatus {
  const hb = item.heartbeat
  if (!hb) return 'Unknown'

  const lastSuccess = hb.lastSuccessAtUtc ? Date.parse(hb.lastSuccessAtUtc) : null
  const lastError = hb.lastErrorAtUtc ? Date.parse(hb.lastErrorAtUtc) : null
  const lastTick = hb.lastTickAtUtc ? Date.parse(hb.lastTickAtUtc) : null

  // Erro recente é prioritário — quando há falhas, sinaliza vermelho mesmo
  // que pareça vivo. Critério: último erro é MAIS recente que último sucesso.
  if (hb.errorCount > 0 && lastError != null && (lastSuccess == null || lastError >= lastSuccess)) {
    return 'Error'
  }

  if (item.lifecycle === 'OneTime') {
    // OneTime: pra startup services, ter tick=1 (sucesso) basta. Sem tick,
    // ainda não rodou (raro, mas pode acontecer entre Started e o trabalho real).
    return hb.tickCount > 0 ? 'Done' : 'Idle'
  }

  // Continuous com intervalo: detecta stale comparando last-tick contra
  // 2x o intervalo esperado. Margem cobre jitter natural sem disparar falso-positivo.
  if (item.intervalSeconds != null && item.intervalSeconds > 0) {
    if (lastTick == null) {
      // Started mas nenhum tick ainda. Pra intervalo curto isso é janela
      // transiente; pra intervalo longo (>60s) ainda é normal logo após boot.
      const ageSinceStartSec = hb.startedAtUtc
        ? (nowUtcMs - Date.parse(hb.startedAtUtc)) / 1000
        : 0
      if (ageSinceStartSec < item.intervalSeconds * 2) return 'Idle'
      return 'Stale'
    }
    const ageSec = (nowUtcMs - lastTick) / 1000
    if (ageSec > item.intervalSeconds * 2) return 'Stale'
    return 'Healthy'
  }

  // Continuous sem intervalo: event-driven (channels, NOTIFY). Sem demanda,
  // tickCount permanece 0 por design — não é "stale", é "Idle aguardando trabalho".
  return hb.tickCount > 0 ? 'Healthy' : 'Idle'
}

const STATUS_CLASSES: Record<WorkerStatus, string> = {
  Healthy: 'bg-success/15 text-success',
  Done: 'bg-success/15 text-success',
  Idle: 'bg-surface-hover text-fg-muted',
  Stale: 'bg-warning/15 text-warning',
  Error: 'bg-danger/15 text-danger',
  Unknown: 'bg-surface-hover text-fg-dim',
}

const STATUS_LABELS: Record<WorkerStatus, string> = {
  Healthy: 'Saudável',
  Done: 'Concluído',
  Idle: 'Aguardando',
  Stale: 'Sem ticks',
  Error: 'Com erros',
  Unknown: 'Sem heartbeat',
}

export function Workers() {
  const state = useApi(
    useCallback((signal) => listBackgroundServices({ signal }), []),
    [],
  )
  const [selectedName, setSelectedName] = useState<string | null>(null)

  if (state.error) {
    return (
      <Card>
        <ErrorState error={state.error} onRetry={state.refetch} />
      </Card>
    )
  }

  const data = state.data
  const nowUtcMs = data ? Date.parse(data.nowUtc) : Date.now()
  const items = data?.items ?? []
  const selectedItem = items.find((i) => i.name === selectedName) ?? null

  // Agrupa por categoria preservando a ordem definida em CATEGORY_ORDER. Itens
  // de categoria fora da ordem (futuro: adicionaram backend sem propagar pro
  // frontend) caem no final em ordem alfabética — UI continua usável.
  const grouped = new Map<BackgroundCategory, BackgroundServiceItem[]>()
  for (const item of items) {
    const list = grouped.get(item.category) ?? []
    list.push(item)
    grouped.set(item.category, list)
  }
  const orderedCategories: BackgroundCategory[] = [
    ...CATEGORY_ORDER.filter((c) => grouped.has(c)),
    ...[...grouped.keys()]
      .filter((c) => !CATEGORY_ORDER.includes(c))
      .sort((a, b) => a.localeCompare(b)),
  ]

  // Sumário no topo: contadores por status. Mostra panorama "está tudo bem?"
  // sem precisar rolar a lista.
  const statusCounts = items.reduce<Record<WorkerStatus, number>>(
    (acc, item) => {
      const s = computeStatus(item, nowUtcMs)
      acc[s] = (acc[s] ?? 0) + 1
      return acc
    },
    { Healthy: 0, Done: 0, Idle: 0, Stale: 0, Error: 0, Unknown: 0 },
  )

  return (
    <div className="flex flex-col gap-6">
      <Card>
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div>
            <CardHeader
              title="Workers em execução"
              description={
                data
                  ? `${formatInt(data.total)} hosted services registrados · pod ativo há ${formatUptime(data.processStartedAtUtc, data.nowUtc)}`
                  : 'Carregando…'
              }
            />
            <div className="mt-3 flex flex-wrap gap-2 text-xs">
              {(['Healthy', 'Done', 'Idle', 'Stale', 'Error', 'Unknown'] as WorkerStatus[]).map((s) => (
                <span
                  key={s}
                  className={cn('inline-flex items-center gap-1 rounded-md px-2 py-0.5', STATUS_CLASSES[s])}
                >
                  <span className="font-semibold">{statusCounts[s] ?? 0}</span>
                  <span className="font-normal opacity-80">{STATUS_LABELS[s]}</span>
                </span>
              ))}
            </div>
          </div>
          <div className="flex items-center gap-2">
            <Button variant="ghost" className="h-7 px-3 text-xs" onClick={state.refetch} disabled={state.loading}>
              {state.loading ? 'Atualizando…' : '↻ Atualizar'}
            </Button>
          </div>
        </div>
      </Card>

      {state.loading && !data && (
        <Card>
          <div className="h-40 animate-pulse rounded-lg bg-surface-hover" />
        </Card>
      )}

      {orderedCategories.map((category) => {
        const list = grouped.get(category) ?? []
        return (
          <Card key={category} padded={false}>
            <div className="p-5">
              <CardHeader
                title={CATEGORY_LABELS[category] ?? category}
                description={CATEGORY_DESCRIPTIONS[category] ?? `${list.length} serviço${list.length === 1 ? '' : 's'}`}
              />
            </div>
            <div className="border-t border-border">
              {list.map((item, idx) => (
                <WorkerRow
                  key={item.name}
                  item={item}
                  nowUtcMs={nowUtcMs}
                  isLast={idx === list.length - 1}
                  onClick={() => setSelectedName(item.name)}
                />
              ))}
            </div>
          </Card>
        )
      })}

      {selectedItem && data && (
        <WorkerDrawer
          item={selectedItem}
          processStartedAtUtc={data.processStartedAtUtc}
          nowUtcMs={nowUtcMs}
          onClose={() => setSelectedName(null)}
        />
      )}
    </div>
  )
}

function WorkerRow({
  item,
  nowUtcMs,
  isLast,
  onClick,
}: {
  item: BackgroundServiceItem
  nowUtcMs: number
  isLast: boolean
  onClick: () => void
}) {
  const status = computeStatus(item, nowUtcMs)
  const hb = item.heartbeat
  const lastTickIso = hb?.lastTickAtUtc ?? null

  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'flex w-full items-center gap-4 px-5 py-3 text-left transition hover:bg-surface-hover',
        !isLast && 'border-b border-border',
      )}
    >
      <span
        className={cn(
          'inline-flex w-24 justify-center rounded-md px-2 py-0.5 text-[11px] font-medium',
          STATUS_CLASSES[status],
        )}
      >
        {STATUS_LABELS[status]}
      </span>

      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-2">
          <span className="truncate font-mono text-xs text-fg">{item.name}</span>
          {item.intervalSeconds != null && (
            <span className="rounded bg-surface-hover px-1.5 py-0.5 text-[10px] text-fg-dim">
              cada {formatInterval(item.intervalSeconds)}
            </span>
          )}
          {item.lifecycle === 'OneTime' && (
            <span className="rounded bg-surface-hover px-1.5 py-0.5 text-[10px] text-fg-dim">
              one-time
            </span>
          )}
        </div>
        <div className="mt-0.5 line-clamp-1 text-[11px] text-fg-muted">{item.description}</div>
      </div>

      <div className="hidden flex-col items-end gap-0.5 text-right md:flex">
        <span className="text-xs text-fg">
          {lastTickIso ? formatRelative(lastTickIso, new Date(nowUtcMs)) : '—'}
        </span>
        <span className="text-[10px] text-fg-dim">
          {hb ? `${formatInt(hb.tickCount)} ticks · ${formatInt(hb.errorCount)} err` : 'sem heartbeat'}
        </span>
      </div>
    </button>
  )
}

function WorkerDrawer({
  item,
  processStartedAtUtc,
  nowUtcMs,
  onClose,
}: {
  item: BackgroundServiceItem
  processStartedAtUtc: string
  nowUtcMs: number
  onClose: () => void
}) {
  const status = computeStatus(item, nowUtcMs)
  const hb = item.heartbeat

  return (
    <div
      className="fixed inset-0 z-40 flex justify-end bg-black/40 backdrop-blur-sm"
      role="dialog"
      onClick={onClose}
    >
      <div
        className="flex h-full w-full max-w-xl flex-col border-l border-border bg-bg shadow-xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start justify-between border-b border-border p-5">
          <div className="min-w-0 flex-1">
            <div className="text-xs uppercase tracking-wide text-fg-dim">
              {CATEGORY_LABELS[item.category] ?? item.category} · {item.lifecycle}
            </div>
            <div className="mt-1 flex items-center gap-2">
              <span
                className={cn(
                  'inline-flex rounded-md px-2 py-0.5 text-[11px] font-medium',
                  STATUS_CLASSES[status],
                )}
              >
                {STATUS_LABELS[status]}
              </span>
              <span className="font-mono text-sm text-fg">{item.name}</span>
            </div>
          </div>
          <Button variant="ghost" className="-mr-2 px-2" onClick={onClose} aria-label="Fechar">
            ×
          </Button>
        </div>

        <div className="flex-1 space-y-5 overflow-y-auto p-5 text-sm">
          <section>
            <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-fg-muted">
              Descrição
            </h3>
            <p className="text-xs text-fg">{item.description}</p>
          </section>

          <section>
            <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-fg-muted">
              Heartbeat (pod atual)
            </h3>
            {hb ? (
              <dl className="grid grid-cols-[max-content_1fr] gap-x-3 gap-y-1 text-xs">
                <dt className="text-fg-muted">Started</dt>
                <dd className="text-fg">
                  {formatTimestampSecond(hb.startedAtUtc)}
                  <span className="ml-2 text-fg-dim">
                    ({formatRelative(hb.startedAtUtc, new Date(nowUtcMs))})
                  </span>
                </dd>
                <dt className="text-fg-muted">Última atividade</dt>
                <dd className="text-fg">
                  {formatTimestampSecond(hb.lastTickAtUtc)}
                  <span className="ml-2 text-fg-dim">
                    ({formatRelative(hb.lastTickAtUtc, new Date(nowUtcMs))})
                  </span>
                </dd>
                <dt className="text-fg-muted">Último sucesso</dt>
                <dd className="text-fg">
                  {formatTimestampSecond(hb.lastSuccessAtUtc)}
                  <span className="ml-2 text-fg-dim">
                    ({formatRelative(hb.lastSuccessAtUtc, new Date(nowUtcMs))})
                  </span>
                </dd>
                <dt className="text-fg-muted">Último erro</dt>
                <dd className="text-fg">
                  {formatTimestampSecond(hb.lastErrorAtUtc)}
                  <span className="ml-2 text-fg-dim">
                    ({formatRelative(hb.lastErrorAtUtc, new Date(nowUtcMs))})
                  </span>
                </dd>
                <dt className="text-fg-muted">Ticks</dt>
                <dd className="text-fg">{formatInt(hb.tickCount)}</dd>
                <dt className="text-fg-muted">Erros</dt>
                <dd className="text-fg">{formatInt(hb.errorCount)}</dd>
              </dl>
            ) : (
              <div className="rounded-md border border-dashed border-border px-3 py-4 text-center text-xs text-fg-dim">
                Sem heartbeat capturado neste pod. Pode estar registrado mas ainda não entrou em ExecuteAsync, ou rodando em outro pod (per-pod in-memory).
              </div>
            )}
          </section>

          {hb?.lastErrorMessage && (
            <section>
              <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-fg-muted">
                Última mensagem de erro
              </h3>
              <pre className="whitespace-pre-wrap rounded-md border border-danger/40 bg-danger/5 p-3 text-xs text-fg">
                {hb.lastErrorMessage}
              </pre>
            </section>
          )}

          <section>
            <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-fg-muted">
              Metadados
            </h3>
            <dl className="grid grid-cols-[max-content_1fr] gap-x-3 gap-y-1 text-xs">
              <dt className="text-fg-muted">Lifecycle</dt>
              <dd className="text-fg">{item.lifecycle}</dd>
              <dt className="text-fg-muted">Categoria</dt>
              <dd className="text-fg">{CATEGORY_LABELS[item.category] ?? item.category}</dd>
              <dt className="text-fg-muted">Intervalo esperado</dt>
              <dd className="text-fg">
                {item.intervalSeconds != null ? formatInterval(item.intervalSeconds) : 'event-driven'}
              </dd>
              <dt className="text-fg-muted">Tipo .NET</dt>
              <dd className="font-mono text-[11px] text-fg-dim">{item.typeName}</dd>
              <dt className="text-fg-muted">Processo</dt>
              <dd className="text-fg">
                ativo há {formatUptime(processStartedAtUtc, new Date(nowUtcMs))}
              </dd>
            </dl>
          </section>
        </div>
      </div>
    </div>
  )
}
