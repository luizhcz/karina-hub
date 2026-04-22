import { useState } from 'react'
import { Link } from 'react-router'
import { Badge } from '../../../shared/ui/Badge'
import { Modal } from '../../../shared/ui/Modal'
import type { ResourceRef } from '../useToolUsage'

interface Props {
  /** Lista de agents/workflows que usam o recurso. Vazia = tool órfã. */
  usedBy: ResourceRef[]
  /** Rota base onde cada item linka — "/agents" ou "/workflows". */
  hrefBase: string
  /** Rótulo do tipo de recurso (plural): "agents", "workflows". */
  resourceLabel: string
  /** Título do modal quando abre. */
  modalTitle?: string
}

/**
 * Badge clicável "Usado por N agents" (ou workflows). Click abre modal
 * listando cada recurso com link para a tela de detalhe. Tool órfã (count=0)
 * renderiza badge cinza "—" sem handler.
 */
export function UsedByBadge({ usedBy, hrefBase, resourceLabel, modalTitle }: Props) {
  const [open, setOpen] = useState(false)
  const count = usedBy.length

  if (count === 0) {
    return (
      <Badge variant="gray">
        <span className="text-text-dimmed">não utilizado</span>
      </Badge>
    )
  }

  return (
    <>
      <button
        type="button"
        onClick={(e) => {
          e.stopPropagation()
          setOpen(true)
        }}
        className="cursor-pointer hover:opacity-80 transition-opacity"
      >
        <Badge variant="blue">{count} {resourceLabel}</Badge>
      </button>

      <Modal
        open={open}
        onClose={() => setOpen(false)}
        title={modalTitle ?? `Usado por ${count} ${resourceLabel}`}
        size="md"
      >
        <ul className="flex flex-col gap-1">
          {usedBy.map((ref) => (
            <li key={ref.id}>
              <Link
                to={`${hrefBase}/${ref.id}`}
                className="flex items-center justify-between px-3 py-2 rounded-md hover:bg-bg-tertiary transition-colors"
                onClick={() => setOpen(false)}
              >
                <span className="text-sm text-text-primary">{ref.name}</span>
                <span className="font-mono text-xs text-text-dimmed">{ref.id}</span>
              </Link>
            </li>
          ))}
        </ul>
      </Modal>
    </>
  )
}
