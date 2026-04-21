export function ErrorBubble({ text }: { text: string }) {
  return (
    <div className="flex justify-start">
      <div className="max-w-[70%] px-4 py-2.5 rounded-2xl rounded-bl-sm text-sm bg-red-500/10 border border-red-500/30 text-red-400">
        ⚠ {text}
      </div>
    </div>
  )
}
