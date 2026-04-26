import { useState } from 'react'
import { Card } from '../../../shared/ui/Card'
import { Badge } from '../../../shared/ui/Badge'
import { Button } from '../../../shared/ui/Button'
import { EmptyState } from '../../../shared/ui/EmptyState'
import { useBlocklistViolations } from '../../../api/blocklist'

interface Props {
  projectId: string
}

const PAGE_SIZE = 20

export function BlocklistViolationsCard({ projectId }: Props) {
  const [page, setPage] = useState(1)
  const { data, isLoading } = useBlocklistViolations(projectId, page, PAGE_SIZE)

  const rows = data ?? []

  return (
    <Card title="Violações Recentes">
      {isLoading ? (
        <p className="text-sm text-text-muted">Carregando…</p>
      ) : rows.length === 0 ? (
        <EmptyState
          title="Nenhuma violação registrada"
          description="Quando o blocklist bloquear conteúdo, as ocorrências aparecem aqui."
        />
      ) : (
        <>
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="text-left text-xs text-text-muted border-b border-border-secondary">
                  <th className="py-2 pr-3">Quando</th>
                  <th className="py-2 pr-3">Categoria</th>
                  <th className="py-2 pr-3">Pattern</th>
                  <th className="py-2 pr-3">Fase</th>
                  <th className="py-2 pr-3">Ação</th>
                  <th className="py-2 pr-3">Agente</th>
                  <th className="py-2">Contexto (ofuscado)</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.auditId} className="border-b border-border-secondary/50">
                    <td className="py-2 pr-3 text-xs text-text-muted whitespace-nowrap">
                      {formatDate(row.detectedAt)}
                    </td>
                    <td className="py-2 pr-3">
                      {row.category && <Badge variant="red">{row.category}</Badge>}
                    </td>
                    <td className="py-2 pr-3 text-xs font-mono text-text-secondary">
                      {row.patternId ?? '—'}
                    </td>
                    <td className="py-2 pr-3 text-xs">
                      {row.phase && <Badge variant="gray">{row.phase}</Badge>}
                    </td>
                    <td className="py-2 pr-3 text-xs">
                      {row.action && (
                        <Badge variant={row.action === 'Block' ? 'red' : 'yellow'}>
                          {row.action}
                        </Badge>
                      )}
                    </td>
                    <td className="py-2 pr-3 text-xs font-mono text-text-secondary truncate max-w-[140px]">
                      {row.agentId}
                    </td>
                    <td className="py-2 text-xs text-text-muted font-mono truncate max-w-[300px]">
                      {row.contextObfuscated ?? '—'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="flex items-center justify-between mt-4 text-xs text-text-muted">
            <span>Página {page} — {rows.length} ocorrência(s)</span>
            <div className="flex gap-2">
              <Button
                variant="ghost"
                size="sm"
                disabled={page === 1}
                onClick={() => setPage(page - 1)}
              >
                Anterior
              </Button>
              <Button
                variant="ghost"
                size="sm"
                disabled={rows.length < PAGE_SIZE}
                onClick={() => setPage(page + 1)}
              >
                Próxima
              </Button>
            </div>
          </div>
        </>
      )}
    </Card>
  )
}

function formatDate(iso: string): string {
  try {
    const d = new Date(iso)
    return d.toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'medium' })
  } catch {
    return iso
  }
}
