import { forwardRef, useEffect, useId, useImperativeHandle, useLayoutEffect, useRef } from 'react'
import { cn } from './cn'

interface TextareaProps extends React.TextareaHTMLAttributes<HTMLTextAreaElement> {
  label?: string
  hint?: string
  error?: string
  monospace?: boolean
  /**
   * Quando true, o textarea expande verticalmente conforme o conteúdo cresce.
   * Após atingir <c>maxAutoGrowHeight</c> (default 240px), o crescimento para
   * e o scroll interno toma conta. Desliga o resize manual (incompatível).
   */
  autoGrow?: boolean
  /** Limite superior do auto-grow em pixels. Default 240 (~12 linhas). */
  maxAutoGrowHeight?: number
}

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaProps>(function Textarea(
  { label, hint, error, monospace, autoGrow, maxAutoGrowHeight = 240, className, id, value, defaultValue, ...rest },
  ref,
) {
  const generatedId = useId()
  const inputId = id ?? generatedId

  // Ref interno pra medir scrollHeight; sincronizado com o ref forwarded
  // via useImperativeHandle pra que callers ainda consigam focus/select.
  const innerRef = useRef<HTMLTextAreaElement | null>(null)
  useImperativeHandle(ref, () => innerRef.current as HTMLTextAreaElement, [])

  // Recalcula altura sempre que value muda (controlled) ou no mount (uncontrolled).
  // useLayoutEffect evita flash de altura errada antes do paint.
  useLayoutEffect(() => {
    if (!autoGrow) return
    const el = innerRef.current
    if (!el) return
    el.style.height = 'auto'
    const next = Math.min(el.scrollHeight, maxAutoGrowHeight)
    el.style.height = `${next}px`
  }, [autoGrow, maxAutoGrowHeight, value])

  // Garante recálculo após render inicial (uncontrolled com defaultValue,
  // ou injeção de className que afeta width/padding).
  useEffect(() => {
    if (!autoGrow) return
    const el = innerRef.current
    if (!el) return
    el.style.height = 'auto'
    el.style.height = `${Math.min(el.scrollHeight, maxAutoGrowHeight)}px`
  }, [autoGrow, maxAutoGrowHeight])

  return (
    <div className="flex flex-col gap-1">
      {label && (
        <label htmlFor={inputId} className="text-xs font-medium text-fg-muted">
          {label}
        </label>
      )}
      <textarea
        ref={innerRef}
        id={inputId}
        value={value}
        defaultValue={defaultValue}
        className={cn(
          'min-h-[60px] w-full rounded-lg border bg-surface px-3 py-2 text-sm text-fg placeholder:text-fg-dim',
          'focus:outline-none focus:ring-2 focus:ring-accent/30',
          error ? 'border-danger/60' : 'border-border focus:border-accent',
          monospace && 'font-mono text-[12px]',
          autoGrow ? 'resize-none overflow-y-auto' : 'resize-y',
          className,
        )}
        aria-invalid={!!error || undefined}
        {...rest}
      />
      {error ? (
        <span className="text-[11px] text-danger">{error}</span>
      ) : hint ? (
        <span className="text-[11px] text-fg-dim">{hint}</span>
      ) : null}
    </div>
  )
})
