// Estado de range de datas + sincronização com query string da URL. Isolado
// num hook pra que toda tela de analytics reuse o MESMO mecanismo de
// shareable links: copiar a URL e abrir em outra aba reproduz o filtro.
// Os componentes consomem `{ from, to, preset, setPreset, setCustom }` sem
// saber que a URL existe.
//
// Decisões:
//   - Presets exportados em uma constante única pra que o DateRangePicker e o
//     hook concordem nos valores (`mtd`, `7d`, etc.).
//   - URL é fonte secundária: estado local é a verdade, URL é só persistência
//     pra back/forward. Histórico browser usa replaceState pra não poluir.
//   - Datas são calculadas em UTC porque o backend espera ISO UTC; user vê
//     formatado em pt-BR via utils/format.

import { useCallback, useEffect, useMemo, useState } from 'react'

export type DateRangePreset = '24h' | '7d' | '30d' | '90d' | 'mtd' | 'custom'

export interface DateRange {
  from: Date
  to: Date
  preset: DateRangePreset
}

export const DATE_RANGE_PRESETS: ReadonlyArray<{ value: DateRangePreset; label: string }> = [
  { value: '24h', label: 'Hoje (24h)' },
  { value: '7d', label: 'Últimos 7 dias' },
  { value: '30d', label: 'Últimos 30 dias' },
  { value: '90d', label: 'Últimos 90 dias' },
  { value: 'mtd', label: 'Mês corrente' },
  { value: 'custom', label: 'Personalizado' },
]

const QUERY_PRESET = 'range'
const QUERY_FROM = 'from'
const QUERY_TO = 'to'

function startOfMonthUtc(now: Date): Date {
  return new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), 1, 0, 0, 0))
}

export function resolveRangeFromPreset(preset: DateRangePreset, now = new Date()): { from: Date; to: Date } {
  switch (preset) {
    case '24h':
      return { from: new Date(now.getTime() - 24 * 3600 * 1000), to: now }
    case '7d':
      return { from: new Date(now.getTime() - 7 * 24 * 3600 * 1000), to: now }
    case '30d':
      return { from: new Date(now.getTime() - 30 * 24 * 3600 * 1000), to: now }
    case '90d':
      return { from: new Date(now.getTime() - 90 * 24 * 3600 * 1000), to: now }
    case 'mtd':
      return { from: startOfMonthUtc(now), to: now }
    case 'custom':
      // Sem preset definido — caller deve setar from/to via setCustom.
      return { from: startOfMonthUtc(now), to: now }
  }
}

function readUrl(): DateRange | null {
  if (typeof window === 'undefined') return null
  const params = new URLSearchParams(window.location.search)
  const presetParam = params.get(QUERY_PRESET) as DateRangePreset | null
  if (presetParam && presetParam !== 'custom') {
    if (!isValidPreset(presetParam)) return null
    const { from, to } = resolveRangeFromPreset(presetParam)
    return { from, to, preset: presetParam }
  }
  if (presetParam === 'custom') {
    const fromIso = params.get(QUERY_FROM)
    const toIso = params.get(QUERY_TO)
    if (!fromIso || !toIso) return null
    const from = new Date(fromIso)
    const to = new Date(toIso)
    if (Number.isNaN(from.getTime()) || Number.isNaN(to.getTime())) return null
    return { from, to, preset: 'custom' }
  }
  return null
}

function isValidPreset(v: string): v is DateRangePreset {
  return DATE_RANGE_PRESETS.some((p) => p.value === v)
}

function writeUrl(range: DateRange) {
  if (typeof window === 'undefined') return
  const url = new URL(window.location.href)
  url.searchParams.set(QUERY_PRESET, range.preset)
  if (range.preset === 'custom') {
    url.searchParams.set(QUERY_FROM, range.from.toISOString())
    url.searchParams.set(QUERY_TO, range.to.toISOString())
  } else {
    url.searchParams.delete(QUERY_FROM)
    url.searchParams.delete(QUERY_TO)
  }
  window.history.replaceState(window.history.state, '', url.toString())
}

const DEFAULT_PRESET: DateRangePreset = 'mtd'

export interface UseDateRange {
  range: DateRange
  setPreset: (preset: DateRangePreset) => void
  setCustom: (from: Date, to: Date) => void
}

export function useDateRange(): UseDateRange {
  const [range, setRange] = useState<DateRange>(() => {
    const fromUrl = readUrl()
    if (fromUrl) return fromUrl
    const { from, to } = resolveRangeFromPreset(DEFAULT_PRESET)
    return { from, to, preset: DEFAULT_PRESET }
  })

  useEffect(() => {
    writeUrl(range)
  }, [range])

  const setPreset = useCallback((preset: DateRangePreset) => {
    if (preset === 'custom') {
      // Custom sem datas explícitas mantém o range atual mas troca o preset
      // — usuário ainda vai escolher datas no picker.
      setRange((prev) => ({ ...prev, preset: 'custom' }))
      return
    }
    const { from, to } = resolveRangeFromPreset(preset)
    setRange({ from, to, preset })
  }, [])

  const setCustom = useCallback((from: Date, to: Date) => {
    setRange({ from, to, preset: 'custom' })
  }, [])

  return useMemo(() => ({ range, setPreset, setCustom }), [range, setPreset, setCustom])
}
