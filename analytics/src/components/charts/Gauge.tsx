/**
 * Gauge — arco de progresso com 3 faixas de cor (verde/amarelo/vermelho).
 *
 * Reusabilidade: domain-agnostic. Caller passa `value` em fração 0..1
 * (ProjectAnalytics devolve nesse formato) e thresholds opcionais. Default
 * é 0.75 / 0.90 — abaixo de 75% verde, 75-90% amarelo, acima vermelho.
 * Mesmos thresholds servirão pra futuras telas (latência, error rate)
 * mudando só o range que caracteriza "saudável" pelo `inverted`.
 *
 * inverted=false: maior é pior (uso/orçamento → quanto mais perto de 100%, pior).
 * inverted=true: maior é melhor (taxa de sucesso, SLA).
 *
 * NÃO faz: animação de varredura entrando (poluição visual em dashboard
 * carregado), tooltip (info já vem no label).
 *
 * Exemplo: <Gauge value={budget.costUsagePct} label="68% do limite diário" />
 *          Reusado por: budget, futuro SLA de execuções.
 */
import { useMemo } from 'react'
import { cn } from '../ui/cn'

interface GaugeProps {
  /** Fração 0..1. Valores >1 são clampados pra 1 visualmente, mas o tom continua "ruim". */
  value: number | null | undefined
  label?: string
  /** Texto auxiliar abaixo (ex.: "US$ 42 / US$ 100"). */
  caption?: string
  /** Threshold pra cor amarela. Default 0.75. */
  warningAt?: number
  /** Threshold pra cor vermelha. Default 0.9. */
  dangerAt?: number
  /** Maior = pior (default). Quando false, inverte (maior = melhor). */
  inverted?: boolean
  size?: number
  className?: string
}

function pickColor(value: number, warningAt: number, dangerAt: number, inverted: boolean): string {
  // Para inverted=true, "value alto = bom" → vermelho quando value < (1-dangerAt).
  if (inverted) {
    if (value <= 1 - dangerAt) return 'rgb(var(--color-danger))'
    if (value <= 1 - warningAt) return 'rgb(var(--color-warning))'
    return 'rgb(var(--color-success))'
  }
  if (value >= dangerAt) return 'rgb(var(--color-danger))'
  if (value >= warningAt) return 'rgb(var(--color-warning))'
  return 'rgb(var(--color-success))'
}

export function Gauge({
  value,
  label,
  caption,
  warningAt = 0.75,
  dangerAt = 0.9,
  inverted = false,
  size = 160,
  className,
}: GaugeProps) {
  const safeValue = useMemo(() => {
    if (value == null || !Number.isFinite(value)) return null
    return Math.max(0, value)
  }, [value])

  // SVG arc semicírculo: viewBox 100x60, raio 45 a partir de (50, 50).
  // Stroke total ~ π*45 ≈ 141.4; mas pra simplificar usamos pathLength=100.
  const clampedForVisual = safeValue == null ? 0 : Math.min(1, safeValue)
  const dash = clampedForVisual * 100
  const color =
    safeValue == null ? 'rgb(var(--color-fg-dim))' : pickColor(safeValue, warningAt, dangerAt, inverted)

  return (
    <div className={cn('flex flex-col items-center gap-2', className)}>
      <svg viewBox="0 0 100 60" width={size} height={size * 0.6} role="img" aria-label={label}>
        {/* Trilha de fundo */}
        <path
          d="M 5 50 A 45 45 0 0 1 95 50"
          fill="none"
          stroke="rgb(var(--color-border))"
          strokeWidth={8}
          strokeLinecap="round"
          pathLength={100}
        />
        {/* Arco preenchido — pathLength=100 permite usar dasharray como porcentagem direta. */}
        <path
          d="M 5 50 A 45 45 0 0 1 95 50"
          fill="none"
          stroke={color}
          strokeWidth={8}
          strokeLinecap="round"
          pathLength={100}
          strokeDasharray={`${dash} ${100 - dash}`}
          style={{ transition: 'stroke-dasharray 240ms ease-out, stroke 240ms' }}
        />
        <text
          x="50"
          y="46"
          textAnchor="middle"
          fontSize="16"
          fontWeight={600}
          fill="rgb(var(--color-fg))"
        >
          {safeValue == null ? '—' : `${Math.round(safeValue * 100)}%`}
        </text>
      </svg>
      {label && <p className="text-sm font-medium text-fg">{label}</p>}
      {caption && <p className="text-xs text-fg-muted">{caption}</p>}
    </div>
  )
}
