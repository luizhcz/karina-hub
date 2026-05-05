import { useEffect, useMemo, useState } from 'react'
import {
  Badge,
  Button,
  CloseIcon,
  IconButton,
  Spinner,
  cn,
} from '../../ui'
import {
  type CampoPerfil,
  type RefinamentoPerfilOutput,
  type Severidade,
  type Sugestao,
  type SugestaoTipo,
  countCriticas,
  groupByCampo,
  severityRank,
} from '../../api/profileAssistant'
import { AssistantDiff } from './AssistantDiff'

interface AssistantDrawerProps {
  open: boolean
  onClose: () => void
  loading: boolean
  result: RefinamentoPerfilOutput | null
  error: string | null
  onRetry: () => void
  onApply: (campo: CampoPerfil, value: string) => void
  fieldValues: Record<CampoPerfil, string>
}

const FIELD_LABELS: Record<CampoPerfil, string> = {
  name: 'Nome',
  description: 'Descrição',
  role: 'Papel',
  goal: 'Objetivo',
  backstory: 'Contexto',
  rules: 'Regras de atuação',
  constraints: 'Restrições',
}

const SEVERITY_LABELS: Record<Severidade, string> = {
  alta: 'Crítica',
  media: 'Importante',
  baixa: 'Polimento',
}

const TIPO_LABELS: Record<SugestaoTipo, string> = {
  lacuna: 'Lacuna',
  melhoria: 'Melhoria',
  risco: 'Risco',
}

const TIPO_TONE: Record<SugestaoTipo, 'neutral' | 'warning' | 'danger' | 'accent'> = {
  lacuna: 'neutral',
  melhoria: 'accent',
  risco: 'danger',
}

const SEV_DOT_CLASS: Record<Severidade, string> = {
  alta: 'bg-danger',
  media: 'bg-warning',
  baixa: 'bg-fg-dim',
}

type Filter = 'all' | Severidade

const DISCLAIMER_KEY = 'efs.assistant.disclaimer.dismissed'

