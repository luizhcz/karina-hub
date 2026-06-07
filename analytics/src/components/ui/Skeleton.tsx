/**
 * Skeleton — retângulo pulsante pra placeholder de loading.
 *
 * Reusabilidade: domain-agnostic. Configurável via className (tamanho, raio,
 * cor opcional via override). Use múltiplos pra reproduzir o layout final —
 * evita CLS quando os dados chegam.
 *
 * NÃO faz: shimmer wave (overkill pra MVP), formato circular nativo (passe
 * rounded-full via className).
 *
 * Exemplo: <Skeleton className="h-8 w-32" /> dentro de um Stat enquanto loading.
 *          Reusado por: Stat, Table (rows), Overview, Custos.
 */
import { cn } from './cn'

interface SkeletonProps {
  className?: string
}

export function Skeleton({ className }: SkeletonProps) {
  return (
    <div
      className={cn('animate-pulse rounded-md bg-surface-hover', className)}
      aria-hidden
    />
  )
}
