import { useRef, useState, useCallback } from 'react'
import Markdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import rehypeHighlight from 'rehype-highlight'
import { StreamingCursor } from './StreamingCursor'

function CopyButton({ codeRef }: { codeRef: React.RefObject<HTMLPreElement | null> }) {
  const [copied, setCopied] = useState(false)

  const handleCopy = useCallback(() => {
    const text = codeRef.current?.textContent ?? ''
    navigator.clipboard.writeText(text).then(() => {
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    })
  }, [codeRef])

  return (
    <button
      onClick={handleCopy}
      className="absolute top-2 right-2 px-2 py-1 text-[10px] rounded bg-white/10 text-text-muted hover:text-text-primary hover:bg-white/20 opacity-0 group-hover:opacity-100 transition-opacity"
    >
      {copied ? 'Copiado!' : 'Copiar'}
    </button>
  )
}

function PreBlock({ children, ...props }: React.ComponentPropsWithoutRef<'pre'>) {
  const ref = useRef<HTMLPreElement>(null)
  return (
    <div className="relative group">
      <pre ref={ref} {...props}>{children}</pre>
      <CopyButton codeRef={ref} />
    </div>
  )
}

export function AssistantBubble({ text, time, isStreaming }: { text: string; time?: string; isStreaming?: boolean }) {
  return (
    <div className="flex justify-start">
      <div className="max-w-[70%] px-4 py-2.5 rounded-2xl rounded-bl-sm text-sm leading-relaxed bg-bg-tertiary text-text-primary border border-border-primary">
        <div className="prose prose-sm prose-invert max-w-none break-words [&>p]:m-0 [&>p+p]:mt-1.5 [&>ul]:my-1 [&>ol]:my-1 [&_strong]:text-white [&_strong]:font-semibold [&_code]:bg-white/10 [&_code]:px-1 [&_code]:rounded [&_code]:text-xs">
          <Markdown
            remarkPlugins={[remarkGfm]}
            rehypePlugins={[rehypeHighlight]}
            components={{ pre: PreBlock }}
          >
            {text}
          </Markdown>
          {isStreaming && <StreamingCursor />}
        </div>
        {time && (
          <p className="text-[10px] mt-1 text-text-muted">{time}</p>
        )}
      </div>
    </div>
  )
}
