import { useState } from 'react'
import { Button } from './Button'
import { IconButton } from './IconButton'
import { Input } from './Input'
import { CloseIcon, PlusIcon } from './Icons'

interface StringListEditorProps {
  values: string[]
  onChange: (values: string[]) => void
  itemPlaceholder?: string
  emptyHint?: string
  monospace?: boolean
  /** Limite máximo de entradas. Esconde o botão Adicionar quando atingido. */
  max?: number
  /** maxLength HTML aplicado a cada Input. */
  maxLength?: number
}

interface InternalRow {
  id: string
  value: string
}

function shortId() {
  return Math.random().toString(36).slice(2, 10)
}

// Editor de lista plana de strings. Mantém ids estáveis internamente pra que
// React não confunda linhas durante remoção/reordenação. Projeta o resultado
// como string[] (descarta vazios) no onChange.
export function StringListEditor({
  values,
  onChange,
  itemPlaceholder = 'item',
  emptyHint = 'Nenhum item ainda.',
  monospace,
  max,
  maxLength,
}: StringListEditorProps) {
  // Estado local com ids estáveis — sincroniza com `values` quando o tamanho
  // muda externamente (ex: load de tool existente).
  const [rows, setRows] = useState<InternalRow[]>(() =>
    values.map((value) => ({ id: shortId(), value })),
  )

  const project = (next: InternalRow[]) =>
    next.map((r) => r.value.trim()).filter((v) => v.length > 0)

  const sync = (next: InternalRow[]) => {
    setRows(next)
    onChange(project(next))
  }

  const update = (id: string, value: string) => {
    sync(rows.map((r) => (r.id === id ? { ...r, value } : r)))
  }

  const remove = (id: string) => {
    sync(rows.filter((r) => r.id !== id))
  }

  const add = () => {
    sync([...rows, { id: shortId(), value: '' }])
  }

  const atLimit = typeof max === 'number' && rows.length >= max

  if (rows.length === 0) {
    return (
      <div className="space-y-3">
        <div className="rounded-lg border border-dashed border-border px-4 py-6 text-center text-xs text-fg-muted">
          {emptyHint}
        </div>
        <Button
          variant="secondary"
          size="sm"
          leftIcon={<PlusIcon className="h-3.5 w-3.5" />}
          onClick={add}
          disabled={atLimit}
        >
          Adicionar
        </Button>
      </div>
    )
  }

  return (
    <div className="space-y-2">
      {rows.map((row) => (
        <div key={row.id} className="flex items-start gap-2">
          <div className="flex-1">
            <Input
              value={row.value}
              onChange={(e) => update(row.id, e.target.value)}
              placeholder={itemPlaceholder}
              monospace={monospace}
              maxLength={maxLength}
            />
          </div>
          <IconButton
            aria-label="Remover item"
            variant="danger"
            size="sm"
            onClick={() => remove(row.id)}
          >
            <CloseIcon className="h-4 w-4" />
          </IconButton>
        </div>
      ))}
      <Button
        variant="secondary"
        size="sm"
        leftIcon={<PlusIcon className="h-3.5 w-3.5" />}
        onClick={add}
        disabled={atLimit}
      >
        Adicionar
      </Button>
      {atLimit && (
        <p className="text-[11px] text-fg-dim">Limite de {max} itens atingido.</p>
      )}
    </div>
  )
}
