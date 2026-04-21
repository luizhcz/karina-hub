import { useState, useRef, useEffect } from 'react'
import { SCHEMA_TYPES, type SchemaType, type FieldEditData } from './types'

interface EditFieldModalProps {
  field: {
    key: string
    types: SchemaType[]
    description: string
    nullable: boolean
    required: boolean
  }
  onSave: (data: FieldEditData) => void
  onCancel: () => void
}

export function EditFieldModal({ field, onSave, onCancel }: EditFieldModalProps) {
  const [name, setName] = useState(field.key)
  const [type, setType] = useState<SchemaType>(field.types[0] || 'string')
  const [desc, setDesc] = useState(field.description)
  const [nullable, setNullable] = useState(field.nullable)
  const [required, setRequired] = useState(field.required)
  const ref = useRef<HTMLInputElement>(null)

  useEffect(() => { ref.current?.focus() }, [])

  return (
    <div className="bg-bg-primary border border-border-secondary rounded-xl p-4 mt-1 mb-2 animate-slideIn">
      <div className="grid grid-cols-2 gap-3 mb-3">
        <div className="flex flex-col gap-1">
          <label className="text-[11px] font-medium text-text-muted uppercase tracking-wide">Nome do campo</label>
          <input
            ref={ref}
            value={name}
            onChange={(e) => setName(e.target.value)}
            className="bg-bg-tertiary border border-border-secondary rounded-md px-2.5 py-1.5 text-sm font-mono text-text-primary focus:outline-none focus:border-accent-blue"
          />
        </div>
        <div className="flex flex-col gap-1">
          <label className="text-[11px] font-medium text-text-muted uppercase tracking-wide">Tipo</label>
          <select
            value={type}
            onChange={(e) => setType(e.target.value as SchemaType)}
            className="bg-bg-tertiary border border-border-secondary rounded-md px-2.5 py-1.5 text-sm text-text-primary focus:outline-none focus:border-accent-blue"
          >
            {SCHEMA_TYPES.map((t) => (
              <option key={t} value={t}>{t}</option>
            ))}
          </select>
        </div>
      </div>
      <div className="flex flex-col gap-1 mb-3">
        <label className="text-[11px] font-medium text-text-muted uppercase tracking-wide">Descricao</label>
        <input
          value={desc}
          onChange={(e) => setDesc(e.target.value)}
          placeholder="Descricao do campo..."
          className="bg-bg-tertiary border border-border-secondary rounded-md px-2.5 py-1.5 text-[13px] text-text-primary placeholder:text-text-dimmed focus:outline-none focus:border-accent-blue"
        />
      </div>
      <div className="flex gap-5 mb-4 items-center">
        <label className="flex items-center gap-1.5 text-[13px] text-text-secondary cursor-pointer">
          <input
            type="checkbox"
            checked={required}
            onChange={(e) => setRequired(e.target.checked)}
            className="accent-accent-blue"
          />
          Obrigatorio
        </label>
        <label className="flex items-center gap-1.5 text-[13px] text-text-secondary cursor-pointer">
          <input
            type="checkbox"
            checked={nullable}
            onChange={(e) => setNullable(e.target.checked)}
            className="accent-accent-blue"
          />
          Nullable
        </label>
      </div>
      <div className="flex gap-2 justify-end">
        <button
          type="button"
          onClick={onCancel}
          className="text-[13px] px-4 py-1.5 rounded-md text-text-secondary hover:text-text-primary hover:bg-white/5 cursor-pointer"
        >
          Cancelar
        </button>
        <button
          type="button"
          onClick={() => onSave({ key: name, type, description: desc, nullable, required, oldKey: field.key })}
          className="text-[13px] px-4 py-1.5 rounded-md bg-text-primary text-bg-primary font-medium cursor-pointer hover:opacity-90"
        >
          Salvar
        </button>
      </div>
    </div>
  )
}
