export function TypingBubble() {
  return (
    <div className="flex justify-start">
      <div className="px-4 py-2.5 rounded-2xl rounded-bl-sm bg-bg-tertiary border border-border-primary">
        <span className="flex gap-1 items-center h-5">
          <span className="w-1.5 h-1.5 rounded-full bg-text-muted animate-bounce [animation-delay:0ms]" />
          <span className="w-1.5 h-1.5 rounded-full bg-text-muted animate-bounce [animation-delay:150ms]" />
          <span className="w-1.5 h-1.5 rounded-full bg-text-muted animate-bounce [animation-delay:300ms]" />
        </span>
      </div>
    </div>
  )
}