export function AssistantDrawer({
  open,
  onClose,
  loading,
  result,
  error,
  onRetry,
  onApply,
  fieldValues,
}: AssistantDrawerProps) {
  const [filter, setFilter] = useState<Filter>('all')
  const [openCampos, setOpenCampos] = useState<Set<CampoPerfil>>(new Set())
  const [diffFor, setDiffFor] = useState<{ campo: CampoPerfil; sugestao: Sugestao } | null>(null)
  const [disclaimerOpen, setDisclaimerOpen] = useState(false)

  useEffect(() => {
    if (!open) return
    setDisclaimerOpen(localStorage.getItem(DISCLAIMER_KEY) !== '1')
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open, onClose])

  const dismissDisclaimer = () => {
    localStorage.setItem(DISCLAIMER_KEY, '1')
    setDisclaimerOpen(false)
  }

  const filtered: Sugestao[] = useMemo(() => {
    if (!result) return []
    const list = filter === 'all' ? result.sugestoes : result.sugestoes.filter((s) => s.severidade === filter)
    return [...list].sort((a, b) => severityRank(a.severidade) - severityRank(b.severidade))
  }, [result, filter])

  const grouped = useMemo(() => groupByCampo(filtered), [filtered])

  // Auto-expande campos com sugestões críticas no primeiro render do resultado.
  useEffect(() => {
    if (!result) return
    const next = new Set<CampoPerfil>()
    for (const s of result.sugestoes) {
      if (s.severidade === 'alta') next.add(s.campo)
    }
    setOpenCampos(next)
  }, [result])

  const toggleCampo = (campo: CampoPerfil) => {
    setOpenCampos((prev) => {
      const next = new Set(prev)
      if (next.has(campo)) next.delete(campo)
      else next.add(campo)
      return next
    })
  }

  if (!open) return null

  return (
    <div className="fixed inset-0 z-40 flex" role="dialog" aria-label="Assistente de Refinamento de Perfil">
      <div className="flex-1 bg-fg/20 backdrop-blur-[1px]" onClick={onClose} />
      <aside className="flex h-full w-[420px] max-w-[100vw] flex-col border-l border-border bg-surface shadow-2xl">
        <DrawerHeader result={result} loading={loading} onClose={onClose} />

        {disclaimerOpen && (
          <div className="border-b border-border bg-warning/10 px-4 py-2.5 text-[11px] leading-relaxed text-fg">
            <div className="flex items-start justify-between gap-2">
              <p>
                O conteúdo do perfil é enviado a um modelo Foundry para análise. Não inclua dados de cliente reais —
                use placeholders.
              </p>
              <button
                onClick={dismissDisclaimer}
                className="shrink-0 text-[11px] font-semibold text-fg-muted hover:text-fg"
              >
                Ok
              </button>
            </div>
          </div>
        )}

        <div className="flex-1 overflow-y-auto">
          {loading && <DrawerSkeleton />}

          {!loading && error && (
            <DrawerError message={error} onRetry={onRetry} />
          )}

          {!loading && !error && result && result.sugestoes.length === 0 && (
            <div className="px-4 py-8 text-center text-sm text-fg-muted">
              Nenhuma sugestão encontrada — perfil parece OK por aqui.
            </div>
          )}

          {!loading && !error && result && result.sugestoes.length > 0 && (
            <>
              <FilterChips
                filter={filter}
                onChange={setFilter}
                counts={{
                  alta: result.sugestoes.filter((s) => s.severidade === 'alta').length,
                  media: result.sugestoes.filter((s) => s.severidade === 'media').length,
                  baixa: result.sugestoes.filter((s) => s.severidade === 'baixa').length,
                }}
                total={result.sugestoes.length}
              />

              {result.resumo && (
                <p className="border-b border-border px-4 py-3 text-xs text-fg-muted">{result.resumo}</p>
              )}

              <div className="divide-y divide-border">
                {Array.from(grouped.entries()).map(([campo, sugestoes]) => (
                  <CampoGroup
                    key={campo}
                    campo={campo}
                    sugestoes={sugestoes}
                    open={openCampos.has(campo)}
                    onToggle={() => toggleCampo(campo)}
                    onUseAsBase={(sugestao) => setDiffFor({ campo, sugestao })}
                    diffFor={diffFor}
                    onDiffApply={(value) => {
                      if (!diffFor) return
                      onApply(diffFor.campo, value)
                      setDiffFor(null)
                    }}
                    onDiffCancel={() => setDiffFor(null)}
                    fieldValues={fieldValues}
                  />
                ))}
              </div>
            </>
          )}
        </div>
      </aside>
    </div>
  )
}

interface DrawerHeaderProps {
  result: RefinamentoPerfilOutput | null
  loading: boolean
  onClose: () => void
}

function DrawerHeader({ result, loading, onClose }: DrawerHeaderProps) {
  const score = result?.score ?? null
  const criticas = result ? countCriticas(result.sugestoes) : 0

  return (
    <div className="flex items-start gap-3 border-b border-border px-4 py-3">
      <div className="min-w-0 flex-1">
        <p className="text-[10px] font-semibold uppercase tracking-widest text-fg-dim">
          Maturidade do perfil
        </p>
        <div className="mt-0.5 flex items-baseline gap-2">
          {loading ? (
            <span className="text-base font-semibold text-fg-muted">Analisando…</span>
          ) : score === null ? (
            <span className="text-base font-semibold text-fg-muted">—</span>
          ) : score < 40 ? (
            <span
              className="text-sm font-semibold text-danger"
              title={`${score}/100`}
            >
              Perfil inicial — {criticas} {criticas === 1 ? 'item crítico' : 'itens críticos'} pra evoluir
            </span>
          ) : score >= 90 ? (
            <>
              <span className="text-2xl font-bold text-success">{score}</span>
              <span className="text-xs text-fg-dim">/100</span>
              <Badge tone="success" className="ml-1">Perfil maduro</Badge>
            </>
          ) : (
            <>
              <span className={cn(
                'text-2xl font-bold',
                score >= 70 ? 'text-success' : 'text-warning',
              )}>{score}</span>
              <span className="text-xs text-fg-dim">/100</span>
            </>
          )}
        </div>
      </div>
      <IconButton aria-label="Fechar assistente" onClick={onClose}>
        <CloseIcon className="h-4 w-4" />
      </IconButton>
    </div>
  )
}

