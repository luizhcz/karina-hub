import { useEffect, useRef, useState } from 'react'
import { Button } from '../../../shared/ui/Button'
import { Input } from '../../../shared/ui/Input'

export type KVValue<TVal> = TVal | string

export interface KeyValueListEditorProps<TVal> {
  value: Record<string, TVal>
  onChange: (next: Record<string, TVal>) => void
  keyLabel?: string
  valueLabel?: string
  buildEmptyValue: () => TVal
  renderValueEditor: (val: TVal, onChange: (next: TVal) => void) => React.ReactNode
  forbidKeys?: ReadonlyArray<string>
  /** Quando definido, o componente exibe uma label para identificar o grupo. */
  groupTitle?: string
}

/**
 * Lista dinâmica chave→valor genérica. Usa array interno pra permitir chaves
 * repetidas/vazias durante edição; o `onChange` projeta em `Record<>` ignorando
 * entradas com chave vazia. forbidKeys (case-insensitive) bloqueia chaves
 * reservadas (ex: Content-Type, Accept).
 */
export function KeyValueListEditor<TVal>({
  value,
  onChange,
  keyLabel = 'Chave',
  valueLabel = 'Valor',
  buildEmptyValue,
  renderValueEditor,
  forbidKeys,
  groupTitle,
}: KeyValueListEditorProps<TVal>) {
  const [rows, setRows] = useState<Array<{ key: string; val: TVal }>>(() =>
    Object.entries(value).map(([key, val]) => ({ key, val })),
  )

  const forbidden = new Set((forbidKeys ?? []).map((k) => k.toLowerCase()))

  const projectRows = (input: Array<{ key: string; val: TVal }>) => {
    const projected: Record<string, TVal> = {}
    for (const r of input) {
      const trimmed = r.key.trim()
      if (!trimmed) continue
      if (forbidden.has(trimmed.toLowerCase())) continue
      projected[trimmed] = r.val
    }
    return projected
  }

  // Re-hidrata rows quando o `value` externo recebe chaves que ainda não
  // estão refletidas na lista local — caso típico em edit-mode quando o load
  // assíncrono completa após o mount inicial. Comparamos pelas chaves
  // projetadas pra evitar reescrita durante typing (chave em rows mas vazia
  // no value é estado intermediário).
  const lastSyncedKeys = useRef<string>('')
  useEffect(() => {
    const projected = projectRows(rows)
    const projectedKeys = Object.keys(projected).sort().join('|')
    const externalKeys = Object.keys(value).sort().join('|')
    if (projectedKeys !== externalKeys) {
      const sig = externalKeys
      if (sig !== lastSyncedKeys.current) {
        lastSyncedKeys.current = sig
        setRows(Object.entries(value).map(([key, val]) => ({ key, val })))
      }
    } else {
      lastSyncedKeys.current = externalKeys
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value])

  const sync = (next: Array<{ key: string; val: TVal }>) => {
    setRows(next)
    onChange(projectRows(next))
  }

  const updateRow = (idx: number, patch: Partial<{ key: string; val: TVal }>) => {
    const next = [...rows]
    next[idx] = { ...next[idx], ...patch }
    sync(next)
  }

  const removeRow = (idx: number) => {
    const next = rows.filter((_, i) => i !== idx)
    sync(next)
  }

  const addRow = () => {
    sync([...rows, { key: '', val: buildEmptyValue() }])
  }

  return (
    <div className="flex flex-col gap-2">
      {groupTitle && (
        <span className="text-xs font-medium text-text-muted">{groupTitle}</span>
      )}
      {rows.length === 0 && (
        <span className="text-xs text-text-dimmed italic">Nenhum item.</span>
      )}
      {rows.map((row, idx) => {
        const trimmed = row.key.trim().toLowerCase()
        const isForbidden = trimmed.length > 0 && forbidden.has(trimmed)
        return (
          <div
            key={idx}
            className="flex items-start gap-2 bg-bg-tertiary border border-border-secondary rounded-lg px-3 py-2"
          >
            <div className="flex-1 flex flex-col gap-1">
              <Input
                label={idx === 0 ? keyLabel : undefined}
                value={row.key}
                onChange={(e) => updateRow(idx, { key: e.target.value })}
                error={isForbidden ? 'Chave reservada' : undefined}
              />
            </div>
            <div className="flex-[2]">
              {idx === 0 && (
                <span className="block text-xs font-medium text-text-muted mb-1">
                  {valueLabel}
                </span>
              )}
              {renderValueEditor(row.val, (next) => updateRow(idx, { val: next }))}
            </div>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={() => removeRow(idx)}
              className="mt-5"
            >
              Remover
            </Button>
          </div>
        )
      })}
      <Button type="button" variant="secondary" size="sm" onClick={addRow}>
        Adicionar
      </Button>
    </div>
  )
}
