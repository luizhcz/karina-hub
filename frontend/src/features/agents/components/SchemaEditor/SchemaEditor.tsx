import { useState, useCallback, useRef, useEffect } from 'react'
import { MonacoEditor } from '../../../../shared/editors/MonacoEditor'
import { FieldNode } from './FieldNode'
import { AddFieldInline } from './AddFieldInline'
import { useSchemaHistory } from './useSchemaHistory'
import { deepClone, navigateToParent, rebuildProp, resolveType, tryParseSchema } from './utils'
import type { EditorMode, FieldEditData, SchemaEditorProps, SchemaType } from './types'

// eslint-disable-next-line @typescript-eslint/no-explicit-any
type SchemaNode = Record<string, any>

export function SchemaEditor({ value, onChange }: SchemaEditorProps) {
  const [mode, setMode] = useState<EditorMode>('visual')
  const [schema, setSchema] = useState<SchemaNode | null>(() => {
    const result = tryParseSchema(value)
    return result.ok ? result.schema : null
  })
  const [parseError, setParseError] = useState<string | null>(null)
  const [adding, setAdding] = useState(false)
  const [toast, setToast] = useState<string | null>(null)
  const lastSerialized = useRef(value)
  const history = useSchemaHistory(value)
  const containerRef = useRef<HTMLDivElement>(null)

  // Sync external value changes (e.g. RHF reset)
  useEffect(() => {
    if (value !== lastSerialized.current) {
      const result = tryParseSchema(value)
      if (result.ok) {
        setSchema(result.schema)
        setParseError(null)
      }
      lastSerialized.current = value
    }
  }, [value])

  // Keyboard shortcuts
  useEffect(() => {
    const el = containerRef.current
    if (!el) return
    const handler = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key === 'z') {
        e.preventDefault()
        if (e.shiftKey) {
          history.redo()
        } else {
          history.undo()
        }
      }
    }
    el.addEventListener('keydown', handler)
    return () => el.removeEventListener('keydown', handler)
  }, [history])

  // Sync history state back to schema + onChange
  useEffect(() => {
    if (history.state !== lastSerialized.current) {
      const result = tryParseSchema(history.state)
      if (result.ok) {
        setSchema(result.schema)
        setParseError(null)
      }
      lastSerialized.current = history.state
      onChange(history.state)
    }
  }, [history.state, onChange])

  const flash = useCallback((msg: string) => {
    setToast(msg)
    setTimeout(() => setToast(null), 2000)
  }, [])

  const pushSchema = useCallback((s: SchemaNode) => {
    const json = JSON.stringify(s, null, 2)
    history.set(json)
    setSchema(s)
    lastSerialized.current = json
    onChange(json)
  }, [history, onChange])

  const handleUpdate = useCallback((path: string[], oldKey: string | null, data: FieldEditData) => {
    if (!schema) return
    const s = deepClone(schema)

    // Adding new field
    if (data.isNew) {
      const parent = navigateToParent(s, path)
      if (!parent.properties) parent.properties = {}
      parent.properties[data.key] = rebuildProp(data.type, data.description, data.nullable)
      if (!parent.required) parent.required = []
      if (data.required && !parent.required.includes(data.key)) parent.required.push(data.key)
      pushSchema(s)
      flash('Campo adicionado')
      return
    }

    if (!oldKey) return
    const parent = navigateToParent(s, path)
    if (!parent.properties?.[oldKey]) return

    const oldProp = parent.properties[oldKey]
    const { resolved: oldResolved } = resolveType(oldProp)
    const preserveChildren = (data.type === 'object' || data.type === 'array') && oldResolved.type === data.type

    let newProp: SchemaNode
    if (preserveChildren && data.type === 'object' && oldResolved.properties) {
      newProp = data.nullable
        ? { anyOf: [{ ...oldResolved, type: 'object' }, { type: 'null' }], ...(data.description ? { description: data.description } : {}) }
        : { ...oldResolved }
      if (data.description) newProp.description = data.description
    } else if (preserveChildren && data.type === 'array' && oldResolved.items) {
      const inner = data.nullable
        ? { anyOf: [{ type: 'array', items: oldResolved.items }, { type: 'null' }] }
        : { type: 'array', items: oldResolved.items }
      if (data.description) (inner as SchemaNode).description = data.description
      newProp = inner
    } else {
      newProp = rebuildProp(data.type, data.description, data.nullable)
    }

    // Handle rename
    if (data.key !== oldKey) {
      const entries = Object.entries(parent.properties).map(([k, v]) =>
        k === oldKey ? [data.key, newProp] : [k, v]
      )
      parent.properties = Object.fromEntries(entries)
      if (parent.required) {
        parent.required = parent.required.filter((r: string) => r !== oldKey)
        if (data.required) parent.required.push(data.key)
      }
    } else {
      parent.properties[data.key] = newProp
      if (parent.required) {
        parent.required = parent.required.filter((r: string) => r !== data.key)
        if (data.required) parent.required.push(data.key)
      }
    }

    pushSchema(s)
    flash('Campo atualizado')
  }, [schema, pushSchema, flash])

  const handleDelete = useCallback((path: string[], key: string) => {
    if (!schema) return
    const s = deepClone(schema)
    const parent = navigateToParent(s, path)
    if (!parent.properties) return
    delete parent.properties[key]
    if (parent.required) parent.required = parent.required.filter((r: string) => r !== key)
    pushSchema(s)
    flash('Campo removido')
  }, [schema, pushSchema, flash])

  const handleAddRoot = useCallback((name: string, type: SchemaType) => {
    if (!schema) {
      const s: SchemaNode = { type: 'object', properties: {}, required: [], additionalProperties: false }
      s.properties[name] = rebuildProp(type, '', false)
      pushSchema(s)
    } else {
      const s = deepClone(schema)
      if (!s.properties) s.properties = {}
      s.properties[name] = rebuildProp(type, '', false)
      pushSchema(s)
    }
    setAdding(false)
    flash('Campo adicionado')
  }, [schema, pushSchema, flash])

  const switchToVisual = useCallback(() => {
    const result = tryParseSchema(history.state)
    if (result.ok) {
      setSchema(result.schema)
      setParseError(null)
      setMode('visual')
    } else {
      setParseError(result.error)
    }
  }, [history.state])

  const handleMonacoChange = useCallback((v: string) => {
    lastSerialized.current = v
    history.set(v)
    onChange(v)
  }, [history, onChange])

  const copyJSON = useCallback(() => {
    const text = history.state || JSON.stringify(schema, null, 2)
    navigator.clipboard?.writeText(text)
    flash('JSON copiado')
  }, [history.state, schema, flash])

  const fieldCount = schema?.properties ? Object.keys(schema.properties).length : 0
  const rootRequired: string[] = schema?.required || []

  return (
    <div ref={containerRef} tabIndex={-1} className="outline-none">
      {/* Animations */}
      <style>{`
        @keyframes slideIn { from { opacity: 0; transform: translateY(-4px); } to { opacity: 1; transform: translateY(0); } }
        .animate-slideIn { animation: slideIn 0.15s ease; }
        @keyframes toastIn { from { opacity: 0; transform: translateY(8px); } to { opacity: 1; transform: translateY(0); } }
      `}</style>

      {/* Toast */}
      {toast && (
        <div
          className="fixed bottom-5 left-1/2 -translate-x-1/2 bg-text-primary text-bg-primary px-5 py-2 rounded-lg text-[13px] font-medium z-50 pointer-events-none"
          style={{ animation: 'toastIn 0.15s ease' }}
        >
          {toast}
        </div>
      )}

      {/* Toolbar */}
      <div className="flex items-center justify-between mb-3 gap-2 flex-wrap">
        <div className="flex items-center gap-3">
          <span className="text-[13px] text-text-secondary">
            {fieldCount} campo{fieldCount !== 1 ? 's' : ''} na raiz
          </span>
          <div className="flex gap-0.5">
            <button
              type="button"
              onClick={() => { history.undo(); flash('Acao desfeita') }}
              disabled={!history.canUndo}
              className="p-1 rounded hover:bg-white/5 disabled:opacity-30 text-text-secondary"
              title="Desfazer (Ctrl+Z)"
            >
              <svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5">
                <path d="M3 7h7a3 3 0 010 6H8" /><path d="M6 4L3 7l3 3" />
              </svg>
            </button>
            <button
              type="button"
              onClick={() => { history.redo(); flash('Acao refeita') }}
              disabled={!history.canRedo}
              className="p-1 rounded hover:bg-white/5 disabled:opacity-30 text-text-secondary"
              title="Refazer (Ctrl+Shift+Z)"
            >
              <svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5">
                <path d="M13 7H6a3 3 0 000 6h2" /><path d="M10 4l3 3-3 3" />
              </svg>
            </button>
          </div>
        </div>
        <div className="flex gap-1.5">
          <button
            type="button"
            onClick={() => setAdding(true)}
            className="flex items-center gap-1 text-xs px-3 py-1.5 rounded-md hover:bg-white/5 text-text-secondary border border-border-secondary cursor-pointer"
          >
            <svg width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5">
              <path d="M8 3v10M3 8h10" />
            </svg>
            Novo campo
          </button>
          <button
            type="button"
            onClick={() => mode === 'visual' ? setMode('json') : switchToVisual()}
            className="text-xs px-3 py-1.5 rounded-md hover:bg-white/5 text-text-secondary border border-border-secondary cursor-pointer"
          >
            {mode === 'visual' ? 'Ver JSON' : 'Visual'}
          </button>
          <button
            type="button"
            onClick={copyJSON}
            className="text-xs px-3 py-1.5 rounded-md hover:bg-white/5 text-text-secondary border border-border-secondary cursor-pointer"
          >
            Copiar
          </button>
        </div>
      </div>

      {/* Parse error banner */}
      {parseError && (
        <div className="mb-3 px-3 py-2 bg-red-500/10 border border-red-500/30 rounded-lg text-xs text-red-400">
          JSON invalido: {parseError}
        </div>
      )}

      {/* Mode: Visual */}
      {mode === 'visual' && (
        <>
          {/* Legend */}
          <div className="flex gap-4 mb-3 flex-wrap text-xs text-text-secondary items-center">
            <span className="flex items-center gap-1.5">
              <span className="w-1.5 h-1.5 rounded-full bg-red-400" /> obrigatorio
            </span>
            <span className="flex items-center gap-1.5">
              <span className="w-1.5 h-1.5 rounded-full border border-border-secondary" /> opcional
            </span>
            <span className="flex items-center gap-1.5">
              <span className="text-[10px] text-text-muted border border-border-tertiary px-1 py-0.5 rounded font-mono">nullable</span>
              aceita null
            </span>
          </div>

          {adding && <AddFieldInline onAdd={handleAddRoot} onCancel={() => setAdding(false)} />}

          {schema?.properties && Object.keys(schema.properties).length > 0 ? (
            Object.keys(schema.properties).map((k) => (
              <FieldNode
                key={k}
                fieldKey={k}
                prop={schema.properties[k]}
                required={rootRequired.includes(k)}
                depth={0}
                path={[]}
                onUpdate={handleUpdate}
                onDelete={handleDelete}
              />
            ))
          ) : (
            !adding && (
              <div className="text-center py-8 text-sm text-text-dimmed">
                Nenhum campo definido.{' '}
                <button type="button" onClick={() => setAdding(true)} className="text-accent-blue hover:underline cursor-pointer">
                  Adicione o primeiro campo
                </button>
              </div>
            )
          )}
        </>
      )}

      {/* Mode: JSON */}
      {mode === 'json' && (
        <MonacoEditor
          value={history.state}
          onChange={handleMonacoChange}
          language="json"
          height="300px"
        />
      )}
    </div>
  )
}
