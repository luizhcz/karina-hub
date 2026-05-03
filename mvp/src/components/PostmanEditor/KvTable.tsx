import { type ReactNode } from 'react'
import { Button, IconButton, Input, cn } from '../../ui'
import { CloseIcon, PlusIcon } from '../../ui/Icons'

export interface KvRow<TVal> {
  id: string
  key: string
  val: TVal
  /** quando true, a chave é controlada externamente (path param detectado da URL) */
  locked?: boolean
}

interface Props<TVal> {
  rows: KvRow<TVal>[]
  onChange: (rows: KvRow<TVal>[]) => void
  keyPlaceholder?: string
  valLabel?: string
  buildEmpty: () => TVal
  renderVal: (val: TVal, onChange: (v: TVal) => void) => ReactNode
  forbidKeys?: string[]
}

// Editor genérico de chave/valor reutilizado por path params, query params e
// headers customizados. Aceita um custom renderer pra valor (string simples
// ou objeto com tipo/descrição/required) e bloqueia chaves reservadas.
export function KvTable<TVal>({
  rows,
  onChange,
  keyPlaceholder = 'chave',
  valLabel = 'valor',
  buildEmpty,
  renderVal,
  forbidKeys,
}: Props<TVal>) {
  const forbidden = new Set((forbidKeys ?? []).map((k) => k.toLowerCase()))

  const updateKey = (id: string, key: string) => {
    onChange(rows.map((r) => (r.id === id ? { ...r, key } : r)))
  }

  const updateVal = (id: string, val: TVal) => {
    onChange(rows.map((r) => (r.id === id ? { ...r, val } : r)))
  }

  const remove = (id: string) => {
    onChange(rows.filter((r) => r.id !== id))
  }

  const add = () => {
    onChange([...rows, { id: shortId(), key: '', val: buildEmpty() }])
  }

  if (rows.length === 0) {
    return (
      <div className="space-y-3">
        <div className="rounded-lg border border-dashed border-border px-4 py-6 text-center text-xs text-fg-muted">
          Nenhum item ainda.
        </div>
        <Button variant="secondary" size="sm" leftIcon={<PlusIcon className="h-3.5 w-3.5" />} onClick={add}>
          Adicionar
        </Button>
      </div>
    )
  }

  return (
    <div className="space-y-2">
      <div className="grid grid-cols-12 gap-2 px-2 text-[10px] uppercase tracking-wider text-fg-dim">
        <div className="col-span-4">{keyPlaceholder}</div>
        <div className="col-span-7">{valLabel}</div>
        <div className="col-span-1" />
      </div>
      {rows.map((row) => {
        const isForbidden =
          row.key.trim().length > 0 && forbidden.has(row.key.trim().toLowerCase())
        return (
          <div key={row.id} className="grid grid-cols-12 items-start gap-2">
            <div className={cn('col-span-4', row.locked && 'opacity-90')}>
              <Input
                value={row.key}
                onChange={(e) => updateKey(row.id, e.target.value)}
                placeholder={keyPlaceholder}
                disabled={row.locked}
                monospace
                error={isForbidden ? 'reservada' : undefined}
                hint={row.locked ? 'vem da URL' : undefined}
              />
            </div>
            <div className="col-span-7">{renderVal(row.val, (v) => updateVal(row.id, v))}</div>
            <div className="col-span-1 flex justify-end pt-1">
              <IconButton
                aria-label="Remover linha"
                variant="danger"
                size="sm"
                onClick={() => remove(row.id)}
              >
                <CloseIcon className="h-4 w-4" />
              </IconButton>
            </div>
          </div>
        )
      })}
      <Button variant="secondary" size="sm" leftIcon={<PlusIcon className="h-3.5 w-3.5" />} onClick={add}>
        Adicionar
      </Button>
    </div>
  )
}

function shortId() {
  return Math.random().toString(36).slice(2, 10)
}
