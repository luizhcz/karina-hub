import { useState, useRef, useEffect } from 'react'
import { SCHEMA_TYPES, type SchemaType } from './types'

interface AddFieldInlineProps {
  onAdd: (name: string, type: SchemaType) => void
  onCancel: () => void
}

export function AddFieldInline({ onAdd, onCancel }: AddFieldInlineProps) {
  const [name, setName] = useState('')
  const [type, setType] = useState<SchemaType>('string')
  const ref = useRef<HTMLInputElement>(null)

  useEffect(() => { ref.current?.focus() }, [])

  const submit = () => {
    const trimmed = name.trim()
    if (trimmed) onAdd(trimmed, type)
  }

  return (
    <div
      className="flex items-center gap-2 px-3 py-2 bg-bg-tertiary rounded-lg mb-1 animate-slideIn"
    >
      <input
        ref={ref}
        value={name}
        onChange={(e) => setName(e.target.value)}
        placeholder="nome_do_campo"
        className="flex-1 bg-transparent border border-border-secondary rounded-md px-2 py-1 text-sm font-mono text-text-primary placeholder:text-text-dimmed focus:outline-none focus:border-accent-blue"
        onKeyDown={(e) => {
          if (e.key === 'Enter' && name.trim()) submit()
          if (e.key === 'Escape') onCancel()
        }}
      />
      <select
        value={type}
        onChange={(e) => setType(e.target.value as SchemaType)}
        className="bg-bg-tertiary border border-border-secondary rounded-md px-2 py-1 text-xs text-text-primary focus:outline-none focus:border-accent-blue w-24"
      >
        {SCHEMA_TYPES.map((t) => (
          <option key={t} value={t}>{t}</option>
        ))}
      </select>
      <button
        type="button"
        onClick={submit}
        className="p-1 rounded-md hover:bg-white/5 text-emerald-400"
        title="Adicionar"
      >
        <svg width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="2">
          <path d="M3 8.5l3.5 3.5 6.5-7" />
        </svg>
      </button>
      <button
        type="button"
        onClick={onCancel}
        className="p-1 rounded-md hover:bg-white/5 text-text-muted"
        title="Cancelar"
      >
        <svg width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5">
          <path d="M4 4l8 8M12 4l-8 8" />
        </svg>
      </button>
    </div>
  )
}
