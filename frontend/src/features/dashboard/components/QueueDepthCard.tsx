import { Card } from '../../../shared/ui/Card'

export function QueueDepthCard() {
  return (
    <Card title="Queue Depth">
      <div className="flex flex-col items-center justify-center gap-2 py-4">
        <span className="text-2xl font-bold text-text-muted">--</span>
        <span className="text-xs text-text-muted">
          OTel integration pending
        </span>
      </div>
    </Card>
  )
}