interface FilterChipsProps {
  filter: Filter
  onChange: (f: Filter) => void
  counts: Record<Severidade, number>
  total: number
}

function FilterChips({ filter, onChange, counts, total }: FilterChipsProps) {
  const items: { key: Filter; label: string; count: number }[] = [
    { key: 'all', label: 'Todas', count: total },
    { key: 'alta', label: 'Críticas', count: counts.alta },
    { key: 'media', label: 'Importantes', count: counts.media },
    { key: 'baixa', label: 'Polimento', count: counts.baixa },
  ]
  return (
    <div className="flex flex-wrap gap-1.5 border-b border-border px-4 py-2.5">
      {items.map((it) => {
        const active = filter === it.key
        return (
          <button
            key={it.key}
            onClick={() => onChange(it.key)}
            className={cn(
              'rounded-full border px-2.5 py-1 text-[11px] font-medium transition',
              active
                ? 'border-accent bg-accent-subtle text-accent'
                : 'border-border bg-surface text-fg-muted hover:bg-surface-hover hover:text-fg',
            )}
          >
            {it.label} <span className="text-fg-dim">{it.count}</span>
          </button>
        )
      })}
    </div>
  )
}

interface CampoGroupProps {
  campo: CampoPerfil
  sugestoes: Sugestao[]
  open: boolean
  onToggle: () => void
  onUseAsBase: (sugestao: Sugestao) => void
  diffFor: { campo: CampoPerfil; sugestao: Sugestao } | null
  onDiffApply: (value: string) => void
  onDiffCancel: () => void
  fieldValues: Record<CampoPerfil, string>
}

function CampoGroup({
  campo,
  sugestoes,
  open,
  onToggle,
  onUseAsBase,
  diffFor,
  onDiffApply,
  onDiffCancel,
  fieldValues,
}: CampoGroupProps) {
  const fieldEmpty = !fieldValues[campo]?.trim()
  const topSeverity = sugestoes[0]?.severidade ?? 'baixa'

  return (
    <div>
      <button
        onClick={onToggle}
        className="flex w-full items-center gap-3 px-4 py-2.5 text-left transition hover:bg-surface-hover"
      >
        <span className={cn('h-2 w-2 shrink-0 rounded-full', SEV_DOT_CLASS[topSeverity])} />
        <span className="flex-1">
          <span className="text-sm font-semibold text-fg">{FIELD_LABELS[campo]}</span>
          {fieldEmpty && (
            <span className="ml-2 text-[10px] uppercase tracking-wider text-fg-dim">vazio</span>
          )}
        </span>
        <span className="text-[11px] text-fg-muted">
          {sugestoes.length} {sugestoes.length === 1 ? 'item' : 'itens'}
        </span>
        <span className={cn('text-fg-dim transition-transform', open && 'rotate-90')}>›</span>
      </button>
      {open && (
        <div className="space-y-2 px-4 pb-3">
          {sugestoes.map((s, idx) => (
            <SugestaoCard
              key={`${campo}-${idx}`}
              sugestao={s}
              currentValue={fieldValues[campo] ?? ''}
              onUseAsBase={() => onUseAsBase(s)}
              showDiff={diffFor?.campo === campo && diffFor.sugestao === s}
              onDiffApply={onDiffApply}
              onDiffCancel={onDiffCancel}
            />
          ))}
        </div>
      )}
    </div>
  )
}

