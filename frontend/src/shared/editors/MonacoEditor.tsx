import Editor, { type OnMount } from '@monaco-editor/react'
import { useCallback } from 'react'

interface MonacoEditorProps {
  value: string
  onChange: (value: string) => void
  language?: string
  height?: string
  readOnly?: boolean
}

export function MonacoEditor({ value, onChange, language = 'markdown', height = '300px', readOnly = false }: MonacoEditorProps) {
  const handleMount: OnMount = useCallback((editor, monaco) => {
    monaco.editor.defineTheme('efsaihub', {
      base: 'vs-dark',
      inherit: true,
      rules: [],
      colors: {
        'editor.background': '#0C1D38',
        'editor.foreground': '#DCE8F5',
        'editorLineNumber.foreground': '#4A6B8A',
        'editor.selectionBackground': '#254980',
        'editor.lineHighlightBackground': '#0C1D3880',
      },
    })
    monaco.editor.setTheme('efsaihub')
    editor.updateOptions({ minimap: { enabled: false }, scrollBeyondLastLine: false, fontSize: 13, lineHeight: 20 })
  }, [])

  return (
    <div className="rounded-lg overflow-hidden border border-border-primary">
      <Editor
        height={height}
        language={language}
        value={value}
        onChange={(v) => onChange(v ?? '')}
        onMount={handleMount}
        options={{ readOnly }}
        theme="vs-dark"
      />
    </div>
  )
}
