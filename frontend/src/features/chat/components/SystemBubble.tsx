export function SystemBubble({ text }: { text: string }) {
  return (
    <div className="flex justify-center my-2">
      <span className="text-xs italic text-text-muted bg-bg-tertiary px-3 py-1 rounded-full border border-border-primary">
        {text}
      </span>
    </div>
  )
}
