import { useState } from 'react'
import { Badge } from '../../../../shared/ui/Badge'
import { resolveType, hasChildren, getChildProps, getChildRequired } from './utils'
import { EditFieldModal } from './EditFieldModal'
import { AddFieldInline } from './AddFieldInline'
import type { SchemaType, FieldEditData, JsonSchemaNode } from './types'

const TYPE_VARIANT: Record<string, 'blue' | 'yellow' | 'purple' | 'green' | 'gray'> = {
  string: 'blue',
  number: 'yellow',
  integer: 'yellow',
  boolean: 'purple',
  object: 'green',
  array: 'purple',
}

interface FieldNodeProps {
  fieldKey: string
  prop: JsonSchemaNode
  required: boolean
  depth: number
  path: string[]
  onUpdate: (path: string[], oldKey: string | null, data: FieldEditData) => void
  onDelete: (path: string[], key: string) => void
}

export function FieldNode({ fieldKey, prop, required, depth, path, onUpdate, onDelete }: FieldNodeProps) {
  const [open, setOpen] = useState(false)
  const [editing, setEditing] = useState(false)
  const [adding, setAdding] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)

  const { types, nullable, resolved } = resolveType(prop)
  const desc = prop.description || ''
  const isComplex = hasChildren(resolved)
  const childProps = isComplex ? getChildProps(resolved) : null
  const childRequired = isComplex ? getChildRequired(resolved) : []

  function handleSave(data: FieldEditData) {
    onUpdate(path, fieldKey, data)
    setEditing(false)
  }

  function handleAddChild(name: string, type: SchemaType) {
    onUpdate([...path, fieldKey], null, {
      key: name,
      oldKey: name,
      type,
      description: '',
      nullable: false,
      required: false,
      isNew: true,
    })
    setAdding(false)
    setOpen(true)
  }

  function handleDelete() {
    if (!confirmDelete) {
      setConfirmDelete(true)
      setTimeout(() => setConfirmDelete(false), 3000)
      return
    }
    onDelete(path, fieldKey)
  }

  return (
    <div className="mb-1">
      <div className="border border-border-tertiary rounded-lg bg-bg-primary overflow-hidden transition-colors">
        {/* Field row */}
        <div
          onClick={() => isComplex && setOpen(!open)}
          className={`group flex items-center gap-2 px-3 py-2 select-none ${isComplex ? 'cursor-pointer' : ''}`}
        >
          {/* Chevron */}
          {isComplex ? (
            <svg
              width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5"
              className={`text-text-muted flex-shrink-0 transition-transform duration-200 ${open ? 'rotate-90' : ''}`}
            >
              <path d="M6 4l4 4-4 4" />
            </svg>
          ) : (
            <div className="w-4 flex-shrink-0" />
          )}

          {/* Required dot */}
          <span
            className={`w-1.5 h-1.5 rounded-full flex-shrink-0 ${
              required ? 'bg-red-400' : 'border border-border-secondary'
            }`}
          />

          {/* Field name */}
          <span className="font-mono text-sm font-medium text-text-primary">{fieldKey}</span>

          {/* Type pills */}
          {types.map((t) => (
            <Badge key={t} variant={TYPE_VARIANT[t] || 'gray'}>{t}</Badge>
          ))}

          {/* Nullable tag */}
          {nullable && (
            <span className="text-[10px] text-text-muted border border-border-tertiary px-1.5 py-0.5 rounded font-mono">
              nullable
            </span>
          )}

          {/* Description */}
          {desc && (
            <span className="text-xs text-text-dimmed flex-1 truncate min-w-0">{desc}</span>
          )}

          {/* Action buttons */}
          <div
            className="flex gap-0.5 ml-auto flex-shrink-0 opacity-0 group-hover:opacity-100 transition-opacity"
            onClick={(e) => e.stopPropagation()}
          >
            {isComplex && (
              <button
                type="button"
                onClick={() => setAdding(!adding)}
                className="p-1 rounded hover:bg-white/5 text-blue-400"
                title="Adicionar campo filho"
              >
                <svg width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5">
                  <path d="M8 3v10M3 8h10" />
                </svg>
              </button>
            )}
            <button
              type="button"
              onClick={() => setEditing(!editing)}
              className="p-1 rounded hover:bg-white/5 text-text-secondary"
              title="Editar"
            >
              <svg width="13" height="13" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.4">
                <path d="M11 2l3 3-9 9H2v-3z" />
              </svg>
            </button>
            <button
              type="button"
              onClick={handleDelete}
              className={`p-1 rounded hover:bg-white/5 ${confirmDelete ? 'text-red-400' : 'text-text-muted'}`}
              title={confirmDelete ? 'Clique de novo para confirmar' : 'Deletar'}
            >
              <svg width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.4">
                <path d="M3 4h10M6 4V3a1 1 0 011-1h2a1 1 0 011 1v1M5 4v9a1 1 0 001 1h4a1 1 0 001-1V4" />
              </svg>
            </button>
          </div>
        </div>

        {/* Children (expanded) */}
        {open && childProps && (
          <div className="pl-8 pr-3 pb-2 pt-0.5 border-t border-border-tertiary">
            {resolved.type === 'array' && (
              <div className="text-[11px] text-text-muted py-1 italic">cada item do array:</div>
            )}
            {adding && <AddFieldInline onAdd={handleAddChild} onCancel={() => setAdding(false)} />}
            {Object.keys(childProps).map((k) => (
              <FieldNode
                key={k}
                fieldKey={k}
                prop={childProps[k]}
                required={childRequired.includes(k)}
                depth={depth + 1}
                path={[...path, fieldKey]}
                onUpdate={onUpdate}
                onDelete={onDelete}
              />
            ))}
          </div>
        )}
      </div>

      {/* Edit modal (below the field) */}
      {editing && (
        <EditFieldModal
          field={{ key: fieldKey, types, description: desc, nullable, required }}
          onSave={handleSave}
          onCancel={() => setEditing(false)}
        />
      )}
    </div>
  )
}
