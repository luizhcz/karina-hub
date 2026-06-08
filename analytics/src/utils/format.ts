// Formatadores centralizados pra que todo dashboard tenha mesma vírgula,
// mesma quantidade de casas e mesma localização. Toda nova métrica deve
// passar por aqui — não disperse `.toFixed()` nem `Intl` direto nas
// renderizações. Locale fixo em pt-BR pra alinhar com o público do banco.

const PT_BR = 'pt-BR'

// Dois formatters: 2 casas pra valores "normais" (KPI principal), 4 casas pra
// valores microscópicos (mês recém-iniciado, modelo barato com poucos tokens).
// Sem isso, "US$ 0,0034" vira "US$ 0,00" e o user lê como zero. Trocamos pelo
// adaptive em formatCurrency abaixo.
const currencyFormatter = new Intl.NumberFormat(PT_BR, {
  style: 'currency',
  currency: 'USD',
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
})

const microCurrencyFormatter = new Intl.NumberFormat(PT_BR, {
  style: 'currency',
  currency: 'USD',
  minimumFractionDigits: 4,
  maximumFractionDigits: 4,
})

const compactCurrencyFormatter = new Intl.NumberFormat(PT_BR, {
  style: 'currency',
  currency: 'USD',
  notation: 'compact',
  maximumFractionDigits: 2,
})

const integerFormatter = new Intl.NumberFormat(PT_BR, {
  maximumFractionDigits: 0,
})

const compactIntegerFormatter = new Intl.NumberFormat(PT_BR, {
  notation: 'compact',
  maximumFractionDigits: 1,
})

const percentFormatter = new Intl.NumberFormat(PT_BR, {
  style: 'percent',
  minimumFractionDigits: 1,
  maximumFractionDigits: 1,
})

/**
 * USD com precisão adaptativa: 2 casas pra ≥ $1 (KPI clássico), 4 casas pra
 * valores microscópicos ($0,0034) que não-zero NÃO podem virar "$0,00" no
 * display — ou o usuário pensa que a métrica está zerada (caso típico no
 * primeiro dia do mês com poucas chamadas). Zero literal renderiza "US$ 0,00".
 */
export function formatCurrency(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value)) return '—'
  if (value === 0) return currencyFormatter.format(0)
  return Math.abs(value) < 1
    ? microCurrencyFormatter.format(value)
    : currencyFormatter.format(value)
}

/** USD compacto (US$ 1,2k). Use em eixos de gráfico e tooltips. */
export function formatCurrencyCompact(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value)) return '—'
  return compactCurrencyFormatter.format(value)
}

export function formatInt(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value)) return '—'
  return integerFormatter.format(value)
}

export function formatIntCompact(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value)) return '—'
  return compactIntegerFormatter.format(value)
}

/**
 * Recebe fração 0..1 (e.g. 0.0234 = 2,3%). Backend devolve nesse formato em
 * ProjectAnalytics (errorRate, successRate). Para valores em percentuais
 * inteiros já pré-multiplicados, dividir antes ou usar formatPercentPoints.
 */
export function formatPercent(fraction: number | null | undefined): string {
  if (fraction == null || !Number.isFinite(fraction)) return '—'
  return percentFormatter.format(fraction)
}

/**
 * Recebe percent points 0..100 (e.g. 86.5 = 86,5%). Usado pelo
 * ExecutionAnalytics (successRate vem do backend já em points pra preservar
 * 1 casa decimal sem precisar carregar 0..1 com 4 casas). Diferente de
 * formatPercent (que opera em fração).
 */
export function formatPercentPoints(points: number | null | undefined): string {
  if (points == null || !Number.isFinite(points)) return '—'
  return `${points.toFixed(1).replace('.', ',')}%`
}

/** Latência em ms → string com unidade. Decide ms/s automaticamente. */
export function formatLatencyMs(ms: number | null | undefined): string {
  if (ms == null || !Number.isFinite(ms)) return '—'
  if (ms >= 1000) return `${(ms / 1000).toFixed(2).replace('.', ',')} s`
  return `${Math.round(ms)} ms`
}

