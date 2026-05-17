import type { ProjectTimeseriesBucket } from '../api/projectAnalytics'

export type PeriodKind = 'month' | 'year'

export interface Period {
  kind: PeriodKind
  /** "YYYY-MM" para mês, "YYYY" para ano. */
  value: string
}

const MIN_YEAR_WINDOW = 2
const MONTH_RE = /^(\d{4})-(0[1-9]|1[0-2])$/
const YEAR_RE = /^(\d{4})$/
const HALF_HOUR_MS = 30 * 60 * 1000

function pad2(n: number): string {
  return n < 10 ? `0${n}` : String(n)
}

export function currentPeriod(kind: PeriodKind): Period {
  const now = new Date()
  if (kind === 'year') {
    return { kind: 'year', value: String(now.getUTCFullYear()) }
  }
  return {
    kind: 'month',
    value: `${now.getUTCFullYear()}-${pad2(now.getUTCMonth() + 1)}`,
  }
}

/**
 * URL hostil ou deep-link de período inválido/futuro cai pro mês corrente em
 * silêncio. Sem mensagem de erro — UX simples. Caller pode comparar com o raw
 * pra detectar coerção e reescrever a URL.
 */
export function parsePeriod(raw: string | null | undefined): Period {
  if (!raw) return currentPeriod('month')
  const monthMatch = MONTH_RE.exec(raw)
  if (monthMatch) {
    const year = Number(monthMatch[1])
    const month = Number(monthMatch[2])
    if (!isFutureMonth(year, month) && year >= currentYear() - MIN_YEAR_WINDOW) {
      return { kind: 'month', value: raw }
    }
    return currentPeriod('month')
  }
  const yearMatch = YEAR_RE.exec(raw)
  if (yearMatch) {
    const year = Number(yearMatch[1])
    const minYear = currentYear() - MIN_YEAR_WINDOW
    if (year >= minYear && year <= currentYear()) {
      return { kind: 'year', value: raw }
    }
    return currentPeriod('year')
  }
  return currentPeriod('month')
}

function currentYear(): number {
  return new Date().getUTCFullYear()
}

function isFutureMonth(year: number, month: number): boolean {
  const now = new Date()
  const y = now.getUTCFullYear()
  const m = now.getUTCMonth() + 1
  return year > y || (year === y && month > m)
}

export function isCurrentMonth(p: Period): boolean {
  if (p.kind !== 'month') return false
  return p.value === currentPeriod('month').value
}

function isCurrentYear(p: Period): boolean {
  if (p.kind !== 'year') return false
  return p.value === currentPeriod('year').value
}

/**
 * Quando o período é o corrente, `to` é arredondado pro próximo bucket de
 * 30min — alinha com o TTL da matview `v_llm_cost` (refresh 30min). Dois
 * refreshes na mesma janela mandam o mesmo `to`, aproveitando cache HTTP/CDN
 * e plan cache do Postgres. Também evita preencher chart com futuro vazio.
 */
export function periodToRange(p: Period): { from: string; to: string } {
  if (p.kind === 'month') {
    const [yStr, mStr] = p.value.split('-')
    const year = Number(yStr)
    const month = Number(mStr)
    const from = new Date(Date.UTC(year, month - 1, 1, 0, 0, 0, 0)).toISOString()
    if (isCurrentMonth(p)) {
      return { from, to: roundedNow() }
    }
    const to = new Date(Date.UTC(year, month, 0, 23, 59, 59, 999)).toISOString()
    return { from, to }
  }
  const year = Number(p.value)
  const from = new Date(Date.UTC(year, 0, 1, 0, 0, 0, 0)).toISOString()
  if (isCurrentYear(p)) {
    return { from, to: roundedNow() }
  }
  const to = new Date(Date.UTC(year, 11, 31, 23, 59, 59, 999)).toISOString()
  return { from, to }
}

function roundedNow(): string {
  return new Date(Math.ceil(Date.now() / HALF_HOUR_MS) * HALF_HOUR_MS).toISOString()
}

const MONTH_LABELS_PT_BR = [
  'janeiro',
  'fevereiro',
  'março',
  'abril',
  'maio',
  'junho',
  'julho',
  'agosto',
  'setembro',
  'outubro',
  'novembro',
  'dezembro',
]

export function formatPeriodLabel(p: Period): string {
  if (p.kind === 'year') return p.value
  const [year, month] = p.value.split('-').map(Number)
  return `${MONTH_LABELS_PT_BR[month - 1]}/${year}`
}

/**
 * Em modo Ano forçamos `groupBy=day` no backend (hour viraria 8760 buckets).
 * Renderizar 365 pontos no Spark é ilegível e custa DOM — colapsamos pra 12
 * buckets mensais antes do chart. Backend continua igual; helper opera no
 * timestamp string `YYYY-MM-DD...` retornado.
 */
export function aggregateByMonth(buckets: ProjectTimeseriesBucket[]): ProjectTimeseriesBucket[] {
  const acc = new Map<string, ProjectTimeseriesBucket>()
  for (const b of buckets) {
    const key = b.bucket.slice(0, 7)
    const cur = acc.get(key)
    if (cur) {
      cur.costUsd += b.costUsd
      cur.tokens += b.tokens
      cur.calls += b.calls
      cur.executions += b.executions
      cur.completed += b.completed
      cur.failed += b.failed
    } else {
      acc.set(key, {
        bucket: `${key}-01T00:00:00Z`,
        costUsd: b.costUsd,
        tokens: b.tokens,
        calls: b.calls,
        executions: b.executions,
        completed: b.completed,
        failed: b.failed,
      })
    }
  }
  return Array.from(acc.values()).sort((a, b) => a.bucket.localeCompare(b.bucket))
}
