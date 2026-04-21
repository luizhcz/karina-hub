export function UserBubble({ text, time }: { text: string; time?: string }) {
  return (
    <div className="flex justify-end">
      <div className="max-w-[70%] px-4 py-2.5 rounded-2xl rounded-br-sm text-sm leading-relaxed bg-accent-blue text-white">
        <p className="whitespace-pre-wrap break-words">{text}</p>
        {time && (
          <p className="text-[10px] mt-1 text-white/60 text-right">{time}</p>
        )}
      </div>
    </div>
  )
}