/**
 * Intervalo em segundos pra string humana: 30s, 5min, 1h, 6h, 1d.
 * Usado na tela Workers pra mostrar o intervalSeconds dos background
 * services (que vai de 300s = 5min até 86400s = 1d).
 */
export function formatInterval(seconds: number | null | undefined): string {
  if (seconds == null || !Number.isFinite(seconds)) return '—'
  if (seconds < 60) return `${Math.round(seconds)}s`
  if (seconds < 3600) return `${Math.round(seconds / 60)}min`
  if (seconds < 86_400) return `${Math.round(seconds / 3600)}h`
  return `${Math.round(seconds / 86_400)}d`
}

const dateTimeFormatter = new Intl.DateTimeFormat(PT_BR, {
  day: '2-digit',
  month: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
})

const dateFormatter = new Intl.DateTimeFormat(PT_BR, {
  day: '2-digit',
  month: '2-digit',
})

/** Formato curto pra tooltip de gráfico (DD/MM HH:MM ou DD/MM). */
export function formatBucketLabel(iso: string, granularity: 'day' | 'hour'): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  return granularity === 'hour' ? dateTimeFormatter.format(d) : dateFormatter.format(d)
}

/** ISO sem milissegundos, UTC. Forma canônica que o backend aceita em from/to. */
export function toIsoUtc(d: Date): string {
  return new Date(Date.UTC(
    d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate(),
    d.getUTCHours(), d.getUTCMinutes(), d.getUTCSeconds(),
  )).toISOString()
}

/**
 * Diferença "humana" entre dois timestamps: "agora", "5s atrás", "3min", "2h", "4d".
 * Aceita ISO strings ou Date. Negativo (futuro) cai em "agora". null → "—".
 * Usado no Workers pra last-tick relativo.
 */
export function formatRelative(value: string | Date | null | undefined, baseUtc: string | Date = new Date()): string {
  if (value == null) return '—'
  const t = typeof value === 'string' ? new Date(value).getTime() : value.getTime()
  const base = typeof baseUtc === 'string' ? new Date(baseUtc).getTime() : baseUtc.getTime()
  if (!Number.isFinite(t) || !Number.isFinite(base)) return '—'
  const diffSec = Math.round((base - t) / 1000)
  if (diffSec < 2) return 'agora'
  if (diffSec < 60) return `${diffSec}s atrás`
  if (diffSec < 3600) return `${Math.round(diffSec / 60)}min atrás`
  if (diffSec < 86_400) return `${Math.round(diffSec / 3600)}h atrás`
  return `${Math.round(diffSec / 86_400)}d atrás`
}

/**
 * Duração positiva em formato "Xd Yh", "Xh Ymin", "Ymin Zs", "Zs". Usado pra
 * mostrar uptime do processo na tela Workers.
 */
export function formatUptime(sinceUtc: string | Date | null | undefined, nowUtc: string | Date = new Date()): string {
  if (sinceUtc == null) return '—'
  const since = typeof sinceUtc === 'string' ? new Date(sinceUtc).getTime() : sinceUtc.getTime()
  const now = typeof nowUtc === 'string' ? new Date(nowUtc).getTime() : nowUtc.getTime()
  if (!Number.isFinite(since) || !Number.isFinite(now)) return '—'
  const diffSec = Math.max(0, Math.round((now - since) / 1000))
  const days = Math.floor(diffSec / 86_400)
  const hours = Math.floor((diffSec % 86_400) / 3600)
  const mins = Math.floor((diffSec % 3600) / 60)
  const secs = diffSec % 60
  if (days > 0) return `${days}d ${hours}h`
  if (hours > 0) return `${hours}h ${mins}min`
  if (mins > 0) return `${mins}min ${secs}s`
  return `${secs}s`
}

/** Timestamp absoluto detalhado pra tooltip / drawer (dd/mm hh:mm:ss). */
export function formatTimestampSecond(value: string | Date | null | undefined): string {
  if (value == null) return '—'
  const d = typeof value === 'string' ? new Date(value) : value
  if (Number.isNaN(d.getTime())) return '—'
  return d.toLocaleString(PT_BR, {
    day: '2-digit',
    month: '2-digit',
    year: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
}
