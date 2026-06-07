/**
 * EmptyState — placeholder pra "sem dados pra mostrar". Distinguir explicito
 * de loading e error: zero registros é uma resposta válida do backend.
 *
 * Reusabilidade: domain-agnostic. Recebe textos + ação opcional. Tem
 * variante visual única (não há "compact"/"large") porque a mensagem deve
 * ser a mesma em qualquer card; padronização > flexibilidade.
 *
 * NÃO faz: chamar refetch, decidir se está vazio (caller faz `data.length === 0`).
 *
 * Exemplo: <EmptyState title="Sem custos no período" description="Tente outro intervalo." />
 *          Reusado por: tabelas vazias, gráficos sem buckets.
 */
interface EmptyStateProps {
  icon?: React.ReactNode
  title: string
  description?: string
  action?: React.ReactNode
}

export function EmptyState({ icon, title, description, action }: EmptyStateProps) {
  return (
    <div className="flex flex-col items-center px-6 py-12 text-center">
      {icon && (
        <div className="mb-4 flex h-12 w-12 items-center justify-center rounded-full bg-accent-subtle text-accent">
          {icon}
        </div>
      )}
      <h3 className="text-base font-semibold text-fg">{title}</h3>
      {description && <p className="mt-1 max-w-sm text-sm text-fg-muted">{description}</p>}
      {action && <div className="mt-6">{action}</div>}
    </div>
  )
}
