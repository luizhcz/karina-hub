import { useState } from 'react'
import { cn } from '../utils/cn'

interface JsonViewerProps {
  data: unknown
  collapsed?: boolean
  maxHeight?: string
  className?: string
}

export function JsonViewer({ data, collapsed = true, maxHeight = '300px', className }: JsonViewerProps) {
  const [expanded, setExpanded] = useState(!collapsed)
  const json = typeof data === 'string' ? data : JSON.stringify(data, null, 2)

  return (
    <div className={cn('bg-bg-tertiary border border-border-primary rounded-lg overflow-hidden', className)}>
      <button
        onClick={() => setExpanded(!expanded)}
        className="w-full px-3 py-1.5 flex items-center justify-between text-xs text-text-muted hover:text-text-secondary"
      >
        <span>JSON</span>
        <span>{expanded ? '▾' : '▸'}</span>
      </button>
      {expanded && (
        <pre
          className="px-3 pb-3 text-xs text-text-secondary overflow-auto font-mono"
          style={{ maxHeight }}
        >
          {json}
        </pre>
      )}
    </div>
  )
}
