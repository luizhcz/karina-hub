export function PlaceholderPage({ title }: { title: string }) {
  return (
    <div className="flex flex-col items-center justify-center h-64 gap-3">
      <div className="text-2xl font-semibold text-text-primary">{title}</div>
      <p className="text-sm text-text-muted">Em construção</p>
    </div>
  )
}