interface SugestaoCardProps {
  sugestao: Sugestao
  currentValue: string
  onUseAsBase: () => void
  showDiff: boolean
  onDiffApply: (value: string) => void
  onDiffCancel: () => void
}

function SugestaoCard({
  sugestao,
  currentValue,
  onUseAsBase,
  showDiff,
  onDiffApply,
  onDiffCancel,
}: SugestaoCardProps) {
  const [copied, setCopied] = useState(false)

  const handleCopy = async () => {
    if (!sugestao.exemplo) return
    try {
      await navigator.clipboard.writeText(sugestao.exemplo)
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    } catch {
      // Clipboard pode falhar em iframe sem permissão — silencioso.
    }
  }

  return (
    <div className="rounded-lg border border-border bg-surface p-3">
      <div className="mb-1.5 flex flex-wrap items-center gap-1.5">
        <span className={cn('h-1.5 w-1.5 rounded-full', SEV_DOT_CLASS[sugestao.severidade])} />
        <Badge tone={TIPO_TONE[sugestao.tipo]} className="text-[10px]">
          {TIPO_LABELS[sugestao.tipo]}
        </Badge>
        <span className="text-[10px] uppercase tracking-wider text-fg-dim">
          {SEVERITY_LABELS[sugestao.severidade]}
        </span>
      </div>
      <p className="text-xs leading-relaxed text-fg">{sugestao.mensagem}</p>
      {sugestao.exemplo && !showDiff && (
        <>
          <pre className="mt-2 max-h-32 overflow-y-auto whitespace-pre-wrap rounded-md border border-border bg-bg-soft px-2.5 py-2 font-sans text-[11px] text-fg-muted">
            {sugestao.exemplo}
          </pre>
          <div className="mt-2 flex items-center gap-2">
            <Button size="sm" variant="ghost" onClick={handleCopy}>
              {copied ? 'Copiado' : 'Copiar'}
            </Button>
            <Button size="sm" variant="secondary" onClick={onUseAsBase}>
              Usar como base
            </Button>
          </div>
        </>
      )}
      {showDiff && sugestao.exemplo && (
        <div className="mt-2">
          <AssistantDiff
            current={currentValue}
            suggested={sugestao.exemplo}
            onApply={onDiffApply}
            onCancel={onDiffCancel}
          />
        </div>
      )}
    </div>
  )
}

function DrawerSkeleton() {
  const [stage, setStage] = useState(0)
  useEffect(() => {
    const labels = ['Lendo papel', 'Avaliando contexto', 'Gerando sugestões']
    const t = setInterval(() => setStage((s) => Math.min(s + 1, labels.length - 1)), 2200)
    return () => clearInterval(t)
  }, [])
  const labels = ['Lendo papel', 'Avaliando contexto', 'Gerando sugestões']

  return (
    <div className="space-y-3 px-4 py-4">
      <div className="flex items-center gap-2 text-xs text-fg-muted">
        <Spinner className="h-3.5 w-3.5" />
        <span>{labels[stage]}…</span>
      </div>
      {[0, 1, 2].map((i) => (
        <div key={i} className="space-y-2 rounded-lg border border-border bg-surface p-3">
          <div className="h-3 w-1/2 animate-pulse rounded bg-bg-soft" />
          <div className="h-3 w-full animate-pulse rounded bg-bg-soft" />
          <div className="h-3 w-4/5 animate-pulse rounded bg-bg-soft" />
        </div>
      ))}
    </div>
  )
}

function DrawerError({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <div className="flex flex-col items-center gap-3 px-4 py-10 text-center">
      <p className="text-sm text-fg-muted">{message}</p>
      <Button size="sm" variant="secondary" onClick={onRetry}>
        Tentar novamente
      </Button>
    </div>
  )
}
